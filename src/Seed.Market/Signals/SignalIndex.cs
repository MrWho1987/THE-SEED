namespace Seed.Market.Signals;

/// <summary>
/// Named constants for each slot in the 110-element signal vector.
/// Grouped by data category with contiguous index ranges.
///
/// V14 layout:
/// - Deribit options replaces dead News/Reddit slots 25-29
/// - Regime expanded from 4→8 signals (88-95) with new session/volatility/trend/correlation
/// - Risk awareness shifted 92-99 → 96-103
/// - Portfolio context added at 104-109 (AvailableMargin, DistanceToSL/KS, etc.)
/// </summary>
public static class SignalIndex
{
    public const int Count = 110;

    // ── Price & Volume (0-11) ──────────────────────────────────────
    public const int BtcPrice = 0;
    public const int BtcReturn1h = 1;
    public const int BtcReturn4h = 2;
    public const int BtcReturn24h = 3;
    public const int BtcVolume1h = 4;
    public const int BtcVolumeRatio = 5;       // current / 24h avg
    public const int BtcBidAskSpread = 6;
    public const int BtcOrderImbalance = 7;    // (bid_vol - ask_vol) / total
    public const int EthPrice = 8;
    public const int EthReturn1h = 9;
    public const int EthBtcRatio = 10;
    public const int EthVolume1h = 11;

    // ── Derivatives (12-22) ────────────────────────────────────────
    public const int FundingRate = 12;
    public const int OpenInterest = 13;
    public const int OiChange1h = 14;
    public const int LongShortRatio = 15;
    public const int TakerBuySellRatio = 16;
    public const int TopTraderLongShort = 17;
    public const int LiquidationLong1h = 18;
    public const int LiquidationShort1h = 19;
    public const int FuturesPremium = 20;      // futures - spot spread
    public const int EthFundingRate = 21;
    public const int EthOpenInterest = 22;

    // ── Sentiment (23-30) ─────────────────────────────────────────
    // Slots 25-29 were dead News/Reddit. Repurposed for Deribit options signals in V14.
    public const int FearGreedIndex = 23;
    public const int FearGreedChange = 24;            // delta from yesterday
    public const int DeribitPutCallRatio = 25;        // put_vol / (put_vol + call_vol)
    public const int DeribitPutCallOI = 26;           // put_OI / (put_OI + call_OI)
    public const int DeribitIVPercentile = 27;        // current IV's percentile in 30-day window
    public const int DeribitSkew = 28;                // 25-delta put IV - 25-delta call IV
    public const int DeribitMaxPainDistance = 29;     // |spot - max_pain| / spot
    public const int SentimentMomentum = 30;          // sentiment change rate (LIVE-only; typically dormant)

    // ── On-Chain (31-40) ───────────────────────────────────────────
    public const int HashRate = 31;
    public const int HashRateChange = 32;
    public const int ActiveAddresses = 33;
    public const int TransactionVolume = 34;
    public const int MempoolSize = 35;
    public const int ExchangeNetFlow = 36;     // positive = inflow (bearish)
    public const int MinerRevenue = 37;
    public const int MiningDifficulty = 38;
    public const int NvtRatio = 39;            // network value to transactions
    public const int SupplyOnExchanges = 40;

    // ── Macro (41-50) ──────────────────────────────────────────────
    public const int Sp500Return = 41;
    public const int Vix = 42;
    public const int VixChange = 43;
    public const int DxyIndex = 44;
    public const int DxyChange = 45;
    public const int GoldPrice = 46;
    public const int GoldReturn = 47;
    public const int Treasury10Y = 48;
    public const int TreasuryChange = 49;
    public const int BtcSp500Correlation = 50;

    // ── Stablecoin & Market Structure (51-56) ──────────────────────
    public const int UsdtMarketCap = 51;
    public const int UsdcMarketCap = 52;
    public const int StablecoinFlowDelta = 53; // net issuance
    public const int BtcDominance = 54;
    public const int TotalMarketCap = 55;
    public const int AltseasonIndex = 56;      // ETH/BTC relative strength

    // ── Technical Indicators (57-68) ───────────────────────────────
    public const int Rsi14 = 57;
    public const int Ema12 = 58;
    public const int Ema26 = 59;
    public const int MacdLine = 60;            // EMA12 - EMA26
    public const int MacdSignal = 61;
    public const int BollingerUpper = 62;
    public const int BollingerLower = 63;
    public const int BollingerWidth = 64;
    public const int Atr14 = 65;
    public const int Vwap = 66;
    public const int VwapDeviation = 67;       // price distance from VWAP
    public const int ObvSlope = 68;            // on-balance volume trend

    // ── Temporal Encoding (69-75) ──────────────────────────────────
    public const int HourSin = 69;
    public const int HourCos = 70;
    public const int DayOfWeekSin = 71;
    public const int DayOfWeekCos = 72;
    public const int MonthSin = 73;
    public const int MonthCos = 74;
    public const int EventProximity = 75;      // days to next FOMC/CPI

    // ── Agent Internal State (76-79) ───────────────────────────────
    public const int CurrentPnl = 76;
    public const int PositionDirection = 77;   // -1 short, 0 flat, +1 long
    public const int HoldingDuration = 78;     // ticks since entry
    public const int CurrentDrawdown = 79;

