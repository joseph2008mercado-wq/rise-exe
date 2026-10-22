using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Rise.App;

public sealed class SkinFrame : Decorator
{
    public static readonly DependencyProperty PaddingProperty = DependencyProperty.Register(nameof(Padding), typeof(Thickness), typeof(SkinFrame), new FrameworkPropertyMetadata(new Thickness(12), FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty FaceProperty = DependencyProperty.Register(nameof(Face), typeof(Brush), typeof(SkinFrame), new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(9, 38, 71)), FrameworkPropertyMetadataOptions.AffectsRender));
    public Thickness Padding { get => (Thickness)GetValue(PaddingProperty); set => SetValue(PaddingProperty, value); }
    public Brush Face { get => (Brush)GetValue(FaceProperty); set => SetValue(FaceProperty, value); }
    protected override Size MeasureOverride(Size constraint)
    {
        var p = Padding; Child?.Measure(new Size(Math.Max(0, constraint.Width - p.Left - p.Right), Math.Max(0, constraint.Height - p.Top - p.Bottom)));
        return new Size((Child?.DesiredSize.Width ?? 0) + p.Left + p.Right, (Child?.DesiredSize.Height ?? 0) + p.Top + p.Bottom);
    }
    protected override Size ArrangeOverride(Size size)
    {
        var p = Padding; Child?.Arrange(new Rect(p.Left, p.Top, Math.Max(0, size.Width - p.Left - p.Right), Math.Max(0, size.Height - p.Top - p.Bottom))); return size;
    }
    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 20 || h < 20) return;
        var metal = new LinearGradientBrush();
        foreach (var (offset, color) in new[] { (0d, "#F8FDFF"), (.07, "#7791A8"), (.18, "#E5EEF4"), (.40, "#65788C"), (.48, "#26384C"), (.52, "#D9E5EE"), (.68, "#718AA0"), (.91, "#203348"), (1d, "#C2D7E5") }) metal.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(color), offset));
        var outline = new Pen(Brushes.Black, 1.5);
        dc.DrawRoundedRectangle(metal, outline, new Rect(1, 1, w - 2, h - 2), 5, 5);
        dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(222, 235, 247)), 1), new Rect(3, 3, w - 6, h - 6), 3, 3);
        dc.DrawRoundedRectangle(Brushes.Black, new Pen(new SolidColorBrush(Color.FromRgb(52, 72, 94)), 2), new Rect(7, 7, w - 14, h - 14), 5, 5);
        dc.DrawRoundedRectangle(Face, new Pen(new SolidColorBrush(Color.FromRgb(136, 168, 199)), 1), new Rect(9, 9, w - 18, h - 18), 3, 3);
        // Machined rim grooves and screw heads keep the frame physical at every size.
        var groove = new Pen(new SolidColorBrush(Color.FromRgb(27, 43, 59)), .8);
        for (double x = 20; x < w - 20; x += 5) { dc.DrawLine(groove, new Point(x, 2), new Point(x - 2, 6)); dc.DrawLine(groove, new Point(x, h - 6), new Point(x + 2, h - 2)); }
        foreach (var point in new[] { new Point(5, 5), new Point(w - 5, 5), new Point(5, h - 5), new Point(w - 5, h - 5) })
        {
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(165, 186, 203)), outline, point, 2.6, 2.6);
            dc.DrawLine(groove, new Point(point.X - 1.5, point.Y + 1.5), new Point(point.X + 1.5, point.Y - 1.5));
        }
    }
}

public sealed class HardwareDial : FrameworkElement
{
    protected override void OnRender(DrawingContext dc)
    {
        double radius = Math.Min(ActualWidth, ActualHeight) / 2 - 2;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var black = new Pen(Brushes.Black, 1.2);
        for (int ring = 0; ring < 5; ring++)
        {
            var brush = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(ring % 2 == 0 ? "#D9E9F5" : "#172A3E"), (Color)ColorConverter.ConvertFromString(ring % 2 == 0 ? "#40566C" : "#9CB7CC"), 90);
            dc.DrawEllipse(brush, black, center, radius - ring * 3, radius - ring * 3);
        }
        double core = Math.Max(3, radius - 15);
        var glass = new RadialGradientBrush(Color.FromRgb(83, 179, 251), Color.FromRgb(0, 23, 58));
        dc.DrawEllipse(glass, black, center, core, core);
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromRgb(135, 215, 255)), 1), center, core - 4, core - 4);
        for (int i = 0; i < 12; i++)
        {
            double angle = i * Math.PI / 6;
            dc.DrawLine(new Pen(Brushes.Black, 1), new Point(center.X + Math.Cos(angle) * (radius - 1), center.Y + Math.Sin(angle) * (radius - 1)), new Point(center.X + Math.Cos(angle) * (radius - 5), center.Y + Math.Sin(angle) * (radius - 5)));
        }
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(171, 237, 255)), 3), center, new Point(center.X + core * .48, center.Y - core * .48));
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(199, 219, 232)), black, center, 3, 3);
    }
}

