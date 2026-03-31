# Market signals catalog

The market layer exposes a fixed-length signal vector used as input to CPPN brains. The authoritative size is **`SignalIndex.Count == 92`**. Some comments elsewhere still say 88 (for example `SignalSnapshot` and `MarketAgent` XML); those references are outdated.

---

## 1. Category index ranges (`SignalIndex.Categories`)

| Category | `*Start` | `*End` | Inclusive count |
|----------|-----------|--------|-----------------|
| Price & Volume | `PriceStart` 0 | `PriceEnd` 11 | 12 |
| Derivatives | `DerivativesStart` 12 | `DerivativesEnd` 22 | 11 |
| Sentiment & Social | `SentimentStart` 23 | `SentimentEnd` 30 | 8 |
| On-Chain | `OnChainStart` 31 | `OnChainEnd` 40 | 10 |
| Macro | `MacroStart` 41 | `MacroEnd` 50 | 10 |
| Stablecoin & Market Structure | `StablecoinStart` 51 | `StablecoinEnd` 56 | 6 |
| Technical Indicators | `TechnicalStart` 57 | `TechnicalEnd` 68 | 12 |
| Temporal Encoding | `TemporalStart` 69 | `TemporalEnd` 75 | 7 |
| Agent Internal State | `AgentStateStart` 76 | `AgentStateEnd` 79 | 4 |
| Multi-Asset Relative | `MultiAssetStart` 80 | `MultiAssetEnd` 87 | 8 |
| Regime Context | `RegimeStart` 88 | `RegimeEnd` 91 | 4 |

---

## 2. Complete signal table (all 92)

Descriptions are taken from trailing comments on the corresponding constants in `SignalIndex.cs`. An em dash (—) means that constant has no such comment in source.

