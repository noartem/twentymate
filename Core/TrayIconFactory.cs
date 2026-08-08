using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace TwentyMate.Core;

/// <summary>
/// Рисует значок трея: та же плитка, что и в иконке приложения — скруглённый
/// квадрат с белым глазом. Чем ближе перерыв, тем сильнее квадрат теряет синеву
/// и уходит в серый; после перерыва цикл начинается заново.
/// </summary>
/// <remarks>
/// Палитра проверена по WCAG 2.1. Контраст белого глаза с плиткой держится
/// в диапазоне 4.8:1 — 5.3:1 на всём переходе, то есть проходит даже строгий
/// порог 4.5:1 для текста. Контраст самой плитки с тёмной панелью задач —
/// 3.1:1 — 3.4:1, со светлой — 4.3:1 — 4.8:1, при пороге 3:1 для графики.
/// Переход почти не меняет светлоту, поэтому контраст не проседает в середине.
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

        var icon = Render(Lerp(FreshTop, SpentTop, fade), Lerp(FreshBottom, SpentBottom, fade));

        var previous = _current;
        _current = icon;
        _currentKey = key;
        Dispose(previous);

        return icon;
    }

    private static Color Lerp(Color from, Color to, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (byte)Math.Round(from.R + (to.R - from.R) * t),
            (byte)Math.Round(from.G + (to.G - from.G) * t),
            (byte)Math.Round(from.B + (to.B - from.B) * t));
    }

    private Icon Render(Color top, Color bottom)
    {
        using var bitmap = new Bitmap(_size, _size);
        using var g = Graphics.FromImage(bitmap);

        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Небольшой отступ, чтобы сглаженный край плитки не упирался в границу значка.
        var inset = _size * 0.02f;
        var tile = new RectangleF(inset, inset, _size - inset * 2, _size - inset * 2);

        DrawTile(g, tile, top, bottom);
        DrawEye(g, tile);

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

    /// <summary>Глаз-«линза»: две зеркальные дуги и зрачок, всегда белые.</summary>
    private static void DrawEye(Graphics g, RectangleF tile)
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

        using var pen = new Pen(Color.White, side * StrokeRatio)
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
        g.FillEllipse(Brushes.White, cx - pupil / 2, cy - pupil / 2, pupil, pupil);
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
