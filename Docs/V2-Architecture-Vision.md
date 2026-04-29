# V2 Architecture Vision

## Context — why v1 needs a successor

THE-SEED v1 (the current V11e codebase) is a neuroevolutionary crypto trading system: NEAT/CPPN genomes compiled into a 20×20×3 recurrent brain (1200 neurons, ~15k edges), evolved under a 9-component fitness function against historical BTC data. After ~1340 generations of training (V11e pipeline, April 2026), v1 has demonstrated:

- **It can produce generalizing agents** (gen 1230 ValFit +0.88; checkpoint_1330 archive species 25 ValFit +1.20 post-analyzer)
- **All 11 action outputs are discoverable** (partial, trail, TP, brainSL, explicit-exit all fired in elite close histograms at various points)
- **Walk-forward generalization is achievable but rare** (14% pass rate in Phase 3, 50% in Phase 2)
- **Complexity and generalization are in tension** under Phase 3's strict weights — simple dir-flip lineages pass WF, rich V11 lineages often fail it

These outcomes are consistent with v1's design constraints. They are not bugs. But they place a ceiling on what v1 can achieve that is well below what modern AI-trading research has demonstrated.

This document sketches what a v2 architecture should look like, what to keep from v1, what to replace, and what the decision gate looks like for committing to a rebuild.

---

## The v1 ceiling — architectural, not training-tunable

v1 has four load-bearing constraints that together place a firm ceiling on agent quality:

| Constraint | v1 value | Consequence |
|---|---|---|
| Brain capacity | 1200 hidden neurons, ~15k edges | Cannot represent multi-regime policies; must time-share between patterns |
| Learning mechanism | NEAT evolution + Hebbian plasticity | Sparse credit assignment; unable to learn long-horizon dependencies efficiently |
| Reward signal | Fitness scalar aggregated per eval window | Thousands of per-tick decisions compressed to one number per genome |
| Training data | ~103k bars (3 years of 15-min BTC) | Limited regime diversity; can overfit to a specific macro cycle |

Making any of these bigger — more neurons, more data, more reward density — requires architectural change. Gradient descent, attention, policy gradient RL, and multi-asset data pipelines are not retrofittable into NEAT; they are a different system.

---

## v2 target — transformer + RL + mixture-of-experts

### Core brain
- **Transformer encoder** (decoder-free for policy prediction) over a sliding window of 256 bars of 110-channel signals. Hidden dim 256–512, 6–12 layers, 8 attention heads. 10M–100M parameters.
- **Action head**: produces the same 11-output signature v1 produces (direction, size, urgency, exit, predict, leverage, partialClose, trailEnable, trailDist, tpOffset, slOverride). This preserves backward compatibility with v1's ActionInterpreter and PaperTrader.
- **Value head** for RL baseline estimation.
- Implemented in **PyTorch** or **JAX**. Python-side training, C# inference via ONNX export for deployment.

### Learning — PPO or SAC
- **Custom Gym/Gymnasium environment** wrapping v1's `MarketEvaluator` logic: given genome (now a PyTorch model), replay historical data and compute per-step rewards.
- **Per-step dense reward** — realized P&L per tick, not aggregated into one fitness scalar. Action-level credit assignment via generalized advantage estimation (GAE).
- **PPO preferred** for training stability with small-to-medium networks. SAC reserved for off-policy continuous-action scaling if PPO plateaus.
- **Multi-environment parallel rollouts** (16–64 parallel rollouts) for sample efficiency.

### Mixture-of-Experts (MoE) for regime handling
- **Regime classifier** (small MLP over recent-bar volatility/trend statistics) outputs a softmax over K experts (K=4–8).
- Each expert is itself a transformer specialized by the gating distribution during training.
- Avoids the v1 pathology where a monolithic brain tries to serve all regimes and specializes to none.

### Online adaptation
- **Replay buffer** of recent 30 days of market state + agent actions.
- **Nightly fine-tune** (small gradient steps on recent data) with strong regularization to avoid catastrophic drift.
- **Distribution-shift detector**: KL divergence on signal marginals; triggers a larger retune if crossed.

### Data layer
- **Multi-asset**: BTC, ETH, SOL, XRP as primary; include ETH/BTC ratio signals for cross-asset context.
- **Multi-timeframe**: 5m, 15m, 1h, 4h, 1d — attention layers can reference all simultaneously via position-encoded concatenation.
- **Tick data** (where exchange permits) for order-flow-aware features.
- **Alternative data**: funding rates (already in v1), Deribit options (already in v1), plus liquidation heatmaps, GitHub commit velocity for L1s, on-chain flow metrics.

### Evaluation rigor
- **k-fold regime-bucketed cross-validation** instead of v1's single fixed holdout. Example: bull/bear/chop/high-vol/low-vol buckets, each evaluated separately, reported as a performance distribution.
- **Walk-forward on multiple horizons** (7-day, 30-day, 90-day) to stress temporal generalization.
- **Monte Carlo stress tests** with synthetic perturbations (fee increases, slippage spikes, feed latency).
- **Live paper trading** as terminal gate: the agent must deliver consistent statistics over 30+ days before any real-money deployment.

---

## Keep vs replace matrix

