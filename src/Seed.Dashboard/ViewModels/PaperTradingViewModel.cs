using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using Seed.Dashboard.Services;

namespace Seed.Dashboard.ViewModels;

public partial class PaperTradingViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private PaperTradingService? _service;
    private readonly GenomeService _genomeService = new();

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _selectedGenome = "";
    [ObservableProperty] private string _sessionDuration = "";
    [ObservableProperty] private bool _isWarmup;

    [ObservableProperty] private decimal _price;
    [ObservableProperty] private string _priceText = "$0.00";
    [ObservableProperty] private string _position = "FLAT";
    [ObservableProperty] private decimal _equity = 10000m;
    [ObservableProperty] private string _equityText = "$10,000.00";
    [ObservableProperty] private decimal _unrealizedPnl;
    [ObservableProperty] private string _unrealizedText = "$0.00";
    [ObservableProperty] private decimal _totalPnl;
    [ObservableProperty] private string _totalPnlText = "$0.00";
    [ObservableProperty] private string _changePctText = "0.0%";
    [ObservableProperty] private int _totalTrades;
    [ObservableProperty] private string _winRateText = "0%";
    [ObservableProperty] private string _rollingSharpeText = "0.00";
    [ObservableProperty] private string _rollingDrawdownText = "0.0%";
    [ObservableProperty] private string _avgPnlText = "$0.00";
    [ObservableProperty] private bool _killSwitch;
    [ObservableProperty] private string _maxDrawdownText = "0.0%";
    [ObservableProperty] private double _drawdownProgress;

    [ObservableProperty] private PlotModel _pricePlotModel;
    [ObservableProperty] private PlotModel _equityPlotModel;

    public ObservableCollection<string> AvailableGenomes { get; } = [];
    public ObservableCollection<TradeRow> TradeHistory { get; } = [];
    public ObservableCollection<FeedHealthInfo> FeedHealth { get; } = [];

    private readonly LineSeries _priceSeries;
    private readonly LineSeries _equitySeries;
    private int _tickCount;

    public PaperTradingViewModel(MainViewModel main)
    {
        _main = main;

        var priceModel = new PlotModel
        {
            PlotAreaBorderColor = OxyColor.FromRgb(0x25, 0x28, 0x30),
            TextColor = OxyColor.FromRgb(0x94, 0xA3, 0xB8),
            Background = OxyColors.Transparent,
            PlotAreaBackground = OxyColors.Transparent,
            Padding = new OxyThickness(0)
        };
        priceModel.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom, Title = "Tick",
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromRgb(0x25, 0x28, 0x30),
            TextColor = OxyColor.FromRgb(0x94, 0xA3, 0xB8)
        });
        priceModel.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left, Title = "BTC Price ($)",
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromRgb(0x25, 0x28, 0x30),
            TextColor = OxyColor.FromRgb(0x94, 0xA3, 0xB8),
            StringFormat = "N0"
        });
        _priceSeries = new LineSeries
        {
            Color = OxyColor.FromRgb(0x3B, 0x82, 0xF6),
            StrokeThickness = 1.5
        };
        priceModel.Series.Add(_priceSeries);
        _pricePlotModel = priceModel;

        var eqModel = new PlotModel
        {
            PlotAreaBorderColor = OxyColor.FromRgb(0x25, 0x28, 0x30),
            TextColor = OxyColor.FromRgb(0x94, 0xA3, 0xB8),
            Background = OxyColors.Transparent,
            PlotAreaBackground = OxyColors.Transparent,
            Padding = new OxyThickness(0)
        };
        eqModel.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, IsAxisVisible = false });
        eqModel.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromRgb(0x25, 0x28, 0x30),
            TextColor = OxyColor.FromRgb(0x94, 0xA3, 0xB8),
            StringFormat = "N0"
        });
        _equitySeries = new LineSeries
        {
            Color = OxyColor.FromRgb(0x00, 0xF6, 0xA1),
            StrokeThickness = 1.5
        };
        eqModel.Series.Add(_equitySeries);
        _equityPlotModel = eqModel;

        RefreshGenomes();
    }

    public void RefreshGenomes()
    {
        AvailableGenomes.Clear();
        var genomes = _genomeService.GetGenomeDropdownItems(PathResolver.DiscoverOutputDirs());
        foreach (var g in genomes) AvailableGenomes.Add(g);
        if (AvailableGenomes.Count > 0 && string.IsNullOrEmpty(SelectedGenome))
            SelectedGenome = AvailableGenomes[0];
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartPaperTrading()
    {
        if (string.IsNullOrEmpty(SelectedGenome) || !System.IO.File.Exists(SelectedGenome)) return;

        var cfg = _main.Config.CurrentConfig;
        _service = new PaperTradingService(cfg, SelectedGenome, OnTick, OnTrade);
        IsRunning = true;
        _tickCount = 0;
        TradeHistory.Clear();
        _priceSeries.Points.Clear();
        _equitySeries.Points.Clear();
        FeedHealth.Clear();
        PricePlotModel.InvalidatePlot(true);
        EquityPlotModel.InvalidatePlot(true);

        _main.UpdatePaperSession(true, $"Paper | Starting...");
        _main.Sessions.RecordEvent("PaperStarted",
            $"Paper trading started with {System.IO.Path.GetFileName(SelectedGenome)}");
        _main.Notifications.Show("Paper Trading Started",
            $"Genome: {System.IO.Path.GetFileName(SelectedGenome)}", NotificationType.Info);

        try
        {
            await _service.RunAsync();
            _main.Sessions.RecordEvent("PaperStopped",
                $"Paper trading stopped. {TotalTrades} trades, P&L: {TotalPnlText}");
        }
        catch (Exception ex)
        {
            _main.Notifications.Show("Paper Trading Error", ex.Message, NotificationType.Error);
        }
        finally
        {
            IsRunning = false;
            _main.UpdatePaperSession(false);
            StartPaperTradingCommand.NotifyCanExecuteChanged();
            StopPaperTradingCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanStart() => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void StopPaperTrading()
    {
        _service?.Stop();
        _main.Notifications.Show("Paper Trading Stopped",
            $"{TotalTrades} trades. Total P&L: {TotalPnlText}", NotificationType.Warning);
    }

    private bool CanStop() => IsRunning;

    private void OnTick(TickData data)
    {
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            Price = data.Price;
            PriceText = $"${data.Price:N2}";
            Position = data.Position;
            Equity = data.Equity;
            EquityText = $"${data.Equity:N2}";
            UnrealizedPnl = data.UnrealizedPnl;
            UnrealizedText = $"{(data.UnrealizedPnl >= 0 ? "+" : "")}{data.UnrealizedPnl:N2}";
            TotalPnl = data.TotalPnl;
            TotalPnlText = $"{(data.TotalPnl >= 0 ? "+" : "")}{data.TotalPnl:N2}";
            var changePct = _main.Config.CurrentConfig.InitialCapital > 0
                ? (data.Equity - _main.Config.CurrentConfig.InitialCapital) / _main.Config.CurrentConfig.InitialCapital * 100
                : 0;
            ChangePctText = $"{(changePct >= 0 ? "+" : "")}{changePct:F1}%";
            TotalTrades = data.TotalTrades;
            WinRateText = $"{data.WinRate:P0}";
            RollingSharpeText = $"{data.RollingSharpe:F2}";
            RollingDrawdownText = $"{data.RollingDrawdown:P1}";
            AvgPnlText = data.TotalTrades > 0 ? $"${data.TotalPnl / data.TotalTrades:N2}" : "$0.00";
            KillSwitch = data.KillSwitch;
            MaxDrawdownText = $"{data.MaxDrawdownReached:P1}";
            double killPct = (double)_main.Config.CurrentConfig.KillSwitchDrawdownPct * 100;
            DrawdownProgress = killPct > 0 ? (double)data.MaxDrawdownReached * 100 / killPct : 0;
            SessionDuration = data.SessionDuration;
            IsWarmup = data.IsWarmup;

            _priceSeries.Points.Add(new DataPoint(data.Tick, (double)data.Price));
            if (_priceSeries.Points.Count > 2000)
                _priceSeries.Points.RemoveAt(0);
            PricePlotModel.InvalidatePlot(true);

            _equitySeries.Points.Add(new DataPoint(data.Tick, (double)data.Equity));
            if (_equitySeries.Points.Count > 2000)
                _equitySeries.Points.RemoveAt(0);
            EquityPlotModel.InvalidatePlot(true);

            FeedHealth.Clear();
            foreach (var f in data.FeedHealth) FeedHealth.Add(f);

            _main.UpdatePaperSession(true, $"Paper | BTC {PriceText} | {Position}");
            _tickCount++;

            if (data.KillSwitch)
            {
                _main.Notifications.Show("Kill Switch Triggered",
                    $"Drawdown exceeded limit at {MaxDrawdownText}. Trading halted.",
                    NotificationType.Error);
            }

            StartPaperTradingCommand.NotifyCanExecuteChanged();
            StopPaperTradingCommand.NotifyCanExecuteChanged();
        });
    }

    private void OnTrade(TradeRow trade)
    {
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            TradeHistory.Insert(0, trade);
        });
    }
}
