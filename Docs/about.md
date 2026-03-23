# The Seed -- How It Works

The Seed is an artificial life system. It creates small digital creatures that live in a simulated world, compete for food, evolve across generations, and learn within their own lifetimes. Nobody tells them what to do. Everything they become -- how they move, what they pay attention to, whether they cooperate or fight -- emerges from evolution and experience.

This document explains how it all works, from the world they live in to the brains they grow, to how they pass down knowledge to the next generation.

---

## The World

Every creature lives in a flat, two-dimensional world -- think of it as a tabletop seen from above. The world has:

- **Walls** around the edges that creatures cannot pass through.
- **Obstacles** (gray rectangles) that block movement. Creatures bounce off them.
- **Hazards** (red zones) that drain energy from any creature standing in them.
- **Food** (green dots) scattered around the world. Food is the primary energy source. When a creature walks over food, it absorbs the energy. After a delay, the food respawns somewhere else.

Food is not evenly distributed. It tends to appear in clusters -- concentrated patches separated by barren stretches. The amount of energy each food item provides also oscillates over time, like seasons: sometimes food is rich, sometimes it is lean. The exact layout of the world shifts slightly between evaluation rounds, forcing creatures to generalize rather than memorize a single map.

A creature starts each episode with a fixed amount of energy. Every tick of the simulation costs a small amount of energy just to exist, and moving costs more. If a creature's energy reaches zero, it dies. The goal -- implied by the fitness function, not explicitly programmed -- is to survive as long as possible and collect as much food as possible.

---

## The Body

Each creature is a small circle that can do two things: **sense** the world around it, and **act** on it.

### Senses (61 inputs)

A creature perceives the world through a set of sensors, similar to how an insect might combine simple eyes with chemical receptors:

- **8 proximity rays** spread across its forward field of vision, like whiskers fanning out ahead. Each ray reports two things: how far away the nearest object is, and what kind of object it hit (wall, obstacle, hazard, food, or another creature). That is 6 values per ray -- 48 values total.
- **Food gradient** -- a directional hint pointing toward the nearest food, translated into the creature's own frame of reference (left/right, forward/backward). Two values.
- **Energy level** -- how much energy the creature currently has. One value.
- **Speed** -- how fast it is currently moving. One value.
- **Bias** -- a constant signal (always 1), which gives the brain a fixed reference point to build computations from. One value.
- **Communication signals** -- two channels of signals broadcast by nearby creatures (explained later), plus a directional gradient pointing toward the strongest signal source. Four values.
- **Social awareness** -- the energy level of the nearest other creature, how crowded the local area is, and feedback from recent sharing or combat interactions. Four values.

All of these values are normalized to roughly the 0-to-1 range so the brain can work with them uniformly.

### Actions (6 outputs)

The brain produces six output values each tick:

| Output | What it does |
|--------|-------------|
| **Thrust** | Accelerate forward (positive) or backward (negative). |
| **Turn** | Rotate left or right. |
| **Signal 0** | Broadcast a value on communication channel 0. Other creatures nearby can hear this. |
| **Signal 1** | Broadcast a value on communication channel 1. |
| **Share** | Voluntarily transfer some of your own energy to the nearest creature. Only activates on positive values. |
| **Attack** | Drain energy from the nearest creature. Costs the attacker a small amount. Only activates on positive values. |

Nobody tells the creature when to use these. The brain decides everything.

---

## The Brain

Each creature has a brain -- a small neural network that reads all 61 sensor values and produces all 6 action outputs. But this is not a simple layered network like the ones used in most machine learning. It is a **sparse, recurrent neural graph** with several unusual properties:

- **Sparse** -- not every neuron connects to every other. Only a fraction of possible connections exist, chosen during development (explained below). This keeps the brain efficient.
- **Recurrent** -- outputs from one tick can feed back into the network on the next tick. This gives the brain a form of short-term memory. A creature can remember what it saw a few moments ago and change its behavior based on patterns over time.
- **Delayed connections** -- some connections have a time delay, so a signal sent on tick 10 arrives on tick 13. This lets the brain process temporal patterns at different timescales.
- **Modulated plasticity** -- the brain can change its own connection strengths while the creature is alive, based on reward and pain signals. This is within-lifetime learning (explained in its own section below).

The activation function (how each neuron transforms its inputs into an output) is **tanh**, which produces values between -1 and +1. Some neurons have a **time constant** that makes them integrate information slowly, acting as a kind of smoothing filter.

