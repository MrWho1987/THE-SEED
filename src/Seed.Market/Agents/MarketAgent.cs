using Seed.Brain;
using Seed.Core;
using Seed.Market.Signals;
using Seed.Market.Trading;

namespace Seed.Market.Agents;

/// <summary>
/// Bridges a CPPN-evolved brain to the market.
/// Takes float[88] market signals, feeds them to the brain,
/// interprets the float[4] output as trading actions.
/// </summary>
public sealed class MarketAgent
{
    public const int InputCount = SignalIndex.Count;   // 88
    public const int OutputCount = ActionInterpreter.OutputCount; // 4

    private readonly BrainRuntime _brain;
    private readonly PaperTrader _trader;
    private readonly PortfolioState _portfolio;
    private int _tick;
    private int _ticksSinceEntry;

    public Guid GenomeId { get; }
    public PortfolioState Portfolio => _portfolio;
    public int Tick => _tick;

    public MarketAgent(Guid genomeId, BrainRuntime brain, PaperTrader trader)
    {
        GenomeId = genomeId;
        _brain = brain;
        _trader = trader;
        _portfolio = trader.CreatePortfolio();
    }

    /// <summary>
    /// Process one tick of market data. Returns the trading action taken (if any).
    /// </summary>
    public TradeResult ProcessTick(SignalSnapshot snapshot, decimal currentPrice)
    {
        // Inject agent internal state into the snapshot
        var signals = new float[InputCount];
        Array.Copy(snapshot.Signals, signals, InputCount);
        InjectAgentState(signals, currentPrice);

        // Feed to brain
        var outputs = _brain.Step(signals, new BrainStepContext(_tick));

        // Interpret brain output as trading signal
        var tradingSignal = ActionInterpreter.Interpret(outputs);

        // Execute through paper trader
        var result = _trader.ProcessSignal(tradingSignal, _portfolio, currentPrice, _tick);

        // Learning: modulate brain based on trade outcomes
        float reward = ComputeReward(currentPrice);
        float pain = ComputePain(currentPrice);
        float curiosity = 0f; // Could be prediction error in future
        Span<float> modulators = stackalloc float[3];
        modulators[0] = reward;
        modulators[1] = pain;
        modulators[2] = curiosity;
        _brain.Learn(modulators, new BrainLearnContext(_tick));

        // Track holding duration
        if (_portfolio.OpenPositions.Count > 0)
            _ticksSinceEntry++;
        else
            _ticksSinceEntry = 0;

        _tick++;
        return result;
    }

    private void InjectAgentState(float[] signals, decimal currentPrice)
    {
        if (_portfolio.OpenPositions.Count > 0)
        {
            var pos = _portfolio.OpenPositions[0];
            float pnlPct = (float)pos.UnrealizedPnlPct(currentPrice) / 100f;
            signals[SignalIndex.CurrentPnl] = Math.Clamp(pnlPct, -1f, 1f);
            signals[SignalIndex.PositionDirection] = pos.Direction == TradeDirection.Long ? 1f
                : pos.Direction == TradeDirection.Short ? -1f : 0f;
            signals[SignalIndex.HoldingDuration] = Math.Min(_ticksSinceEntry / 100f, 1f);
        }
        else
        {
            signals[SignalIndex.CurrentPnl] = 0f;
            signals[SignalIndex.PositionDirection] = 0f;
            signals[SignalIndex.HoldingDuration] = 0f;
        }

        float drawdown = _portfolio.MaxEquity > 0
            ? (float)((_portfolio.MaxEquity - _portfolio.Equity(currentPrice)) / _portfolio.MaxEquity)
            : 0f;
        signals[SignalIndex.CurrentDrawdown] = Math.Clamp(drawdown, 0f, 1f);
    }

    private float ComputeReward(decimal currentPrice)
    {
        if (_portfolio.TradeHistory.Count == 0) return 0f;
        var last = _portfolio.TradeHistory[^1];
        return Math.Clamp((float)(last.Pnl / _portfolio.InitialBalance) * 100f, -1f, 1f);
    }

    private float ComputePain(decimal currentPrice)
    {
        if (_portfolio.OpenPositions.Count == 0) return 0f;
        var pos = _portfolio.OpenPositions[0];
        float pnlPct = (float)pos.UnrealizedPnlPct(currentPrice) / 100f;
        return pnlPct < 0 ? Math.Clamp(-pnlPct, 0f, 1f) : 0f;
    }

    public void Reset()
    {
        _brain.Reset();
        _tick = 0;
        _ticksSinceEntry = 0;
    }
}
