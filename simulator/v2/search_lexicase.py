"""Lexicase GA over explicit action sequences (v2 simulator).

Same genome as search_ga but with lexicase selection using milestone features.
"""
import random
import math
from itertools import groupby

import sys, os
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from config import SimConfig, load_config
from sim import SimState, step, run_sequence_mean
from metrics import simulate_with_metrics

ACTIONS = ["run", "cashPerLoop", "cloneCount"]


FEATURE_NAMES = None


class LexicaseGA:
    def __init__(self, config: SimConfig, seed: int = 42,
                 pop_size: int = 300, n_cases: int = 5,
                 mutation_rate: float = 0.4, epsilon: float = 3.0,
                 max_genome_len: int = 200):
        self.config = config
        self.rng = random.Random(seed)
        self.pop_size = pop_size
        self.n_cases = n_cases
        self.mutation_rate = mutation_rate
        self.epsilon = epsilon
        self.max_len = max_genome_len
        self.eval_seeds = [seed + i * 7919 for i in range(n_cases)]

    def evaluate(self, genome: list[str]) -> list[float]:
        global FEATURE_NAMES
        all_values = []
        for s in self.eval_seeds:
            features = simulate_with_metrics(self.config, genome, random.Random(s))
            if FEATURE_NAMES is None:
                FEATURE_NAMES = list(features.keys())
            all_values.extend(features[k] for k in FEATURE_NAMES)
        return all_values

    def random_genome(self) -> list[str]:
        length = self.rng.randint(10, 80)
        genome = []
        for _ in range(length):
            r = self.rng.random()
            if r < 0.6:
                genome.append("run")
            elif r < 0.8:
                genome.append("cashPerLoop")
            else:
                genome.append("cloneCount")
        return genome

    def lexicase_select(self, pop):
        candidates = list(range(len(pop)))
        indices = list(range(len(pop[0][0])))
        self.rng.shuffle(indices)
        for idx in indices:
            if len(candidates) <= 1:
                break
            best = min(pop[i][0][idx] for i in candidates)
            candidates = [i for i in candidates if pop[i][0][idx] <= best + self.epsilon]
        return pop[self.rng.choice(candidates)][1]

    def crossover(self, a, b):
        if not a or not b:
            return list(a or b)
        cut_a = self.rng.randint(0, len(a))
        cut_b = self.rng.randint(0, len(b))
        return (a[:cut_a] + b[cut_b:])[:self.max_len]

    def mutate(self, genome):
        genome = list(genome)
        op = self.rng.choice(["insert", "delete", "change", "swap", "block_shuffle"])
        if op == "insert" and len(genome) < self.max_len:
            genome.insert(self.rng.randint(0, len(genome)), self.rng.choice(ACTIONS))
        elif op == "delete" and len(genome) > 5:
            genome.pop(self.rng.randint(0, len(genome) - 1))
        elif op == "change" and genome:
            genome[self.rng.randint(0, len(genome) - 1)] = self.rng.choice(ACTIONS)
        elif op == "swap" and len(genome) > 1:
            i, j = self.rng.sample(range(len(genome)), 2)
            genome[i], genome[j] = genome[j], genome[i]
        elif op == "block_shuffle" and len(genome) > 3:
            s = self.rng.randint(0, len(genome) - 3)
            e = self.rng.randint(s + 2, min(s + 10, len(genome)))
            block = genome[s:e]
            self.rng.shuffle(block)
            genome[s:e] = block
        return genome[:self.max_len]

    def search(self, generations: int = 1000, verbose: bool = True) -> list[str]:
        pop = []
        for _ in range(self.pop_size):
            g = self.random_genome()
            scores = self.evaluate(g)
            pop.append((scores, g))

        best_mean_wj = float("inf")
        best_genome = None
        n_features = len(FEATURE_NAMES)

        for gen in range(generations):
            for scores, genome in pop:
                wj_times = [scores[i * n_features] for i in range(self.n_cases)]
                mean_wj = sum(wj_times) / len(wj_times)
                if mean_wj < best_mean_wj:
                    best_mean_wj = mean_wj
                    best_genome = genome
                    if verbose:
                        buys = [a for a in genome if a != "run"]
                        parts = []
                        for k, g in groupby(buys):
                            n = len(list(g))
                            parts.append(f"{n}x{k}" if n > 1 else k)
                        print(f"  gen {gen}: {best_mean_wj:.1f}s ({len(genome)} actions, {len(buys)} buys) {','.join(parts)}")

            pop.sort(key=lambda x: sum(x[0][i * n_features] for i in range(self.n_cases)))
            elite_count = self.pop_size // 10
            new_pop = list(pop[:elite_count])

            while len(new_pop) < self.pop_size:
                a = self.lexicase_select(pop)
                b = self.lexicase_select(pop)
                child = self.crossover(a, b)
                if self.rng.random() < self.mutation_rate:
                    child = self.mutate(child)
                scores = self.evaluate(child)
                new_pop.append((scores, child))

            pop = new_pop

        if verbose:
            print(f"  Final best: {best_mean_wj:.1f}s")
        return best_genome


if __name__ == "__main__":
    import argparse as _ap
    _p = _ap.ArgumentParser()
    _p.add_argument("--profile", "-p", default="mysko")
    _args, _ = _p.parse_known_args()
    config = load_config(profile=_args.profile)

    ga = LexicaseGA(config, seed=42, pop_size=300, n_cases=5, mutation_rate=0.4, epsilon=3.0)
    genome = ga.search(generations=1000)

    buys = [a for a in genome if a != "run"]
    parts = []
    for k, g in groupby(buys):
        n = len(list(g))
        parts.append(f"{n}x{k}" if n > 1 else k)
    print(f"\nBuy summary: {', '.join(parts)}")

    mean = run_sequence_mean(config, genome + ["wallJump"], n_sims=10000, seed=42)
    print(f"Validated (10000 sims): Mean={mean:.1f}s")