The brain is structured in layers:

1. **Input layer** -- one node per sensor (61 nodes).
2. **Hidden layers** -- a grid of internal neurons arranged in 2 layers. These do the actual "thinking."
3. **Output layer** -- one node per action (6 nodes).

Information flows from input through hidden layers to output, but recurrent connections can also loop backward, giving the network memory and internal dynamics.

---

## The Genome (DNA)

Here is where it gets interesting. The brain is not designed by hand. It is **grown from a genome**, much like a biological brain develops from DNA.

Each creature carries a **genome** that encodes:

1. **A small network called a CPPN** (Compositional Pattern-Producing Network). This is not the brain itself -- it is a recipe for building the brain. Think of it as an architect's blueprint, not the building.
2. **Development parameters** -- how aggressively to prune connections, how strong initial weights should be, how many candidate connections to consider.
3. **Learning parameters** -- how fast the brain should learn during its lifetime, how quickly it forgets, how much it should respond to reward vs. pain vs. curiosity.
4. **Stability parameters** -- limits on how large connection weights can grow, how strongly the brain resists runaway activity.

### How the CPPN Builds the Brain

The CPPN is queried for every potential connection in the brain. For each pair of neurons (source and destination), the system asks: "Given where these two neurons are located in the brain's spatial layout, should they be connected? If so, how strong should the connection be?"

The CPPN takes as input the positions, layers, and distances of the two neurons and outputs a set of values: a connection score (should this connection exist?), a weight (how strong?), a time constant, and whether this should be a normal connection or a modulatory one (used for learning).

This approach is called **indirect encoding**. Instead of storing thousands of connection weights in the genome (which would make evolution impossibly slow), the genome stores a compact function that *generates* the connection pattern. This is analogous to how biological DNA does not contain a wiring diagram for every synapse in the brain -- it contains developmental rules that produce the wiring.

The CPPN itself evolves over generations, getting more complex as nodes and connections are added to it through mutation.

---

## Evolution

Evolution is how the population improves over time. It works the same way natural selection does, just faster:

### 1. Evaluation

All creatures in the population are placed into a **shared arena** -- the same world, at the same time. They compete for the same food, see each other, can signal each other, and can share energy or attack. This is not a solo test; it is a community.

Each creature is evaluated over multiple rounds (different world layouts) to ensure its fitness is not a fluke of one particular map. The fitness score combines:

- **Survival time** -- how long did it stay alive?
- **Food collected** -- how much food did it eat?
- **Efficiency** -- food collected relative to energy spent.
- **Net energy** -- did it end with more or less energy than it started with?
- **Brain stability** -- penalty for erratic neural activity.

### 2. Speciation

After evaluation, creatures are grouped into **species** based on how similar their genomes are. The similarity metric looks at the structure of their CPPNs -- how many connections they share, how many are unique to one genome, and how different the shared connection weights are.

Species serve as a protective mechanism. A creature with an unusual genome that performs moderately well will not be immediately wiped out by the dominant strategy. Instead, it competes primarily within its own species. This preserves diversity and gives novel strategies time to mature.

### 3. Reproduction

Each species receives a share of offspring proportional to its average fitness. Within a species:

- The **best performers are preserved** unchanged into the next generation (elitism).
- Other offspring are created by **crossover** (combining two parent genomes, aligning their CPPN connections by innovation history and taking weights from the fitter parent) or by **cloning** a single parent.
- Every offspring is then **mutated**: connection weights are nudged, new connections or neurons may be added to the CPPN, and learning/development parameters may shift slightly.

This cycle repeats: evaluate, speciate, reproduce, mutate. Over hundreds of generations, the population discovers increasingly sophisticated strategies for survival.

---

## Learning Within a Lifetime

Evolution operates between generations -- it changes the genome. But The Seed also implements **within-lifetime learning**: a creature's brain can modify its own connection weights while it is alive, based on experience.

This works through three signals, analogous to neurotransmitters in biological brains:

| Signal | What it represents | When it fires |
|--------|-------------------|---------------|
| **Reward** | "That was good" | When the creature eats food or receives shared energy. |
| **Pain** | "That was bad" | When the creature takes hazard damage, uses energy, or gets attacked. |
| **Curiosity** | "That was new" | When the creature's sensors change in an unexpected way (novelty detection). |

These three signals are combined into a single **modulation value** that scales how much the brain's connections change on each tick. The genome controls the mixing weights -- how much the creature cares about reward relative to pain relative to curiosity.

