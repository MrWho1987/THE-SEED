using Seed.Brain;
using Seed.Core;
using Seed.Market.Signals;
using Seed.Market.Trading;

namespace Seed.Market.Agents;

/// <summary>
/// Bridges a CPPN-evolved brain to the market.
/// Takes float[88] market signals, feeds them to the brain,
/// interprets the float[5] output as trading actions + price prediction.
/// </summary>
public sealed class MarketAgent
{
    public const int InputCount = SignalIndex.Count;   // 88
    public const int OutputCount = ActionInterpreter.OutputCount; // 5

    private readonly BrainRuntime _brain;
    private readonly PaperTrader _trader;
    private readonly AblationConfig _ablation;
    private readonly PortfolioState _portfolio;
    private int _tick;
    private int _ticksSinceEntry;
    private float _elapsedHoursAtEntry;

    private int _lastTradeCount;
    private float _lastUnrealizedPnl;
    private decimal _lastPrice;
    private decimal _prevEquity;
    private TradingSignal? _pendingSignal;
    private TradingSignal _lastGeneratedSignal;

    public Guid GenomeId { get; }
    public PortfolioState Portfolio => _portfolio;
    public int Tick => _tick;
    public TradingSignal LastGeneratedSignal => _lastGeneratedSignal;

    public MarketAgent(Guid genomeId, BrainRuntime brain, PaperTrader trader,
        AblationConfig? ablation = null)
    {
        GenomeId = genomeId;
        _brain = brain;
        _trader = trader;
        _ablation = ablation ?? AblationConfig.Default;
        _portfolio = trader.CreatePortfolio();
        _prevEquity = _portfolio.InitialBalance;
    }

    public TradeResult ProcessTick(SignalSnapshot snapshot, decimal currentPrice)
    {
        return ProcessTick(snapshot, new TickContext(currentPrice, 0m, 0f, _tick, (float)_tick));
    }

    public TradeResult ProcessTick(SignalSnapshot snapshot, TickContext ctx)
    {
        var signals = new float[InputCount];
        Array.Copy(snapshot.Signals, signals, InputCount);
        InjectAgentState(signals, ctx.Price, ctx.ElapsedHours);

        var outputs = _brain.Step(signals, new BrainStepContext(_tick));

        var currentSignal = ActionInterpreter.Interpret(outputs);
        _lastGeneratedSignal = currentSignal;

        var signalToExecute = _pendingSignal ?? new TradingSignal(TradeDirection.Flat, 0f, 0f, false);
        _pendingSignal = currentSignal;

        var result = _trader.ProcessSignal(signalToExecute, _portfolio, ctx);

        float reward = ComputeReward(ctx.Price);
        float pain = ComputePain(ctx.Price);
        float curiosity = _ablation.CuriosityEnabled ? ComputeCuriosity(outputs, ctx.Price) : 0f;

        Span<float> modulators = stackalloc float[3];
        modulators[0] = reward;
        modulators[1] = pain;
        modulators[2] = curiosity;
        _brain.Learn(modulators, new BrainLearnContext(_tick, (float)_tick));

        if (_portfolio.OpenPositions.Count > 0)
        {
            _ticksSinceEntry++;
        }
        else
        {
            _ticksSinceEntry = 0;
            _elapsedHoursAtEntry = ctx.ElapsedHours;
        }

        _lastPrice = ctx.Price;
        _tick++;
        return result;
    }

    private void InjectAgentState(float[] signals, decimal currentPrice, float elapsedHours)
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
        float reward = 0f;

        if (_portfolio.TradeHistory.Count > _lastTradeCount)
        {
            var last = _portfolio.TradeHistory[^1];
            reward += Math.Clamp((float)(last.Pnl / _portfolio.InitialBalance) * 50f, -1f, 1f);
            _lastTradeCount = _portfolio.TradeHistory.Count;
        }

        if (_portfolio.OpenPositions.Count > 0)
        {
            float currentPnlPct = (float)_portfolio.OpenPositions[0].UnrealizedPnlPct(currentPrice) / 100f;
            float delta = currentPnlPct - _lastUnrealizedPnl;
            reward += Math.Clamp(delta * 30f, -0.5f, 0.5f);
            _lastUnrealizedPnl = currentPnlPct;
        }
        else
        {
            decimal equity = _portfolio.Equity(currentPrice);
            float equityDelta = Math.Clamp((float)((equity - _prevEquity) / _portfolio.InitialBalance) * 5f, -0.1f, 0.1f);
            reward += equityDelta;
            _lastUnrealizedPnl = 0f;
        }

        _prevEquity = _portfolio.Equity(currentPrice);
        return reward;
    }

    private float ComputePain(decimal currentPrice)
    {
        if (_portfolio.OpenPositions.Count == 0) return 0f;
        var pos = _portfolio.OpenPositions[0];
        float pnlPct = (float)pos.UnrealizedPnlPct(currentPrice) / 100f;
        return pnlPct < 0 ? Math.Clamp(-pnlPct, 0f, 1f) : 0f;
    }

    private float ComputeCuriosity(ReadOnlySpan<float> outputs, decimal currentPrice)
    {
        if (_lastPrice <= 0) return 0f;

        float predicted = outputs.Length > 4 ? MathF.Tanh(outputs[4]) : 0f;
        float actual = MathF.Sign((float)(currentPrice - _lastPrice));
        return MathF.Abs(predicted - actual);
    }

    public void Reset()
    {
        _brain.Reset();
        _tick = 0;
        _ticksSinceEntry = 0;
        _elapsedHoursAtEntry = 0f;
        _lastTradeCount = 0;
        _lastUnrealizedPnl = 0f;
        _lastPrice = 0m;
        _prevEquity = _portfolio.InitialBalance;
        _pendingSignal = null;
        _lastGeneratedSignal = default;
    }
}
