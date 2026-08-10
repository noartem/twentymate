using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace TwentyMate.Core;

/// <summary>
/// Draws the tray icon: the same tile as the app icon — a rounded square
/// with an eye. The closer the break gets, the more the square loses its
/// blue and drifts toward gray; during the break itself the tile burns down
/// to graphite and the eye lights up red. The cycle starts over after the break.
/// </summary>
/// <remarks>
/// The palette is checked against WCAG 2.1. On the "blue → gray" transition,
/// the white eye's contrast against the tile stays in the 4.8:1 — 5.3:1 range,
/// clearing even the strict 4.5:1 threshold for text; the tile's own contrast
/// against a dark taskbar is 3.1:1 — 3.4:1, against a light one 4.3:1 — 4.8:1,
/// against the 3:1 threshold for graphics. The transition barely changes
/// lightness, so contrast doesn't dip in the middle.
///
/// During the break, lightness changes deliberately: a red eye and a gray tile
/// are equally light and can't be told apart, so the tile drifts to graphite.
/// The red eye's contrast against it is 3.2:1, clearing the graphics threshold.
/// The graphite tile itself only gives 1.7:1 against a dark taskbar, but the
/// eye carries the information, and it's contrasty against any taskbar:
/// 5.3:1 against dark, 8.6:1 against light.
/// </remarks>
public sealed class TrayIconFactory : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    // Start of the cycle — the blue tile from the app icon.
    private static readonly Color FreshTop = Color.FromArgb(0x17, 0x74, 0xC9);
    private static readonly Color FreshBottom = Color.FromArgb(0x1A, 0x6F, 0xC0);

    // End of the cycle — the same lightness, but no color.
    private static readonly Color SpentTop = Color.FromArgb(0x6B, 0x72, 0x80);
    private static readonly Color SpentBottom = Color.FromArgb(0x67, 0x6E, 0x7C);

    // Break — the far point past gray: the tile dims, the eye lights up.
    private static readonly Color BreakTop = Color.FromArgb(0x3E, 0x44, 0x51);
    private static readonly Color BreakBottom = Color.FromArgb(0x3A, 0x40, 0x4C);

    private static readonly Color CalmEye = Color.White;
    private static readonly Color AlertEye = Color.FromArgb(0xFF, 0x5A, 0x4F);

    // Fraction of the break over which gray burns down to the "time's up" signal — held after that.
    private const double BreakFadeInFraction = 0.2;

    // Fractions of the tile's side — match Assets/generate-icon.py.
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
    /// The icon for the current state. Identical states return the same instance,
    /// so this method can be called on every timer tick.
    /// </summary>
    public Icon Get(SchedulerState state, double progress)
    {
        // Round progress to 32 steps: an eye can't tell the color transition apart,
        // and it cuts the number of redraws way down.
        var step = (int)Math.Round(Math.Clamp(progress, 0, 1) * 32);
        var key = $"{state}|{step}";
        if (_current is not null && key == _currentKey) return _current;

        // Outside the cycle — there's no work happening, so no blue: the tile is gray right away.
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
    /// Transition into the "time's up" signal: the gray tile with a white eye (as at
    /// the end of the working phase) smoothly burns down to graphite with a red eye
    /// over the break's first fifth, then holds at that limit — a sudden color jump
    /// at the start of the break would otherwise read as a glitch rather than a transition.
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

        // A small inset so the tile's anti-aliased edge doesn't butt against the icon's border.
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
            // Without reflection, the gradient produces an artifact band at the rectangle's edge.
            WrapMode = WrapMode.TileFlipXY,
        };

        g.FillPath(brush, path);
    }

    /// <summary>The eye "lens": two mirrored arcs and a pupil.</summary>
    private static void DrawEye(Graphics g, RectangleF tile, Color color)
    {
        var side = tile.Width;
        var cx = tile.Left + side / 2;
        var cy = tile.Top + side / 2;

        var radius = side * LensRadiusRatio;
        var offset = side * LensOffsetRatio;

        // The circles' intersection points lie on the horizontal through the center.
        var halfWidth = (float)Math.Sqrt(radius * radius - offset * offset);
        var angle = (float)(Math.Atan2(offset, halfWidth) * 180 / Math.PI);
        var sweep = 180 - angle * 2;

        using var pen = new Pen(color, side * StrokeRatio)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };

        // Upper lid — an arc of the circle offset downward.
        var fromBelow = new RectangleF(cx - radius, cy + offset - radius, radius * 2, radius * 2);
        g.DrawArc(pen, fromBelow, angle - 180, sweep);

        // Lower lid — the mirrored arc of the circle offset upward.
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
            // Clone it so we own a managed copy and can release the GDI handle right away.
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