| Index | Constant | Category | Description (from `SignalIndex.cs` comments) |
|------:|----------|----------|-----------------------------------------------|
| 0 | `BtcPrice` | Price & Volume | — |
| 1 | `BtcReturn1h` | Price & Volume | — |
| 2 | `BtcReturn4h` | Price & Volume | — |
| 3 | `BtcReturn24h` | Price & Volume | — |
| 4 | `BtcVolume1h` | Price & Volume | — |
| 5 | `BtcVolumeRatio` | Price & Volume | current / 24h avg |
| 6 | `BtcBidAskSpread` | Price & Volume | — |
| 7 | `BtcOrderImbalance` | Price & Volume | (bid_vol - ask_vol) / total |
| 8 | `EthPrice` | Price & Volume | — |
| 9 | `EthReturn1h` | Price & Volume | — |
| 10 | `EthBtcRatio` | Price & Volume | — |
| 11 | `EthVolume1h` | Price & Volume | — |
| 12 | `FundingRate` | Derivatives | — |
| 13 | `OpenInterest` | Derivatives | — |
| 14 | `OiChange1h` | Derivatives | — |
| 15 | `LongShortRatio` | Derivatives | — |
| 16 | `TakerBuySellRatio` | Derivatives | — |
| 17 | `TopTraderLongShort` | Derivatives | — |
| 18 | `LiquidationLong1h` | Derivatives | — |
| 19 | `LiquidationShort1h` | Derivatives | — |
| 20 | `FuturesPremium` | Derivatives | futures - spot spread |
| 21 | `EthFundingRate` | Derivatives | — |
| 22 | `EthOpenInterest` | Derivatives | — |
| 23 | `FearGreedIndex` | Sentiment & Social | — |
| 24 | `FearGreedChange` | Sentiment & Social | delta from yesterday |
| 25 | `NewsHeadlineSentiment` | Sentiment & Social | — |
| 26 | `NewsVolume` | Sentiment & Social | articles per hour |
| 27 | `RedditSentiment` | Sentiment & Social | — |
| 28 | `RedditPostVolume` | Sentiment & Social | — |
| 29 | `SocialBullBearRatio` | Sentiment & Social | — |
| 30 | `SentimentMomentum` | Sentiment & Social | sentiment change rate |
| 31 | `HashRate` | On-Chain | — |
| 32 | `HashRateChange` | On-Chain | — |
| 33 | `ActiveAddresses` | On-Chain | — |
| 34 | `TransactionVolume` | On-Chain | — |
| 35 | `MempoolSize` | On-Chain | — |
| 36 | `ExchangeNetFlow` | On-Chain | positive = inflow (bearish) |
| 37 | `MinerRevenue` | On-Chain | — |
| 38 | `MiningDifficulty` | On-Chain | — |
| 39 | `NvtRatio` | On-Chain | network value to transactions |
| 40 | `SupplyOnExchanges` | On-Chain | — |
| 41 | `Sp500Return` | Macro | — |
| 42 | `Vix` | Macro | — |
| 43 | `VixChange` | Macro | — |
| 44 | `DxyIndex` | Macro | — |
| 45 | `DxyChange` | Macro | — |
| 46 | `GoldPrice` | Macro | — |
| 47 | `GoldReturn` | Macro | — |
| 48 | `Treasury10Y` | Macro | — |
| 49 | `TreasuryChange` | Macro | — |
| 50 | `BtcSp500Correlation` | Macro | — |
| 51 | `UsdtMarketCap` | Stablecoin & Market Structure | — |
| 52 | `UsdcMarketCap` | Stablecoin & Market Structure | — |
| 53 | `StablecoinFlowDelta` | Stablecoin & Market Structure | net issuance |
| 54 | `BtcDominance` | Stablecoin & Market Structure | — |
| 55 | `TotalMarketCap` | Stablecoin & Market Structure | — |
| 56 | `AltseasonIndex` | Stablecoin & Market Structure | ETH/BTC relative strength |
| 57 | `Rsi14` | Technical Indicators | — |
| 58 | `Ema12` | Technical Indicators | — |
| 59 | `Ema26` | Technical Indicators | — |
| 60 | `MacdLine` | Technical Indicators | EMA12 - EMA26 |
| 61 | `MacdSignal` | Technical Indicators | — |
| 62 | `BollingerUpper` | Technical Indicators | — |
| 63 | `BollingerLower` | Technical Indicators | — |
| 64 | `BollingerWidth` | Technical Indicators | — |
| 65 | `Atr14` | Technical Indicators | — |
| 66 | `Vwap` | Technical Indicators | — |
| 67 | `VwapDeviation` | Technical Indicators | price distance from VWAP |
| 68 | `ObvSlope` | Technical Indicators | on-balance volume trend |
| 69 | `HourSin` | Temporal Encoding | — |
| 70 | `HourCos` | Temporal Encoding | — |
| 71 | `DayOfWeekSin` | Temporal Encoding | — |
| 72 | `DayOfWeekCos` | Temporal Encoding | — |
| 73 | `MonthSin` | Temporal Encoding | — |
| 74 | `MonthCos` | Temporal Encoding | — |
| 75 | `EventProximity` | Temporal Encoding | days to next FOMC/CPI |
| 76 | `CurrentPnl` | Agent Internal State | — |
| 77 | `PositionDirection` | Agent Internal State | -1 short, 0 flat, +1 long |
| 78 | `HoldingDuration` | Agent Internal State | ticks since entry |
| 79 | `CurrentDrawdown` | Agent Internal State | — |
| 80 | `BtcEthSpread` | Multi-Asset Relative | — |
| 81 | `BtcEthCorrelation` | Multi-Asset Relative | — |
| 82 | `BtcVolatility` | Multi-Asset Relative | realized vol |
| 83 | `EthVolatility` | Multi-Asset Relative | — |
| 84 | `VolatilityRatio` | Multi-Asset Relative | BTC vol / ETH vol |
| 85 | `BtcMomentum` | Multi-Asset Relative | rate of change |
| 86 | `EthMomentum` | Multi-Asset Relative | — |
| 87 | `MomentumDivergence` | Multi-Asset Relative | BTC momentum - ETH momentum |
| 88 | `RegimeVolatility` | Regime Context | rolling 24h realized vol percentile [0,1] |
| 89 | `RegimeTrend` | Regime Context | rolling momentum strength [-1,1] |
| 90 | `RegimeChange` | Regime Context | rate of regime transition |
| 91 | `MarketStress` | Regime Context | composite stress indicator |

---

## 3. Live data feeds: poll intervals and signal indices

