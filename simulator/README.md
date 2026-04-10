# IGTAP Simulator - Upgrade Order Optimizer

Finds the optimal upgrade purchase order to minimize time-to-wallJump in a game where a player runs a course repeatedly, earning cash to buy upgrades. Two upgrade types exist:

- **cashPerLoop** (cash multiplier): increases reward per successful course completion
- **cloneCount** (clones): adds AI clones that complete the course on a fixed timer, generating passive income

The core question: **in what order should you buy these upgrades to reach wallJump as fast as possible?**

## Player Profiles

Each player has a profile under `profiles/<name>/` containing:

- `success_times.csv` - recorded completion times for successful runs
- `failure_times.csv` - recorded times for failed runs
- `profile.json` - clone course duration and notes

Success rate, average times, etc. are computed from the CSV data. All scripts accept `--profile <name>` (default: `mysko`).

```
profiles/
  mysko/       # 66% success, 2.2s, clone 2.1s
  grill/       # 50% success, 2.3s, clone 2.1s
  honululu/    # 20% success, 2.7s, clone 2.4s
```

## Core Simulation

### simulator.py - Stochastic Game Simulator

The ground truth. Executes a policy through an explicit finite state machine:

```
AT_ENTRANCE -> (run course) -> AT_EXIT -> AT_BOX -> (buy or leave) -> AT_ENTRANCE
```

Each run samples a random success/failure outcome and completion time from the player's recorded data. Clones generate income continuously based on a fixed course duration. All search algorithms evaluate candidates against this simulator.

**Class:** `Simulator(config, seed)`
- `run(policy) -> GameState` - execute one full game
- `run_batch(policy, n) -> list[float]` - run n simulations, return times

### state.py - Game State

Tracks current time, cash, upgrade levels, and derived properties like `reward_per_completion` and `clone_count`.

### fsm.py - State Machine Transitions

Defines transition times between locations:
- AT_EXIT -> AT_ENTRANCE: 0.75s
- AT_EXIT -> AT_BOX: 2.0s
- AT_BOX -> AT_BOX: 0.75s (buying another upgrade while at box)
- AT_BOX -> AT_ENTRANCE: 2.5s

### config.py - Configuration

Loads game data (`data/level1_data.json`) and player profiles. Computes upgrade cost curves, success rates, and time distributions.

### policy.py - Decision Policies

Strategies that decide what to buy at each decision point:

| Policy | Description |
|--------|-------------|
| `SaveForWallJump` | Buy wallJump ASAP, nothing else |
| `CheapestFirst` | Always buy the cheapest available upgrade |
| `ClonesFirst` | Prioritize cloneCount, then cashPerLoop |
| `CashFirst` | Prioritize cashPerLoop, then cloneCount |
| `GreedyROI` | Buy upgrade with best return-on-investment ratio |
| `FixedSequence` | Follow a predetermined buy order |

## Search Algorithms

### 1. Genetic Algorithm (`genetic.py`)

Standard single-objective GA. Evolves purchase sequences to minimize mean time-to-wallJump.

**Class:** `GeneticSearch(config, seed, pop_size=200, elite_count=20, mutation_rate=0.3, eval_sims=3)`

- **Genome:** List of `["cashPerLoop", "cloneCount", ...]` (variable length, capped at upgrade limits)
- **Selection:** Tournament (k=3)
- **Crossover:** Single-point
- **Mutation:** 5 operators - insert, delete, swap, change type, shuffle block
- **Fitness:** Mean wallJump time over `eval_sims` stochastic simulations
- **Elitism:** Top `elite_count` survive unchanged

```
python3.11 -c "
from config import load_config; from genetic import GeneticSearch
config = load_config(profile='mysko')
ga = GeneticSearch(config, seed=42, pop_size=300, eval_sims=5)
best = ga.search(generations=500, verbose=True)
"
```

### 2. Lexicase Selection GA (`lexicase.py`)

Multi-feature evolutionary algorithm using lexicase selection, which maintains population diversity by selecting parents based on randomly ordered feature priorities.

**Class:** `LexicaseGA(config, seed, pop_size=300, n_cases=5, mutation_rate=0.4, epsilon=3.0)`