| Subsystem | Keep / Replace | Notes |
|---|---|---|
| `Seed.Market.Signals` (110 signals, normalizer) | **Keep** | Well-tested; add support for multi-timeframe aggregation |
| `Seed.Market.Data` (BinanceSpotFeed, Deribit, Coinglass, etc.) | **Keep** | Live feeds already work; just expand to multi-asset |
| `Seed.Market.Backtest` (cache, enrichment) | **Keep** | Cache behavior verified, multi-asset extension is additive |
| `Seed.Market.Trading` (PaperTrader, RiskManager, ActionInterpreter) | **Keep** | Execution layer is independent of brain implementation; v2 brain emits same 11 outputs |
| `Seed.Observatory` (JSONL logging) | **Keep** | Useful for both training and deployment |
| `Seed.Market.Agents.MarketAgent` | **Reimplement** | v1 version wires brain ↔ trader via IBrain; keep the wiring pattern, replace IBrain with v2 model |
| `Seed.Brain.BrainRuntime` (sparse recurrent) | **Replace** | Use PyTorch/ONNX in v2 |
| `Seed.Development.BrainDeveloper` (CPPN→graph) | **Delete** | No substrate, no CPPN |
| `Seed.Genetics.SeedGenome` (NEAT-style CPPN) | **Delete** | Replaced by direct PyTorch state dict |
| `Seed.Market.Evolution.MarketEvolution` (speciation, mutation) | **Delete** | Replaced by PPO training loop |
| `Seed.Market.Evolution.MarketFitness` (9-component scalar) | **Adapt** | Same metrics, but now computed from per-episode rollout for RL reward shaping |
| `tools/Seed.CheckpointEval` (v1 analyzer) | **Keep, adapt** | Evaluates v2 model checkpoints against validation data with same ValFit semantics |

---

## Migration path

### Interface boundary: `IBrain` stays
v1's `IBrain` interface defines `Step(inputs, outputs)` — this is the contract. v2's transformer model will implement `IBrain` via an ONNX-exported wrapper. Downstream code (`MarketAgent`, `PaperTrader`, risk management, observability) changes zero.

### Data pipeline shared
`Seed.Market.Signals.SignalSnapshot` remains the input format. Multi-timeframe is added by introducing `MultiTimeframeSnapshot` with child `SignalSnapshot[]` per interval. v1 single-timeframe code uses the base class unchanged.

### Training in Python, inference in C#
- Training loop: Python / PyTorch with custom gym env that calls v1's C# evaluator via IPC or PyThon.NET.
- Model export: ONNX per epoch checkpoint.
- Deployment: C# loads ONNX via `Microsoft.ML.OnnxRuntime`, wraps in IBrain, plugs into the same MarketAgent.

### Checkpoint analyzer reuse
The existing `tools/Seed.CheckpointEval` reads saved genomes and evaluates them against a fixed validation window. For v2, it becomes a thin wrapper that loads ONNX models and invokes them through a v2-compatible IBrain implementation. Output format identical.

---

## Compute + timeline

### Phase A — Proof of concept (4–6 weeks)
- One transformer (40M params), one asset (BTC), one timeframe (15m)
- PPO training with single-env rollouts
- Compute: single A100 GPU (rent, ~$1500/month)
- Goal: validate that gradient-trained transformer can match v1's best-val genome (ValFit +1.20 target)

### Phase B — Multi-regime + MoE (8–12 weeks)
- K=4 mixture-of-experts with regime gating
- Multi-env parallel rollouts
- Compute: 4×A100 or 1×H100
- Goal: demonstrate generalization across diverse regime slices (not just our fixed holdout)

### Phase C — Multi-asset + multi-timeframe (12–16 weeks)
- 4 assets × 4 timeframes
- Scale to 100M params if Phase A/B indicate capacity-bound
- Compute: 8×A100 or cloud-native (AWS p4d, GCP a2)
- Goal: portfolio-level agent with cross-asset awareness

### Phase D — Live deployment (4–8 weeks of observation)
- Paper trading with nightly fine-tunes
- Real-money phased rollout (small capital → full sizing)
- Compute: CPU inference only

**Total**: 6–10 months engineering, $20–60k compute budget (rent vs own), one strong ML engineer.

---

## Decision gate — when to commit to v2

Do NOT start v2 until all of these hold:

1. **v1's best deployed agent** (post-analyzer, post-Phase 4) is identified and live-paper-traded for 2+ weeks with known performance characteristics.
2. **Failure mode documented**: specifically, what does v1's agent do poorly? Is it regime-sensitivity? Overtrading? Underweight on new assets? The v2 design targets these gaps specifically, not generic "make it bigger."
3. **Budget approved**: $20k+ compute + 6 months engineer time.
4. **One dedicated engineer** with RL + transformer experience — this is not a side project.

If any of these is missing, delay v2 and continue extracting value from v1. The v1 system is a legitimate production pathway; v2 is a R&D investment.

---

## What we ARE committing to now (independent of v2 decision)

Regardless of whether v2 happens:

- **Deploy v1's best agent to paper trading**. Target: 2-week performance envelope, statistical characterization, first real-money consideration.
- **Publish checkpoint_eval tool** as a first-class feature — v1's "extract hidden best genome from population" capability.
- **Phase 4 run** under relaxed weights to probe whether v1 can produce V11-rich generalizers (the data point we don't have yet).
- **Document v1 comprehensively** in paper-trading findings and observations so v2 (if built) has clear targets to beat.

These steps are useful whether or not v2 is eventually funded. They maximize the return on v1's sunk cost and produce the baseline that any v2 proposal must beat to justify itself.