`DataAggregator` constructs feeds in this order. The **`Interval`** property on each `IDataFeed` implementation is what gates HTTP refreshes (compared to `DateTimeOffset.UtcNow` in `TickAsync`). `MarketConfig` poll fields are not used for this scheduling.

| Feed class | `Interval` | Signal indices populated by this feed |
|------------|------------|--------------------------------------|
| `BinanceSpotFeed` | `TimeSpan.FromSeconds(5)` | 0–7, 8–11 |
| `BinanceFuturesFeed` | `TimeSpan.FromSeconds(15)` | 12–22 |
| `SentimentFeed` | `TimeSpan.FromMinutes(5)` | 23–24, 25–26, 27–28, 29, 30 |
| `OnChainFeed` | `TimeSpan.FromHours(1)` | 31–40 (see notes below) |
| `MacroFeed` | `TimeSpan.FromHours(1)` | 41–49; 50 set to `0f` as placeholder (aggregator may overwrite 50) |
| `StablecoinFeed` | `TimeSpan.FromHours(1)` | 51–56 |

### Feed implementation notes (from source)

- **`BinanceSpotFeed`**: BTC 1h klines `limit=25`; ETH 1h `limit=2`; depth `limit=20`. `BtcReturn1h` uses previous fetch close; volume ratio uses a rolling queue of up to 24 hourly volumes. `BtcBidAskSpread` = `(bestAsk - bestBid) / bestAsk`; `BtcOrderImbalance` = `(bidVol - askVol) / (bidVol + askVol)` summed over the depth levels.
- **`BinanceFuturesFeed`**: `LiquidationLong1h` / `LiquidationShort1h` are set to `0f` (REST placeholder). `FuturesPremium` = `(markPrice - indexPrice) / indexPrice` from `premiumIndex`. `OiChange1h` is relative change vs previous open interest sample.
- **`SentimentFeed`**: Fear & Greed from alternative.me; `SentimentMomentum` = current FG − previous FG (zero if no previous). RSS titles from Cointelegraph and CoinDesk (10 items each); Reddit `r/cryptocurrency` and `r/bitcoin` hot, `limit=25`. Headline/post text is scored with `VaderSentiment.Score`. `NewsVolume` is the count of scored RSS titles; `RedditPostVolume` is scored post count; `SocialBullBearRatio` = `bullish / (bullish + bearish)` among posts with score > 0.05 or < −0.05.
- **`OnChainFeed`**: `ExchangeNetFlow` and `SupplyOnExchanges` are `0f` (comment: premium APIs). `NvtRatio` = `hashRate / transactionVolume` from the same chart endpoints (placeholder-style derivation).
- **`MacroFeed`**: `Sp500Return` is daily close-to-close return vs previous successful fetch (not intraday). `BtcSp500Correlation` is written as `0f` here; live correlation is computed in `DataAggregator.ComputeDerivedSignals`.
- **`StablecoinFeed`**: `AltseasonIndex` = `ethDom / btcDom` from CoinGecko global `market_cap_percentage`. `StablecoinFlowDelta` = relative change in USDT+USDC market caps vs previous sample.

---

## 4. Derived and computed signals (`DataAggregator`, live path)

After successful feed updates, inside the same lock, the aggregator:

1. **`UpdateCandleHistory`**: Builds hourly OHLCV from `BtcPrice` and incremental volume from `BtcVolume1h` (capped at 200 candles).
2. **Technical indicators** (indices 57–68): if `_candleHistory.Count >= 26`, runs `TechnicalIndicators.Compute` and writes those slots.
3. **Temporal encoding** (69–75): `TimeEncoding.Compute(now)`.
4. **`ComputeDerivedSignals`**: multi-asset and regime (below).
5. **`ZeroMaskLiveOnlySignals`**: zeros selected indices (see section 9).

### `ComputeDerivedSignals` (indices 80–87 and inputs to 88–91)

