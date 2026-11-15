using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DiskWatch.App;

/// <summary>矢量哨塔与水面。确定进度跟随百分比；未知进度循环扫查，不伪造完成比例。</summary>
public sealed class WatchtowerProgress : FrameworkElement
{
    private static readonly DependencyProperty SweepProperty = DependencyProperty.Register(nameof(Sweep), typeof(double), typeof(WatchtowerProgress), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty WaveProperty = DependencyProperty.Register(nameof(Wave), typeof(double), typeof(WatchtowerProgress), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    private double Sweep { get => (double)GetValue(SweepProperty); set => SetValue(SweepProperty, value); }
    private double Wave => (double)GetValue(WaveProperty);
    private bool active, unknown, running;
    private double fraction;
    private static readonly Brush Tower = Solid("#215565"), Roof = Solid("#133E50"), Window = Solid("#FFE6A3");

    public WatchtowerProgress()
    {
        Height = 96; ClipToBounds = true; IsHitTestVisible = false;
        AutomationProperties.SetName(this, "扫描进度");
        Loaded += (_, _) => UpdateClocks();
        Unloaded += (_, _) => StopClocks();
        IsVisibleChanged += (_, _) => UpdateClocks();
    }

    public void SetState(double? progress, bool indeterminate, bool scanning)
    {
        var next = progress is double p && double.IsFinite(p) ? Math.Clamp(p, 0, 1) : 0;
        bool changedMode = unknown != indeterminate || active != scanning;
        bool changedProgress = Math.Abs(next - fraction) > .00001;
        unknown = indeterminate; active = scanning; fraction = next;
        AutomationProperties.SetHelpText(this, unknown ? "正在读取目录，正在估算进度" : $"扫描进度 {fraction:P0}");
        if (changedMode) StopClocks();
        UpdateClocks();
        if (!unknown && (changedProgress || changedMode))
        {
            double from = Sweep;
            BeginAnimation(SweepProperty, null); Sweep = fraction;
            if (running) BeginAnimation(SweepProperty, new DoubleAnimation(from, fraction, TimeSpan.FromMilliseconds(650)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
        }
        InvalidateVisual();
    }

    private void UpdateClocks()
    {
        bool shouldRun = active && IsLoaded && IsVisible && SystemParameters.ClientAreaAnimation;
        if (!shouldRun) { StopClocks(); return; }
        if (running) return;
        running = true;
        var waves = new DoubleAnimation(0, Math.PI * 2, TimeSpan.FromSeconds(4)) { RepeatBehavior = RepeatBehavior.Forever };
        Timeline.SetDesiredFrameRate(waves, 24); BeginAnimation(WaveProperty, waves);
        if (unknown)
        {
            var sweep = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(4.8)) { RepeatBehavior = RepeatBehavior.Forever };
            Timeline.SetDesiredFrameRate(sweep, 24); BeginAnimation(SweepProperty, sweep);
        }
    }

    private void StopClocks()
    {
        BeginAnimation(WaveProperty, null); BeginAnimation(SweepProperty, null);
        running = false; Sweep = unknown ? .35 : fraction;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new WatchtowerPeer(this);
    private sealed class WatchtowerPeer(WatchtowerProgress owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ProgressBar;
        protected override string GetClassNameCore() => nameof(WatchtowerProgress);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double w = ActualWidth, h = ActualHeight;
        if (w < 1 || h < 1) return;
        double scale = Math.Min(1, w / 240);
        dc.PushTransform(new ScaleTransform(scale, h / 96));
        w /= scale;
        dc.DrawRoundedRectangle(Solid("#E7F4F5"), null, new Rect(0, 0, w, 96), 7, 7);
        double left = 12, end = Math.Max(left + 1, w - 12), spot = left + (end - left) * Math.Clamp(Sweep, 0, 1);
        double center = w / 2;
        // 透视水面与较深的前立面：进度槽位于透明水层后方。
        var depth = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#397F96"), (Color)ColorConverter.ConvertFromString("#163F61"), new Point(0, 0), new Point(0, 1));
        dc.DrawRoundedRectangle(depth, null, new Rect(0, 60, w, 34), 7, 7);
        double trackLeft = 18, trackWidth = Math.Max(1, w - 36);
        dc.DrawRoundedRectangle(Solid("#143B52"), new Pen(Solid("#68B5C0"), .7), new Rect(trackLeft, 79, trackWidth, 8), 4, 4);
        if (!unknown && fraction > 0)
        {
            double fill = trackWidth * fraction;
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(48, 119, 255, 224)), null, new Rect(trackLeft, 76, fill, 14), 6, 6);
            var submerged = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#B7FCE8"), (Color)ColorConverter.ConvertFromString("#42B8BD"), new Point(0, 0), new Point(0, 1));
            dc.DrawRoundedRectangle(submerged, null, new Rect(trackLeft, 80, fill, 6), 3, 3);
        }
        // 覆盖水色和细小折射纹，而非把进度条贴在水面边缘。
        dc.DrawRoundedRectangle(new LinearGradientBrush(Color.FromArgb(20, 78, 186, 190), Color.FromArgb(58, 30, 99, 143), new Point(0, 0), new Point(0, 1)), null, new Rect(0, 63, w, 31), 7, 7);
        for (int row = 0; row < 2; row++)
        {
            var ripple = new StreamGeometry();
            using (var c = ripple.Open())
            {
                c.BeginFigure(new Point(5, 71 + row * 18), false, false);
                for (double x = 5; x <= w - 5; x += 10) c.LineTo(new Point(x, 71 + row * 18 + Math.Sin(x / 42 + Wave + row) * 1.4), true, false);
            }
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(65, 159, 234, 225)), .7), ripple);
        }
        var surface = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#97D6D5"), (Color)ColorConverter.ConvertFromString("#3993A3"), new Point(0, 0), new Point(0, 1));
        var plane = Polygon(new Point(22, 44), new Point(w - 22, 44), new Point(w, 64), new Point(0, 64));
        dc.DrawGeometry(surface, null, plane);
        dc.PushClip(plane);
        for (int row = 0; row < 3; row++)
        {
            var wave = new StreamGeometry();
            using (var c = wave.Open())
            {
                c.BeginFigure(new Point(0, 48 + row * 6), false, false);
                for (double x = 0; x <= w + 8; x += 8) c.LineTo(new Point(x, 48 + row * 6 + Math.Sin(x / 30 + Wave + row * 1.8) * .8), true, false);
            }
            dc.DrawGeometry(null, new Pen(Solid("#BCE8E4"), .65), wave);
        }
        dc.Pop();
        dc.DrawLine(new Pen(Solid("#BBEFE8"), 1.2), new Point(5, 64), new Point(w - 5, 64));
        var beam = new LinearGradientBrush(Color.FromArgb(125, 234, 171, 53), Color.FromArgb(220, 255, 214, 115), new Point(0, 0), new Point(0, 1));
        dc.DrawGeometry(beam, null, Polygon(new Point(center, 20), new Point(Math.Max(0, spot - 40), 58), new Point(Math.Min(w, spot + 40), 58)));
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(225, 255, 227, 155)), null, new Point(spot, 58), 36, 3.5);
        // 水下的微弱灯影与塔影提供前后层次。
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(45, 255, 229, 156)), null, Polygon(new Point(spot - 18, 65), new Point(spot + 18, 65), new Point(spot + 8, 76), new Point(spot - 8, 76)));
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(38, 10, 37, 53)), null, new Point(center, 70), 22, 5);
        // 居中哨塔缩短最远照射距离；光束仍按扫描进度从左扫到右。
        dc.PushTransform(new TranslateTransform(center - 44, 0));
        dc.DrawGeometry(Tower, null, Polygon(new Point(35, 29), new Point(53, 29), new Point(60, 59), new Point(28, 59)));
        dc.DrawRoundedRectangle(Roof, null, new Rect(29, 13, 30, 18), 2, 2);
        dc.DrawRectangle(Window, null, new Rect(34, 17, 20, 9));
        dc.DrawLine(new Pen(Tower, 2), new Point(44, 17), new Point(44, 26));
        dc.DrawGeometry(Roof, null, Polygon(new Point(25, 13), new Point(44, 3), new Point(63, 13)));
        dc.DrawLine(new Pen(Tower, 2), new Point(24, 33), new Point(64, 33));
        dc.DrawLine(new Pen(Tower, 1.5), new Point(26, 28), new Point(26, 34));
        dc.DrawLine(new Pen(Tower, 1.5), new Point(62, 28), new Point(62, 34));
        dc.DrawRoundedRectangle(Roof, null, new Rect(20, 58, 48, 5), 2, 2);
        dc.DrawRectangle(Window, null, new Rect(41, 39, 6, 9));
        dc.Pop();
        dc.Pop();
    }

    private static Brush Solid(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
    private static StreamGeometry Polygon(params Point[] points)
    {
        var g = new StreamGeometry(); using (var c = g.Open()) { c.BeginFigure(points[0], true, true); for (int i = 1; i < points.Length; i++) c.LineTo(points[i], true, false); } g.Freeze(); return g;
    }
}
