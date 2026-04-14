# Domain Concepts

## NEAT (NeuroEvolution of Augmenting Topologies)

NEAT evolves neural network topology alongside weights. Key mechanisms:
- **Innovation tracking**: Every new connection or node split gets a unique innovation ID, enabling gene alignment during crossover.
- **Speciation**: Genomes are clustered by structural similarity (excess genes, disjoint genes, weight difference). Distance formula: `d = C1*excess/N + C2*disjoint/N + C3*avgWeightDiff` (C1=1, C2=1, C3=0.5).
- **Fitness sharing**: Raw fitness divided by species size, preventing any single species from dominating.
- **Complexification**: Networks start minimal and grow through structural mutations (add connection 5%, add node 2%).

## CPPN (Compositional Pattern-Producing Network)

Instead of evolving brain weights directly, each genome encodes a CPPN — a small network that generates connectivity patterns. The CPPN takes geometric coordinates of two neurons and outputs whether they should connect, with what weight, delay, and modulation type.

**CPPN inputs** (9 dimensions): source position (Xi, Yi, Li), target position (Xj, Yj, Lj), displacement (Dx, Dy), Euclidean distance (Dist).

**CPPN outputs** (6 dimensions): connection score (C, threshold ≥0.20), weight (W), delay, tau, modulatory tag, gate.

**Activation functions**: Identity, Tanh, Sigmoid, Sin, Gauss — compositional combinations produce complex spatial patterns from compact genomes.

## Brain Development (HyperNEAT-style)

The `BrainDeveloper` compiles a CPPN into a sparse recurrent `BrainGraph`:

1. **Substrate layout**: Input neurons (0-91) → Hidden grid (Width × Height × Layers, default 16×16×3 = 768) → Output neurons (5: direction, size, urgency, exit, price prediction).
2. **Connection candidates**: For each hidden/output neuron, gather local neighbors (within radius 3, ≤1 layer away) + global random samples (24 per neuron).
3. **CPPN query**: Evaluate each candidate pair → get connection score, weight, delay, etc.
4. **TopK selection**: Keep top 16 inputs per neuron (sorted by connection score), respecting max 20 outgoing per source.
5. **Edge construction**: Dual weights (WSlow/WFast), plasticity gain from gate output, edge type (Normal/Modulatory), synaptic delay (0-3 ticks).

## Brain Runtime

Sparse recurrent network with two-speed Hebbian plasticity:

- **Forward pass**: Micro-step recurrence (1-3 iterations), tanh activation, RMS incoming normalization, homeostasis scaling, synaptic delay buffers (circular history).
- **Learning**: Eligibility traces decay at rate λ. Weight updates modulated by reward (realized P&L), pain (unrealized loss), and curiosity (prediction error). Fast weights update quickly; slow weights consolidate gradually (`β=0.01`). Critical period: learning rate decays to 10% over 1000 hours.
- **Stability**: Weight clamping [-3, +3], homeostasis (`scale = exp(-β * (|a_mean| - target))`), saturation tracking for instability penalty.

## Signal System (92 Inputs)

All signals normalized to [-1, 1]. Grouped by category:

| Range | Category | Examples |
|-------|----------|---------|
| 0-11 | Price & Volume | BTC price, returns (1h/4h/24h), volume, spread, imbalance |
| 12-22 | Derivatives | Funding rate, open interest, long/short ratio, liquidations |
| 23-30 | Sentiment | Fear & Greed index, news volume, Reddit sentiment |
| 31-40 | On-Chain | Hash rate, active addresses, exchange flow, NVT |
| 41-50 | Macro | S&P500, VIX, DXY, gold, 10Y treasury, BTC-SP500 correlation |
| 51-56 | Stablecoin | USDT/USDC market caps, flow, BTC dominance |
| 57-68 | Technical | RSI14, EMA 12/26, MACD, Bollinger, ATR, VWAP, OBV |
| 69-75 | Temporal | Hour/day/month sin/cos encoding, event proximity |
| 76-79 | Agent State | Current P&L %, position direction, holding duration, drawdown |
| 80-87 | Multi-Asset | BTC-ETH spread, correlation, volatility ratio, momentum divergence |
| 88-91 | Regime | Volatility percentile, trend momentum, regime change rate, stress |

## Brain Outputs → Trading Actions

The brain produces 5 float outputs, interpreted by `ActionInterpreter`:

| Output | Activation | Interpretation |
|--------|-----------|----------------|
| 0 | tanh | Direction: <-0.15 = short, >0.15 = long, else flat (deadzone) |
| 1 | sigmoid | Position size as fraction of max allowed (0-1) |
| 2 | sigmoid | Urgency: market order vs limit order preference |
| 3 | sigmoid | Exit current position flag (>0.5 = close) |
| 4 | raw | Price direction prediction (used for curiosity loss) |

## Fitness Formula

Multi-objective composite with Bayesian shrinkage:

```
confidence = 1 - K / (K + trade_count)    [K=10, shrinks metrics when few trades]

fitness = adjusted_sharpe × 0.45
        + adjusted_sortino × 0.15
        + log_return × 0.20
        - drawdown_duration × 0.10
        - cvar_5pct × 0.10
```

**Constraints**:
- 0 trades → fitness = -0.10 (inactivity penalty)
- 1-2 trades → linear interpolation to full fitness at 3+ trades
- -0.05 penalty per open position at evaluation window end
- Metrics clamped to ±10 after shrinkage adjustment

**Sharpe**: `(μ_returns / σ_returns) × √8760` (hourly → annualized)
**Sortino**: Same but using downside deviation only
**CVaR(5%)**: Average of worst 5% daily returns
**Drawdown Duration**: Max consecutive bars below equity watermark, normalized by curve length

## Evolution Loop (Per Generation)

1. **Evaluate** population (parallel): compile CPPN → brain, run agent on market window, compute fitness
2. **Diversity bonus**: KNN in NEAT distance space, add `distance × 0.02` to active genomes
3. **Speciate**: Assign to species via distance threshold, adjust threshold toward target (10-50 species)
4. **Archive update**: Track per-species best genome (EliteArchive, max 100)
5. **Stagnation check**: If species best hasn't improved for 25 gens → replace 50% offspring with archive elites
6. **Reproduce**: 1 elite clone + tournament selection → crossover (35%) or mutation (65%)
7. **Log**: Fitness stats, species count, brain diagnostics (saturation, edge counts)

## Genome Mutation Operators

| Operator | Probability | Details |
|----------|-------------|---------|
| Weight mutation | 50% | Gaussian perturbation σ=0.10, or full reset (10% chance) |
| Bias mutation | 30% | Per-node Gaussian σ=0.10 |
| Add connection | 5% | Random src→dst, innovation-tracked |
| Add node | 2% | Split existing connection, preserve weight flow |
| Parameter mutation | 20% each | Dev params, learning params, stability params within bounds |

## Risk Management (Execution Layer)

- **Stop-loss**: 2% hard protective (between brain decisions)
- **Max position**: 25% of equity per trade
- **Max concurrent positions**: 3
- **Kill switch**: Close all if drawdown exceeds 15% from equity watermark
- **Daily loss limit**: 5% — no new trades after breach
- **VaR(95%)**: Parametric, 24h rolling — scales position size if VaR > 5% threshold

## Paper Trading Realism

- **Dynamic slippage**: `multiplier = 1 + (participation%)²`, max 20x base slippage
- **Funding rates**: 8-hour slots, long pays negative funding, short receives
- **Fees**: Taker 0.06%, maker 0.04%
