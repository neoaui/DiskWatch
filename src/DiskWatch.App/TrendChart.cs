using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DiskWatch.Core;

namespace DiskWatch.App;

public sealed class TrendChart : FrameworkElement
{
    private List<VolumeSample> samples = new();
    private string metric = "剩余空间";
    private double gapSeconds = 180;
    private readonly Brush[] colors = { new SolidColorBrush(Color.FromRgb(8,127,140)), new SolidColorBrush(Color.FromRgb(198,135,66)), new SolidColorBrush(Color.FromRgb(85,132,174)), new SolidColorBrush(Color.FromRgb(140,117,170)) };
    private DateTimeOffset start, end;
    public void SetData(List<VolumeSample> points, string value, int interval)
    { samples = points; metric = value; gapSeconds=Math.Max(180, interval*3); InvalidateVisual(); }
    private double Value(VolumeSample s, VolumeSample first) => (metric=="剩余空间" ? s.FreeBytes : metric=="已用空间" ? s.UsedBytes : s.UsedBytes-first.UsedBytes)/1073741824d;
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Brushes.White,null,new Rect(RenderSize));
        void Text(string value,double x,double y,Brush? brush=null) => dc.DrawText(new FormattedText(value,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),12,brush??Brushes.DimGray,VisualTreeHelper.GetDpi(this).PixelsPerDip),new Point(x,y));
        if(samples.Count==0) { Text("此时间范围还没有容量记录。程序运行后会定期记录剩余空间。",30,50); return; }
        double w=ActualWidth-115,h=ActualHeight-115;
        if(w<100||h<80)return;
        start=samples.Min(s=>s.Time); end=samples.Max(s=>s.Time);
        double seconds=Math.Max(60,(end-start).TotalSeconds);
        var groups=samples.GroupBy(s=>s.VolumeId).Select(g=>g.OrderBy(s=>s.Time).ToList()).ToList();
        var all=groups.SelectMany(g=>g.Select(s=>Value(s,g[0]))).ToList();
        double lo=all.Min(),hi=all.Max(),pad=Math.Max(0.1,(hi-lo)*0.08);lo-=pad;hi+=pad;if(metric!="已用净变化")lo=Math.Max(0,lo);
        Point Position(VolumeSample s,VolumeSample first)=>new(80+(s.Time-start).TotalSeconds/seconds*w,45+h-(Value(s,first)-lo)/(hi-lo)*h);
        Text(metric+" / GiB",10,8);
        for(int i=0;i<=4;i++)
        {
            double y=45+h-i*h/4;dc.DrawLine(new Pen(Brushes.LightGray,0.6),new Point(80,y),new Point(80+w,y));Text((lo+(hi-lo)*i/4).ToString("N2"),5,y-8);
            double x=80+i*w/4;Text(start.AddSeconds(seconds*i/4).ToLocalTime().ToString("MM-dd HH:mm"),Math.Max(65,x-35),h+55);
        }
        for(int g=0;g<groups.Count;g++)
        {
            var points=groups[g];var color=colors[g%colors.Length];Text(points[0].Root,200+g*100,8,color);
            // Keep first, extrema and last within each pixel column, preserving peaks.
            var segments=new List<List<VolumeSample>>();var segment=new List<VolumeSample>();
            foreach(var s in points){if(segment.Count>0&&(s.Time-segment[^1].Time).TotalSeconds>gapSeconds){segments.Add(segment);segment=new();}segment.Add(s);}segments.Add(segment);
            foreach(var part in segments)
            {
                var reduced=part.GroupBy(s=>(int)((s.Time-start).TotalSeconds/seconds*w)).SelectMany(b=>new[]{b.First(),b.MinBy(s=>Value(s,points[0]))!,b.MaxBy(s=>Value(s,points[0]))!,b.Last()}.Distinct().OrderBy(s=>s.Time)).ToList();
                var geometry=new StreamGeometry();using(var ctx=geometry.Open()){ctx.BeginFigure(Position(reduced[0],points[0]),false,false);foreach(var s in reduced.Skip(1))ctx.LineTo(Position(s,points[0]),true,false);}geometry.Freeze();dc.DrawGeometry(null,new Pen(color,2),geometry);
                if(reduced.Count==1)dc.DrawEllipse(color,null,Position(reduced[0],points[0]),3,3);
            }
        }
        Text("横轴为本地时间。鼠标悬停可查看数值，监测中断处曲线会断开。净变化以所选范围的第一个记录为基准。",80,h+83);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);if(samples.Count==0||ActualWidth<=115)return;
        var fraction=Math.Clamp((e.GetPosition(this).X-80)/(ActualWidth-115),0,1);var time=start.AddSeconds(Math.Max(60,(end-start).TotalSeconds)*fraction);
        ToolTip=string.Join("\n",samples.GroupBy(s=>s.VolumeId).Select(g=>{var ordered=g.OrderBy(s=>s.Time).ToList();var nearest=ordered.MinBy(s=>Math.Abs((s.Time-time).TotalSeconds))!;return $"{nearest.Root} {nearest.Time.ToLocalTime():MM-dd HH:mm:ss}  {Value(nearest,ordered[0]):N3} GiB";}));
    }
}