- **Evaluation:** Each genome is simulated across `n_cases` random seeds. Per seed, ~30 behavioral metrics are extracted (milestone times, income rates, clone utilization, etc.), producing a ~150-dimensional feature vector.
- **Selection:** Shuffle all features randomly. Filter population to candidates within `epsilon` of best on feature 1, then among those keep best on feature 2, etc. This naturally preserves specialists.
- **Tension Features** (from `metrics.py`):
  - Clone income share at t=50, t=100 (early clone vs late cash)
  - Transition overhead vs buy gap variance (efficiency vs responsiveness)
  - Income doubling time, CPS at 25/50/75% of run (growth curve shape)

```
python3.11 -c "
from config import load_config; from lexicase import LexicaseGA
config = load_config(profile='mysko')
ga = LexicaseGA(config, seed=42, pop_size=300, n_cases=5, epsilon=3.0)
best = ga.search(generations=200, verbose=True)
"
```

### 3. Novelty Search (`novelty.py`)

Evolutionary search that blends fitness with behavioral novelty to avoid premature convergence.

**Class:** `NoveltyGA(config, seed, pop_size=300, k_nearest=15, novelty_weight=0.4, mutation_rate=0.4, n_eval_seeds=3)`

- **Behavior Vector:** Average metric values across seeds (same metrics as lexicase)
- **Novelty Score:** K-nearest-neighbor distance in z-score normalized behavior space
- **Selection:** Tournament on blended rank: `(1 - novelty_weight) * fitness_rank + novelty_weight * novelty_rank`
- **Archive:** Random 5% of population behaviors are archived each generation to maintain long-term diversity pressure

```
python3.11 -c "
from config import load_config; from novelty import NoveltyGA
config = load_config(profile='mysko')
ga = NoveltyGA(config, seed=42, pop_size=300, k_nearest=15, novelty_weight=0.4)
best = ga.search(generations=200, verbose=True)
"
```

### 4. Block-Pattern Exhaustive Search (`sim_search.py`)

Enumerates all possible block-structured purchase patterns (e.g., "2c, 3cl, 8c, 7cl") up to 4 blocks, then evaluates each in the stochastic simulator.

- **Generators:** `gen_2block_seqs(nc, nk)`, `gen_3block_seqs(nc, nk)`, `gen_4block_seqs(nc, nk)`
- **Two-pass screening:** Quick eval (200 sims) to filter, then thorough validation (5000 sims) on top candidates
- **Key insight:** Optimal sequences tend to have block structure, so exhaustive search over blocks is tractable

```
python3.11 sim_search.py --nc 10 --nk 10 --blocks 4 --sims 200 --top 10 --profile mysko
```

### 5. Dijkstra Graph Search (`graph.py`)

Models the upgrade problem as a shortest-path graph. States encode (cash_level, clone_level, leftover_cash, at_box, prev_buy_type). Edge weights are computed from expected earning times.

- **Exact algorithm** - finds provably optimal path in the simplified model
- **Handles batching** - buying the same type at the box is cheaper (0.75s) than switching (requires leaving and returning: 2.5s + 2.0s)
- **Limitation:** Uses continuous expected-value earning model, which approximates discrete stochastic runs

```
python3.11 -c "
from config import load_config; from graph import solve
config = load_config(profile='mysko')
path = solve(config, verbose=True)
"
```

### 6. Full Expanded Graph Search (`graph_full.py`)

Like `graph.py` but with finer state granularity. Instead of computing "time to earn X cash" analytically, it models each run cycle as an explicit graph edge.

- **State:** `(n_cash, n_clone, cash_scaled, at_box, prev_type)` where cash is scaled by 3 to handle 2/3 success rate as integers
- **Much larger state space** (~1.4M states at SCALE=3)
- **More accurate** than `graph.py` for discrete earning dynamics

### 7. Monte Carlo Tree Search (`mcts.py`)

Standard MCTS with UCB1 selection over purchase sequences.

**Class:** `MCTS(config, seed)`

- **Nodes:** Partial purchase sequences
- **Selection:** UCB1 balancing exploitation/exploration
- **Expansion:** Add one untried purchase action
- **Rollout:** Random completion with increasing stop probability
- **Backpropagation:** Negated time (lower is better)

