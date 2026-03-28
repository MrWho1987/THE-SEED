using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Seed.Brain;

namespace Seed.Dashboard.Controls;

public partial class BrainTopologyControl : UserControl
{
    public static readonly DependencyProperty BrainJsonProperty = DependencyProperty.Register(
        nameof(BrainJson),
        typeof(string),
        typeof(BrainTopologyControl),
        new PropertyMetadata(null, OnBrainJsonChanged));

    private readonly GraphVisualHost _graphHost = new();
    private BrainGraph? _graph;
    private readonly Dictionary<int, Point> _nodeCenters = new();
    private double _maxEdgeMag = 1e-6;
    private double _zoom = 1.0;
    private Vector _pan;
    private bool _isPanning;
    private Point _panStart;
    private Point _panOrigin;

    private const double NodeRadius = 4.5;
    private const double HitPadding = 2.0;

    private static readonly Color InputColor = Color.FromRgb(0x3B, 0x82, 0xF6);
    private static readonly Color OutputColor = Color.FromRgb(0x00, 0xF6, 0xA1);
    private static readonly Color HiddenColor = Color.FromRgb(0x94, 0xA3, 0xB8);

    public BrainTopologyControl()
    {
        InitializeComponent();
        Loaded += (_, _) => { AttachHost(); Redraw(); };
        SizeChanged += (_, _) => Redraw();
    }

    public string? BrainJson
    {
        get => (string?)GetValue(BrainJsonProperty);
        set => SetValue(BrainJsonProperty, value);
    }

