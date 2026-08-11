using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using TwentyMate.Platform;

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
    // Start of the cycle — the blue tile from the app icon.
    private static readonly Color FreshTop = Color.FromRgb(0x17, 0x74, 0xC9);
    private static readonly Color FreshBottom = Color.FromRgb(0x1A, 0x6F, 0xC0);

    // End of the cycle — the same lightness, but no color.
    private static readonly Color SpentTop = Color.FromRgb(0x6B, 0x72, 0x80);
    private static readonly Color SpentBottom = Color.FromRgb(0x67, 0x6E, 0x7C);

    // Break — the far point past gray: the tile dims, the eye lights up.
    private static readonly Color BreakTop = Color.FromRgb(0x3E, 0x44, 0x51);
    private static readonly Color BreakBottom = Color.FromRgb(0x3A, 0x40, 0x4C);

    private static readonly Color CalmEye = Colors.White;
    private static readonly Color AlertEye = Color.FromRgb(0xFF, 0x5A, 0x4F);

    // Fraction of the break over which gray burns down to the "time's up" signal — held after that.
    private const double BreakFadeInFraction = 0.2;

    // Fractions of the tile's side — match Assets/generate-icon.py.
    private const double CornerRatio = 0.22;
    private const double LensRadiusRatio = 0.50;
    private const double LensOffsetRatio = 0.335;
    private const double StrokeRatio = 0.085;
    private const double PupilRadiusRatio = 0.105;

    private readonly int _size;
    private IntPtr _current;
    private string _currentKey = "";

    public TrayIconFactory(int size = 32) => _size = size;

    /// <summary>
    /// The HICON for the current state. Identical states return the same handle,
    /// so this method can be called on every timer tick.
    /// </summary>
    public IntPtr Get(SchedulerState state, double progress)
    {
        // Round progress to 32 steps: an eye can't tell the color transition apart,
        // and it cuts the number of redraws way down.
        var step = (int)Math.Round(Math.Clamp(progress, 0, 1) * 32);
        var key = $"{state}|{step}";
        if (_current != IntPtr.Zero && key == _currentKey) return _current;

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
        if (previous != IntPtr.Zero) HIcon.Destroy(previous);

        return icon;
    }

    /// <summary>
    /// Transition into the "time's up" signal: the gray tile with a white eye (as at
    /// the end of the working phase) smoothly burns down to graphite with a red eye
    /// over the break's first fifth, then holds at that limit — a sudden color jump
    /// at the start of the break would otherwise read as a glitch rather than a transition.
    /// </summary>
    private IntPtr RenderBreak(double progress)
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
        return Color.FromRgb(
            (byte)Math.Round(from.R + (to.R - from.R) * t),
            (byte)Math.Round(from.G + (to.G - from.G) * t),
            (byte)Math.Round(from.B + (to.B - from.B) * t));
    }

    private IntPtr Render(Color top, Color bottom, Color eye) => HIcon.Create(RenderPixels(top, bottom, eye), _size, _size);

    /// <summary>Renders the tile to a top-down 32bpp premultiplied-BGRA buffer, as CreateIconIndirect requires.</summary>
    private byte[] RenderPixels(Color top, Color bottom, Color eye)
    {
        using var target = new RenderTargetBitmap(new PixelSize(_size, _size), new Vector(96, 96));
        using (var context = target.CreateDrawingContext())
        {
            // A small inset so the tile's anti-aliased edge doesn't butt against the icon's border.
            var inset = _size * 0.02;
            var tile = new Rect(inset, inset, _size - inset * 2, _size - inset * 2);

            DrawTile(context, tile, top, bottom);
            DrawEye(context, tile, eye);
        }

        using var writeable = new WriteableBitmap(
            new PixelSize(_size, _size), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var frame = writeable.Lock();
        target.CopyPixels(frame);

        var rowBytes = _size * 4;
        var bytes = new byte[rowBytes * _size];
        if (frame.RowBytes == rowBytes)
        {
            Marshal.Copy(frame.Address, bytes, 0, bytes.Length);
        }
        else
        {
            for (var y = 0; y < _size; y++)
                Marshal.Copy(frame.Address + y * frame.RowBytes, bytes, y * rowBytes, rowBytes);
        }

        return bytes;
    }

    private static void DrawTile(DrawingContext context, Rect tile, Color top, Color bottom)
    {
        var radius = tile.Width * CornerRatio;
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            // Without reflection, the gradient produces an artifact band at the rectangle's edge.
            SpreadMethod = GradientSpreadMethod.Reflect,
            GradientStops = { new GradientStop { Offset = 0, Color = top }, new GradientStop { Offset = 1, Color = bottom } },
        };

        context.DrawRectangle(brush, null, tile, radius, radius);
    }

    /// <summary>The eye "lens": two mirrored arcs and a pupil.</summary>
    private static void DrawEye(DrawingContext context, Rect tile, Color color)
    {
        var side = tile.Width;
        var cx = tile.Left + side / 2;
        var cy = tile.Top + side / 2;

        var radius = side * LensRadiusRatio;
        var offset = side * LensOffsetRatio;

        // The circles' intersection points lie on the horizontal through the center.
        var halfWidth = Math.Sqrt(radius * radius - offset * offset);
        var angle = Math.Atan2(offset, halfWidth) * 180 / Math.PI;
        var sweep = 180 - angle * 2;

        var pen = new Pen(new SolidColorBrush(color), side * StrokeRatio) { LineCap = PenLineCap.Round };

        // Upper lid — an arc of the circle offset downward; lower lid — the mirrored arc of
        // the circle offset upward. GDI+'s (center, radius, start/sweep-degrees) arc convention
        // maps directly onto Avalonia/WPF's (start point, end point, isLargeArc) one: both
        // measure angles clockwise from the +X axis in a Y-down screen space.
        var (start1, end1, large1) = ArcEndpoints(new Point(cx, cy + offset), radius, angle - 180, sweep);
        var (start2, end2, large2) = ArcEndpoints(new Point(cx, cy - offset), radius, angle, sweep);

        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(start1, isFilled: false);
            geometryContext.ArcTo(end1, new Size(radius, radius), 0, large1, SweepDirection.Clockwise);
            geometryContext.EndFigure(false);

            geometryContext.BeginFigure(start2, isFilled: false);
            geometryContext.ArcTo(end2, new Size(radius, radius), 0, large2, SweepDirection.Clockwise);
            geometryContext.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);

        var pupilRadius = side * PupilRadiusRatio;
        context.DrawEllipse(new SolidColorBrush(color), null, new Point(cx, cy), pupilRadius, pupilRadius);
    }

    private static (Point Start, Point End, bool IsLargeArc) ArcEndpoints(
        Point center, double radius, double startDegrees, double sweepDegrees)
    {
        var startRadians = startDegrees * Math.PI / 180;
        var endRadians = (startDegrees + sweepDegrees) * Math.PI / 180;

        var start = new Point(center.X + radius * Math.Cos(startRadians), center.Y + radius * Math.Sin(startRadians));
        var end = new Point(center.X + radius * Math.Cos(endRadians), center.Y + radius * Math.Sin(endRadians));

        return (start, end, Math.Abs(sweepDegrees) > 180);
    }

    public void Dispose()
    {
        if (_current != IntPtr.Zero) HIcon.Destroy(_current);
        _current = IntPtr.Zero;
    }
}
