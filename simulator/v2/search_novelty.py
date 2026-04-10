"""Novelty search GA over explicit action sequences (v2 simulator).

Novelty = z-score normalized distance to k-nearest neighbors in behavior space.
Selection: blend of fitness rank and novelty rank.
"""
import random
import math
from itertools import groupby

import sys, os
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from config import SimConfig, load_config
from sim import SimState, step, run_sequence_mean
from metrics import simulate_with_metrics
from search_lexicase import FEATURE_NAMES


class NoveltyGA:
    def __init__(self, config: SimConfig, seed: int = 42,
                 pop_size: int = 300, k_nearest: int = 15,
                 novelty_weight: float = 0.4, mutation_rate: float = 0.4,
                 archive_rate: float = 0.05, n_eval_seeds: int = 3,
                 max_genome_len: int = 200):
        self.config = config
        self.rng = random.Random(seed)
        self.pop_size = pop_size
        self.k_nearest = k_nearest
        self.novelty_weight = novelty_weight
        self.mutation_rate = mutation_rate
        self.archive_rate = archive_rate
        self.n_eval_seeds = n_eval_seeds
        self.max_len = max_genome_len
        self.eval_seeds = [seed + i * 7919 for i in range(n_eval_seeds)]
        self.archive = []

    def behavior(self, genome: list[str]) -> list[float]:
        all_features = []
        for s in self.eval_seeds:
            features = simulate_with_metrics(self.config, genome, random.Random(s))
            all_features.append(features)
        keys = list(all_features[0].keys())
        return [sum(f[k] for f in all_features) / len(all_features) for k in keys]

    def fitness(self, genome: list[str]) -> float:
        total = 0.0
        for s in self.eval_seeds:
            features = simulate_with_metrics(self.config, genome, random.Random(s))
            total += features["time_to_walljump"]
        return total / self.n_eval_seeds

    def compute_novelty_scores(self, pop_bvs, archive_bvs):
        all_bvs = pop_bvs + archive_bvs
        if not all_bvs or not all_bvs[0]:
            return [0.0] * len(pop_bvs)
        n_f = len(all_bvs[0])
        means = [sum(bv[i] for bv in all_bvs) / len(all_bvs) for i in range(n_f)]
        stds = [math.sqrt(sum((bv[i] - means[i])**2 for bv in all_bvs) / len(all_bvs)) or 1.0 for i in range(n_f)]

        def norm(bv):
            return [(bv[i] - means[i]) / stds[i] for i in range(n_f)]

        norm_all = [norm(bv) for bv in all_bvs]
        scores = []
        for i in range(len(pop_bvs)):
            dists = sorted(math.sqrt(sum((norm_all[i][f] - norm_all[j][f])**2 for f in range(n_f)))
                           for j in range(len(norm_all)) if j != i)
            k = min(self.k_nearest, len(dists))
            scores.append(sum(dists[:k]) / k if k > 0 else 0.0)
        return scores

    def random_genome(self):
        length = self.rng.randint(10, 80)
        genome = []
        for _ in range(length):
            r = self.rng.random()
            if r < 0.6: genome.append("run")
            elif r < 0.8: genome.append("cashPerLoop")
            else: genome.append("cloneCount")
        return genome

    def crossover(self, a, b):
        if not a or not b: return list(a or b)
        return (a[:self.rng.randint(0, len(a))] + b[self.rng.randint(0, len(b)):])[:self.max_len]

    def mutate(self, genome):
        genome = list(genome)
        op = self.rng.choice(["insert", "delete", "change", "swap", "block_shuffle"])
        if op == "insert" and len(genome) < self.max_len:
            genome.insert(self.rng.randint(0, len(genome)), self.rng.choice(["run", "cashPerLoop", "cloneCount"]))
        elif op == "delete" and len(genome) > 5:
            genome.pop(self.rng.randint(0, len(genome) - 1))
        elif op == "change" and genome:
            genome[self.rng.randint(0, len(genome) - 1)] = self.rng.choice(["run", "cashPerLoop", "cloneCount"])
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

    def tournament_select(self, blended, k=3):
        contestants = self.rng.sample(blended, min(k, len(blended)))
        return min(contestants, key=lambda x: x[0])[2]

    def search(self, generations: int = 1000, verbose: bool = True) -> list[str]:
        pop = []
        for _ in range(self.pop_size):
            g = self.random_genome()
            f = self.fitness(g)
            bv = self.behavior(g)
            pop.append((f, bv, g))

        best_fitness = float("inf")
        best_genome = None

        for gen in range(generations):
            for f, _, g in pop:
                if f < best_fitness:
                    best_fitness = f
                    best_genome = g
                    if verbose:
                        buys = [a for a in g if a != "run"]
                        parts = []
                        for k, gg in groupby(buys):
                            n = len(list(gg))
                            parts.append(f"{n}x{k}" if n > 1 else k)
                        print(f"  gen {gen}: {best_fitness:.1f}s ({len(g)} actions, {len(buys)} buys) {','.join(parts)}")

            pop_bvs = [bv for _, bv, _ in pop]
            novelty_scores = self.compute_novelty_scores(pop_bvs, self.archive)

            fit_ranked = sorted(range(len(pop)), key=lambda i: pop[i][0])
            nov_ranked = sorted(range(len(pop)), key=lambda i: -novelty_scores[i])
            fit_rank = {idx: rank for rank, idx in enumerate(fit_ranked)}
            nov_rank = {idx: rank for rank, idx in enumerate(nov_ranked)}

            blended = []
            for i, (f, bv, g) in enumerate(pop):
                score = (1 - self.novelty_weight) * fit_rank[i] + self.novelty_weight * nov_rank[i]
                blended.append((score, bv, g))

            for f, bv, g in pop:
                if self.rng.random() < self.archive_rate:
                    self.archive.append(bv)

            pop_by_fit = sorted(pop, key=lambda x: x[0])
            elite_count = self.pop_size // 10
            new_pop = list(pop_by_fit[:elite_count])

            while len(new_pop) < self.pop_size:
                a = self.tournament_select(blended)
                b = self.tournament_select(blended)
                child = self.crossover(a, b)
                if self.rng.random() < self.mutation_rate:
                    child = self.mutate(child)
                f = self.fitness(child)
                bv = self.behavior(child)
                new_pop.append((f, bv, child))

            pop = new_pop

        if verbose:
            print(f"  Final best: {best_fitness:.1f}s")
        return best_genome


if __name__ == "__main__":
    import argparse as _ap
    _p = _ap.ArgumentParser()
    _p.add_argument("--profile", "-p", default="mysko")
    _args, _ = _p.parse_known_args()
    config = load_config(profile=_args.profile)

    ga = NoveltyGA(config, seed=42, pop_size=300, k_nearest=15,
                   novelty_weight=0.4, mutation_rate=0.4, n_eval_seeds=3)
    genome = ga.search(generations=1000)

    buys = [a for a in genome if a != "run"]
    parts = []
    for k, g in groupby(buys):
        n = len(list(g))
        parts.append(f"{n}x{k}" if n > 1 else k)
    print(f"\nBuy summary: {', '.join(parts)}")

    mean = run_sequence_mean(config, genome + ["wallJump"], n_sims=10000, seed=42)
    print(f"Validated (10000 sims): Mean={mean:.1f}s")
