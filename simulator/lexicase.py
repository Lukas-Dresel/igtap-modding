"""Genetic algorithm with lexicase selection.

Features (evaluated per genome):
  - Time to wallJump (main objective)
  - Time to clone 1, 2, 3, ..., 9
  - Time to multiplier 2x, 4x, 6x, 8x, 10x
  - Time to first 100 cash, 500 cash earned
  - Income rate at t=60, t=100, t=140
  - Number of buy trips
  - Highest total cash earned
  - Highest cash on hand at any point
  - Performance under different success rates (50%, 66%, 80%)
  - Performance under different run times (fast/slow)

Lexicase selection: for each parent pick, randomly shuffle features,
then filter candidates sequentially — keep those within epsilon of
best on feature 1, then among those keep best on feature 2, etc.
"""
import random
import math
from itertools import groupby

from config import SimConfig, load_config
from state import GameState
from fsm import State, transition_time
from simulator import _clone_income_between
from policy import FixedSequence
from metrics import simulate_with_metrics


def _old_simulate_with_metrics(config: SimConfig, genome: list[str], rng: random.Random) -> dict[str, float]:
    """Run one simulation tracking all milestone metrics."""
    seq = genome + ["wallJump"]
    state = GameState(config=config)
    clone_start_time = None
    fsm_state = State.AT_ENTRANCE
    seq_idx = 0

    # Tracking
    total_earned = 0.0
    max_cash_on_hand = 0.0
    max_total_earned = 0.0
    buy_trips = 0
    income_snapshots = {}  # time -> income so far
    milestones = {}

    def record_milestone(name, time):
        if name not in milestones:
            milestones[name] = time

    def advance(duration):
        nonlocal clone_start_time, total_earned, max_total_earned, max_cash_on_hand
        if duration <= 0:
            state.time += duration
            return
        if state.clone_count > 0 and clone_start_time is not None:
            ci = _clone_income_between(state, config, clone_start_time,
                                        state.time, state.time + duration)
            state.cash += ci
            total_earned += ci
        state.time += duration
        max_cash_on_hand = max(max_cash_on_hand, state.cash)
        max_total_earned = max(max_total_earned, total_earned)

    def get_action():
        nonlocal seq_idx
        if seq_idx >= len(seq):
            return "run", None
        target = seq[seq_idx]
        if state.can_afford(target):
            return "buy", target
        return "run", None

    iters = 0
    while state.time < 100000 and not state.has_wall_jump:
        iters += 1
        if iters > 100000:
            break

        if fsm_state == State.AT_ENTRANCE:
            advance(transition_time(State.AT_ENTRANCE, State.RUNNING))

            # Run course
            if rng.random() < config.success_rate:
                rt = rng.choice(config.success_times)
                advance(rt)
                reward = state.reward_per_completion
                state.cash += reward
                total_earned += reward
            else:
                rt = rng.choice(config.failure_times)
                advance(rt)

            max_cash_on_hand = max(max_cash_on_hand, state.cash)
            max_total_earned = max(max_total_earned, total_earned)

            # Record income snapshots
            for t in [60, 100, 140]:
                if state.time >= t and f"income_at_{t}" not in milestones:
                    milestones[f"income_at_{t}"] = total_earned

            # Record cash milestones
            if total_earned >= 100:
                record_milestone("cash_100", state.time)
            if total_earned >= 500:
                record_milestone("cash_500", state.time)

            fsm_state = State.AT_EXIT

        elif fsm_state == State.AT_EXIT:
            action_type, target = get_action()
            if action_type == "buy" and target:
                advance(transition_time(State.AT_EXIT, State.AT_BOX))
                state.buy_upgrade(target)
                seq_idx += 1
                buy_trips += 1
                if clone_start_time is None and state.clone_count > 0:
                    clone_start_time = state.time
                # Record clone milestones
                for n in range(1, 10):
                    if state.clone_count >= n:
                        record_milestone(f"clone_{n}", state.time)
                # Record multiplier milestones
                for mult in [2, 4, 6, 8, 10]:
                    if state.cash_per_loop + 1 >= mult:
                        record_milestone(f"mult_{mult}x", state.time)
                fsm_state = State.AT_BOX
            else:
                advance(transition_time(State.AT_EXIT, State.AT_ENTRANCE))
                fsm_state = State.AT_ENTRANCE

        elif fsm_state == State.AT_BOX:
            if state.has_wall_jump:
                break
            action_type, target = get_action()
            if action_type == "buy" and target:
                advance(transition_time(State.AT_BOX, State.AT_BOX))
                state.buy_upgrade(target)
                seq_idx += 1
                if clone_start_time is None and state.clone_count > 0:
                    clone_start_time = state.time
                for n in range(1, 10):
                    if state.clone_count >= n:
                        record_milestone(f"clone_{n}", state.time)
                for mult in [2, 4, 6, 8, 10]:
                    if state.cash_per_loop + 1 >= mult:
                        record_milestone(f"mult_{mult}x", state.time)
                fsm_state = State.AT_BOX
            else:
                advance(transition_time(State.AT_BOX, State.AT_ENTRANCE))
                fsm_state = State.AT_ENTRANCE

    # Build feature vector (lower is better for time-based, negate for "higher is better")
    INF = 100000.0
    features = {
        "time_to_walljump": state.time if state.has_wall_jump else INF,
        "buy_trips": float(buy_trips),
        "neg_max_cash_on_hand": -max_cash_on_hand,
        "neg_max_total_earned": -max_total_earned,
    }

    for n in range(1, 10):
        features[f"time_to_clone_{n}"] = milestones.get(f"clone_{n}", INF)

    for mult in [2, 4, 6, 8, 10]:
        features[f"time_to_mult_{mult}x"] = milestones.get(f"mult_{mult}x", INF)

    features["time_to_cash_100"] = milestones.get("cash_100", INF)
    features["time_to_cash_500"] = milestones.get("cash_500", INF)

    for t in [60, 100, 140]:
        features[f"neg_income_at_{t}"] = -milestones.get(f"income_at_{t}", 0)

    return features


