"""GA over explicit action sequences (run/buy interleaved).

Genome: list of "run" / "cashPerLoop" / "cloneCount".
wallJump is bought when affordable after the genome ends (or mid-sequence).
"""
import random
import math
from itertools import groupby

import sys, os
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from config import SimConfig, load_config
from sim import SimState, step, run_sequence, run_sequence_mean


ACTIONS = ["run", "cashPerLoop", "cloneCount"]


class GA:
    def __init__(self, config: SimConfig, seed: int = 42,
                 pop_size: int = 300, elite_count: int = 30,
                 mutation_rate: float = 0.4, eval_sims: int = 5,
                 max_genome_len: int = 200):
        self.config = config
        self.rng = random.Random(seed)
        self.pop_size = pop_size
        self.elite_count = elite_count
        self.mutation_rate = mutation_rate
        self.eval_sims = eval_sims
        self.max_len = max_genome_len

    def evaluate(self, genome: list[str]) -> float:
        # Append wallJump at end
        full = genome + ["wallJump"]
        total = 0.0
        for i in range(self.eval_sims):
            rng = random.Random(self.rng.randint(0, 2**31))
            state = SimState(self.config)
            for action in full:
                if state.done:
                    break
                step(state, action, rng)
            # If wallJump not bought yet, keep running until affordable
            while not state.done and state.game.time < 100000:
                step(state, "run", rng)
                if state.game.can_afford("wallJump"):
                    step(state, "wallJump", rng)
            total += state.game.time
        return total / self.eval_sims

    def random_genome(self) -> list[str]:
        length = self.rng.randint(10, 80)
        genome = []
        for _ in range(length):
            # Bias toward "run" since most actions are runs
            r = self.rng.random()
            if r < 0.6:
                genome.append("run")
            elif r < 0.8:
                genome.append("cashPerLoop")
            else:
                genome.append("cloneCount")
        return genome

    def crossover(self, a: list[str], b: list[str]) -> list[str]:
        if not a or not b:
            return list(a or b)
        cut_a = self.rng.randint(0, len(a))
        cut_b = self.rng.randint(0, len(b))
        child = a[:cut_a] + b[cut_b:]
        return child[:self.max_len]

    def mutate(self, genome: list[str]) -> list[str]:
        genome = list(genome)
        op = self.rng.choice(["insert", "delete", "change", "swap", "block_shuffle", "block_delete"])

        if op == "insert" and len(genome) < self.max_len:
            pos = self.rng.randint(0, len(genome))
            gene = self.rng.choice(ACTIONS)
            genome.insert(pos, gene)
        elif op == "delete" and len(genome) > 5:
            genome.pop(self.rng.randint(0, len(genome) - 1))
        elif op == "change" and genome:
            pos = self.rng.randint(0, len(genome) - 1)
            genome[pos] = self.rng.choice(ACTIONS)
        elif op == "swap" and len(genome) > 1:
            i, j = self.rng.sample(range(len(genome)), 2)
            genome[i], genome[j] = genome[j], genome[i]
        elif op == "block_shuffle" and len(genome) > 3:
            start = self.rng.randint(0, len(genome) - 3)
            end = self.rng.randint(start + 2, min(start + 10, len(genome)))
            block = genome[start:end]
            self.rng.shuffle(block)
            genome[start:end] = block
        elif op == "block_delete" and len(genome) > 10:
            start = self.rng.randint(0, len(genome) - 5)
            end = self.rng.randint(start + 1, min(start + 5, len(genome)))
            del genome[start:end]

        return genome[:self.max_len]

    def tournament_select(self, pop, k=3):
        contestants = self.rng.sample(pop, min(k, len(pop)))
        return min(contestants, key=lambda x: x[0])[1]

    def search(self, generations: int = 500, verbose: bool = True) -> list[str]:
        pop = []
        for _ in range(self.pop_size):
            g = self.random_genome()
            t = self.evaluate(g)
            pop.append((t, g))
        pop.sort()

        best_time = pop[0][0]
        best_genome = pop[0][1]

        for gen in range(generations):
            for t, g in pop:
                if t < best_time:
                    best_time = t
                    best_genome = g
                    if verbose:
                        buys = [a for a in g if a != "run"]
                        parts = []
                        for k, gg in groupby(buys):
                            n = len(list(gg))
                            parts.append(f"{n}x{k}" if n > 1 else k)
                        print(f"  gen {gen}: {best_time:.1f}s ({len(g)} actions, {len(buys)} buys) {','.join(parts)}")

            pop.sort()
            new_pop = list(pop[:self.elite_count])

            while len(new_pop) < self.pop_size:
                a = self.tournament_select(pop)
                b = self.tournament_select(pop)
                child = self.crossover(a, b)
                if self.rng.random() < self.mutation_rate:
                    child = self.mutate(child)
                t = self.evaluate(child)
                new_pop.append((t, child))

            pop = sorted(new_pop)

        if verbose:
            print(f"  Final best: {best_time:.1f}s")
        return best_genome


def summarize(genome: list[str]) -> str:
    buys = [a for a in genome if a != "run"]
    parts = []
    for k, g in groupby(buys):
        n = len(list(g))
        parts.append(f"{n}x{k}" if n > 1 else k)
    return ", ".join(parts)


if __name__ == "__main__":
    import argparse as _ap
    _p = _ap.ArgumentParser()
    _p.add_argument("--profile", "-p", default="mysko")
    _p.add_argument("--course", "-c", default="course1")
    _args, _ = _p.parse_known_args()
    config = load_config(profile=_args.profile, course=_args.course)

    ga = GA(config, seed=42, pop_size=300, elite_count=30,
            mutation_rate=0.4, eval_sims=5)
    genome = ga.search(generations=1000)

    print(f"\nBuy summary: {summarize(genome)}")
    print(f"Full sequence ({len(genome)} actions): {genome}")

    # Validate
    mean = run_sequence_mean(config, genome + ["wallJump"], n_sims=10000, seed=42)
    print(f"Validated (10000 sims): Mean={mean:.1f}s")
