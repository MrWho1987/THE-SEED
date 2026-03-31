namespace Seed.Market.Signals;

/// <summary>
/// Named constants for each slot in the 92-element signal vector.
/// Grouped by data category with contiguous index ranges.
/// </summary>
public static class SignalIndex
{
    public const int Count = 92;

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

    // ── Sentiment & Social (23-30) ─────────────────────────────────
    public const int FearGreedIndex = 23;
    public const int FearGreedChange = 24;     // delta from yesterday
    public const int NewsHeadlineSentiment = 25;
    public const int NewsVolume = 26;          // articles per hour
    public const int RedditSentiment = 27;
    public const int RedditPostVolume = 28;
    public const int SocialBullBearRatio = 29;
    public const int SentimentMomentum = 30;   // sentiment change rate

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

    // ── Regime Context (88-91) ───────────────────────────────────
    public const int RegimeVolatility = 88;    // rolling 24h realized vol percentile [0,1]
    public const int RegimeTrend = 89;         // rolling momentum strength [-1,1]
    public const int RegimeChange = 90;        // rate of regime transition
    public const int MarketStress = 91;        // composite stress indicator

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
        public const int RegimeEnd = 91;
    }
}