| Index | Computation |
|------:|---------------|
| `BtcEthSpread` | `ethPrice / btcPrice` when both > 0, else `0`. |
| `BtcSp500Correlation` | Pearson correlation between queued hourly BTC returns and `Sp500Return` sampled each new hour (`CorrWindow` = 24). |
| `BtcEthCorrelation` | Pearson correlation between queued hourly BTC and ETH returns (same window). |
| `BtcVolatility` | `StdDev` of queued hourly BTC returns (population-style RMS around mean, see `StdDev` in `DataAggregator`). |
| `EthVolatility` | Same for ETH hourly returns. |
| `VolatilityRatio` | `BtcVolatility / EthVolatility` if `EthVolatility` > 0, else `1`. |
| `BtcMomentum` / `EthMomentum` | If at least 12 hourly prices in queue: `(price - price_{t-lookback}) / price_{t-lookback}` with `lookback = min(12, queue length)`. |
| `MomentumDivergence` | `BtcMomentum - EthMomentum`. |

Hourly return queues update on clock hour change when BTC price is positive, seeding `_lastHourlyBtcPrice` / `_lastHourlyEthPrice` on first valid tick.

### `ComputeRegimeSignals` (indices 88–91)

Uses `btcArr` = queued hourly BTC returns (same series as volatility). Let `vol = BtcVolatility`, `momentum = BtcMomentum`, `volPercentile = min(vol / 0.05, 1)`.

| Index | Formula |
|------:|---------|
| `RegimeVolatility` | `volPercentile` |
| `RegimeTrend` | `Clamp(momentum / 0.10, -1, 1)` |
| `RegimeChange` | `Clamp((vol - _prevRegimeVolatility) / 0.02, -1, 1)` then `_prevRegimeVolatility = vol` |
| `MarketStress` | `Clamp(|VixChange| * 2 + (LiqLong + LiqShort) * 0.5 + |FundingRate| * 10 + volPercentile * 0.5, 0, 1)` |

---

## 5. Technical indicators (`TechnicalIndicators.Compute`)

Computation runs only when `candles.Length >= 26` (comment: at least 26 bars for EMA-26). The live aggregator also requires `_candleHistory.Count >= 26`.

Let closes `C`, highs `H`, lows `L`, volumes `V`, last close `C_n`.

| Output | Definition (from code) |
|--------|-------------------------|
| `Rsi14` | Wilder-style smoothing: initial average gain/loss over first 14 intervals; then iterate updating averages; RSI = `100 - 100/(1 + avgGain/avgLoss)` with 0/100 edge cases. |
| `Ema12` / `Ema26` | Full-series EMA on closes with multiplier `2/(period+1)`, seeded with `data[0]`. |
| `MacdLine` | `Ema12 - Ema26` (last-bar values from full-series EMA). |
| `MacdSignal` | EMA with period 9 applied to the full MACD line series; value is the final EMA. |
| `BollingerUpper` / `BollingerLower` | Last 20 closes: mean `μ`, variance `σ² = mean(C²) - μ²`, std `σ`; upper = `μ + 2σ`, lower = `μ - 2σ`. |
| `BollingerWidth` | `(upper - lower) / C_n` if `C_n > 0`, else `0`. |
| `Atr14` | Simple average of the last 14 true ranges: `TR = max(H-L, |H-C_{n-1}|, |L-C_{n-1}|)`. |
| `Vwap` | Over the last up to 24 candles: `sum(tp * V) / sum(V)` where `tp = (H+L+C)/3`. |
| `VwapDeviation` | `(C_n - VWAP) / VWAP` if VWAP > 0. |
| `ObvSlope` | OBV direction ±1 or 0 from close change, cumulated over the last 14 volume-weighted steps from index `closes.Length - period - 1` to end; return `(obv - obvStart) / period`. |

---

## 6. Temporal encoding (`TimeEncoding.Compute`)

| Index | Formula |
|------:|---------|
| `HourSin` / `HourCos` | `hourAngle = 2π * Hour / 24`; `sin`, `cos`. |
| `DayOfWeekSin` / `DayOfWeekCos` | `dayAngle = 2π * (int)DayOfWeek / 7`. |
| `MonthSin` / `MonthCos` | `monthAngle = 2π * (Month - 1) / 12`. |
| `EventProximity` | `eventProximity = min` absolute day distance to any date in built-in FOMC (2018–2026, 18:00 UTC) or CPI (second Wednesday of each month 2018–2026, 13:30 UTC); then `Clamp(1 - eventProximity/15, -1, 1)` via `Max(-1, Min(1, ...))`. |

---

## 7. Agent state injection (`MarketAgent.InjectAgentState`)

