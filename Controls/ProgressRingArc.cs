using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace TwentyMate.Controls;

/// <summary>
/// A progress ring with rounded ends: a full-circle track and an arc for the
/// <see cref="Value"/> (0..1), measured clockwise from 12 o'clock.
/// </summary>
public sealed class ProgressRingArc : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<ProgressRingArc, double>(nameof(Value));

    public static readonly StyledProperty<double> ThicknessProperty =
        AvaloniaProperty.Register<ProgressRingArc, double>(nameof(Thickness), 8d);

    public static readonly StyledProperty<IBrush> TrackBrushProperty =
        AvaloniaProperty.Register<ProgressRingArc, IBrush>(nameof(TrackBrush), Brushes.Gray);

    public static readonly StyledProperty<IBrush> ArcBrushProperty =
        AvaloniaProperty.Register<ProgressRingArc, IBrush>(nameof(ArcBrush), Brushes.DeepSkyBlue);

    static ProgressRingArc() =>
        AffectsRender<ProgressRingArc>(ValueProperty, ThicknessProperty, TrackBrushProperty, ArcBrushProperty);

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Thickness
    {
        get => GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public IBrush TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public IBrush ArcBrush
    {
        get => GetValue(ArcBrushProperty);
        set => SetValue(ArcBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;

        var thickness = Math.Min(Thickness, size / 2);
        var radius = (size - thickness) / 2;
        if (radius <= 0) return;

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);

        var trackPen = new Pen(TrackBrush, thickness);
        context.DrawEllipse(null, trackPen, center, radius, radius);

        var value = Math.Clamp(Value, 0, 1);
        if (value <= 0) return;

        var pen = new Pen(ArcBrush, thickness) { LineCap = PenLineCap.Round };

        // Draw a near-full circle as an ellipse: an ArcTo at 360° degenerates to a point.
        if (value >= 0.999)
        {
            context.DrawEllipse(null, pen, center, radius, radius);
            return;
        }

        var angle = value * 360;
        var start = new Point(center.X, center.Y - radius);
        var radians = (angle - 90) * Math.PI / 180;
        var end = new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));

        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(start, isFilled: false);
            geometryContext.ArcTo(end, new Size(radius, radius), 0, angle > 180, SweepDirection.Clockwise);
            geometryContext.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);
    }
}
