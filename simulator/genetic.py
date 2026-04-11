"""Genetic algorithm for upgrade sequence optimization.

Genome: a list of upgrade names (non-terminal buyable upgrades).
The terminal upgrade is implicit at the end.
Fitness: negative total time (lower is better).

Operators:
  - Crossover: single-point crossover of two sequences
  - Mutation: insert, delete, swap, or change an upgrade
  - Selection: tournament selection
"""
import random
import statistics
from itertools import groupby

from config import SimConfig
from simulator import Simulator
from policy import FixedSequence


class GeneticSearch:
    def __init__(self, config: SimConfig, seed: int = 42,
                 pop_size: int = 200, elite_count: int = 20,
                 mutation_rate: float = 0.3, eval_sims: int = 3):
        self.config = config
        self.rng = random.Random(seed)
        self.sim = Simulator(config, seed=seed)
        self.pop_size = pop_size
        self.elite_count = elite_count
        self.mutation_rate = mutation_rate
        self.eval_sims = eval_sims
        self.upgrade_names = config.buyable_upgrade_names
        self.upgrade_caps = {name: config.buyable_upgrades[name].cap
                            for name in self.upgrade_names}

    def evaluate(self, genome: list[str]) -> float:
        """Average time over eval_sims runs, each with a different seed."""
        total = 0.0
        for i in range(self.eval_sims):
            sim = Simulator(self.config, seed=self.rng.randint(0, 2**31))
            policy = FixedSequence(genome + [self.config.terminal_upgrade])
            state = sim.run(policy)
            total += state.time
        return total / self.eval_sims

    def random_genome(self) -> list[str]:
        """Generate a random valid genome."""
        genome = []
        for name in self.upgrade_names:
            cap = self.upgrade_caps[name]
            n = self.rng.randint(0, min(20, cap))
            genome.extend([name] * n)
        self.rng.shuffle(genome)
        return genome

    def crossover(self, a: list[str], b: list[str]) -> list[str]:
        """Single-point crossover."""
        if not a or not b:
            return list(a or b)
        cut_a = self.rng.randint(0, len(a))
        cut_b = self.rng.randint(0, len(b))
        child = a[:cut_a] + b[cut_b:]
        # Enforce caps
        return self._clamp(child)

    def mutate(self, genome: list[str]) -> list[str]:
        """Random mutation: insert, delete, swap, or change."""
        genome = list(genome)
        op = self.rng.choice(["insert", "delete", "swap", "change", "shuffle_block"])

        if op == "insert" and len(genome) < 40:
            pos = self.rng.randint(0, len(genome))
            gene = self.rng.choice(self.upgrade_names)
            genome.insert(pos, gene)

        elif op == "delete" and len(genome) > 1:
            pos = self.rng.randint(0, len(genome) - 1)
            genome.pop(pos)

        elif op == "swap" and len(genome) > 1:
            i = self.rng.randint(0, len(genome) - 1)
            j = self.rng.randint(0, len(genome) - 1)
            genome[i], genome[j] = genome[j], genome[i]

        elif op == "change" and genome:
            pos = self.rng.randint(0, len(genome) - 1)
            # Pick a different upgrade type
            others = [n for n in self.upgrade_names if n != genome[pos]]
            if others:
                genome[pos] = self.rng.choice(others)

        elif op == "shuffle_block" and len(genome) > 2:
            # Shuffle a random contiguous block
            start = self.rng.randint(0, len(genome) - 2)
            end = self.rng.randint(start + 1, min(start + 8, len(genome)))
            block = genome[start:end]
            self.rng.shuffle(block)
            genome[start:end] = block

        return self._clamp(genome)

    def _clamp(self, genome: list[str]) -> list[str]:
        """Enforce upgrade caps."""
        result = []
        counts = {name: 0 for name in self.upgrade_names}
        for g in genome:
            if g in counts and counts[g] < self.upgrade_caps[g]:
                result.append(g)
                counts[g] += 1
        return result

    def tournament_select(self, pop: list[tuple[float, list[str]]], k: int = 3) -> list[str]:
        """Select best of k random individuals."""
        contestants = self.rng.sample(pop, min(k, len(pop)))
        return min(contestants, key=lambda x: x[0])[1]

    def search(self, generations: int = 500, verbose: bool = True,
                on_improvement: callable = None) -> list[str]:
        # Initialize population
        pop = []
        for _ in range(self.pop_size):
            g = self.random_genome()
            t = self.evaluate(g)
            pop.append((t, g))
        pop.sort()

        best_time = pop[0][0]
        best_genome = pop[0][1]
        if on_improvement:
            on_improvement(best_genome, best_time)

        for gen in range(generations):
            # Elitism: keep top individuals
            new_pop = list(pop[:self.elite_count])

            # Fill rest with crossover + mutation
            while len(new_pop) < self.pop_size:
                parent_a = self.tournament_select(pop)
                parent_b = self.tournament_select(pop)
                child = self.crossover(parent_a, parent_b)
                if self.rng.random() < self.mutation_rate:
                    child = self.mutate(child)
                t = self.evaluate(child)
                new_pop.append((t, child))

            pop = sorted(new_pop)

            if pop[0][0] < best_time:
                best_time = pop[0][0]
                best_genome = pop[0][1]
                if on_improvement:
                    on_improvement(best_genome, best_time)
                if verbose:
                    parts = []
                    for k, g in groupby(best_genome):
                        n = len(list(g))
                        parts.append(f"{n}x{k}" if n > 1 else k)
                    print(f"  gen {gen}: {best_time:.1f}s ({len(best_genome)} buys) {','.join(parts)}")

        if verbose:
            print(f"  Final best: {best_time:.1f}s")
        return best_genome
