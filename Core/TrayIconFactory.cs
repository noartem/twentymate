using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace TwentyMate.Core;

/// <summary>
/// Рисует значок трея: всегда логотип-глаз, меняется только его цвет.
/// Чем ближе перерыв, тем более блёклой становится синева; после перерыва
/// отсчёт начинается заново и цвет снова насыщенный.
/// </summary>
public sealed class TrayIconFactory : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>Начало цикла — насыщенный синий из логотипа.</summary>
    private static readonly Color Fresh = Color.FromArgb(255, 47, 158, 245);

    /// <summary>Конец цикла — та же форма, но обесцвеченная и приглушённая.</summary>
    private static readonly Color Spent = Color.FromArgb(190, 122, 138, 156);

    /// <summary>Пауза и нерабочие часы — нейтральный серый, вне цикла.</summary>
    private static readonly Color Idle = Color.FromArgb(160, 150, 155, 162);

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

        var icon = Render(ColorFor(state, step / 32.0));

        var previous = _current;
        _current = icon;
        _currentKey = key;
        Dispose(previous);

        return icon;
    }

    private static Color ColorFor(SchedulerState state, double progress) => state switch
    {
        // Во время перерыва цикл на дне — значок остаётся блёклым до его конца.
        SchedulerState.Break => Spent,
        SchedulerState.Paused or SchedulerState.OffHours => Idle,
        _ => Lerp(Fresh, Spent, progress),
    };

    private static Color Lerp(Color from, Color to, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (byte)(from.A + (to.A - from.A) * t),
            (byte)(from.R + (to.R - from.R) * t),
            (byte)(from.G + (to.G - from.G) * t),
            (byte)(from.B + (to.B - from.B) * t));
    }

    private Icon Render(Color color)
    {
        using var bitmap = new Bitmap(_size, _size);
        using var g = Graphics.FromImage(bitmap);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        DrawLogo(g, color);

        return ToIcon(bitmap);
    }

    /// <summary>
    /// Тот же контур, что и в иконке приложения: «линза» из пересечения двух
    /// окружностей плюс зрачок в центре.
    /// </summary>
    private void DrawLogo(Graphics g, Color color)
    {
        var cx = _size / 2f;
        var cy = _size / 2f;

        var radius = _size * 0.50f;
        var offset = _size * 0.335f;
        var thickness = _size * 0.10f;

        // Точки пересечения окружностей лежат на горизонтали через центр.
        var halfWidth = (float)Math.Sqrt(radius * radius - offset * offset);
        var angle = (float)(Math.Atan2(offset, halfWidth) * 180 / Math.PI);

        using var pen = new Pen(color, thickness)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };

        var sweep = 180 - angle * 2;

        // Верхнее веко — дуга окружности, смещённой вниз.
        var fromBelow = new RectangleF(cx - radius, cy + offset - radius, radius * 2, radius * 2);
        g.DrawArc(pen, fromBelow, angle - 180, sweep);

        // Нижнее веко — зеркальная дуга окружности, смещённой вверх.
        var fromAbove = new RectangleF(cx - radius, cy - offset - radius, radius * 2, radius * 2);
        g.DrawArc(pen, fromAbove, angle, sweep);

        var pupil = _size * 0.21f;
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, cx - pupil / 2, cy - pupil / 2, pupil, pupil);
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
