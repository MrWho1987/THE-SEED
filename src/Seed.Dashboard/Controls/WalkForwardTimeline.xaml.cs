using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Seed.Dashboard.Controls;

public record WalkForwardEvent(int Generation, string Status, float Fitness);

public partial class WalkForwardTimeline : UserControl
{
    public static readonly DependencyProperty EventsProperty = DependencyProperty.Register(
        nameof(Events),
        typeof(ObservableCollection<WalkForwardEvent>),
        typeof(WalkForwardTimeline),
        new PropertyMetadata(null, OnEventsChanged));

    public static readonly DependencyProperty CurrentGenerationProperty = DependencyProperty.Register(
        nameof(CurrentGeneration),
        typeof(int),
        typeof(WalkForwardTimeline),
        new PropertyMetadata(0, OnRedraw));

    public static readonly DependencyProperty TotalGenerationsProperty = DependencyProperty.Register(
        nameof(TotalGenerations),
        typeof(int),
        typeof(WalkForwardTimeline),
        new PropertyMetadata(1, OnRedraw));

    private readonly TimelineVisualHost _host = new();
    public WalkForwardTimeline()
    {
        InitializeComponent();
        Loaded += (_, _) => AttachHost();
        SizeChanged += (_, _) => Redraw();
    }

    public ObservableCollection<WalkForwardEvent>? Events
    {
        get => (ObservableCollection<WalkForwardEvent>?)GetValue(EventsProperty);
        set => SetValue(EventsProperty, value);
    }

    public int CurrentGeneration
    {
        get => (int)GetValue(CurrentGenerationProperty);
        set => SetValue(CurrentGenerationProperty, value);
    }

    public int TotalGenerations
    {
        get => (int)GetValue(TotalGenerationsProperty);
        set => SetValue(TotalGenerationsProperty, value);
    }

    private static void OnEventsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not WalkForwardTimeline t)
            return;
        if (e.OldValue is ObservableCollection<WalkForwardEvent> oldOc)
            oldOc.CollectionChanged -= t.OnCollectionChanged;
        if (e.NewValue is ObservableCollection<WalkForwardEvent> newOc)
            newOc.CollectionChanged += t.OnCollectionChanged;
        t.Redraw();
    }

    private static void OnRedraw(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WalkForwardTimeline t)
            t.Redraw();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Redraw();

    private void AttachHost()
    {
        if (TimelineCanvas.Children.Contains(_host))
            return;
        TimelineCanvas.Children.Add(_host);
        Canvas.SetLeft(_host, 0);
        Canvas.SetTop(_host, 0);
        _host.Width = TimelineCanvas.ActualWidth;
        _host.Height = TimelineCanvas.ActualHeight;
        Redraw();
    }

    private void Redraw()
    {
        _host.Width = Math.Max(TimelineCanvas.ActualWidth, 1);
        _host.Height = Math.Max(TimelineCanvas.ActualHeight, 72);

        var events = Events;
        var total = Math.Max(TotalGenerations, 1);
        var current = Math.Clamp(CurrentGeneration, 0, total);

        var bgBrush = TryFindResource("SeedBackgroundBrush") as Brush ?? Brushes.Black;
        var elevatedBrush = TryFindResource("SeedSurfaceElevatedBrush") as Brush ?? Brushes.Gray;
        var passBrush = TryFindResource("SeedPrimaryBrush") as Brush ?? Brushes.Lime;
        var failBrush = TryFindResource("SeedWarningBrush") as Brush ?? Brushes.Red;
        var forceBrush = TryFindResource("SeedCautionBrush") as Brush ?? Brushes.Orange;
        var infoBrush = TryFindResource("SeedInfoBrush") as Brush ?? Brushes.CornflowerBlue;
        var textPriBrush = TryFindResource("SeedTextPrimaryBrush") as SolidColorBrush ?? new SolidColorBrush(Colors.White);
        var textDarkBrush = TryFindResource("SeedBackgroundBrush") as Brush ?? Brushes.Black;

        SolidColorBrush trackFill;
        if (TryFindResource("SeedSurfaceBrush") is SolidColorBrush sc)
        {
            trackFill = new SolidColorBrush(sc.Color) { Opacity = 0.45 };
            trackFill.Freeze();
        }
        else
        {
            trackFill = new SolidColorBrush(Colors.DimGray) { Opacity = 0.45 };
            trackFill.Freeze();
        }

        var dv = _host.Visual;
        using (var dc = dv.RenderOpen())
        {
            var w = _host.Width;
            var h = _host.Height;
            dc.DrawRectangle(bgBrush, null, new Rect(0, 0, w, h));

            const double margin = 16;
            var barY = h * 0.55;
            var barH = 10;
            var barW = Math.Max(w - 2 * margin, 0);
            var barRect = new Rect(margin, barY, barW, barH);
            var trackPen = new Pen(elevatedBrush, 1);
            trackPen.Freeze();
            dc.DrawRectangle(trackFill, trackPen, barRect);

            var dpi = VisualTreeHelper.GetDpi(this);
            var px = dpi.PixelsPerDip;

            if (total > 0)
            {
                var curX = margin + current / (double)total * barW;
                var linePen = new Pen(infoBrush, 1.5);
                linePen.Freeze();
                dc.DrawLine(linePen, new Point(curX, barY - 6), new Point(curX, barY + barH + 6));
            }

            if (events != null)
            {
                foreach (var ev in events)
                {
                    var gx = Math.Clamp(ev.Generation, 0, total);
                    var cx = margin + gx / (double)total * barW;
                    var cy = barY + barH / 2;
                    var (sym, fillBrush) = ev.Status.ToUpperInvariant() switch
                    {
                        "PASSED" => ("✓", passBrush),
                        "FAILED" => ("✕", failBrush),
                        "FORCE" => ("→", forceBrush),
                        _ => ("?", textPriBrush)
                    };

                    var outline = new Pen(elevatedBrush, 1);
                    outline.Freeze();
                    dc.DrawEllipse(fillBrush, outline, new Point(cx, cy), 9, 9);

                    var ft = new FormattedText(
                        sym,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface(new FontFamily("Segoe UI Symbol"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                        11,
                        textDarkBrush,
                        px);
                    dc.DrawText(ft, new Point(cx - ft.Width / 2, cy - ft.Height / 2));
                }
            }
        }
    }

    private sealed class TimelineVisualHost : FrameworkElement
    {
        private readonly VisualCollection _children;
        internal DrawingVisual Visual { get; } = new();

        public TimelineVisualHost()
        {
            _children = new VisualCollection(this);
            _children.Add(Visual);
        }

        protected override int VisualChildrenCount => _children.Count;

        protected override Visual GetVisualChild(int index) => _children[index];
    }
}
