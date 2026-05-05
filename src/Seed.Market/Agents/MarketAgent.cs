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
    public const int InputCount = SignalIndex.Count;
    public const int OutputCount = ActionInterpreter.OutputCount; // 6: dir/size/urgency/exit/predict/leverage

    private readonly BrainRuntime _brain;
    private readonly PaperTrader _trader;
    private readonly AblationConfig _ablation;
    private readonly PortfolioState _portfolio;
    private readonly float _maxLeverage;
    private readonly float _explicitExitBonus;
    private readonly float _peakExitBonus;
    // S8 — Stale-position penalty parameters. When the open position's age (ticks since open)
    // exceeds _staleThresholdTicks, ComputeReward subtracts _stalePenaltyPerTick × (age − threshold)
    // from the per-tick reward. Defaults to 0/0 (disabled) so behavior is unchanged for callers
    // that don't opt in. Phase 4 minimal observed pop[149] sitting at full leverage with rawExit
    // stuck at 0.500 for 12+ hours; this penalty gives selection a gradient toward learning the
    // explicit-exit output instead of relying on dirFlip closures.
    private readonly int _staleThresholdTicks;
    private readonly float _stalePenaltyPerTick;
    private int _tick;
    private int _ticksSinceEntry;
    private float _elapsedHoursAtEntry;

    private int _lastTradeCount;
    private float _lastUnrealizedPnl;
    private float _peakUnrealizedPnl;  // tracks peak P&L of current open position for peak-exit bonus
    private decimal _lastPrice;
    private decimal _prevEquity;
    private TradingSignal? _pendingSignal;
    private TradingSignal _lastGeneratedSignal;

    private readonly RollingMetrics _rolling = new(100);
    private int _consecutiveWins;
    private int _consecutiveLosses;
    private float _totalFees;
    private float _prevRollingSharpe;
    private readonly Queue<int> _recentTradeTicks = new();

    // V11d Fix 7 observability: Welford's online mean/variance per output (raw post-Interpret values).
    // Used to detect dormant outputs (std ≈ 0) vs actively-modulated outputs (std > 0.05).
    private readonly int _outputObsCount = 11;  // matches ActionInterpreter.OutputCount
    private readonly long[] _obsCounts;
    private readonly double[] _obsMean;
    private readonly double[] _obsM2;

    public Guid GenomeId { get; }
    public PortfolioState Portfolio => _portfolio;
    public int Tick => _tick;
    public TradingSignal LastGeneratedSignal => _lastGeneratedSignal;
    public float MaxLeverage => _maxLeverage;

    public MarketAgent(Guid genomeId, BrainRuntime brain, PaperTrader trader,
        AblationConfig? ablation = null, float maxLeverage = 1.0f, float explicitExitBonus = 0.02f,
        float peakExitBonus = 0.1f,
        int staleThresholdTicks = 0, float stalePenaltyPerTick = 0f)
    {
        GenomeId = genomeId;
        _brain = brain;
        _trader = trader;
        _ablation = ablation ?? AblationConfig.Default;
        _maxLeverage = MathF.Max(1.0f, maxLeverage);
        _explicitExitBonus = MathF.Max(0f, explicitExitBonus);
        _peakExitBonus = MathF.Max(0f, peakExitBonus);
        _staleThresholdTicks = Math.Max(0, staleThresholdTicks);
        _stalePenaltyPerTick = MathF.Max(0f, stalePenaltyPerTick);
        _portfolio = trader.CreatePortfolio();
        _prevEquity = _portfolio.InitialBalance;

        _obsCounts = new long[_outputObsCount];
        _obsMean = new double[_outputObsCount];
        _obsM2 = new double[_outputObsCount];
    }

    /// <summary>
    /// V11d Fix 7: snapshot of running mean/std for each of the 11 raw brain outputs
    /// (post-tanh/sigmoid, pre-deadzone). Used by training observability to identify
    /// dormant outputs (std ≈ 0 → never used) vs actively varied outputs (std &gt; 0.05).
    /// </summary>
    public OutputObservation GetOutputObservation()
    {
        var means = new float[_outputObsCount];
        var stds = new float[_outputObsCount];
        for (int i = 0; i < _outputObsCount; i++)
        {
            means[i] = (float)_obsMean[i];
            stds[i] = _obsCounts[i] > 1
                ? (float)Math.Sqrt(_obsM2[i] / (_obsCounts[i] - 1))
                : 0f;
        }
        return new OutputObservation(means, stds, _obsCounts[0]);
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

        // V11d Fix 7: Welford's online mean/var update for raw brain outputs (pre-Interpret).
        // Captures the brain's actual output distribution so observability can detect dormant
        // outputs (low std) vs intentionally-modulated outputs (higher std).
        int obsLen = Math.Min(outputs.Length, _outputObsCount);
        for (int i = 0; i < obsLen; i++)
        {
            float v = outputs[i];
            if (float.IsNaN(v) || float.IsInfinity(v)) continue;
            _obsCounts[i]++;
            double delta = v - _obsMean[i];
            _obsMean[i] += delta / _obsCounts[i];
            _obsM2[i] += delta * (v - _obsMean[i]);
        }

        var currentSignal = ActionInterpreter.Interpret(outputs, _maxLeverage);
        _lastGeneratedSignal = currentSignal;

        var signalToExecute = _pendingSignal ?? new TradingSignal(TradeDirection.Flat, 0f, 0f, false);
        _pendingSignal = currentSignal;

        var result = _trader.ProcessSignal(signalToExecute, _portfolio, ctx);

        _rolling.Add((float)_portfolio.Equity(ctx.Price));
        UpdateTradeTracking(ctx.Price);

        float reward = ComputeReward(ctx.Price);
        float pain = ComputePain(ctx.Price);
        float curiosity = _ablation.CuriosityEnabled ? ComputeCuriosity(outputs, ctx.Price) : 0f;
        float risk = ComputeRisk(ctx.Price);

        Span<float> modulators = stackalloc float[4];
        modulators[0] = reward;
        modulators[1] = pain;
        modulators[2] = curiosity;
        modulators[3] = risk;
        _brain.Learn(modulators, new BrainLearnContext(_tick, ctx.ElapsedHours));

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
        decimal equity = _portfolio.Equity(currentPrice);

        // V14 H3: aggregate across multi-positions instead of assuming OpenPositions[0].
        //   CurrentPnl: total unrealized PnL as fraction of initial balance
        //   PositionDirection: net signed notional (long - short) / total notional in [-1, 1]
        //   HoldingDuration: _ticksSinceEntry (tracked from first open)
        if (_portfolio.OpenPositions.Count > 0)
        {
            decimal totalUnrealized = 0m;
            decimal longNotional = 0m;
            decimal shortNotional = 0m;
            foreach (var p in _portfolio.OpenPositions)
            {
                totalUnrealized += p.UnrealizedPnl(currentPrice);
                decimal notional = p.Size * currentPrice;
                if (p.Direction == TradeDirection.Long) longNotional += notional;
                else if (p.Direction == TradeDirection.Short) shortNotional += notional;
            }

            float pnlPct = _portfolio.InitialBalance > 0m
                ? (float)(totalUnrealized / _portfolio.InitialBalance) : 0f;
            signals[SignalIndex.CurrentPnl] = Math.Clamp(pnlPct, -1f, 1f);

            decimal totalNot = longNotional + shortNotional;
            float netDirection = totalNot > 0m
                ? (float)((longNotional - shortNotional) / totalNot) : 0f;
            signals[SignalIndex.PositionDirection] = netDirection;

            signals[SignalIndex.HoldingDuration] = Math.Min(_ticksSinceEntry / 100f, 1f);
        }
        else
        {
            signals[SignalIndex.CurrentPnl] = 0f;
            signals[SignalIndex.PositionDirection] = 0f;
            signals[SignalIndex.HoldingDuration] = 0f;
        }

        float drawdown = _portfolio.MaxEquity > 0
            ? (float)((_portfolio.MaxEquity - equity) / _portfolio.MaxEquity)
            : 0f;
        signals[SignalIndex.CurrentDrawdown] = Math.Clamp(drawdown, 0f, 1f);

        // Risk awareness signals (96-103)
        signals[SignalIndex.RollingSharpe] = MathF.Tanh(_rolling.RollingSharpe / 5f);
        signals[SignalIndex.RollingDrawdown] = Math.Clamp(_rolling.RollingDrawdown, 0f, 1f);

        int tradeCount = _portfolio.TradeHistory.Count;
        if (tradeCount > 0)
        {
            int wins = _portfolio.TradeHistory.Count(t => t.Pnl > 0);
            signals[SignalIndex.WinRate] = (float)wins / tradeCount;

            float avgHold = (float)_portfolio.TradeHistory.Average(t => t.HoldingTicks);
            signals[SignalIndex.AvgHoldingDuration] = Math.Clamp(avgHold / 100f, 0f, 1f);
        }

        // Dequeue stale entries from recent trade window
        while (_recentTradeTicks.Count > 0 && _tick - _recentTradeTicks.Peek() > 100)
            _recentTradeTicks.Dequeue();
        signals[SignalIndex.TradeFrequency] = Math.Clamp(_recentTradeTicks.Count / 10f, 0f, 1f);

        signals[SignalIndex.CumulativeFees] = _portfolio.InitialBalance > 0
            ? Math.Clamp(_totalFees / (float)_portfolio.InitialBalance, 0f, 1f) : 0f;
        signals[SignalIndex.ConsecutiveWins] = Math.Clamp(_consecutiveWins / 5f, 0f, 1f);
        signals[SignalIndex.ConsecutiveLosses] = Math.Clamp(_consecutiveLosses / 5f, 0f, 1f);

        // V14 portfolio context signals (104-109)
        var cfg = _trader.Config;
        decimal totalNotional = 0m;
        foreach (var p in _portfolio.OpenPositions)
            totalNotional += p.Size * currentPrice;

        // AvailableMarginPct = (equity - notional) / equity, clamped
        float availableMargin = equity > 0m
            ? (float)((equity - totalNotional) / equity) : 1f;
        signals[SignalIndex.AvailableMarginPct] = Math.Clamp(availableMargin, 0f, 1f);

        // DistanceToStopLoss: closest active SL distance in % (0 if flat)
        // Uses config StopLossPct as the protective threshold.
        if (_portfolio.OpenPositions.Count > 0 && cfg.StopLossPct > 0m)
        {
            decimal minDist = decimal.MaxValue;
            foreach (var p in _portfolio.OpenPositions)
            {
                decimal unrealized = p.UnrealizedPnlPct(currentPrice) / 100m;
                // Distance = slack before stop fires = stopLossPct + unrealized (negative unrealized reduces slack)
                decimal dist = cfg.StopLossPct + unrealized;
                if (dist < minDist) minDist = dist;
            }
            signals[SignalIndex.DistanceToStopLoss] = Math.Clamp((float)minDist / (float)cfg.StopLossPct, 0f, 1f);
        }
        else
        {
            signals[SignalIndex.DistanceToStopLoss] = 1f;  // no position = full slack
        }

        // DistanceToKillSwitch: (current equity - KS threshold) / initial_equity
        if (cfg.KillSwitchDrawdownPct > 0m && _portfolio.InitialBalance > 0m)
        {
            decimal ksThreshold = _portfolio.MaxEquity * (1m - cfg.KillSwitchDrawdownPct);
            float distKS = (float)((equity - ksThreshold) / _portfolio.InitialBalance);
            signals[SignalIndex.DistanceToKillSwitch] = Math.Clamp(distKS, 0f, 1f);
        }
        else
        {
            signals[SignalIndex.DistanceToKillSwitch] = 1f;
        }

        // TimeSinceLastTrade: ticks since last trade, clamped
        int ticksSince = _recentTradeTicks.Count > 0
            ? _tick - _recentTradeTicks.Last()
            : _tick;
        signals[SignalIndex.TimeSinceLastTrade] = Math.Clamp(ticksSince / 100f, 0f, 1f);

        // EffectiveLeverage: total notional / equity
        float effLev = equity > 0m ? (float)(totalNotional / equity) : 0f;
        signals[SignalIndex.EffectiveLeverage] = Math.Clamp(effLev / Math.Max(1f, cfg.MaxLeverage), 0f, 1f);

        // WinLossStreakMagnitude: log ratio of avg win to avg loss, clamped to [-1, 1]
        if (tradeCount >= 4)
        {
            decimal avgWin = 0m;
            decimal avgLoss = 0m;
            int winCount = 0, lossCount = 0;
            foreach (var t in _portfolio.TradeHistory)
            {
                if (t.Pnl > 0) { avgWin += t.Pnl; winCount++; }
                else if (t.Pnl < 0) { avgLoss += -t.Pnl; lossCount++; }
            }
            if (winCount > 0) avgWin /= winCount;
            if (lossCount > 0) avgLoss /= lossCount;

            if (avgWin > 0m && avgLoss > 0m)
            {
                float logRatio = MathF.Log((float)(avgWin / avgLoss));
                signals[SignalIndex.WinLossStreakMagnitude] = Math.Clamp(logRatio / 2f, -1f, 1f);
            }
            else
            {
                signals[SignalIndex.WinLossStreakMagnitude] = 0f;
            }
        }
        else
        {
            signals[SignalIndex.WinLossStreakMagnitude] = 0f;
        }
    }

    private void UpdateTradeTracking(decimal currentPrice)
    {
        if (_portfolio.TradeHistory.Count > _lastTradeCount)
        {
            for (int i = _lastTradeCount; i < _portfolio.TradeHistory.Count; i++)
            {
                var trade = _portfolio.TradeHistory[i];
                _totalFees += (float)trade.Fee;
                _recentTradeTicks.Enqueue(_tick);

                if (trade.Pnl > 0)
                {
                    _consecutiveWins++;
                    _consecutiveLosses = 0;
                }
                else
                {
                    _consecutiveLosses++;
                    _consecutiveWins = 0;
                }
            }
        }
    }

    /// <summary>
    /// S8 — Computes the stale-position penalty contribution to reward. Returns
    /// <paramref name="perTick"/> × (age − threshold) when age &gt; threshold, otherwise 0.
    /// Returns 0 when threshold ≤ 0 or perTick ≤ 0 (penalty disabled). Public static so
    /// tests can verify the math without driving a full ProcessTick cycle.
    /// </summary>
    public static float ComputeStalePenalty(int currentTick, int openTick, int threshold, float perTick)
    {
        if (threshold <= 0 || perTick <= 0f) return 0f;
        int age = currentTick - openTick;
        if (age <= threshold) return 0f;
        return perTick * (age - threshold);
    }

    private float ComputeReward(decimal currentPrice)
    {
        float reward = 0f;

        if (_portfolio.TradeHistory.Count > _lastTradeCount)
        {
            var last = _portfolio.TradeHistory[^1];
            float realizedPct = (float)(last.Pnl / _portfolio.InitialBalance);
            reward += Math.Clamp(realizedPct * 50f, -1f, 1f);

            // Brain-driven exit bonus: reward the brain for USING any of its action outputs
            // to drive the close — output[3] ExitSignal, output[6] PartialClose,
            // output[7]/[8] TrailingStop, output[9] TakeProfit, output[10] BrainStopLoss.
            // Bootstrap signal that helps the brain discover each new output beyond indirect
            // P&L feedback. Does NOT fire for reactive closes (DirectionFlip, protective
            // StopLoss fallback, KillSwitch, EndOfSession).
            if (last.IsBrainDrivenExit)
                reward += _explicitExitBonus;

            // Peak-exit bonus: reward for closing profitable positions near their peak.
            // captureRatio = realizedPct / peakUnrealized, clamped to [0, 1].
            // Encourages holding winners to their peak rather than closing early.
            if (last.Pnl > 0 && _peakUnrealizedPnl > 0.001f)
            {
                float captureRatio = Math.Clamp(realizedPct / _peakUnrealizedPnl, 0f, 1f);
                reward += captureRatio * _peakExitBonus;
            }

            // Reset peak tracker for next position
            _peakUnrealizedPnl = 0f;
            _lastTradeCount = _portfolio.TradeHistory.Count;
        }

        if (_portfolio.OpenPositions.Count > 0)
        {
            var pos = _portfolio.OpenPositions[0];
            float currentPnlPct = (float)pos.UnrealizedPnlPct(currentPrice) / 100f;

            // Track peak profit (for peak-exit bonus when trade closes — V11 addition kept)
            if (currentPnlPct > _peakUnrealizedPnl) _peakUnrealizedPnl = currentPnlPct;

            // V11d: symmetric delta-based reward. Pre-V11 proven shape with zero expected
            // value for random trading, allowing free exploration. The asymmetric absolute-pnl
            // reward I added in A1 created aversive conditioning that suppressed exploration
            // and locked the population in the passive local optimum.
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
            _peakUnrealizedPnl = 0f;  // reset when flat
        }

        // Reward reshaping: volatility penalty
        float portfolioVol = _rolling.RollingVolatility;
        if (portfolioVol > 0.005f)
            reward -= Math.Clamp((portfolioVol - 0.005f) * 10f, 0f, 0.1f);

        // Reward reshaping: Sharpe improvement bonus
        float currentSharpe = _rolling.RollingSharpe;
        float sharpeDelta = currentSharpe - _prevRollingSharpe;
        if (sharpeDelta > 0f)
            reward += Math.Clamp(sharpeDelta * 0.5f, 0f, 0.05f);
        _prevRollingSharpe = currentSharpe;

        // V11d: holding time penalty applies ONLY to LOSING positions held > 20 ticks.
        // Pre-V11 proven shape — incentivizes cutting losers fast while letting winners run
        // freely (no penalty on winning holds). The symmetric-after-40-ticks penalty I added
        // in A1 punished winners and discouraged riding profitable trends.
        if (_portfolio.OpenPositions.Count > 0)
        {
            var pos2 = _portfolio.OpenPositions[0];
            float pnlPct = (float)pos2.UnrealizedPnlPct(currentPrice) / 100f;
            if (pnlPct <= 0f && _ticksSinceEntry > 20)
                reward -= Math.Clamp((_ticksSinceEntry - 20) / 200f, 0f, 0.05f);
        }

        // S8 — Stale-position penalty. Linear ramp once age > _staleThresholdTicks; applies
        // regardless of P&L direction. Unlike the V11d losing-hold penalty above (which only
        // pressures losers), this one targets *all* long-held positions to push the population
        // toward learning the explicit-exit output. Disabled (no effect) when threshold or
        // per-tick rate is 0.
        if (_portfolio.OpenPositions.Count > 0)
        {
            var posStale = _portfolio.OpenPositions[0];
            reward -= ComputeStalePenalty(Tick, posStale.OpenTick, _staleThresholdTicks, _stalePenaltyPerTick);
        }

        _prevEquity = _portfolio.Equity(currentPrice);
        return reward;
    }

    private float ComputeRisk(decimal currentPrice)
    {
        float risk = 0f;

        // Exposure × Volatility
        if (_portfolio.OpenPositions.Count > 0)
        {
            var pos = _portfolio.OpenPositions[0];
            decimal equity = _portfolio.Equity(currentPrice);
            float exposurePct = equity > 0 ? (float)(pos.Size * currentPrice / equity) : 0f;
            float vol = _rolling.RollingVolatility;
            risk += Math.Clamp(exposurePct * vol * 20f, 0f, 0.5f);
        }

        // Negative Sharpe penalty
        float sharpe = _rolling.RollingSharpe;
        if (sharpe < 0f)
            risk += Math.Clamp(-sharpe / 5f, 0f, 0.3f);

        // Trailing equity gap
        if (_portfolio.MaxEquity > 0)
        {
            float equityGap = (float)((_portfolio.MaxEquity - _portfolio.Equity(currentPrice))
                                       / _portfolio.MaxEquity);
            risk += Math.Clamp(equityGap * 2f, 0f, 0.2f);
        }

        return Math.Clamp(risk, 0f, 1f);
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
        _peakUnrealizedPnl = 0f;
        _lastPrice = 0m;
        _prevEquity = _portfolio.InitialBalance;
        _pendingSignal = null;
        _lastGeneratedSignal = default;
        _consecutiveWins = 0;
        _consecutiveLosses = 0;
        _totalFees = 0f;
        _prevRollingSharpe = 0f;
        _recentTradeTicks.Clear();

        Array.Clear(_obsCounts);
        Array.Clear(_obsMean);
        Array.Clear(_obsM2);
    }
}

/// <summary>
/// V11d Fix 7: Snapshot of brain output statistics across an evaluation. Captured per
/// tick using Welford's online algorithm; exposed for training observability.
/// </summary>
public readonly record struct OutputObservation(float[] Means, float[] Stds, long TickCount);
