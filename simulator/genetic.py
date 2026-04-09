"""Genetic algorithm for upgrade sequence optimization.

Genome: a list of upgrade names (cashPerLoop / cloneCount).
wallJump is implicit at the end.
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
        self.cash_cap = config.upgrades["cashPerLoop"].cap
        self.clone_cap = config.upgrades["cloneCount"].cap

    def evaluate(self, genome: list[str]) -> float:
        """Average time over eval_sims runs, each with a different seed."""
        total = 0.0
        for i in range(self.eval_sims):
            sim = Simulator(self.config, seed=self.rng.randint(0, 2**31))
            policy = FixedSequence(genome + ["wallJump"])
            state = sim.run(policy)
            total += state.time
        return total / self.eval_sims

    def random_genome(self) -> list[str]:
        """Generate a random valid genome."""
        n_cash = self.rng.randint(0, min(20, self.cash_cap))
        n_clone = self.rng.randint(0, min(15, self.clone_cap))
        genome = ["cashPerLoop"] * n_cash + ["cloneCount"] * n_clone
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
            gene = self.rng.choice(["cashPerLoop", "cloneCount"])
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
            genome[pos] = "cloneCount" if genome[pos] == "cashPerLoop" else "cashPerLoop"

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
        cash = 0
        clone = 0
        for g in genome:
            if g == "cashPerLoop" and cash < self.cash_cap:
                result.append(g)
                cash += 1
            elif g == "cloneCount" and clone < self.clone_cap:
                result.append(g)
                clone += 1
        return result

    def tournament_select(self, pop: list[tuple[float, list[str]]], k: int = 3) -> list[str]:
        """Select best of k random individuals."""
        contestants = self.rng.sample(pop, min(k, len(pop)))
        return min(contestants, key=lambda x: x[0])[1]

    def search(self, generations: int = 500, verbose: bool = True) -> list[str]:
        # Initialize population
        pop = []
        for _ in range(self.pop_size):
            g = self.random_genome()
            t = self.evaluate(g)
            pop.append((t, g))
        pop.sort()

        best_time = pop[0][0]
        best_genome = pop[0][1]

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
                if verbose:
                    parts = []
                    for k, g in groupby(best_genome):
                        n = len(list(g))
                        parts.append(f"{n}x{k}" if n > 1 else k)
                    print(f"  gen {gen}: {best_time:.1f}s ({len(best_genome)} buys) {','.join(parts)}")

        if verbose:
            print(f"  Final best: {best_time:.1f}s")
        return best_genome
