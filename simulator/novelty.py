"""Genetic algorithm with novelty search.

Instead of selecting for fitness alone, novelty search rewards individuals
that are DIFFERENT from the rest of the population and from an archive
of previously seen behaviors. This prevents premature convergence.

Behavior characterization: a vector of milestone times (same as lexicase features).
Novelty = average distance to k-nearest neighbors in behavior space.
Selection: blend of novelty and fitness.
"""
import random
import math
from itertools import groupby

from config import SimConfig
from simulator import Simulator
from policy import FixedSequence
from metrics import simulate_with_metrics
from lexicase import FEATURE_NAMES


class NoveltyGA:
    def __init__(self, config: SimConfig, seed: int = 42,
                 pop_size: int = 300, k_nearest: int = 15,
                 novelty_weight: float = 0.5, mutation_rate: float = 0.4,
                 archive_rate: float = 0.05, n_eval_seeds: int = 3):
        self.config = config
        self.rng = random.Random(seed)
        self.pop_size = pop_size
        self.k_nearest = k_nearest
        self.novelty_weight = novelty_weight  # 0=pure fitness, 1=pure novelty
        self.mutation_rate = mutation_rate
        self.archive_rate = archive_rate
        self.n_eval_seeds = n_eval_seeds
        self.upgrade_names = config.buyable_upgrade_names
        self.upgrade_caps = {name: config.buyable_upgrades[name].cap
                            for name in self.upgrade_names}
        self.eval_seeds = [seed + i * 7919 for i in range(n_eval_seeds)]
        self.archive = []  # list of behavior vectors

    def behavior(self, genome: list[str]) -> list[float]:
        """Compute behavior vector: average feature values across eval seeds."""
        all_features = []
        for s in self.eval_seeds:
            features = simulate_with_metrics(self.config, genome, s)
            all_features.append(features)

        # Average each feature across seeds
        keys = list(all_features[0].keys())
        avg = []
        for k in keys:
            vals = [f[k] for f in all_features]
            avg.append(sum(vals) / len(vals))
        return avg

    def fitness(self, genome: list[str]) -> float:
        """Mean wallJump time across seeds (lower is better)."""
        total = 0.0
        for s in self.eval_seeds:
            features = simulate_with_metrics(self.config, genome, s)
            total += features["time_to_terminal"]
        return total / self.n_eval_seeds

    def compute_novelty_scores(self, pop_bvs: list[list[float]], archive_bvs: list[list[float]]) -> list[float]:
        """Compute novelty score for each member of pop against pop+archive.
        Normalizes features by z-score before distance computation."""
        all_bvs = pop_bvs + archive_bvs
        if not all_bvs or not all_bvs[0]:
            return [0.0] * len(pop_bvs)

        # Compute mean and std per feature
        n_features = len(all_bvs[0])
        means = [0.0] * n_features
        for bv in all_bvs:
            for i, v in enumerate(bv):
                means[i] += v
        means = [m / len(all_bvs) for m in means]

        stds = [0.0] * n_features
        for bv in all_bvs:
            for i, v in enumerate(bv):
                stds[i] += (v - means[i]) ** 2
        stds = [math.sqrt(s / len(all_bvs)) if s > 0 else 1.0 for s in stds]

        # Normalize all behavior vectors
        def normalize(bv):
            return [(v - means[i]) / stds[i] if stds[i] > 0 else 0.0
                    for i, v in enumerate(bv)]

        norm_all = [normalize(bv) for bv in all_bvs]
        norm_pop = norm_all[:len(pop_bvs)]

        # Compute novelty for each pop member
        scores = []
        for i, nbv in enumerate(norm_pop):
            dists = sorted(self._dist(nbv, other) for j, other in enumerate(norm_all) if j != i)
            k = min(self.k_nearest, len(dists))
            scores.append(sum(dists[:k]) / k if k > 0 else 0.0)
        return scores

    def _dist(self, a: list[float], b: list[float]) -> float:
        total = 0.0
        for x, y in zip(a, b):
            diff = x - y
            total += diff * diff
        return math.sqrt(total)

    def random_genome(self) -> list[str]:
        genome = []
        for name in self.upgrade_names:
            cap = self.upgrade_caps[name]
            n = self.rng.randint(0, min(20, cap))
            genome.extend([name] * n)
        self.rng.shuffle(genome)
        return genome

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

    def tournament_select(self, scored_pop, k=3):
        """Tournament on blended score."""
        contestants = self.rng.sample(scored_pop, min(k, len(scored_pop)))
        return min(contestants, key=lambda x: x[0])[2]  # (score, bv, genome)

    def search(self, generations: int = 1000, verbose: bool = True,
                on_improvement: callable = None) -> list[str]:
        # Initialize population: (fitness, behavior, genome)
        pop = []
        for _ in range(self.pop_size):
            g = self.random_genome()
            f = self.fitness(g)
            bv = self.behavior(g)
            pop.append((f, bv, g))

        best_fitness = float("inf")
        best_genome = None

        for gen in range(generations):
            # Compute novelty scores (z-score normalized)
            pop_bvs = [bv for _, bv, _ in pop]
            novelty_scores = self.compute_novelty_scores(pop_bvs, self.archive)

            # Rank by fitness and novelty separately
            fit_ranked = sorted(range(len(pop)), key=lambda i: pop[i][0])
            nov_ranked = sorted(range(len(pop)), key=lambda i: -novelty_scores[i])

            fit_rank = {idx: rank for rank, idx in enumerate(fit_ranked)}
            nov_rank = {idx: rank for rank, idx in enumerate(nov_ranked)}

            # Blended rank for selection
            blended = []
            for i, (f, bv, g) in enumerate(pop):
                score = (1 - self.novelty_weight) * fit_rank[i] + self.novelty_weight * nov_rank[i]
                blended.append((score, bv, g))

            # Track best by fitness
            for f, _, g in pop:
                if f < best_fitness:
                    best_fitness = f
                    best_genome = g
                    if on_improvement:
                        on_improvement(best_genome, best_fitness)
                    if verbose:
                        parts = []
                        for k, gg in groupby(g):
                            n = len(list(gg))
                            parts.append(f"{n}x{k}" if n > 1 else k)
                        print(f"  gen {gen}: {best_fitness:.1f}s ({len(g)} buys) {','.join(parts)}")

            # Add random individuals to archive
            for f, bv, g in pop:
                if self.rng.random() < self.archive_rate:
                    self.archive.append(bv)

            # Elitism by fitness
            pop_by_fit = sorted(pop, key=lambda x: x[0])
            elite_count = self.pop_size // 10
            new_pop = list(pop_by_fit[:elite_count])

            # Select rest using blended tournament
            while len(new_pop) < self.pop_size:
                parent_a = self.tournament_select(blended)
                parent_b = self.tournament_select(blended)
                child = self.crossover(parent_a, parent_b)
                if self.rng.random() < self.mutation_rate:
                    child = self.mutate(child)
                f = self.fitness(child)
                bv = self.behavior(child)
                new_pop.append((f, bv, child))

            pop = new_pop

        if verbose:
            print(f"  Final best: {best_fitness:.1f}s")
        return best_genome
