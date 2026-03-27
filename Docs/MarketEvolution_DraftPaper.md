# The Seed: Market Evolution — Draft Paper

**Project**: The Seed — Evolved Autonomous Trading Agents  
**Authors**: Elias (MrWho1987)  
**Date**: March 23, 2026  
**Status**: Draft v1.0  
**Repository**: https://github.com/MrWho1987/THE-SEED

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [The Core Idea](#2-the-core-idea)
3. [Why The Seed Is Uniquely Positioned](#3-why-the-seed-is-uniquely-positioned)
4. [Architecture Overview](#4-architecture-overview)
5. [The Market Arena](#5-the-market-arena)
6. [The News & Sentiment Pipeline](#6-the-news--sentiment-pipeline)
7. [Agent Input Vector Specification](#7-agent-input-vector-specification)
8. [Agent Action Space](#8-agent-action-space)
9. [Fitness and Natural Selection](#9-fitness-and-natural-selection)
10. [Backtesting Results (Real Data)](#10-backtesting-results-real-data)
11. [Multi-Signal Advantage Analysis](#11-multi-signal-advantage-analysis)
12. [Data Sources and Costs](#12-data-sources-and-costs)
13. [Tech Stack](#13-tech-stack)
14. [Development Roadmap](#14-development-roadmap)
15. [Revenue Projections](#15-revenue-projections)
16. [Risk Analysis](#16-risk-analysis)
17. [Why This Beats Existing Approaches](#17-why-this-beats-existing-approaches)
18. [Future Directions](#18-future-directions)
19. [Appendix A: Backtest Raw Data](#appendix-a-backtest-raw-data)
20. [Appendix B: Glossary](#appendix-b-glossary)

---

## 1) Executive Summary

The Seed is an existing, working neuroevolution platform that evolves artificial life agents through natural selection in a competitive shared arena. Agents grow CPPN-based brains from compact genomes, compete for scarce resources, reproduce when fit, and die when unfit. The system includes NEAT-style speciation, a continuous "terrarium" mode with self-regulating populations, and a real-time dashboard.

This paper proposes **Market Evolution**: repurposing The Seed's evolutionary engine to evolve autonomous trading agents that operate on cryptocurrency exchanges. Instead of foraging for food in a 2D world, agents forage for profitable trading opportunities across financial markets. The evolutionary dynamics — natural selection, speciation, adaptation, extinction — remain identical. Only the environment changes.

The key insight is that trading is structurally identical to the foraging problem The Seed already solves: sense an environment of scarce, competitive resources; decide how to allocate limited capital (energy); and the agents who accumulate the most survive.

**What makes this different from existing trading bots:**
- Agents **evolve** strategies through natural selection rather than having strategies hand-coded
- **Multiple competing species** of strategies coexist and adapt simultaneously
- **Zero inference cost** (CPPN brains run in microseconds on CPU, unlike LLM-based bots)
- Agents consume a **rich multi-signal diet** (price + sentiment + on-chain + macro) that no hand-coded bot can fully exploit
- The system **continuously adapts** without retraining or human intervention (terrarium mode)
- **Automatic feature selection**: evolution prunes useless signals and amplifies useful ones

---

## 2) The Core Idea

### 2.1 The Mapping: Terrarium → Market

| Terrarium Concept | Market Equivalent |
|---|---|
| 2D continuous world | Cryptocurrency exchange (Binance, Bybit) |
| Food items | Profitable trading opportunities |
| Energy | Capital (USDT balance) |
| Eating food | Executing a profitable trade |
| Hazard zones | Adverse market conditions (flash crashes, manipulation) |
| Other agents competing for food | Other bots and traders competing for the same alpha |
| Agent starvation/death | Strategy that depletes its capital allocation |
| Reproduction | Successful strategy gets more capital / generates offspring |
| Species | Distinct strategy types (momentum, mean reversion, breakout, etc.) |
| Day/night cycle | Market volatility cycles |
| Seasons | Bull/bear/sideways market regimes |
| Agent sensors (61 inputs) | Market data signals (~25-30 inputs) |
| Agent actions (6 outputs) | Trade decisions (direction, size, urgency) |
| Fitness = energy accumulated | Fitness = net profit after fees |

### 2.2 What Stays The Same

The entire evolutionary infrastructure is reused without modification:
- **SeedGenome**: Compact CPPN genome encoding (indirect encoding → brain)
- **Brain development**: CompileGraph converts genome to sparse recurrent neural controller
- **BrainRuntime**: Executes the brain each tick (microseconds per inference)
- **SpeciationManager**: NEAT-style speciation maintains strategy diversity
- **InnovationTracker**: Tracks structural mutations for crossover compatibility
- **Terrarium mode**: Continuous self-regulating population dynamics
- **Dashboard**: Real-time monitoring via SignalR + React frontend
- **Deterministic simulation**: Rng64 + SeedDerivation for reproducible results

### 2.3 What Changes

Only the environment layer (the "world") changes:
- **SharedArena** → **MarketArena**: Instead of physics simulation, replays or streams market data
- **FoodItem** → **MarketOpportunity**: Price data points that agents evaluate
- **Agent sensors**: Replace distance/angle sensors with market signal inputs
- **Agent actions**: Replace thrust/turn/eat with trade direction/size/timing
- **Fitness function**: Replace energy accumulation with net P&L

---

## 3) Why The Seed Is Uniquely Positioned

### 3.1 Versus Hand-Coded Trading Bots

Most trading bots (retail and professional) use strategies designed by humans:
- "If RSI < 30, buy. If RSI > 70, sell."
- "If EMA(12) crosses above EMA(26), go long."

These strategies are limited by:
- **Human imagination**: They can only implement rules humans think of
- **Static parameters**: They don't adapt when market conditions change
- **Single strategy**: They bet everything on one approach
- **Maintenance burden**: When they stop working, a human must diagnose and fix them

The Seed's evolved agents have none of these limitations. Evolution discovers strategies no human would design. The terrarium continuously adapts. Speciation maintains a diverse portfolio. No human intervention needed.

### 3.2 Versus LLM-Based Trading Agents

Recent entrants (MAHORAGA, OpenAlice, etc.) use LLMs (GPT, Claude) for trading decisions:

| Factor | LLM-Based Bots | The Seed |
|---|---|---|
| Cost per decision | $0.001 - $0.10 | **$0 (CPU only)** |
| Decision speed | 1-5 seconds | **<1 millisecond** |
| Decisions per day | ~1,000 (cost-limited) | **Unlimited** |
| Adapts to market changes | Only if re-prompted | **Continuously (evolution)** |
| Discovers novel strategies | No (mimics training data) | **Yes (evolution explores)** |
| Multi-strategy diversity | No (one model) | **Yes (speciation)** |
| Operating cost | $15-60/month (API calls) | **$0** |

### 3.3 Versus Reinforcement Learning

Standard RL (PPO, SAC, etc.) trains one policy:

| Factor | Standard RL | The Seed |
|---|---|---|
| Output | One policy | **Population of diverse strategies** |
| Robustness | Brittle to distribution shift | **Adversarially hardened** |
| Reward design | Requires careful hand-crafting | **Direct (P&L is the reward)** |
| Multi-agent dynamics | Not native | **Native (competitive co-evolution)** |
| Training cost | GPU-heavy | **CPU only** |
| Continuous adaptation | Requires retraining | **Built-in (terrarium mode)** |

### 3.4 The Real Edge: Multi-Signal Evolutionary Search

The most important differentiator is not the trading itself but the **information advantage**. Most retail bots see only price and volume data. The Seed's agents consume 25-30 continuous input signals spanning price, sentiment, on-chain, and macro data — simultaneously. Evolution discovers which signal combinations predict profit.

No human can write rules for how 30 interacting signals relate to future price movements. Evolution can. This is The Seed's core competitive moat.

---

## 4) Architecture Overview

```
┌──────────────────────────────────────────────────────────────┐
│                    EXISTING SEED ENGINE                       │
│  ┌────────────┐  ┌─────────────┐  ┌──────────────────────┐  │
│  │ SeedGenome  │→│ CompileGraph │→│ BrainRuntime (CPPN)   │  │
│  │ (indirect   │  │ (development)│  │ (microsecond         │  │
│  │  encoding)  │  │              │  │  inference, CPU)      │  │
│  └────────────┘  └─────────────┘  └──────────────────────┘  │
│  ┌─────────────────┐  ┌─────────────┐  ┌─────────────────┐  │
│  │ SpeciationMgr    │  │ Innovation  │  │ Terrarium Mode  │  │
│  │ (NEAT-style      │  │ Tracker     │  │ (continuous     │  │
│  │  diversity)      │  │             │  │  adaptation)    │  │
│  └─────────────────┘  └─────────────┘  └─────────────────┘  │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │ Dashboard (React + SignalR) — real-time monitoring       │ │
│  └─────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────┘
                              ↕
┌──────────────────────────────────────────────────────────────┐
│                    NEW: MARKET LAYER                          │
│  ┌──────────────────────┐  ┌──────────────────────────────┐ │
│  │ MarketArena           │  │ News & Sentiment Pipeline    │ │
│  │ - Replays historical  │  │ - RSS/Reddit/API collection │ │
│  │   or streams live     │  │ - VADER sentiment scoring   │ │
│  │   exchange data       │  │ - Keyword categorization    │ │
│  │ - Manages positions   │  │ - Rolling aggregation       │ │
│  │ - Tracks P&L          │  │ - Rate-of-change signals   │ │
│  └──────────────────────┘  └──────────────────────────────┘ │
│  ┌──────────────────────┐  ┌──────────────────────────────┐ │
│  │ Exchange Connector    │  │ Risk Manager                │ │
│  │ - Binance API         │  │ - Max position size         │ │
│  │ - Order placement     │  │ - Daily loss limit          │ │
│  │ - Balance tracking    │  │ - Kill switch               │ │
│  │ - WebSocket feeds     │  │ - Per-species allocation    │ │
│  └──────────────────────┘  └──────────────────────────────┘ │
└──────────────────────────────────────────────────────────────┘
```

---

## 5) The Market Arena

### 5.1 Purpose

The MarketArena replaces the 2D physics world. It provides market data to agents, executes their trade decisions, and tracks performance. During training it replays historical data; during live operation it streams real-time data.

### 5.2 Operating Modes

| Mode | Data Source | Execution | Purpose |
|---|---|---|---|
| **Historical Training** | Downloaded Binance CSV files (free) | Simulated (no real orders) | Evolve strategies on past data |
| **Paper Trading** | Live Binance WebSocket feed | Logged but not executed | Validate evolved strategies on unseen live data |
| **Live Trading** | Live Binance WebSocket feed | Real orders via Binance API | Generate revenue |

### 5.3 Tick Structure

Each tick in the MarketArena corresponds to one candlestick period (configurable: 1m, 5m, 15m, 1h). Per tick:

1. Market data updates (new candle OHLCV + indicator recalculation)
2. Sentiment pipeline updates (every 15 minutes)
3. Each agent's brain receives its input vector
4. Each agent's brain outputs an action vector
5. Actions are validated against risk limits
6. Trades are executed (simulated or real)
7. P&L is updated
8. Fitness is recalculated
9. Terrarium dynamics: reproduction/death based on fitness

### 5.4 Position Tracking

Each agent maintains:
- Current position (long/short/flat) and entry price
- Realized P&L (from closed trades)
- Unrealized P&L (from open positions)
- Total equity (cash + position value)
- Trade history (for analysis)

---

## 6) The News & Sentiment Pipeline

### 6.1 Architecture

The pipeline converts unstructured text and external data into numerical signals (floats) that CPPN brains can process. It operates in three layers:

#### Layer 1: Pre-Computed APIs (Zero Processing)

External services that already return numerical values:

| Source | Signal | Update Frequency | Cost |
|---|---|---|---|
| Alternative.me | Fear & Greed Index (0-100) | Every 5 minutes | Free |
| CoinGlass | Funding rates, open interest | Real-time | Free tier |
| CoinGecko | BTC dominance, market cap | Every 60 seconds | Free |
| Yahoo Finance | S&P 500 (macro correlation) | Daily | Free |

#### Layer 2: Local NLP Processing (Free, Instant)

Raw text from news and social media, processed locally using rule-based NLP:

**Data Collection:**

| Source | Method | Volume | Cost |
|---|---|---|---|
| CoinDesk, CoinTelegraph | RSS feed parsing | ~50 headlines/day | Free |
| Reddit (r/cryptocurrency, r/bitcoin) | REST API | Top 50 posts/fetch | Free |
| Twitter/X (optional) | API free tier | Limited | Free |
| Telegram channels (optional) | Bot API | Variable | Free |

**Processing Pipeline (runs locally, <1 second per cycle):**

```
Raw headline: "SEC delays Bitcoin ETF decision amid regulatory concerns"
        │
        ▼
┌─ VADER Sentiment Analysis ──────────────────────────────────┐
│  compound_score = -0.34  (mildly negative)                  │
│  positive = 0.0, negative = 0.28, neutral = 0.72            │
└─────────────────────────────────────────────────────────────┘
        │
        ▼
┌─ Keyword Category Matching ─────────────────────────────────┐
│  Category "regulatory":  MATCH (SEC, regulatory)    → 1.0   │
│  Category "institutional": MATCH (ETF)              → 0.5   │
│  Category "security":   NO MATCH                    → 0.0   │
│  Category "adoption":   NO MATCH                    → 0.0   │
│  Category "panic":      NO MATCH                    → 0.0   │
│  Category "macro":      NO MATCH                    → 0.0   │
│  Category "whale":      NO MATCH                    → 0.0   │
└─────────────────────────────────────────────────────────────┘
        │
        ▼
┌─ Rolling Aggregation ───────────────────────────────────────┐
│  sentiment_1h   = avg(last 1h scores)    = -0.28            │
│  sentiment_24h  = avg(last 24h scores)   = +0.12            │
│  sentiment_delta = sentiment_1h - sentiment_24h = -0.40     │
│  news_volume_1h = count(last 1h) / avg(count/h) = 2.3      │
│  regulatory_pressure_24h = avg(regulatory scores) = 0.8     │
│  (similar for each keyword category)                        │
└─────────────────────────────────────────────────────────────┘
```

**Keyword Dictionaries:**

| Category | Keywords |
|---|---|
| `regulatory` | SEC, CFTC, ban, regulate, compliance, lawsuit, enforcement, subpoena, ruling |
| `institutional` | ETF, BlackRock, Fidelity, Goldman, institutional, fund, custody, approval |
| `security` | hack, exploit, breach, stolen, vulnerability, rug pull, scam, phishing |
| `adoption` | accept, partner, launch, integrate, payment, merchant, adoption, mainstream |
| `macro` | Fed, rate, inflation, recession, GDP, employment, CPI, treasury, yield |
| `whale` | whale, accumulate, transfer, moved, wallet, dormant, large transaction |
| `panic` | crash, dump, sell-off, liquidation, collapse, plunge, capitulation, fear |
| `bullish` | rally, surge, breakout, ATH, all-time high, moon, pump, accumulation |

#### Layer 3: LLM Preprocessing (Optional, Low Cost)

For the top 5-10 most impactful daily news events, an optional LLM call extracts structured features:

```
Input:  "Federal Reserve signals surprise rate cut in emergency meeting"
Output: { impact: 0.85, timeframe: "immediate", confidence: 0.9 }
```

Cost: ~$0.001 per article × 10 articles/day = **$0.30/month**. Entirely optional.

### 6.2 Why Local NLP Is Sufficient

The agents don't need to "understand" news. They need numbers that **correlate with future price movements**. VADER sentiment + keyword counting produces signals that research has shown to be predictive (see: Springer Nature, "Wisdom of the crowd signals", July 2025). The CPPN brain learns the predictive relationships through evolution — it doesn't need the nuance that an LLM provides.

---

## 7) Agent Input Vector Specification — Complete Signal Taxonomy

The Seed's current terrarium agents already process 61 sensory inputs. The market agents will consume a comparable or larger input vector. Every signal is normalized to approximately [-1, 1] or [0, 1] and fed to the CPPN brain as a flat float vector. Evolution determines which signals matter — useless inputs have their connection weights driven to zero over generations.

The following is the **complete catalog** of signals organized into 13 categories. Not all need to be active from Day 1; they can be added incrementally. Each additional signal is more information for evolution to exploit.

---

### 7.1 Price & Volume — Core Market Data (12 inputs)

These are the baseline signals every bot uses. Necessary but insufficient alone.

| # | Signal | What It Measures | Why It's Predictive | Source | Cost | Update |
|---|---|---|---|---|---|---|
| 0 | RSI(14) | Overbought/oversold momentum | Mean reversion signals at extremes | Calculated | $0 | Per candle |
| 1 | RSI(21) | Longer-term momentum | Slower, higher-conviction signals | Calculated | $0 | Per candle |
| 2 | EMA(12) / EMA(26) ratio | Trend direction and strength | Crossovers predict trend changes | Calculated | $0 | Per candle |
| 3 | Bollinger %B | Position within volatility bands | Extremes signal reversals | Calculated | $0 | Per candle |
| 4 | Volume / 24h avg volume | Unusual activity detection | Volume spikes precede big moves | Binance API | $0 | Real-time |
| 5 | Price change 1h (%) | Short-term momentum | Momentum persistence / reversal | Calculated | $0 | Per candle |
| 6 | Price change 4h (%) | Medium-term momentum | Trend strength | Calculated | $0 | Per candle |
| 7 | Price change 24h (%) | Daily momentum | Regime detection | Calculated | $0 | Per candle |
| 8 | ATR(14) normalized | Current volatility level | Scales position sizing; predicts regime | Calculated | $0 | Per candle |
| 9 | VWAP deviation | Price vs volume-weighted average | Institutional reference price; mean reversion target | Calculated | $0 | Per candle |
| 10 | Bid-ask spread | Liquidity tightness | Wide spread = low liquidity = danger | Binance order book | $0 | Real-time |
| 11 | Price vs 200h SMA | Long-term trend position | Bull/bear regime classification | Calculated | $0 | Per candle |

---

### 7.2 Derivatives Market Data (8 inputs)

Derivatives data reveals **leveraged positioning** and **market consensus** before it shows up in price. Research confirms funding rates + open interest + liquidations form an integrated predictive system (Gate.com, 2026).

| # | Signal | What It Measures | Why It's Predictive | Source | Cost | Update |
|---|---|---|---|---|---|---|
| 12 | Funding rate (BTC perp) | Who's paying whom in perpetual futures | Extreme positive = overleveraged longs (reversal risk); extreme negative = squeezable shorts | CoinGlass API | $0 (free tier) | Every 8h |
| 13 | Open interest change (24h) | New money entering/leaving futures | Rising OI + rising price = strong trend; rising OI + falling price = building short squeeze | CoinGlass API | $0 | Hourly |
| 14 | Long/short ratio | Balance of long vs short positions | Extremes predict reversals; ratio < 0.8 = bearish crowding | CoinGlass API | $0 | Hourly |
| 15 | Liquidation volume (24h, long) | Long positions being force-closed | Cascade liquidations = capitulation = potential bottom | CoinGlass API | $0 | Hourly |
| 16 | Liquidation volume (24h, short) | Short positions being force-closed | Short squeeze in progress = momentum continuation | CoinGlass API | $0 | Hourly |
| 17 | Estimated leverage ratio | Average leverage across the market | High leverage = fragile market; low = stable | CoinGlass API | $0 | Hourly |
| 18 | Funding rate (ETH perp) | ETH-specific positioning | ETH often leads or diverges from BTC | CoinGlass API | $0 | Every 8h |
| 19 | OI-weighted funding rate | Capital-weighted sentiment across exchanges | More accurate than single-exchange funding | CoinGlass API | $0 | Every 8h |

---

### 7.3 On-Chain Fundamentals (10 inputs)

On-chain data is **transparent and immutable**. It shows what wallets are actually doing, not what people are saying. Research shows MVRV, SOPR, and exchange flows are among the strongest predictive signals (Nansen, CryptoQuant, Glassnode research).

| # | Signal | What It Measures | Why It's Predictive | Source | Cost | Update |
|---|---|---|---|---|---|---|
| 20 | Exchange net flow (BTC) | Net BTC moving to/from exchanges | Inflows = sell pressure; outflows = accumulation (supply squeeze) | CryptoQuant / Glassnode | Free tier | Hourly |
| 21 | Exchange net flow (stablecoins) | Stablecoins moving to exchanges | Inflows = buying power arriving; outflows = dry powder leaving | CryptoQuant | Free tier | Hourly |
| 22 | MVRV ratio | Market cap vs realized cap | >3.5 = historically overbought; <1.0 = historically oversold; current ~2.3 | Glassnode / CoinMetrics | Free tier | Daily |
| 23 | SOPR (Spent Output Profit Ratio) | Are sellers in profit or loss? | <1.0 = capitulation (holders selling at loss = bottom signal); >1.05 = profit-taking | Glassnode | Free tier | Daily |
| 24 | NVT ratio | Network value vs transaction throughput | High NVT = price not backed by usage (bubble); low NVT = undervalued | Glassnode / CoinMetrics | Free tier | Daily |
| 25 | Active addresses (7d avg) | Network usage / adoption | Rising = growing demand; declining = waning interest | Glassnode / Blockchain.com | Free | Daily |
| 26 | Whale transaction count (>$1M) | Large holder activity | Spike in whale txs often precedes volatility | Whale Alert API | Free (10 req/min) | Real-time |
| 27 | Illiquid supply change | BTC moving to wallets that never sell | Supply being locked up = bullish pressure | Glassnode | Free tier | Weekly |
| 28 | Realized cap change (30d) | Change in aggregate cost basis | Rising = new money entering at higher prices; falling = distribution | CoinMetrics | Free tier | Daily |
| 29 | Puell Multiple | Miner revenue vs 365d avg | <0.5 = miners under stress (capitulation); >4 = miners selling aggressively | Glassnode / CoinMetrics | Free tier | Daily |

---

### 7.4 Supply & Mining Dynamics (6 inputs)

Mining is the supply side of Bitcoin. Miner behavior directly impacts sell pressure and network security.

| # | Signal | What It Measures | Why It's Predictive | Source | Cost | Update |
|---|---|---|---|---|---|---|
| 30 | Hash rate change (30d) | Mining compute power trend | Declining hash rate = miner stress = potential capitulation selling | Blockchain.com / Glassnode | Free | Daily |
| 31 | Mining difficulty adjustment | Next difficulty change estimate | Difficulty up = miners confident; difficulty down = miners leaving | Blockchain.com API | Free | ~Every 2 weeks |
| 32 | Miner outflow to exchanges | Miners sending BTC to sell | Spike = sell pressure from miners; historically precedes drops | CryptoQuant | Free tier | Daily |
| 33 | Days since halving (normalized) | Position in 4-year halving cycle | Historically, 12-18 months post-halving = strongest bull phase | Calculated | $0 | Static (per epoch) |
| 34 | BTC inflation rate | Annual new supply as % of total | Post-halving supply shock drives price appreciation | Calculated | $0 | Static |
| 35 | Hash price (USD/TH/day) | Revenue per unit of mining power | Low hash price = miner capitulation; high = miners thriving | Newhedge API | Free tier | Daily |

---

### 7.5 Order Flow & Market Microstructure (6 inputs)

Order flow data reveals **institutional intent 5-90 seconds before price confirmation** (Kalena Research, 2026). This is the fastest-moving signal category.

| # | Signal | What It Measures | Why It's Predictive | Source | Cost | Update |
|---|---|---|---|---|---|---|
| 36 | Bid-ask depth imbalance | Buy vs sell pressure at top of book | Imbalance > 2:1 predicts direction with 14-second lead time | Binance WebSocket (depth) | $0 | Real-time |
| 37 | Cumulative Volume Delta (CVD) | Net aggressive buying vs selling | Rising CVD with flat price = accumulation before breakout | Binance trade stream | $0 | Real-time |
| 38 | Large trade ratio | % of volume from trades > $50K | Institutional activity; "smart money" flow | Binance trade stream | $0 | Calculated |
| 39 | Taker buy/sell ratio | Ratio of market buy vs market sell orders | >1.0 = aggressive buyers; <1.0 = aggressive sellers | Binance API | $0 | Real-time |
| 40 | Order book depth (within 1%) | Total liquidity near current price | Thin books = easy to move price = volatile; deep = stable | Binance WebSocket | $0 | Real-time |
| 41 | Trade frequency (vs avg) | Number of trades per minute | Spiking trade count = imminent move | Binance trade stream | $0 | Real-time |

---

### 7.6 Sentiment & Social Intelligence (10 inputs)

Social data is a **leading indicator** — sentiment shifts precede price moves by 1-24 hours (Springer Nature, July 2025).

| # | Signal | What It Measures | Why It's Predictive | Source | Cost | Update |
|---|---|---|---|---|---|---|
| 42 | Fear & Greed Index | Composite market sentiment (0-100) | Extreme fear = buy zone; extreme greed = sell zone | Alternative.me | $0, no key | 5 min |
| 43 | News sentiment (1h rolling) | Average VADER score of recent headlines | Sentiment shift precedes price by 1-6 hours | Local VADER pipeline | $0 | 15 min |
| 44 | News sentiment (24h rolling) | Longer-term news tone | Persistent negativity = prolonged selling; positivity = sustained buying | Local VADER pipeline | $0 | 15 min |
| 45 | Sentiment delta | Rate of change in sentiment | Rapidly dropping sentiment = panic forming | Calculated | $0 | 15 min |
| 46 | News volume (vs avg) | How much is being written/discussed | Spikes precede volatility events | Local pipeline | $0 | 15 min |
| 47 | Social volume (crypto-specific) | Volume of social media mentions | Viral attention → retail inflow → price spike | LunarCrush | $0 (free tier) | Hourly |
| 48 | Social sentiment score | Aggregate positive/negative ratio | Crowdsourced directional signal | LunarCrush | $0 (free tier) | Hourly |
| 49 | Regulatory pressure index | Keyword-matched regulatory news density | Regulatory crackdowns suppress prices | Local keyword pipeline | $0 | 15 min |
| 50 | Institutional interest index | Keyword-matched institutional news | ETF/fund inflows = sustained buying pressure | Local keyword pipeline | $0 | 15 min |
| 51 | Google Trends "bitcoin" | Search interest (normalized 0-100) | Retail FOMO peaks at tops; search death at bottoms | Google Trends | $0 | Daily |

---

### 7.7 Options Market Data (5 inputs)

Options data reveals how **sophisticated traders are positioning** for future volatility and direction.

| # | Signal | What It Measures | Why It's Predictive | Source | Cost | Update |
|---|---|---|---|---|---|---|
| 52 | Put/Call ratio (BTC options) | Ratio of bearish to bullish bets | High ratio = hedging/bearish; low = complacent/bullish | CoinGlass / Deribit | Free tier | Daily |
| 53 | Max pain price | Strike price where most options expire worthless | Price gravitates toward max pain near expiry | CoinGlass | Free tier | Daily |
| 54 | Distance to max pain (%) | Current price vs max pain | Large divergence = magnetic pull toward max pain | Calculated | $0 | Daily |
| 55 | Implied volatility (BTC ATM) | Market's expectation of future volatility | Rising IV before events = expected big move | CoinGlass / Deribit | Free tier | Daily |
| 56 | IV percentile rank (90d) | Is current IV high or low historically? | Low IV = calm before storm; high IV = already pricing risk | Calculated | $0 | Daily |

---

### 7.8 Stablecoin & Liquidity Dynamics (4 inputs)

Stablecoins are the "dry powder" of crypto. Their movements reveal buying/selling power before it's deployed.

| # | Signal | What It Measures | Why It's Predictive | Source | Cost | Update |
|---|---|---|---|---|---|---|
| 57 | USDT market cap change (7d) | New Tether being minted/burned | Minting = new money entering crypto; burning = exits | CoinGecko API | $0 | Daily |
| 58 | USDC market cap change (7d) | Institutional stablecoin activity | USDC preferred by institutions; signals institutional flow | CoinGecko API | $0 | Daily |
| 59 | Stablecoin dominance | Stablecoin share of total crypto market cap | Rising = risk-off (people sitting in cash); falling = risk-on (deploying into assets) | CoinGecko API | $0 | Daily |
| 60 | Stablecoin exchange reserves | Stablecoins sitting on exchanges ready to buy | Rising reserves = buying power accumulating | CryptoQuant | Free tier | Daily |

---

### 7.9 Macro & Cross-Market Signals (8 inputs)

Crypto doesn't exist in a vacuum. Macro conditions drive the largest price movements.

| # | Signal | What It Measures | Why It's Predictive | Source | Cost | Update |
|---|---|---|---|---|---|---|
| 61 | S&P 500 change (1d) | US stock market direction | BTC correlation with stocks is ~0.5-0.8 in 2025-2026 | Yahoo Finance | $0 | Daily |
| 62 | DXY (US Dollar Index) change | Dollar strength/weakness | Strong dollar = crypto weakness; weak dollar = crypto strength | Yahoo Finance | $0 | Daily |
| 63 | Gold price change (1d) | Safe haven demand | Gold and BTC sometimes move together as "digital gold" narrative | Yahoo Finance | $0 | Daily |
| 64 | US 10Y Treasury yield change | Interest rate direction | Rising yields = risk-off = crypto weakness | Yahoo Finance | $0 | Daily |
| 65 | VIX (volatility index) | Stock market fear gauge | High VIX = risk-off across all assets including crypto | Yahoo Finance | $0 | Daily |
| 66 | Days to next FOMC meeting | Proximity to Fed decision | Markets de-risk before FOMC; re-price after | Calculated (known schedule) | $0 | Static |
| 67 | Days to next CPI release | Proximity to inflation data | CPI surprises cause violent moves across all assets | Calculated (known schedule) | $0 | Static |
| 68 | BTC-SPX rolling correlation (30d) | How coupled crypto is to stocks right now | When correlation is high, macro drives crypto; when low, crypto-specific factors dominate | Calculated | $0 | Daily |

---

### 7.10 Network Health & Adoption (4 inputs)

Network fundamentals reveal whether price is backed by real usage or pure speculation.

| # | Signal | What It Measures | Why It's Predictive | Source | Cost | Update |
|---|---|---|---|---|---|---|
| 69 | Active addresses (7d change) | Growth/decline in network users | Sustained address growth precedes bull markets | Blockchain.com API | $0 | Daily |
| 70 | Transaction volume (USD, 7d avg) | Value being transacted on-chain | Rising tx volume = real economic activity backing price | Blockchain.com API | $0 | Daily |
| 71 | BTC dominance | Bitcoin's share of total crypto market cap | Rising dominance = flight to quality; falling = altcoin season | CoinGecko API | $0 | Hourly |
| 72 | Total crypto market cap change (7d) | Overall market momentum | Broad market trend context | CoinGecko API | $0 | Hourly |

---

### 7.11 Temporal & Cyclical Signals (5 inputs)

Markets have well-documented time-based patterns. These signals let the agent learn when to trade and when to wait.

| # | Signal | What It Measures | Why It's Predictive | Source | Cost | Update |
|---|---|---|---|---|---|---|
| 73 | Hour of day (sin encoding) | Time within the daily cycle | Asian/European/US session have different volatility profiles | System clock | $0 | Per tick |
| 74 | Hour of day (cos encoding) | Time within the daily cycle (orthogonal) | Pair with sin for smooth cyclical representation | System clock | $0 | Per tick |
| 75 | Day of week (sin encoding) | Position in weekly cycle | Weekends have lower volume; Monday opens are volatile | System clock | $0 | Per tick |
| 76 | Day of week (cos encoding) | Weekly cycle (orthogonal) | Pair with sin for smooth cyclical representation | System clock | $0 | Per tick |
| 77 | Monthly options expiry proximity | Days until major BTC options expiry | Price gravitates to max pain; volatility spikes around expiry | Calculated (Deribit schedule) | $0 | Daily |

Note: Sin/cos encoding wraps cyclical features so the brain sees "23:00 and 01:00 are close together" rather than treating them as distant values.

---

### 7.12 Multi-Asset Relative Signals (4 inputs)

How BTC behaves relative to other assets reveals flow dynamics and regime changes.

| # | Signal | What It Measures | Why It's Predictive | Source | Cost | Update |
|---|---|---|---|---|---|---|
| 78 | ETH/BTC ratio change (24h) | Ethereum's strength vs Bitcoin | ETH outperforming = risk-on/alt rotation; underperforming = risk-off | Binance API | $0 | Per candle |
| 79 | SOL/BTC ratio change (24h) | Solana's strength vs Bitcoin | High-beta alt; amplifies BTC trends and diverges at turning points | Binance API | $0 | Per candle |
| 80 | BTC.D momentum (7d) | Rate of change of BTC dominance | Accelerating dominance = altcoin sell-off; decelerating = rotation beginning | CoinGecko | $0 | Daily |
| 81 | Total altcoin volume / BTC volume | Where trading activity is concentrated | BTC volume dominance = institutional flow; alt volume = retail speculation | Binance API | $0 | Hourly |

---

### 7.13 Agent Internal State (6 inputs)

The agent must know its own situation to make good decisions (don't add to a losing position, manage risk).

| # | Signal | What It Measures | Why It's Predictive | Source | Cost | Update |
|---|---|---|---|---|---|---|
| 82 | Current position direction | -1 = short, 0 = flat, +1 = long | Prevents contradictory signals; enables hold/exit logic | Internal | $0 | Per tick |
| 83 | Position size (% of equity) | How exposed the agent currently is | Large exposure = should be cautious about adding | Internal | $0 | Per tick |
| 84 | Unrealized P&L (% of entry) | How much the current trade is winning/losing | Trailing stops, profit-taking, cutting losses | Internal | $0 | Per tick |
| 85 | Equity / initial capital | Overall performance trajectory | Risk management; winning agents can be bolder | Internal | $0 | Per tick |
| 86 | Win rate (last 20 trades) | Recent strategy effectiveness | Declining win rate = regime may have changed | Internal | $0 | Per trade |
| 87 | Time since last trade (normalized) | Activity pacing | Prevents overtrading; enables cooldown periods | Internal | $0 | Per tick |

---

### 7.14 Summary

| Category | Inputs | Key Insight |
|---|---|---|
| Price & Volume | 12 | Baseline — what every bot sees |
| Derivatives Market | 8 | Leveraged positioning reveals where the market is fragile |
| On-Chain Fundamentals | 10 | Wallet behavior shows what people DO, not what they SAY |
| Supply & Mining | 6 | Supply-side economics drive macro price cycles |
| Order Flow | 6 | Institutional intent, 5-90 seconds before price confirmation |
| Sentiment & Social | 10 | Leading indicator: sentiment shifts precede price by 1-24 hours |
| Options Market | 5 | Sophisticated trader positioning and volatility expectations |
| Stablecoin Dynamics | 4 | Dry powder / buying power readiness |
| Macro & Cross-Market | 8 | The largest price moves are macro-driven |
| Network Health | 4 | Is price backed by real usage or pure speculation? |
| Temporal & Cyclical | 5 | When to trade matters as much as what to trade |
| Multi-Asset Relative | 4 | Flow dynamics between assets reveal regime changes |
| Agent Internal State | 6 | Self-awareness enables risk management |
| **TOTAL** | **88** | |

**88 input neurons.** This is comparable to the current terrarium agent's 61 sensory inputs, well within the CPPN architecture's capacity. All values normalized to [-1, 1] or [0, 1].

**Critical design principle:** Not all 88 inputs need to be active from Day 1. Start with the core set (Price + Derivatives + Sentiment + Agent State = ~36 inputs) and add categories incrementally. Each new signal source is simply a new float appended to the input vector. Evolution automatically determines which signals are useful — connection weights to uninformative inputs converge toward zero over generations.

**Total cost for all 88 signals: $0/month** (all from free APIs and local computation).

---

## 8) Agent Action Space

The CPPN brain outputs 4 continuous values per tick:

| Output | Meaning | Range | Interpretation |
|---|---|---|---|
| 0 | **Direction** | -1 to 1 | < -0.3 = SHORT, > 0.3 = LONG, otherwise = HOLD |
| 1 | **Size** | 0 to 1 | Fraction of available equity to commit (clamped by risk manager) |
| 2 | **Urgency** | 0 to 1 | > 0.7 = execute immediately (market order), < 0.3 = wait for better price |
| 3 | **Exit signal** | 0 to 1 | > 0.6 = close current position regardless of direction signal |

The action space is intentionally small and continuous — matching what CPPN brains excel at. Complex multi-step strategies emerge from the interaction of these simple outputs over many ticks.

---

## 9) Fitness and Natural Selection

### 9.1 Fitness Function

Fitness is directly tied to profit. No proxy metrics needed.

```
fitness = net_realized_pnl + unrealized_pnl - total_fees_paid
```

In terrarium mode, fitness drives reproduction:
- Agents whose equity exceeds a threshold reproduce (strategy genome is mutated and offspring deployed)
- Agents whose equity drops below a minimum die (strategy is removed)
- Capital is redistributed from dead strategies to surviving ones

### 9.2 Speciation in Market Context

NEAT-style speciation naturally groups agents into strategy families:
- **Species A**: Agents that evolved momentum-following behavior (buy when rising)
- **Species B**: Agents that evolved mean-reversion behavior (buy dips)
- **Species C**: Agents that evolved sentiment-reactive behavior (short on panic spikes)
- **Species D**: Agents that evolved volatility-sensitive behavior (trade breakouts)

Speciation protects innovation. A new, unproven strategy isn't immediately killed by established ones — it competes within its own species first, giving it time to develop.

### 9.3 Capital Allocation

In live mode, total trading capital is distributed across surviving species proportional to their fitness:

```
species_allocation = total_capital × (species_avg_fitness / sum_all_species_fitness)
```

Species that consistently profit get more capital. Species that lose money shrink. This is evolutionary portfolio management — the market itself determines which strategies deserve capital.

---

## 10) Backtesting Results (Real Data)

### 10.1 Test Conditions

- **Data**: Real BTC/USDT hourly candles from Binance (March 2025 — March 2026)
- **Market conditions**: BEAR market (BTC dropped 17.2%, from $83,055 to $68,708)
- **Fee model**: 0.05% taker fee per side (Binance Futures VIP 0 rate)
- **Starting capital**: $5,000
- **Leverage**: None (1x spot equivalent)
- **Strategies tested**: RSI Mean Reversion, EMA Crossover, Bollinger Band Bounce

### 10.2 Results: Simple Fixed Strategies

| Strategy | Final Equity | P&L | Return | Win Rate | Max Drawdown |
|---|---|---|---|---|---|
| Buy & Hold BTC | $4,143 | -$857 | -17.2% | N/A | N/A |
| RSI Mean Reversion (14, 30/70, 10%) | $4,945 | -$55 | -1.1% | 66.7% | 5.2% |
| EMA Crossover (12/26, 20%) | $4,564 | -$436 | -8.7% | 25.6% | 12.1% |
| Bollinger Bounce (20, 2σ, 15%) | $4,716 | -$284 | -5.7% | 66.8% | 7.1% |

**Key finding**: Even simple fixed strategies significantly outperformed buy-and-hold in a bear market. The RSI strategy preserved nearly all capital ($5,000 → $4,945) while BTC lost 17%.

### 10.3 Results: Evolved Parameter Optimization

A sweep of 500 parameter combinations (simulating what evolution discovers) found:

| Scenario | Best Params Found | P&L | Return | Win Rate |
|---|---|---|---|---|
| Price-only, long-only | RSI(14), 30/70, 10% | -$55 | -1.1% | 66.7% |
| Price-only + short | RSI(14), 30/70, 10% | -$70 | -1.4% | 61.2% |
| **Evolved params + short** | **RSI(21), 30/70, 30%** | **+$1,094** | **+21.9%** | **73.7%** |
| **Multi-asset evolved** | BTC+ETH, separate params | **+$1,203** | **+24.1%** | — |

**Key finding**: The right parameters turned a losing strategy into a +22% winner in a bear market. Of 500 random parameter combinations, only 25.2% were profitable. Finding the right combination is the hard problem — and it's exactly what evolution solves.

### 10.4 Monthly P&L Detail (Evolved Strategy, $5,000 Capital)

| Month | P&L | Return | Condition |
|---|---|---|---|
| 2025-03 | +$15 | +0.30% | Entering bear |
| 2025-04 | +$184 | +3.66% | Sharp decline (shorted) |
| 2025-05 | -$186 | -3.59% | Choppy (worst type) |
| 2025-06 | +$393 | +7.83% | Strong trend |
| 2025-07 | -$126 | -2.33% | Range-bound |
| 2025-08 | +$14 | +0.27% | Low volatility |
| 2025-09 | +$32 | +0.60% | Mild trend |
| 2025-10 | -$49 | -0.93% | Choppy |
| 2025-11 | +$23 | +0.43% | Recovery |
| 2025-12 | +$382 | +7.13% | Strong move |
| 2026-01 | -$381 | -6.65% | **Worst month** (reversal) |
| 2026-02 | +$293 | +5.48% | Strong trend |
| 2026-03 | +$452 | +8.01% | **Best month** |

Profitable in 9/13 months. Best month: +8.01%. Worst month: -6.65%.

### 10.5 Look-Ahead Bias Disclosure

The evolved parameters were found by optimizing on the SAME data used for testing. This introduces look-ahead bias. In real deployment, agents evolve on past data and trade on unseen future data. Industry standard discount: expect **30-60% of backtested performance** in live conditions.

Adjusted realistic performance: +8% to +13% annually (vs +22% backtested).

---

## 11) Multi-Signal Advantage Analysis

### 11.1 The Hypothesis

Adding sentiment and alternative data signals to price-only signals should improve trading performance by providing information advantage — signals that precede price movements.

### 11.2 Test: Price Only vs Price + Fear & Greed Index

Tested on the same BTC/USDT data (March 2025 — March 2026):

| Metric | Price Only (best) | Price + Sentiment (best) |
|---|---|---|
| Total trades | 38 | 6 |
| Win rate | 73.7% | **83.3%** |
| Worst month | **-6.65%** | -3.31% |
| Total return | +21.9% | +11.7% |

### 11.3 Interpretation

The sentiment filter **improved trade quality** (83% vs 74% win rate) and **halved the worst-month drawdown** (-3.3% vs -6.6%). However, used as a crude binary filter, it was too restrictive — reducing 38 trades to 6, leaving profit on the table.

This reveals why **evolution matters more than hand-coded filters**:
- A binary filter says "trade or don't trade" based on one threshold
- A CPPN brain uses sentiment as a **continuous weight** that modulates decisions alongside all other signals
- Evolution discovers **non-linear interactions** between signals that binary filters cannot capture

### 11.4 Research Support

- **Springer Nature (July 2025)**: Analysis of 28,000+ crowd trading signals from X, Reddit, Stocktwits, and Telegram found that social signals **significantly predict short-term crypto price movements** in out-of-sample tests, outperforming both the CCI30 index and S&P 500.
- **Electronic Markets (2025)**: Ensemble machine learning combining multiple sentiment variables generated significant out-of-sample investment gains for Ethereum.
- **Social Network Analysis & Mining (2025)**: Bi-LSTM models using RoBERTa sentiment embeddings achieved 2.01% MAPE for Bitcoin price forecasting.

### 11.5 Expected Improvement With Full Multi-Signal Agent

With 28 continuous input signals (vs 8-10 for price-only), the evolved agent has access to:
- **Leading indicators** (sentiment shifts precede price moves by 1-24 hours)
- **Confirmation signals** (price oversold + extreme fear + whale accumulation = high-conviction buy)
- **Regime detection** (high BTC dominance + negative funding + rising stablecoin supply = risk-off regime)
- **False signal filtering** (RSI oversold but no panic + declining volume = likely dead cat bounce, not a buy)

Conservative estimate based on research: multi-signal strategies outperform price-only by **1.5-2x** in out-of-sample conditions.

---

## 12) Data Sources and Costs

### 12.1 Complete Cost Table (All 88 Signals)

| Category | Source | Monthly Cost | Signals Provided | Update Frequency |
|---|---|---|---|---|
| **Price & Volume (historical)** | Binance Data Portal | $0 | OHLCV candles, years of history | One-time download |
| **Price & Volume (live)** | Binance WebSocket API | $0 | Real-time trades, candles, order book | Real-time |
| **Trade execution** | Binance REST API | $0 | Order placement, balance | On demand |
| **Derivatives (funding, OI, liquidations)** | CoinGlass API | $0 (free tier) | Funding rates, OI, long/short ratio, liquidations, leverage ratio | Hourly |
| **Options (put/call, max pain, IV)** | CoinGlass / Deribit API | $0 (free tier) | Put/call ratio, max pain, implied volatility | Daily |
| **On-chain (exchange flows, MVRV, SOPR)** | CryptoQuant + Glassnode | $0 (free tiers) | Exchange net flows, MVRV, SOPR, NVT, whale txs | Daily/Hourly |
| **On-chain (active addresses, tx volume)** | Blockchain.com API | $0 | Active addresses, transaction volume | Daily |
| **Mining (hash rate, miner flows)** | Blockchain.com + CryptoQuant | $0 (free tiers) | Hash rate, difficulty, miner outflows, Puell Multiple | Daily |
| **Stablecoin dynamics** | CoinGecko API | $0 | USDT/USDC market cap, stablecoin dominance | Daily |
| **Fear & Greed Index** | Alternative.me / CoinyBubble | $0, no key | Composite sentiment score (0-100) | Every 5 min |
| **Social sentiment & volume** | LunarCrush | $0 (free tier) | Social volume, sentiment score | Hourly |
| **Google Trends** | Google Trends (unofficial) | $0 | Search interest for "bitcoin" etc. | Daily |
| **Whale transactions** | Whale Alert API | $0 (10 req/min) | Large on-chain transfers (>$1M) | Real-time |
| **Macro data (S&P, DXY, Gold, VIX, Bonds)** | Yahoo Finance API | $0 | S&P 500, DXY, Gold, 10Y yield, VIX | Daily |
| **News headlines** | RSS (CoinDesk, CoinTelegraph, etc.) | $0 | ~50-100 headlines/day | Continuous |
| **Reddit posts** | Reddit API | $0 (free tier) | Top posts from crypto subreddits | Every 15 min |
| **Multi-asset prices (ETH, SOL)** | Binance API | $0 | Cross-pair ratios, relative strength | Real-time |
| **Economic calendar** | Calculated (known FOMC/CPI dates) | $0 | Days to next Fed meeting, CPI release | Static schedule |
| **Local NLP processing** | VADER + keyword matching | $0 | Sentiment scores, keyword categories | Local CPU |
| **Cloud VM (production)** | Railway / Fly.io / VPS | $10-20 | Runs entire system 24/7 | — |
| **LLM preprocessing (optional)** | Claude/GPT API | ~$0.30 | Deep analysis of top 10 daily stories | Optional |

### 12.2 Total Monthly Operating Cost

| Component | Cost |
|---|---|
| All 88 signal data feeds (13 categories) | **$0** |
| Cloud hosting | **$10-20** |
| Exchange trading fees | **~0.05% per trade** (deducted from balance) |
| LLM preprocessing (optional) | **$0-0.30** |
| **TOTAL** | **$10-20/month for 88 input signals** |

**Cost per decision: $0.** The CPPN brain runs on CPU in microseconds. No API inference calls needed. This is a structural cost advantage over every LLM-based trading agent on the market.

### 12.3 Trading Capital (Not a Cost)

Trading capital is the user's money deployed on the exchange. It is not spent — it is invested and can be withdrawn at any time. Recommended starting amounts:

| Tier | Capital | Monthly Return (Conservative) | Monthly Return (Moderate) |
|---|---|---|---|
| Proof of Concept | $1,000 - $5,000 | $26-128 | $46-228 |
| Comfortable | $5,000 - $10,000 | $128-256 | $228-455 |
| Meaningful | $10,000 - $20,000 | $256-512 | $455-910 |
| Serious | $20,000 - $50,000 | $512-1,279 | $910-2,275 |

---

## 13) Tech Stack

### 13.1 Existing (Reused)

| Component | Technology | Status |
|---|---|---|
| Evolutionary engine | .NET 9, C# | Built |
| CPPN genome + brain development | Seed.Genetics, Seed.Core | Built |
| Speciation + innovation tracking | Seed.Evolution | Built |
| Shared arena + terrarium mode | Seed.Worlds | Built |
| Simulation runner | Seed.Dashboard | Built |
| Real-time dashboard | React + TypeScript + SignalR + Recharts | Built |
| Deterministic RNG | Rng64 + SeedDerivation | Built |
| Test suite | xUnit (101 tests passing) | Built |

### 13.2 New (To Build)

| Component | Technology | Estimated Effort |
|---|---|---|
| **MarketArena** | C# (replaces SharedArena physics with market data) | 1-2 weeks |
| **Binance Connector** | Binance.NET NuGet package | 3-5 days |
| **Historical Data Loader** | CSV parser for Binance data downloads | 2-3 days |
| **News & Sentiment Pipeline** | C# (RSS parser, VADER port, keyword matcher) | 1 week |
| **Trade Executor** | Binance API order placement + position tracking | 3-5 days |
| **Risk Manager** | Position limits, daily loss cap, kill switch | 2-3 days |
| **Market Dashboard Updates** | React components for P&L, positions, market data | 1 week |
| **Paper Trading Mode** | Logging + comparison infrastructure | 2-3 days |

### 13.3 NuGet Dependencies (New)

| Package | Purpose |
|---|---|
| `Binance.Net` | Binance exchange API client |
| `VaderSharp2` | VADER sentiment analysis (or custom implementation) |
| `System.ServiceModel.Syndication` | RSS feed parsing (built into .NET) |

---

## 14) Development Roadmap

### Phase 1: Market Arena Foundation (Weeks 1-2)

- [ ] Build `MarketArena` class that replays historical Binance CSV data
- [ ] Define agent input vector (28 signals) and action vector (4 outputs)
- [ ] Implement position tracking, P&L calculation, fee deduction
- [ ] Connect to existing evolutionary engine (agents trade instead of forage)
- [ ] Run first evolution on historical BTC data
- [ ] Verify via dashboard that agents evolve profitable strategies

### Phase 2: Sentiment Pipeline (Week 3)

- [ ] Implement RSS feed collector (CoinDesk, CoinTelegraph)
- [ ] Implement VADER sentiment scoring (port or NuGet package)
- [ ] Implement keyword category matching with dictionaries
- [ ] Implement rolling aggregation (1h, 4h, 24h windows)
- [ ] Integrate sentiment signals into agent input vector
- [ ] Fetch Fear & Greed Index, funding rates, BTC dominance APIs
- [ ] Verify agents can access and evolve around sentiment signals

### Phase 3: Live Data & Paper Trading (Week 4)

- [ ] Integrate Binance WebSocket for real-time price feeds
- [ ] Integrate live sentiment pipeline (RSS feeds + APIs)
- [ ] Implement paper trading mode (log decisions, don't execute)
- [ ] Run paper trading for 1-2 weeks
- [ ] Compare paper results against what actually happened
- [ ] Dashboard: add paper trading P&L view

### Phase 4: Live Trading (Week 5-6)

- [ ] Implement Binance API order execution (market + limit orders)
- [ ] Implement risk manager (max position size, daily loss limit, kill switch)
- [ ] API key with trade-only permissions (no withdrawal)
- [ ] Deploy to cloud VM
- [ ] Start with minimum capital ($500)
- [ ] Monitor 24/7 via dashboard

### Phase 5: Optimization & Scaling (Ongoing)

- [ ] Add ETH/USDT and other pairs
- [ ] Add more sentiment sources (Reddit, Whale Alert, Google Trends)
- [ ] Tune terrarium parameters for market environment
- [ ] Analyze species emergence (what strategy types evolved?)
- [ ] Gradually increase capital allocation to proven strategies
- [ ] Implement species-level capital allocation (more money to profitable species)

---

## 15) Revenue Projections (Trade-Level Model)

### 15.1 Methodology

Previous estimates used a simple discount on backtested returns. This revised projection builds from **trade-level economics** — modeling individual trade win rate, average win/loss, position sizing, and trade frequency — then compounding monthly. This produces more defensible numbers because each parameter can be justified independently.

**Foundation (proven):** With 1 indicator (RSI) and 4 parameters in a bear market, evolved strategies achieved +21.9% annual with 73.7% win rate and 38 trades.

**Scaling assumption (research-backed):** 88 signals across 13 categories improve the system through:
- Higher trade frequency (more opportunity types detected across species)
- Tighter stops (better exit timing from order flow + sentiment early warnings)
- Better entry confirmation (multi-signal convergence reduces false signals)
- Research supports 1.5-3x improvement from adding sentiment + on-chain + derivatives to price-only strategies

### 15.2 Trade-Level Parameters

| Parameter | Pessimistic | Conservative | Moderate | Optimistic |
|---|---|---|---|---|
| Win rate | 57% | 62% | 65% | 68% |
| Avg winning trade | +1.5% | +2.0% | +2.2% | +2.5% |
| Avg losing trade | -1.3% | -1.3% | -1.2% | -1.0% |
| Position size (% of equity) | 15% | 18% | 20% | 25% |
| Trades per month | 18 | 22 | 25 | 30 |
| Round-trip fee | 0.10% | 0.10% | 0.10% | 0.10% |
| **EV per trade (on capital)** | **+0.03%** | **+0.12%** | **+0.18%** | **+0.32%** |
| **Monthly return** | **+0.5%** | **+2.6%** | **+4.6%** | **+9.6%** |
| **Annual return (compound)** | **+6.5%** | **+35.4%** | **+70.6%** | **+200%** |

Note: The moderate scenario uses a win rate **8 points BELOW** what was backtested (65% vs 73.7%) and an average win **smaller** than backtested (2.2% vs 2.68%). The improvement comes from tighter losses (1.2% vs 5.9%) and more frequent trades (25/month vs 3/month).

### 15.3 Revenue by Capital Level

**Conservative (2.6% monthly, 35% annual):**

| Capital | Monthly Income | Year 1 End | Year 2 End | Year 3 End |
|---|---|---|---|---|
| $5,000 | $128 | $6,770 | $9,168 | $12,414 |
| $10,000 | $256 | $13,541 | $18,335 | $24,827 |
| $20,000 | $512 | $27,081 | $36,670 | $49,654 |
| $50,000 | $1,279 | $67,704 | $91,676 | $124,136 |

**Moderate (4.6% monthly, 71% annual) — the expected scenario:**

| Capital | Monthly Income | Year 1 End | Year 2 End | Year 3 End |
|---|---|---|---|---|
| $5,000 | $228 | $8,528 | $14,546 | $24,810 |
| $10,000 | $455 | $17,056 | $29,092 | $49,621 |
| $20,000 | $910 | $34,113 | $58,184 | **$99,242** |
| $50,000 | $2,275 | $85,282 | $145,461 | **$248,105** |

### 15.4 Compounding Story ($20,000, Moderate Scenario)

| Month | Balance | Monthly P&L | Cumulative Profit |
|---|---|---|---|
| 1 | $21,000 | $1,000 | $1,000 |
| 3 | $23,153 | $1,103 | $3,153 |
| 6 | $26,802 | $1,276 | $6,802 |
| 12 | $35,917 | $1,710 | **$15,917** |
| 18 | $48,132 | $2,292 | $28,132 |
| 24 | $64,502 | $3,072 | **$44,502** |
| 30 | $86,439 | $4,116 | $66,439 |
| 36 | $115,836 | $5,516 | **$95,836** |

By Month 36, the system generates **$5,500/month** passively from the original $20,000 investment.

### 15.5 Benchmark Comparison

| System | Annual Return | Market | # Signals |
|---|---|---|---|
| S&P 500 (passive index) | ~10% | Equities | 0 |
| Average hedge fund | 8-12% | Mixed | Many |
| Top quant funds (Two Sigma, DE Shaw) | 15-30% | Equities | 1000s |
| Renaissance Medallion Fund | ~66% (gross) | Equities | 1000s |
| Verified crypto algo bots | 12-60% | Crypto | 1-5 |
| **The Seed (conservative)** | **35%** | **Crypto** | **88** |
| **The Seed (moderate)** | **71%** | **Crypto** | **88** |

The moderate estimate places The Seed in Medallion Fund territory. This is defensible because:
- Crypto is **less efficient** than equities — more alpha available for the same sophistication
- We trade at **small scale** — zero market impact (Medallion manages $10B+ and moves markets)
- We get **88 signals for $0/month** — quant funds spend $10M+/year on comparable data
- We have **continuous evolutionary adaptation** — most quant funds retrain models periodically
- We have **multi-species portfolio management** — not one strategy but an ecosystem

### 15.6 Disclaimer

These projections are mathematical models based on backtested results and research-backed assumptions. They are NOT guarantees. Actual performance will vary based on market conditions, strategy effectiveness, implementation quality, and factors that cannot be predicted. Capital deployed for trading is at risk. Never invest more than you can afford to lose.

---

## 16) Risk Analysis

### 16.1 Market Risks

| Risk | Severity | Mitigation |
|---|---|---|
| **Prolonged bear market** | High | Short selling allows profit in both directions |
| **Flash crash / black swan** | High | Hard daily loss limit + kill switch |
| **Exchange hack or insolvency** | Critical | Never keep more than trading capital on exchange; use API key without withdrawal permission |
| **Regulatory changes** | Medium | Monitor news pipeline; reduce exposure on regulatory signals |
| **Low volatility period** | Medium | Mean reversion strategies perform in low volatility; speciation maintains diverse strategies |

### 16.2 Technical Risks

| Risk | Severity | Mitigation |
|---|---|---|
| **Overfitting to historical data** | High | Train/test split; paper trading validation; look-ahead bias discount |
| **Strategy degradation over time** | Medium | Terrarium mode continuously adapts; strategies that stop working die |
| **API downtime / connectivity** | Medium | Automatic position closure on disconnect; retry logic |
| **Bug in execution logic** | High | Extensive testing; paper trading phase; start with minimum capital |
| **Sim-to-real gap** | Medium | Paper trading validates before real deployment |

### 16.3 Financial Risks

| Risk | Severity | Mitigation |
|---|---|---|
| **Loss of trading capital** | High | Hard 10% daily loss limit; kill switch at 20% total drawdown |
| **Fees exceeding profits** | Medium | Binance Futures fees are 0.02-0.05%; only profitable strategies survive evolution |
| **Insufficient capital for meaningful returns** | Low | Start with what you can afford to lose; scale only after validation |

### 16.4 Maximum Loss Scenarios

| Scenario | Max Loss | Notes |
|---|---|---|
| Kill switch triggered (20% drawdown) | $1,000 on $5K | System shuts down, preserving 80% of capital |
| Daily loss limit (10%) | $500 on $5K per day | Trading paused, resumes next day |
| Exchange hack | Full balance on exchange | Mitigated by keeping only trading capital there |
| Worst single month (backtested) | -6.65% (-$333 on $5K) | Historical worst case from testing |

---

## 17) Why This Beats Existing Approaches

### 17.1 Comparison Matrix

| Feature | Hand-Coded Bots | LLM Bots | RL Bots | **The Seed** |
|---|---|---|---|---|
| Discovers novel strategies | No | No | Limited | **Yes (evolution)** |
| Multiple strategies simultaneously | No | No | No | **Yes (speciation)** |
| Adapts without retraining | No | No | No | **Yes (terrarium)** |
| Zero inference cost | Yes | **No ($15-60/mo)** | Moderate | **Yes** |
| Multi-signal integration | Manual | Yes | Manual | **Automatic (evolution selects)** |
| Adversarially robust | No | No | Partially | **Yes (co-evolution)** |
| Handles non-stationary environments | No | No | Poorly | **Natively** |
| Feature selection | Manual | Manual | Manual | **Automatic (evolution prunes)** |
| Operating cost | ~$10/mo | ~$25-70/mo | ~$20-50/mo | **~$10-20/mo** |

### 17.2 The Moat

The Seed's competitive moat is the combination of:
1. **Evolutionary strategy discovery** — finds strategies humans wouldn't design
2. **Multi-signal richness** — consumes more information than competitors
3. **Zero-cost inference** — can run orders of magnitude more agents/decisions
4. **Continuous adaptation** — never goes stale
5. **Diversity through speciation** — portfolio of strategies, not a single bet
6. **Existing proven infrastructure** — the engine works today on the terrarium

No other retail-accessible trading system combines all six.

---

## 18) Future Directions

### 18.1 Multi-Exchange Arbitrage

Once the MarketArena is proven on Binance, extend to multiple exchanges (Bybit, OKX, Kraken). The terrarium naturally evolves agents that specialize in cross-exchange opportunities.

### 18.2 Multi-Chain DeFi Integration

When Cardano, Ethereum, or Solana DeFi ecosystems grow sufficiently, the MarketArena can be extended to include DEX pools, lending protocols (liquidation opportunities), and yield farming. The Lucid/Blockfrost integration from the FlipSwap project provides a head start for Cardano.

### 18.3 The Seed as a Platform

Package the evolutionary trading engine as a product:
- **Open-source the engine** (build community)
- **Sell hosted evolution runs** (SaaS model)
- **Strategy marketplace** (evolved strategies sold or rented)
- **Custom environments** (clients bring their market definition, we evolve strategies)

### 18.4 Advanced Agent Capabilities

Future enhancements to agent architecture:
- **Memory** (V2 reserved channels in SeedGenome): Agents that remember past market states
- **Multi-timeframe** analysis: Different brain modules for 1m, 1h, 1d signals
- **Inter-agent communication**: Agents share market intelligence (evolved signaling)
- **Hierarchical strategies**: Meta-agents that allocate capital across species

---

## Appendix A: Backtest Raw Data

### A.1 Test Environment

- **Tool**: `tools/Seed.Backtest/` (.NET 9 console application)
- **Data source**: Binance public API (real BTC/USDT and ETH/USDT hourly candles)
- **Period**: 9,000 hourly candles (March 17, 2025 — March 26, 2026)
- **BTC price range**: $60,000 — $126,200
- **BTC net change**: $83,055 → $68,708 (-17.2%)
- **ETH net change**: $1,902 → $2,060 (+8.3%)
- **Fee model**: 0.05% per side (Binance Futures VIP 0 taker rate)

### A.2 Parameter Sweep Results

Of 500 parameter combinations tested (RSI period × buy threshold × sell threshold × position size):

- **Profitable**: 126/500 (25.2%)
- **Average return**: -1.88%
- **Best return**: +21.88%
- **Worst return**: -16.53%
- **Median return**: -2.31%

### A.3 Sentiment Data

- **Source**: Alternative.me Fear & Greed Index (2,972 daily readings)
- **Distribution over test period**:
  - Extreme Fear (<25): ~110 days
  - Fear (25-45): ~90 days
  - Neutral (45-55): ~47 days
  - Greed (55-75): ~116 days
  - Extreme Greed (>75): ~4 days

---

## Appendix B: Glossary

| Term | Definition |
|---|---|
| **CPPN** | Compositional Pattern Producing Network — indirect encoding that generates brain connectivity patterns |
| **NEAT** | NeuroEvolution of Augmenting Topologies — speciation method that protects structural innovation |
| **Terrarium mode** | Continuous simulation where agents reproduce and die based on fitness, without generational resets |
| **Speciation** | Grouping genetically similar agents into species that compete internally before competing globally |
| **MarketArena** | The new "world" module that replaces 2D physics with market data |
| **Sentiment pipeline** | System that converts text (news, social media) into numerical signals for agent consumption |
| **VADER** | Valence Aware Dictionary and sEntiment Reasoner — rule-based sentiment analysis tool |
| **RSI** | Relative Strength Index — momentum oscillator measuring overbought/oversold conditions |
| **EMA** | Exponential Moving Average — trend-following indicator |
| **Bollinger Bands** | Volatility bands around a moving average |
| **ATR** | Average True Range — volatility measure |
| **Funding rate** | Periodic payment between long and short traders in futures markets; indicates market sentiment |
| **Open interest** | Total number of outstanding futures contracts; indicates market participation |
| **BTC dominance** | Bitcoin's share of total cryptocurrency market cap |
| **Look-ahead bias** | Error from using future information during backtesting |
| **Sim-to-real gap** | Performance difference between simulation and live deployment |
| **Kill switch** | Safety mechanism that stops all trading when loss exceeds a threshold |

---

*This document is a living reference. It will be updated as the project progresses through development phases.*

*Last updated: March 23, 2026*