Called from `ProcessTick` **after** copying the snapshot into a mutable `float[]` and **before** `BrainRuntime.Step`. It overwrites indices 76–79 on that copy only.

| Index | Rule |
|------:|------|
| `CurrentPnl` | If an open position: `Clamp(UnrealizedPnlPct(currentPrice) / 100, -1, 1)`; else `0`. |
| `PositionDirection` | `+1` long, `-1` short, `0` flat / no position. |
| `HoldingDuration` | If open: `min((elapsedHours - _elapsedHoursAtEntry) / 100, 1)`; else `0`. |
| `CurrentDrawdown` | If `MaxEquity > 0`: `Clamp((MaxEquity - Equity(price)) / MaxEquity, 0, 1)`; else `0`. |

These slots are therefore **not** raw market data in the tensor consumed by the brain; they reflect portfolio state at decision time.

Note: `SignalIndex.HoldingDuration` is documented in `SignalIndex.cs` as “ticks since entry,” but `InjectAgentState` implements duration as **elapsed hours** since entry (`elapsedHours - _elapsedHoursAtEntry`), scaled by `/100` and capped at `1`. The `_ticksSinceEntry` field in `MarketAgent` is not written into this signal.

---

## 8. `SignalNormalizer`

- **Constructor**: `lookbackTicks` default **500**, `clip` default **1**. Per signal: `_mean[i]`, `_variance[i]` (initialized to **1**).
- **EMA decay**: `α = 2 / (lookbackTicks + 1)`.
- **Per tick** (after first initialization): `delta = x - mean`; `mean += α * delta`; `variance = (1-α)*variance + α*delta²`; `std = sqrt(variance)` after that update; `z = delta / std` if `std > 1e-8`, else `0`; output `Clamp(z, -clip, clip)`.
- **First `Normalize` call**: copies `raw` into `_mean`, sets variance to **1**, sets `_initialized`. For finite inputs, `delta` is initially zero so outputs are **0**; `mean`/`variance` then move on subsequent ticks.
- **NaN / Infinity**: normalized value **0**; early `continue` skips mean and variance updates for that index.

---

## 9. Backtest vs live

### `ZeroMaskLiveOnlySignals` (live only)

In `DataAggregator.TickAsync`, after derived computation, these raw values are forced to **0** before normalization:

- `BtcBidAskSpread` (6), `BtcOrderImbalance` (7)
- `NewsHeadlineSentiment` (25), `NewsVolume` (26), `RedditSentiment` (27), `RedditPostVolume` (28), `SocialBullBearRatio` (29)
- `FuturesPremium` (20)

This runs on the live aggregation path when feeds update. **`TickFromRaw`** does not apply this mask. Historical replay via `HistoricalDataStore.CandlesToSignals` does not call it; enriched backtests can supply non-zero values for masked slots unless cleared elsewhere.

### `RegimeTrend` derivation

| Mode | `RegimeTrend` (index 89) |
|------|---------------------------|
| **Live `DataAggregator`** | `Clamp(BtcMomentum / 0.10, -1, 1)` where `BtcMomentum` comes from hourly price queue (up to 12-hour lookback). |
| **Backtest `HistoricalDataStore.CandlesToSignals`** | `Clamp(BtcReturn24h / 0.10, -1, 1)` using the bar’s 24h return (`closes[i]` vs `closes[i-24]`). |

So the same index uses **different inputs** between live rolling momentum and backtest 24h return.

### Related backtest pipeline

`HistoricalDataStore` recomputes regime block from candles and overwrites `RegimeVolatility` / `RegimeTrend` / `RegimeChange` / `MarketStress` with its own `rollingVol` and stress formula aligned to `DataAggregator` math where noted. Optional `HistoricalSignalEnricher` fills many feed slots and multi-asset indices; it does not change the `RegimeTrend` line in `CandlesToSignals`, which always uses `BtcReturn24h` for index 89.

---

## 10. RSS / Reddit headline scoring (`VaderSentiment`)

Not part of `TechnicalIndicators`; used by `SentimentFeed` for token-level lexicon scores, optional intensifier/negation, bigram lexicon hits, and normalization `compound = totalScore / sqrt(totalScore² + 15)`, clamped to `[-1, 1]`.