```
python3.11 -c "
from config import load_config; from mcts import MCTS
config = load_config(profile='mysko')
m = MCTS(config, seed=42)
best = m.search(iterations=50000, verbose=True)
"
```

### 8. Hierarchical MCTS (`hmcts.py`)

Two-level MCTS that operates on blocks instead of individual purchases, dramatically reducing the branching factor.

**Class:** `HierarchicalMCTS(config, seed, max_block=10)`

- **Actions:** Blocks like "buy 3 cashPerLoop" or "buy 5 cloneCount" (1 to max_block of each type)
- **Branching factor:** ~20 block actions vs ~2 single actions, but only ~4-6 depth vs ~20
- **Same MCTS mechanics** as `mcts.py` (UCB1, rollouts, backprop)

### 9. Z3 SMT Solver (`z3_optimizer.py`)

Encodes the upgrade ordering problem as a satisfiability modulo theories (SMT) constraint problem.

- **Decision variables:** Binary vector `x[i]` (0=cashPerLoop, 1=cloneCount) for each step
- **Constraints:** Exactly nc cashPerLoop and nk cloneCount purchases
- **Objective:** Minimize total time using continuous expected-value income model
- **Provably optimal** within its mathematical model (no simulation noise)
- **Also includes** brute-force enumeration for small instances

```
python3.11 z3_optimizer.py --profile mysko
```

## Meta-Optimizer (`optimize.py`)

Runs all algorithms in parallel, collecting candidates into a shared JSONL file. Each algorithm writes candidates as improvements are found (survives timeouts). Finally validates all unique candidates in the stochastic simulator.

```
python3.11 optimize.py --profile mysko \
  --ga-gens 300 --lexicase-gens 100 --novelty-gens 100 \
  --mcts-iters 30000 --validate-sims 2000
```

**Algorithms run:** GA (3 seeds), Lexicase (3 seeds), Novelty (2 seeds), Graph, GraphFull, MCTS (2 seeds), HMCTS (2 seeds), SimSearch

**Output:**
- `profiles/<name>/candidates.jsonl` - all candidates found (append-only, survives timeouts)
- `profiles/<name>/results.json` - validated top results
- Live progress with internal scores: `163.4s  [simsearch] 2c, 3cl, 8c, 6cl`

## V2 Algorithms (`v2/`)

Reimplementations that operate on explicit **action sequences** including "run" actions (not just purchase order). This allows optimizing *when* to run vs buy, not just purchase order.

| File | Algorithm | Difference from V1 |
|------|-----------|-------------------|
| `v2/search_ga.py` | GA | Genome includes "run" actions |
| `v2/search_lexicase.py` | Lexicase GA | Action-based with tension features |
| `v2/search_novelty.py` | Novelty Search | Behavior diversity on action sequences |
| `v2/sim.py` | Simulator | Single-step action execution |
| `v2/metrics.py` | Metrics | Feature extraction for action sequences |

## Analysis Tools

| File | Purpose | Usage |
|------|---------|-------|
| `cli.py` | Compare predefined policies | `python3.11 cli.py 2000 --profile mysko` |
| `eval_seqs.py` | Batch-evaluate sequences from JSON | `python3.11 eval_seqs.py seqs.json 2000 --profile mysko` |
| `plot.py` | Visualize cash/time curves | `python3.11 plot.py --profile mysko` |
| `metrics.py` | Detailed behavioral metrics | Used by lexicase/novelty for feature extraction |

## Results

Optimal sequences by player profile (validated @ 5000+ simulations):

| Profile | Success Rate | Clone Time | Best Time | Optimal Sequence |
|---------|-------------|-----------|-----------|-----------------|
| mysko | 66% | 2.1s | **164.0s** | 2c, 3cl, 8c, 7cl |
| grill | 50% | 2.1s | **175.2s** | 2c, 3cl, 1c, 1cl, 7c, 7cl |
| honululu | 20% | 2.4s | **237.7s** | 2c, 5cl, 8c, 6cl |

Key insight: worse players benefit from more early clones (3cl -> 5cl), since clone income matters more when most runs fail.