public sealed class OrbitalMark : FrameworkElement
{
    protected override void OnRender(DrawingContext dc)
    {
        double cx = ActualWidth * .53, cy = ActualHeight * .43;
        var silver = new RadialGradientBrush { Center = new Point(.3, .2), GradientOrigin = new Point(.25, .18), RadiusX = .8, RadiusY = .8 };
        silver.GradientStops.Add(new GradientStop(Color.FromRgb(255, 255, 255), 0));
        silver.GradientStops.Add(new GradientStop(Color.FromRgb(178, 176, 207), .25));
        silver.GradientStops.Add(new GradientStop(Color.FromRgb(85, 68, 111), .48));
        silver.GradientStops.Add(new GradientStop(Color.FromRgb(26, 24, 42), .52));
        silver.GradientStops.Add(new GradientStop(Color.FromRgb(182, 170, 205), .9));
        dc.DrawEllipse(silver, new Pen(new SolidColorBrush(Color.FromRgb(169, 150, 194)), 1), new Point(cx, cy), 31, 31);
        dc.PushClip(new EllipseGeometry(new Point(cx, cy), 31, 31));
        for (int i = -26; i < 30; i += 5) dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(70, 32, 20, 45)), 1), new Point(cx - 32, cy + i), new Point(cx + 32, cy + i));
        dc.Pop();
        dc.PushTransform(new RotateTransform(-24, cx, cy));
        var cyan = new SolidColorBrush(Color.FromRgb(144, 239, 236));
        dc.DrawEllipse(null, new Pen(cyan, 1), new Point(cx, cy), 62, 13);
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(110, 215, 147, 255)), 3), new Point(cx, cy), 58, 16);
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(187, 255, 95)), null, new Point(cx + 56, cy - 5), 3, 3);
        dc.Pop();
        var pen = new Pen(cyan, 1);
        dc.DrawLine(pen, new Point(cx - 54, cy - 25), new Point(cx - 44, cy - 25));
        dc.DrawLine(pen, new Point(cx - 49, cy - 30), new Point(cx - 49, cy - 20));
        dc.DrawLine(pen, new Point(cx + 43, cy + 25), new Point(cx + 51, cy + 25));
        dc.DrawLine(pen, new Point(cx + 47, cy + 21), new Point(cx + 47, cy + 29));
    }
}

public sealed class DisplayTexture : FrameworkElement
{
    protected override void OnRender(DrawingContext dc)
    {
        var line = new Pen(new SolidColorBrush(Color.FromArgb(65, 0, 0, 0)), 1);
        for (double y = 0; y < ActualHeight; y += 3) dc.DrawLine(line, new Point(0, y), new Point(ActualWidth, y));
        var dots = new SolidColorBrush(Color.FromArgb(27, 120, 180, 168));
        for (double y = 5; y < ActualHeight; y += 12) for (double x = 5; x < ActualWidth; x += 12) dc.DrawRectangle(dots, null, new Rect(x, y, 1, 1));
    }
}

public sealed class DigitalClock : FrameworkElement
{
    public string Time { get; set; } = "00:00:00";
    private static readonly int[] masks = [0x3F, 0x06, 0x5B, 0x4F, 0x66, 0x6D, 0x7D, 0x07, 0x7F, 0x6F];
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double scale = Math.Min(ActualWidth / 354, ActualHeight / 65);
        dc.PushTransform(new TranslateTransform((ActualWidth - 354 * scale) / 2, (ActualHeight - 65 * scale) / 2));
        dc.PushTransform(new ScaleTransform(scale, scale));
        double x = 0;
        Brush on = new SolidColorBrush(Color.FromRgb(139, 226, 255)), off = new SolidColorBrush(Color.FromRgb(16, 53, 85));
        foreach (char c in Time)
        {
            if (c == ':') { dc.DrawRectangle(on, null, new Rect(x + 3, 19, 5, 5)); dc.DrawRectangle(on, null, new Rect(x + 3, 43, 5, 5)); x += 18; continue; }
            int mask = masks[c - '0'];
            Rect[] segments = [new(x + 7, 0, 33, 5), new(x + 41, 6, 5, 24), new(x + 41, 35, 5, 24), new(x + 7, 60, 33, 5), new(x + 1, 35, 5, 24), new(x + 1, 6, 5, 24), new(x + 7, 30, 33, 5)];
            for (int i = 0; i < 7; i++) dc.DrawRoundedRectangle((mask & (1 << i)) != 0 ? on : off, null, segments[i], 1, 1);
            x += 53;
        }
        dc.Pop(); dc.Pop();
    }
}

public sealed class Spectrum : FrameworkElement
{
    public float[]? Bands { get; set; }
    private readonly double[] levels = new double[28];
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double w = ActualWidth / 28;
        for (int i = 0; i < 28; i++)
        {
            levels[i] = Math.Max(Bands?[i] ?? .015, levels[i] * .78);
            int height = Math.Max(1, (int)(levels[i] * 9));
            for (int j = 0; j < height; j++)
                dc.DrawRectangle(new SolidColorBrush(j > 6 ? Color.FromRgb(255, 113, 198) : j > 3 ? Color.FromRgb(176, 255, 86) : Color.FromRgb(77, 182, 177)), null, new Rect(i * w, Math.Max(0, ActualHeight - (j + 1) * 3), Math.Max(1, w - 3), 2));
        }
    }
}
