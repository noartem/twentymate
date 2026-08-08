using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace TwentyMate.Core;

/// <summary>
/// Рисует значок трея: та же плитка, что и в иконке приложения — скруглённый
/// квадрат с глазом. Чем ближе перерыв, тем сильнее квадрат теряет синеву
/// и уходит в серый; на самом перерыве плитка догорает до графита, а глаз
/// загорается красным. После перерыва цикл начинается заново.
/// </summary>
/// <remarks>
/// Палитра проверена по WCAG 2.1. На переходе «синяя → серая» контраст белого
/// глаза с плиткой держится в диапазоне 4.8:1 — 5.3:1, то есть проходит даже
/// строгий порог 4.5:1 для текста; контраст самой плитки с тёмной панелью задач —
/// 3.1:1 — 3.4:1, со светлой — 4.3:1 — 4.8:1, при пороге 3:1 для графики.
/// Переход почти не меняет светлоту, поэтому контраст не проседает в середине.
///
/// На перерыве светлота меняется сознательно: красный глаз и серая плитка
/// одинаково светлы, различить их нельзя, поэтому плитка уходит в графит.
/// Контраст красного глаза с ней — 3.2:1, порог для графики выдержан. Сама
/// графитовая плитка на тёмной панели задач даёт лишь 1.7:1, но информацию
/// несёт глаз, а он контрастен с любой панелью: 5.3:1 с тёмной, 8.6:1 со светлой.
/// </remarks>
public sealed class TrayIconFactory : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    // Начало цикла — синяя плитка из иконки приложения.
    private static readonly Color FreshTop = Color.FromArgb(0x17, 0x74, 0xC9);
    private static readonly Color FreshBottom = Color.FromArgb(0x1A, 0x6F, 0xC0);

    // Конец цикла — та же светлота, но без цвета.
    private static readonly Color SpentTop = Color.FromArgb(0x6B, 0x72, 0x80);
    private static readonly Color SpentBottom = Color.FromArgb(0x67, 0x6E, 0x7C);

    // Перерыв — крайняя точка за серым: плитка гаснет, глаз загорается.
    private static readonly Color BreakTop = Color.FromArgb(0x3E, 0x44, 0x51);
    private static readonly Color BreakBottom = Color.FromArgb(0x3A, 0x40, 0x4C);

    private static readonly Color CalmEye = Color.White;
    private static readonly Color AlertEye = Color.FromArgb(0xFF, 0x5A, 0x4F);

    // Доля перерыва, за которую серый догорает до сигнала «пора» — дальше держим предел.
    private const double BreakFadeInFraction = 0.2;

    // Доли от стороны плитки — совпадают с Assets/generate-icon.py.
    private const float CornerRatio = 0.22f;
    private const float LensRadiusRatio = 0.50f;
    private const float LensOffsetRatio = 0.335f;
    private const float StrokeRatio = 0.085f;
    private const float PupilRadiusRatio = 0.105f;

    private readonly int _size;
    private Icon? _current;
    private string _currentKey = "";

    public TrayIconFactory(int size = 32) => _size = size;

    /// <summary>
    /// Значок для текущего состояния. Одинаковые состояния отдают тот же экземпляр,
    /// поэтому метод можно звать хоть каждый тик таймера.
    /// </summary>
    public Icon Get(SchedulerState state, double progress)
    {
        // Прогресс округляем до 32 ступеней: переход цвета глаз не различит,
        // а перерисовок становится в разы меньше.
        var step = (int)Math.Round(Math.Clamp(progress, 0, 1) * 32);
        var key = $"{state}|{step}";
        if (_current is not null && key == _currentKey) return _current;

        // Вне цикла — работы нет, поэтому и синевы нет: плитка сразу серая.
        var fade = state switch
        {
            SchedulerState.Break or SchedulerState.Paused or SchedulerState.OffHours => 1.0,
            _ => step / 32.0,
        };

        var icon = state is SchedulerState.Break
            ? RenderBreak(step / 32.0)
            : Render(Lerp(FreshTop, SpentTop, fade), Lerp(FreshBottom, SpentBottom, fade), CalmEye);

        var previous = _current;
        _current = icon;
        _currentKey = key;
        Dispose(previous);

        return icon;
    }

    /// <summary>
    /// Переход в сигнал «пора»: серая плитка с белым глазом (как на исходе рабочей
    /// фазы) плавно догорает до графита с красным глазом за первую пятую перерыва,
    /// дальше держится предельным — резкий скачок цвета в начале перерыва иначе
    /// выглядел бы как сбой, а не как переход.
    /// </summary>
    private Icon RenderBreak(double progress)
    {
        var fade = Math.Clamp(progress / BreakFadeInFraction, 0, 1);
        return Render(
            Lerp(SpentTop, BreakTop, fade),
            Lerp(SpentBottom, BreakBottom, fade),
            Lerp(CalmEye, AlertEye, fade));
    }

    private static Color Lerp(Color from, Color to, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (byte)Math.Round(from.R + (to.R - from.R) * t),
            (byte)Math.Round(from.G + (to.G - from.G) * t),
            (byte)Math.Round(from.B + (to.B - from.B) * t));
    }

    private Icon Render(Color top, Color bottom, Color eye)
    {
        using var bitmap = new Bitmap(_size, _size);
        using var g = Graphics.FromImage(bitmap);

        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Небольшой отступ, чтобы сглаженный край плитки не упирался в границу значка.
        var inset = _size * 0.02f;
        var tile = new RectangleF(inset, inset, _size - inset * 2, _size - inset * 2);

        DrawTile(g, tile, top, bottom);
        DrawEye(g, tile, eye);

        return ToIcon(bitmap);
    }

    private static void DrawTile(Graphics g, RectangleF tile, Color top, Color bottom)
    {
        var radius = tile.Width * CornerRatio;

        using var path = RoundedRect(tile, radius);
        using var brush = new LinearGradientBrush(tile, top, bottom, LinearGradientMode.Vertical)
        {
            // Без отражения градиент на границе прямоугольника даёт полосу артефактов.
            WrapMode = WrapMode.TileFlipXY,
        };

        g.FillPath(brush, path);
    }

    /// <summary>Глаз-«линза»: две зеркальные дуги и зрачок.</summary>
    private static void DrawEye(Graphics g, RectangleF tile, Color color)
    {
        var side = tile.Width;
        var cx = tile.Left + side / 2;
        var cy = tile.Top + side / 2;

        var radius = side * LensRadiusRatio;
        var offset = side * LensOffsetRatio;

        // Точки пересечения окружностей лежат на горизонтали через центр.
        var halfWidth = (float)Math.Sqrt(radius * radius - offset * offset);
        var angle = (float)(Math.Atan2(offset, halfWidth) * 180 / Math.PI);
        var sweep = 180 - angle * 2;

        using var pen = new Pen(color, side * StrokeRatio)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };

        // Верхнее веко — дуга окружности, смещённой вниз.
        var fromBelow = new RectangleF(cx - radius, cy + offset - radius, radius * 2, radius * 2);
        g.DrawArc(pen, fromBelow, angle - 180, sweep);

        // Нижнее веко — зеркальная дуга окружности, смещённой вверх.
        var fromAbove = new RectangleF(cx - radius, cy - offset - radius, radius * 2, radius * 2);
        g.DrawArc(pen, fromAbove, angle, sweep);

        var pupil = side * PupilRadiusRatio * 2;
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, cx - pupil / 2, cy - pupil / 2, pupil, pupil);
    }

    private static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();

        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }

    private static Icon ToIcon(Bitmap bitmap)
    {
        var handle = bitmap.GetHicon();
        try
        {
            // Клонируем, чтобы владеть управляемой копией и сразу отпустить дескриптор GDI.
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static void Dispose(Icon? icon) => icon?.Dispose();

    public void Dispose()
    {
        Dispose(_current);
        _current = null;
    }
}