The learning mechanism uses **eligibility traces**: when two neurons fire together, their connection is "tagged" as a candidate for change. If a reward signal arrives shortly after, that connection is strengthened. If pain arrives, it is weakened. This is a biologically plausible form of learning called **neuromodulated Hebbian plasticity**.

The brain also maintains **fast and slow weights**. Fast weights change quickly (within-lifetime learning) while slow weights change gradually (consolidation). This lets the creature adapt rapidly to new situations without catastrophically forgetting everything it has previously learned.

The key insight: **evolution does not just evolve the brain's structure. It evolves the brain's ability to learn.** Some creatures may evolve to be highly plastic (learn quickly during their lifetime), while others evolve to be more hardwired (rely on the structure they were born with). The system discovers which strategy works best.

---

## Social Behavior

Creatures are not isolated. They share a world and can interact in several ways:

### Communication (Signals)

Each creature broadcasts two signal values every tick. These signals propagate through space -- nearby creatures can sense them, with strength decreasing with distance. A creature can also sense the *direction* of the strongest signal source.

Signals have no predefined meaning. They are just numbers. But evolution can assign meaning to them: a signal might evolve to mean "food is here" or "danger nearby" or "I am of the same species." The meaning emerges from whether using signals in a particular way improves fitness.

### Sharing

A creature can voluntarily transfer energy to the nearest other creature. The transfer is not perfectly efficient -- some energy is lost (80% reaches the recipient). Sharing creates evolutionary pressure for cooperation, kin recognition, and reciprocal altruism. A creature that shares with relatives (who carry similar genes) indirectly benefits its own genome's chances of survival.

### Attacking

A creature can drain energy from the nearest other creature. Attacking has a cost (the attacker pays a small energy fee) and is only partially efficient (the attacker keeps 50% of what it drains). This creates evolutionary pressure for predation, defense, and arms races. Creatures may evolve to avoid attackers, to signal danger, or to form defensive groups.

These interaction mechanisms give evolution something genuinely complex to optimize. Without them, every creature is just a solo forager. With them, social cognition, communication, and competitive strategy become fitness-relevant.

---

## The Dashboard

The dashboard is a real-time visualization that lets you watch evolution and individual creature behavior as it happens. It connects to the simulation backend via a live data stream (SignalR).

### World View

The main canvas shows the 2D world from above. You can see:

- **Creatures** as colored circles with directional indicators showing which way they face.
- **Food** as glowing green dots (brightness pulsates with the energy oscillation cycle).
- **Obstacles** as gray rectangles, **hazards** as red rectangles.
- **Signal rings** around creatures that are broadcasting -- the color encodes the signal channel values.
- **Interaction rings**: a red ring appears around a creature being attacked; a green ring appears around one receiving shared energy.
- Click on any creature to select it and see its brain.

### Brain Graph

When a creature is selected, its brain is displayed as an interactive force-directed graph. Nodes represent neurons, links represent connections. The layout reflects the brain's actual spatial structure -- input sensors on the left, hidden layers in the middle, action outputs on the right.

### Fitness History

A line chart tracks the population's fitness over generations: best, mean, and worst. This is the primary indicator of whether evolution is making progress.

### Controls

Play, pause, step through individual ticks, adjust simulation speed, or reset the population to start fresh.

---

## The Big Picture

The Seed is not trying to solve a specific problem. It is building a system where **complex behavior emerges from simple rules**, just as it does in nature:

1. A genome encodes a recipe for building a brain.
2. The brain reads the world through sensors and acts through actuators.
3. The brain can learn during its lifetime through reward, pain, and curiosity.
4. Creatures that survive and collect food pass their genomes to the next generation.
5. Mutations introduce variation; natural selection retains what works.
6. Social interactions (signals, sharing, attacking) create pressure for increasingly sophisticated cognition.

No behavior is programmed. No strategy is hardwired. The system starts from random genomes producing random behavior, and over generations, discovers movement, foraging, hazard avoidance, communication, and social interaction -- or whatever strategies the evolutionary pressure selects for.

What makes The Seed different from most artificial life systems is the combination of **indirect encoding** (compact genomes that grow complex brains), **within-lifetime learning** (creatures that adapt to their specific environment), and **multi-agent social dynamics** (interaction pressure that drives cognitive complexity). These three forces together create the conditions for open-ended evolution -- the kind of evolution that does not converge on a single solution but keeps producing novel strategies indefinitely.

The seed has been planted. What grows from it is not predetermined.