    private static void OnBrainJsonChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BrainTopologyControl c)
        {
            c.LoadGraph();
            c.Redraw();
        }
    }

    private void AttachHost()
    {
        if (RootCanvas.Children.Contains(_graphHost))
            return;
        RootCanvas.Children.Add(_graphHost);
        Canvas.SetLeft(_graphHost, 0);
        Canvas.SetTop(_graphHost, 0);
        _graphHost.Width = ActualWidth;
        _graphHost.Height = ActualHeight;
    }

    private void LoadGraph()
    {
        _graph = null;
        _nodeCenters.Clear();
        _maxEdgeMag = 1e-6;

        var json = BrainJson;
        if (string.IsNullOrWhiteSpace(json))
            return;

        try
        {
            _graph = BrainGraph.FromJson(json);
            ComputeLayout();
            foreach (var kv in _graph.IncomingByDst)
            {
                foreach (var e in kv.Value)
                {
                    var mag = Math.Abs(e.WSlow) + Math.Abs(e.WFast);
                    if (mag > _maxEdgeMag)
                        _maxEdgeMag = mag;
                }
            }
        }
        catch
        {
            _graph = null;
            _nodeCenters.Clear();
        }
    }

    private void ComputeLayout()
    {
        _nodeCenters.Clear();
        if (_graph == null || _graph.Nodes.Count == 0)
            return;

        var w = Math.Max(_graphHost.ActualWidth, 1);
        var h = Math.Max(_graphHost.ActualHeight, 1);
        const double margin = 20;
        var innerW = w - 2 * margin;
        var innerH = h - 2 * margin;

        var nodes = _graph.Nodes;
        var ymin = nodes.Min(n => n.Y);
        var ymax = nodes.Max(n => n.Y);
        var yspan = Math.Max(ymax - ymin, 1e-6f);

        var inputs = nodes.Where(n => n.Type == BrainNodeType.Input).ToList();
        var hiddens = nodes.Where(n => n.Type == BrainNodeType.Hidden).ToList();
        var outputs = nodes.Where(n => n.Type == BrainNodeType.Output).ToList();

        void PlaceByLayer(IReadOnlyList<BrainNode> list, double xMin, double xMax)
        {
            if (list.Count == 0)
                return;
            var minL = list.Min(n => n.Layer);
            var maxL = list.Max(n => n.Layer);
            var span = Math.Max(maxL - minL, 1);
            foreach (var n in list)
            {
                var tx = (n.Layer - minL) / (double)span;
                var x = xMin + tx * (xMax - xMin);
                var yn = (n.Y - ymin) / yspan;
                var y = margin + yn * innerH;
                _nodeCenters[n.NodeId] = new Point(x, y);
            }
        }

        PlaceByLayer(inputs, margin, margin + innerW * 0.26);
        PlaceByLayer(hiddens, margin + innerW * 0.30, margin + innerW * 0.70);
        PlaceByLayer(outputs, margin + innerW * 0.74, margin + innerW);
    }

    private Point GraphToScreen(Point g) =>
        new(g.X * _zoom + _pan.X, g.Y * _zoom + _pan.Y);

    private void Redraw()
    {
        AttachHost();
        _graphHost.Width = ActualWidth;
        _graphHost.Height = ActualHeight;

        if (_graph != null)
            ComputeLayout();

        var dv = _graphHost.Visual;
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, _graphHost.Width, _graphHost.Height));

            if (_graph == null || _nodeCenters.Count == 0)
                return;

            foreach (var kv in _graph.IncomingByDst)
            {
                if (!_nodeCenters.TryGetValue(kv.Key, out var p2))
                    continue;
                foreach (var edge in kv.Value)
                {
                    if (!_nodeCenters.TryGetValue(edge.SrcNodeId, out var p1))
                        continue;
                    var mag = Math.Abs(edge.WSlow) + Math.Abs(edge.WFast);
                    var t = _maxEdgeMag > 1e-9 ? mag / _maxEdgeMag : 0;
                    var opacity = 0.06 + 0.94 * Math.Clamp(t, 0, 1);
                    var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 0x94, 0xA3, 0xB8)), 0.65 * _zoom);
                    pen.Freeze();
                    dc.DrawLine(pen, GraphToScreen(p1), GraphToScreen(p2));
                }
            }

            foreach (var node in _graph.Nodes)
            {
                if (!_nodeCenters.TryGetValue(node.NodeId, out var c))
                    continue;
                var fill = node.Type switch
                {
                    BrainNodeType.Input => new SolidColorBrush(InputColor),
                    BrainNodeType.Output => new SolidColorBrush(OutputColor),
                    _ => new SolidColorBrush(HiddenColor)
                };
                fill.Freeze();
                var sc = GraphToScreen(c);
                dc.DrawEllipse(fill, null, sc, NodeRadius * _zoom, NodeRadius * _zoom);
            }
        }
    }

    private Point GraphFromScreen(Point screen)
    {
        return new Point(
            (screen.X - _pan.X) / _zoom,
            (screen.Y - _pan.Y) / _zoom);
    }

    private BrainNode? HitTestNode(Point screen)
    {
        if (_graph == null)
            return null;
        var g = GraphFromScreen(screen);
        var r = NodeRadius + HitPadding;
        BrainNode? best = null;
        var bestD = double.MaxValue;
        foreach (var n in _graph.Nodes)
        {
            if (!_nodeCenters.TryGetValue(n.NodeId, out var c))
                continue;
            var d = (g - c).Length;
            if (d <= r && d < bestD)
            {
                bestD = d;
                best = n;
            }
        }
        return best;
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var pos = e.GetPosition(_graphHost);
        var oldZoom = _zoom;
        var factor = e.Delta > 0 ? 1.12 : 1 / 1.12;
        _zoom = Math.Clamp(_zoom * factor, 0.15, 8.0);
        var zr = _zoom / oldZoom;
        _pan.X = pos.X - (pos.X - _pan.X) * zr;
        _pan.Y = pos.Y - (pos.Y - _pan.Y) * zr;
        Redraw();
        UpdateTooltip(pos);
        e.Handled = true;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle)
            return;
        _isPanning = true;
        _panStart = e.GetPosition(this);
        _panOrigin = new Point(_pan.X, _pan.Y);
        CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(_graphHost);
        if (_isPanning && e.MiddleButton == MouseButtonState.Pressed)
        {
            var now = e.GetPosition(this);
            var d = now - _panStart;
            _pan = new Vector(_panOrigin.X + d.X, _panOrigin.Y + d.Y);
            Redraw();
        }
        UpdateTooltip(pos);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle)
            return;
        _isPanning = false;
        ReleaseMouseCapture();
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        ToolTipService.SetToolTip(this, null);
    }

    private void UpdateTooltip(Point posOnHost)
    {
        var node = HitTestNode(posOnHost);
        if (node == null)
        {
            ToolTipService.SetToolTip(this, null);
            return;
        }

        var tip = $"NodeId: {node.NodeId}\nType: {node.Type}\nLayer: {node.Layer}";
        ToolTipService.SetToolTip(this, tip);
    }

    private sealed class GraphVisualHost : FrameworkElement
    {
        private readonly VisualCollection _children;
        internal DrawingVisual Visual { get; } = new();

        public GraphVisualHost()
        {
            _children = new VisualCollection(this);
            _children.Add(Visual);
        }

        protected override int VisualChildrenCount => _children.Count;

        protected override Visual GetVisualChild(int index) => _children[index];
    }
}
