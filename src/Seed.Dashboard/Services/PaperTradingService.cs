using Seed.Brain;
using Seed.Core;
using Seed.Development;
using Seed.Genetics;
using Seed.Market;
using Seed.Market.Agents;
using Seed.Market.Data;
using Seed.Market.Evolution;
using Seed.Market.Signals;
using Seed.Market.Trading;

namespace Seed.Dashboard.Services;

public record TickData(
    int Tick, decimal Price, string Position, decimal UnrealizedPnl,
    decimal Equity, decimal TotalPnl, int TotalTrades, float WinRate,
    float RollingSharpe, float RollingDrawdown, bool KillSwitch,
    decimal MaxDrawdownReached, string SessionDuration,
    bool IsWarmup, FeedHealthInfo[] FeedHealth);

public record FeedHealthInfo(string Name, bool IsHealthy, string LastUpdate);

public record TradeRow(
    int Number, string Time, string Direction, decimal Entry, decimal Exit,
    decimal Size, decimal Pnl, decimal Fee, int HoldTicks, string PnlPct);

public class PaperTradingService
{
    private readonly MarketConfig _config;
    private readonly string _genomePath;
    private readonly Action<TickData> _onTick;
    private readonly Action<TradeRow> _onTrade;
    private CancellationTokenSource? _cts;

    public PaperTradingService(MarketConfig config, string genomePath,
        Action<TickData> onTick, Action<TradeRow> onTrade)
    {
        _config = config;
        _genomePath = genomePath;
        _onTick = onTick;
        _onTrade = onTrade;
    }

    public async Task RunAsync()
    {
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        var genomeJson = await System.IO.File.ReadAllTextAsync(_genomePath, ct);
        var genome = SeedGenome.FromJson(genomeJson);

        var developer = new BrainDeveloper(MarketAgent.InputCount, MarketAgent.OutputCount);
        var devCtx = new DevelopmentContext(_config.RunSeed, 0);
        var budget = MarketEvaluator.MarketBrainBudget with
        {
            HiddenWidth = genome.Dev.SubstrateWidth,
            HiddenHeight = genome.Dev.SubstrateHeight,
            HiddenLayers = genome.Dev.SubstrateLayers
        };
        var graph = developer.CompileGraph(genome, budget, devCtx);
        var brain = new BrainRuntime(graph, genome.Learn, genome.Stable, 1);
        var trader = new PaperTrader(_config);
        var agent = new MarketAgent(genome.GenomeId, brain, trader);

        using var aggregator = new DataAggregator(_config);
        using var tradeLog = new TradeLogger(_config.ResolvedTradeLogPath);

        var rolling = new RollingMetrics(100);
        int feedTick = 0;
        int decisionTick = 0;
        int lastDecisionHour = -1;
        int prevTradeCount = 0;
        var startTime = DateTimeOffset.UtcNow;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var snapshot = await aggregator.TickAsync(ct);
                decimal price = (decimal)aggregator.LastRawBtcPrice;

                if (price <= 0)
                {
                    await Task.Delay(_config.SpotPollMs, ct);
                    continue;
                }

                int currentHour = DateTimeOffset.UtcNow.DayOfYear * 24 + DateTimeOffset.UtcNow.Hour;
                bool isDecisionTick = lastDecisionHour == -1 || currentHour != lastDecisionHour;

                if (isDecisionTick)
                {
                    lastDecisionHour = currentHour;
                    agent.ProcessTick(snapshot, price);
                    decisionTick++;

                    if (agent.Portfolio.TradeHistory.Count > prevTradeCount)
                    {
                        for (int i = prevTradeCount; i < agent.Portfolio.TradeHistory.Count; i++)
                        {
                            var t = agent.Portfolio.TradeHistory[i];
                            tradeLog.LogTrade(t);
                            _onTrade(new TradeRow(
                                i + 1, t.CloseTime.ToString("HH:mm:ss"),
                                t.Direction.ToString(), t.EntryPrice, t.ExitPrice,
                                t.Size, t.Pnl, t.Fee, t.HoldingTicks,
                                t.EntryPrice > 0 ? (t.Pnl / (t.EntryPrice * t.Size) * 100).ToString("F2") + "%" : "0%"));
                        }
                        prevTradeCount = agent.Portfolio.TradeHistory.Count;
                    }
                }

                agent.Portfolio.RecordEquity(price);
                rolling.Add((float)agent.Portfolio.Equity(price));

                var portfolio = agent.Portfolio;
                decimal equity = portfolio.Equity(price);
                decimal unrealized = equity - portfolio.Balance;
                string pos = portfolio.OpenPositions.Count > 0
                    ? portfolio.OpenPositions[0].Direction == TradeDirection.Long ? "LONG" : "SHORT"
                    : "FLAT";

                var elapsed = DateTimeOffset.UtcNow - startTime;
                bool isWarmup = decisionTick < 24;

                var feedHealth = new FeedHealthInfo[]
                {
                    new("Binance Spot", true, "Live"),
                    new("Binance Futures", true, "Live"),
                    new("Sentiment", true, "Live"),
                    new("On-Chain", true, "Live"),
                    new("Macro", true, "Live"),
                    new("Stablecoin", true, "Live"),
                };

                decimal maxDd = portfolio.MaxDrawdown;

                _onTick(new TickData(
                    feedTick, price, pos, unrealized, equity, portfolio.TotalPnl,
                    portfolio.TotalTrades, portfolio.WinRate,
                    rolling.RollingSharpe, rolling.RollingDrawdown,
                    portfolio.KillSwitchTriggered, maxDd,
                    FormatDuration(elapsed), isWarmup, feedHealth));

                feedTick++;
                await Task.Delay(_config.SpotPollMs, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PAPER] Feed error: {ex.Message}");
                await Task.Delay(5000, ct);
            }
        }

        decimal finalPrice = agent.Portfolio.OpenPositions.Count > 0
            ? (decimal)aggregator.LastRawBtcPrice
            : _config.InitialCapital;
        trader.CloseAllPositions(agent.Portfolio, finalPrice, decisionTick);
    }

    public void Stop() => _cts?.Cancel();

    private static string FormatDuration(TimeSpan ts) =>
        ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}h {ts.Minutes:D2}m" : $"{ts.Minutes}m {ts.Seconds:D2}s";
}