    // ── Multi-Asset Relative (80-87) ───────────────────────────────
    public const int BtcEthSpread = 80;
    public const int BtcEthCorrelation = 81;
    public const int BtcVolatility = 82;       // realized vol
    public const int EthVolatility = 83;
    public const int VolatilityRatio = 84;     // BTC vol / ETH vol
    public const int BtcMomentum = 85;         // rate of change
    public const int EthMomentum = 86;
    public const int MomentumDivergence = 87;  // BTC momentum - ETH momentum

    // ── Regime Context (88-95) ─────────────────────────────────────
    // V14 expansion: added TimeOfDaySession, VolatilityPercentile, TrendStrengthAdx, CorrelationRegime
    // These feed the brain's gate layer to provide richer regime awareness.
    public const int RegimeVolatility = 88;       // rolling 24h realized vol percentile [0,1]
    public const int RegimeTrend = 89;            // rolling momentum strength [-1,1]
    public const int RegimeChange = 90;           // rate of regime transition
    public const int MarketStress = 91;           // composite stress indicator
    public const int TimeOfDaySession = 92;       // UTC hour / 24; gates learn Asian/EU/US session patterns
    public const int VolatilityPercentile = 93;   // rolling 100-bar realized vol percentile rank
    public const int TrendStrengthAdx = 94;       // |EMA12 - EMA26| / Atr14 (ADX-like)
    public const int CorrelationRegime = 95;      // BTC-ETH correlation normalized to [0,1]

    // ── Risk Awareness (96-109) ────────────────────────────────────
    // V14: shifted from 92-99 → 96-103 to accommodate regime expansion.
    // New portfolio context signals added at 104-109.
    public const int RollingSharpe = 96;          // annualized, tanh(x/5) [-1,1]
    public const int RollingDrawdown = 97;        // 100-tick rolling max DD [0,1]
    public const int WinRate = 98;                // closed trades win ratio [0,1]
    public const int TradeFrequency = 99;         // recent trades / 10 [0,1]
    public const int AvgHoldingDuration = 100;    // mean ticks / 100 [0,1]
    public const int CumulativeFees = 101;        // total fees / balance [0,1]
    public const int ConsecutiveWins = 102;       // current win streak / 5 [0,1]
    public const int ConsecutiveLosses = 103;     // current loss streak / 5 [0,1]

    // NEW portfolio context (V14): give agent direct awareness of available margin,
    // distance to safety thresholds, and trade cadence.
    public const int AvailableMarginPct = 104;       // (equity - total_notional) / equity
    public const int DistanceToStopLoss = 105;       // closest active SL distance in % (0 if flat)
    public const int DistanceToKillSwitch = 106;     // (equity - ks_threshold) / initial_equity
    public const int TimeSinceLastTrade = 107;       // ticks / 100 clamped [0,1]
    public const int EffectiveLeverage = 108;        // total_notional / equity
    public const int WinLossStreakMagnitude = 109;   // log(avg_win$ / avg_loss$) clamped

    public static class Categories
    {
        public const int PriceStart = 0;
        public const int PriceEnd = 11;
        public const int DerivativesStart = 12;
        public const int DerivativesEnd = 22;
        public const int SentimentStart = 23;
        public const int SentimentEnd = 30;
        public const int OnChainStart = 31;
        public const int OnChainEnd = 40;
        public const int MacroStart = 41;
        public const int MacroEnd = 50;
        public const int StablecoinStart = 51;
        public const int StablecoinEnd = 56;
        public const int TechnicalStart = 57;
        public const int TechnicalEnd = 68;
        public const int TemporalStart = 69;
        public const int TemporalEnd = 75;
        public const int AgentStateStart = 76;
        public const int AgentStateEnd = 79;
        public const int MultiAssetStart = 80;
        public const int MultiAssetEnd = 87;
        public const int RegimeStart = 88;
        public const int RegimeEnd = 95;
        public const int RiskAwarenessStart = 96;
        public const int RiskAwarenessEnd = 109;
    }

    public static int GetCategoryIndex(int signalIndex) => signalIndex switch
    {
        >= Categories.PriceStart and <= Categories.PriceEnd => 0,
        >= Categories.DerivativesStart and <= Categories.DerivativesEnd => 1,
        >= Categories.SentimentStart and <= Categories.SentimentEnd => 2,
        >= Categories.OnChainStart and <= Categories.OnChainEnd => 3,
        >= Categories.MacroStart and <= Categories.MacroEnd => 4,
        >= Categories.StablecoinStart and <= Categories.StablecoinEnd => 5,
        >= Categories.TechnicalStart and <= Categories.TechnicalEnd => 6,
        >= Categories.TemporalStart and <= Categories.TemporalEnd => 7,
        >= Categories.AgentStateStart and <= Categories.AgentStateEnd => 8,
        >= Categories.MultiAssetStart and <= Categories.MultiAssetEnd => 9,
        >= Categories.RegimeStart and <= Categories.RegimeEnd => 10,
        >= Categories.RiskAwarenessStart and <= Categories.RiskAwarenessEnd => 11,
        _ => 0
    };

    public const int CategoryCount = 12;
}
