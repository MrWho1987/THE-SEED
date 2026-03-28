using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Seed.Market.Signals;

namespace Seed.Dashboard.Controls;

public partial class SignalHeatmapControl : UserControl
{
    public static readonly DependencyProperty SignalsProperty = DependencyProperty.Register(
        nameof(Signals),
        typeof(float[]),
        typeof(SignalHeatmapControl),
        new PropertyMetadata(null, OnSignalsChanged));

    private static readonly string[] SignalNames = BuildSignalNames();

    private readonly Border[] _valueRects = new Border[SignalIndex.Count];

    private static readonly (string Title, int Start, int End)[] CategoryRanges =
    [
        ("Price", SignalIndex.Categories.PriceStart, SignalIndex.Categories.PriceEnd),
        ("Derivatives", SignalIndex.Categories.DerivativesStart, SignalIndex.Categories.DerivativesEnd),
        ("Sentiment", SignalIndex.Categories.SentimentStart, SignalIndex.Categories.SentimentEnd),
        ("OnChain", SignalIndex.Categories.OnChainStart, SignalIndex.Categories.OnChainEnd),
        ("Macro", SignalIndex.Categories.MacroStart, SignalIndex.Categories.MacroEnd),
        ("Stablecoin", SignalIndex.Categories.StablecoinStart, SignalIndex.Categories.StablecoinEnd),
        ("Technical", SignalIndex.Categories.TechnicalStart, SignalIndex.Categories.TechnicalEnd),
        ("Temporal", SignalIndex.Categories.TemporalStart, SignalIndex.Categories.TemporalEnd),
        ("AgentState", SignalIndex.Categories.AgentStateStart, SignalIndex.Categories.AgentStateEnd),
        ("MultiAsset", SignalIndex.Categories.MultiAssetStart, SignalIndex.Categories.MultiAssetEnd)
    ];

    private static readonly Color Blue = Color.FromRgb(0x3B, 0x82, 0xF6);
    private static readonly Color Red = Color.FromRgb(0xEF, 0x44, 0x44);

    public SignalHeatmapControl()
    {
        InitializeComponent();
        BuildCategoryUi();
        ApplySignals();
    }

    public float[]? Signals
    {
        get => (float[]?)GetValue(SignalsProperty);
        set => SetValue(SignalsProperty, value);
    }

    private static void OnSignalsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SignalHeatmapControl c)
            c.ApplySignals();
    }

    private static string[] BuildSignalNames()
    {
        var fields = typeof(SignalIndex)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(int) && f.Name != "Count")
            .Select(f => ((int)f.GetValue(null)!, f.Name))
            .OrderBy(t => t.Item1)
            .ToArray();

        var names = new string[SignalIndex.Count];
        foreach (var (index, name) in fields)
        {
            if (index >= 0 && index < names.Length)
                names[index] = name;
        }
        for (var i = 0; i < names.Length; i++)
        {
            if (string.IsNullOrEmpty(names[i]))
                names[i] = $"S{i}";
        }
        return names;
    }

    private void BuildCategoryUi()
    {
        CategoryPanel.Children.Clear();
        foreach (var (title, start, end) in CategoryRanges)
        {
            var expander = new Expander
            {
                Header = title,
                IsExpanded = true,
                Margin = new Thickness(0, 0, 0, 8),
                Foreground = (Brush)FindResource("SeedTextPrimaryBrush")
            };
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            for (var i = start; i <= end; i++)
            {
                wrap.Children.Add(CreateCell(i));
            }

            expander.Content = wrap;
            CategoryPanel.Children.Add(expander);
        }
    }

    private UIElement CreateCell(int index)
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(4, 3, 4, 3),
            MinWidth = 56
        };
        var label = new TextBlock
        {
            Text = SignalNames[index],
            FontSize = 9,
            MaxWidth = 88,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("SeedTextSecondaryBrush")
        };
        var rect = new Border
        {
            Width = 32,
            Height = 14,
            CornerRadius = new CornerRadius(2),
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)FindResource("SeedSurfaceElevatedBrush"),
            SnapsToDevicePixels = true
        };
        _valueRects[index] = rect;
        stack.Children.Add(label);
        stack.Children.Add(rect);
        return stack;
    }

    private void ApplySignals()
    {
        var data = Signals;
        var maxAbs = 1e-12f;
        if (data != null && data.Length == SignalIndex.Count)
        {
            for (var i = 0; i < SignalIndex.Count; i++)
                maxAbs = Math.Max(maxAbs, Math.Abs(data[i]));
        }

        for (var i = 0; i < SignalIndex.Count; i++)
        {
            var rect = _valueRects[i];
            if (data == null || i >= data.Length)
            {
                rect.Background = new SolidColorBrush(Colors.White);
                continue;
            }

            var v = data[i];
            rect.Background = new SolidColorBrush(ValueToColor(v, maxAbs));
        }
    }

    private static Color ValueToColor(float v, float maxAbs)
    {
        if (maxAbs < 1e-12f)
            return Colors.White;
        var t = v / maxAbs;
        t = Math.Clamp(t, -1f, 1f);
        if (t <= 0f)
            return Lerp(Blue, Colors.White, 1f + t);
        return Lerp(Colors.White, Red, t);
    }

    private static Color Lerp(Color a, Color b, float u)
    {
        u = Math.Clamp(u, 0f, 1f);
        var r = (byte)(a.R + (b.R - a.R) * u);
        var g = (byte)(a.G + (b.G - a.G) * u);
        var bl = (byte)(a.B + (b.B - a.B) * u);
        return Color.FromRgb(r, g, bl);
    }
}
