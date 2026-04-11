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
from simulator import _clone_income_between
from policy import FixedSequence
from metrics import simulate_with_metrics



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
        self.upgrade_names = config.buyable_upgrade_names
        self.upgrade_caps = {name: config.buyable_upgrades[name].cap
                            for name in self.upgrade_names}
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
        genome = []
        for name in self.upgrade_names:
            cap = self.upgrade_caps[name]
            n = self.rng.randint(0, min(20, cap))
            genome.extend([name] * n)
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
                         self.rng.choice(self.upgrade_names))
        elif op == "delete" and len(genome) > 1:
            genome.pop(self.rng.randint(0, len(genome) - 1))
        elif op == "swap" and len(genome) > 1:
            i, j = self.rng.sample(range(len(genome)), 2)
            genome[i], genome[j] = genome[j], genome[i]
        elif op == "change" and genome:
            pos = self.rng.randint(0, len(genome) - 1)
            others = [n for n in self.upgrade_names if n != genome[pos]]
            if others:
                genome[pos] = self.rng.choice(others)
        elif op == "shuffle_block" and len(genome) > 2:
            start = self.rng.randint(0, len(genome) - 2)
            end = self.rng.randint(start + 1, min(start + 8, len(genome)))
            block = genome[start:end]
            self.rng.shuffle(block)
            genome[start:end] = block

        return self._clamp(genome)

    def _clamp(self, genome: list[str]) -> list[str]:
        result = []
        counts = {name: 0 for name in self.upgrade_names}
        for g in genome:
            if g in counts and counts[g] < self.upgrade_caps[g]:
                result.append(g)
                counts[g] += 1
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