FEATURE_NAMES = None  # set on first eval


class LexicaseGA:
    def __init__(self, config: SimConfig, seed: int = 42,
                 pop_size: int = 300, n_cases: int = 5,
                 mutation_rate: float = 0.4, epsilon: float = 3.0):
        self.config = config
        self.rng = random.Random(seed)
        self.pop_size = pop_size
        self.n_cases = n_cases  # number of random seeds to eval each genome on
        self.mutation_rate = mutation_rate
        self.epsilon = epsilon
        self.cash_cap = config.upgrades["cashPerLoop"].cap
        self.clone_cap = config.upgrades["cloneCount"].cap
        self.eval_seeds = [seed + i * 7919 for i in range(n_cases)]

    def evaluate(self, genome: list[str]) -> list[float]:
        """Evaluate genome across all seeds. Returns flat feature vector
        (features × seeds concatenated)."""
        global FEATURE_NAMES
        all_values = []
        for s in self.eval_seeds:
            features = simulate_with_metrics(self.config, genome, s)
            if FEATURE_NAMES is None:
                FEATURE_NAMES = list(features.keys())
            all_values.extend(features[k] for k in FEATURE_NAMES)
        return all_values

    def n_features(self) -> int:
        return len(FEATURE_NAMES) * self.n_cases if FEATURE_NAMES else 0

    def random_genome(self) -> list[str]:
        n_cash = self.rng.randint(0, min(20, self.cash_cap))
        n_clone = self.rng.randint(0, min(15, self.clone_cap))
        genome = ["cashPerLoop"] * n_cash + ["cloneCount"] * n_clone
        self.rng.shuffle(genome)
        return genome

    def lexicase_select(self, pop: list[tuple[list[float], list[str]]]) -> list[str]:
        candidates = list(range(len(pop)))
        indices = list(range(len(pop[0][0])))
        self.rng.shuffle(indices)

        for idx in indices:
            if len(candidates) <= 1:
                break
            best = min(pop[i][0][idx] for i in candidates)
            candidates = [i for i in candidates
                         if pop[i][0][idx] <= best + self.epsilon]

        return pop[self.rng.choice(candidates)][1]

    def crossover(self, a: list[str], b: list[str]) -> list[str]:
        if not a or not b:
            return list(a or b)
        cut_a = self.rng.randint(0, len(a))
        cut_b = self.rng.randint(0, len(b))
        return self._clamp(a[:cut_a] + b[cut_b:])

    def mutate(self, genome: list[str]) -> list[str]:
        genome = list(genome)
        op = self.rng.choice(["insert", "delete", "swap", "change", "shuffle_block"])

        if op == "insert" and len(genome) < 40:
            genome.insert(self.rng.randint(0, len(genome)),
                         self.rng.choice(["cashPerLoop", "cloneCount"]))
        elif op == "delete" and len(genome) > 1:
            genome.pop(self.rng.randint(0, len(genome) - 1))
        elif op == "swap" and len(genome) > 1:
            i, j = self.rng.sample(range(len(genome)), 2)
            genome[i], genome[j] = genome[j], genome[i]
        elif op == "change" and genome:
            pos = self.rng.randint(0, len(genome) - 1)
            genome[pos] = "cloneCount" if genome[pos] == "cashPerLoop" else "cashPerLoop"
        elif op == "shuffle_block" and len(genome) > 2:
            start = self.rng.randint(0, len(genome) - 2)
            end = self.rng.randint(start + 1, min(start + 8, len(genome)))
            block = genome[start:end]
            self.rng.shuffle(block)
            genome[start:end] = block

        return self._clamp(genome)

    def _clamp(self, genome: list[str]) -> list[str]:
        result = []
        cash, clone = 0, 0
        for g in genome:
            if g == "cashPerLoop" and cash < self.cash_cap:
                result.append(g)
                cash += 1
            elif g == "cloneCount" and clone < self.clone_cap:
                result.append(g)
                clone += 1
        return result

    def search(self, generations: int = 1000, verbose: bool = True,
                on_improvement: callable = None) -> list[str]:
        pop = []
        for _ in range(self.pop_size):
            g = self.random_genome()
            scores = self.evaluate(g)
            pop.append((scores, g))

        best_mean_wj = float("inf")
        best_genome = None

        for gen in range(generations):
            for scores, genome in pop:
                wj_times = [scores[i * len(FEATURE_NAMES)] for i in range(self.n_cases)]
                mean_wj = sum(wj_times) / len(wj_times)
                if mean_wj < best_mean_wj:
                    best_mean_wj = mean_wj
                    best_genome = genome
                    if on_improvement:
                        on_improvement(best_genome, best_mean_wj)
                    if verbose:
                        parts = []
                        for k, g in groupby(genome):
                            n = len(list(g))
                            parts.append(f"{n}x{k}" if n > 1 else k)
                        print(f"  gen {gen}: {best_mean_wj:.1f}s ({len(genome)} buys) {','.join(parts)}")

            # Elitism
            pop.sort(key=lambda x: sum(x[0][i * len(FEATURE_NAMES)] for i in range(self.n_cases)))
            elite_count = self.pop_size // 10
            new_pop = list(pop[:elite_count])

            while len(new_pop) < self.pop_size:
                parent_a = self.lexicase_select(pop)
                parent_b = self.lexicase_select(pop)
                child = self.crossover(parent_a, parent_b)
                if self.rng.random() < self.mutation_rate:
                    child = self.mutate(child)
                scores = self.evaluate(child)
                new_pop.append((scores, child))

            pop = new_pop

        if verbose:
            print(f"  Final best: {best_mean_wj:.1f}s")
        return best_genome
