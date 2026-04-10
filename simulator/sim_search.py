#!/usr/bin/env python3
"""
Simulator-based block-pattern search for optimal upgrade ordering.

Generates candidate sequences as block patterns (XC YK ZC WK ...),
evaluates each in the stochastic simulator, and reports the best.

Usage:
  python3 sim_search.py [--nc 10] [--nk 10] [--sims 200] [--top-sims 5000] [--top 10]
"""

import argparse
import itertools
import statistics
import time as _timer

from config import load_config
from simulator import Simulator
from policy import FixedSequence


def gen_2block_seqs(nc, nk):
    """2-block: XC YK (nc-X)C (nk-Y)K"""
    for x in range(1, nc + 1):
        for y in range(1, nk + 1):
            rest_c = nc - x
            rest_k = nk - y
            seq = [0] * x + [1] * y
            if rest_c > 0:
                seq += [0] * rest_c
            if rest_k > 0:
                seq += [1] * rest_k
            yield seq


def gen_3block_seqs(nc, nk):
    """3-block: X1C Y1K X2C Y2K X3C (or trailing clone block)"""
    # cash-clone-cash-clone-cash
    for x1 in range(1, nc):
        for y1 in range(1, nk):
            for x2 in range(1, nc - x1 + 1):
                y2 = nk - y1
                x3 = nc - x1 - x2
                if x3 >= 0 and y2 >= 0:
                    seq = [0] * x1 + [1] * y1 + [0] * x2
                    if y2 > 0:
                        seq += [1] * y2
                    if x3 > 0:
                        seq += [0] * x3
                    if len(seq) == nc + nk:
                        yield seq
    # cash-clone-cash-clone (no trailing cash)
    for x1 in range(1, nc):
        for y1 in range(1, nk):
            x2 = nc - x1
            y2 = nk - y1
            if x2 > 0 and y2 > 0:
                seq = [0] * x1 + [1] * y1 + [0] * x2 + [1] * y2
                if len(seq) == nc + nk:
                    yield seq


def gen_4block_seqs(nc, nk):
    """4-block: X1C Y1K X2C Y2K X3C Y3K (+ optional trailing)"""
    for x1 in range(1, nc - 1):
        for y1 in range(1, nk - 1):
            for x2 in range(1, nc - x1):
                for y2 in range(1, nk - y1):
                    x3 = nc - x1 - x2
                    y3 = nk - y1 - y2
                    if x3 > 0 and y3 > 0:
                        seq = ([0] * x1 + [1] * y1 +
                               [0] * x2 + [1] * y2 +
                               [0] * x3 + [1] * y3)
                        if len(seq) == nc + nk:
                            yield seq


def deduplicate(candidates, N):
    """Deduplicate and filter to correct length."""
    seen = set()
    unique = []
    for seq in candidates:
        if len(seq) != N:
            continue
        key = tuple(seq)
        if key not in seen:
            seen.add(key)
            unique.append(seq)
    return unique


def rle(seq):
    """Run-length encode: [0,0,1,1,1] → '2C 3K'"""
    parts = []
    i = 0
    while i < len(seq):
        j = i
        while j < len(seq) and seq[j] == seq[i]:
            j += 1
        label = "C" if seq[i] == 0 else "K"
        parts.append(f"{j - i}{label}")
        i = j
    parts.append("WJ")
    return " ".join(parts)


def seq_to_names(seq):
    return ["cashPerLoop" if v == 0 else "cloneCount" for v in seq]


def evaluate(seq, config, n_sims, seed):
    """Evaluate a sequence in the stochastic simulator."""
    names = seq_to_names(seq) + ["wallJump"]
    policy = FixedSequence(names)
    sim = Simulator(config, seed=seed)
    times = sim.run_batch(policy, n=n_sims)
    times.sort()
    return {
        "mean": statistics.mean(times),
        "median": statistics.median(times),
        "p10": times[int(len(times) * 0.10)],
        "p90": times[min(len(times) - 1, int(len(times) * 0.90))],
    }


def main():
    parser = argparse.ArgumentParser(description="Sim-based block search for optimal upgrade order")
    parser.add_argument("--nc", type=int, default=10, help="Number of cashPerLoop purchases")
    parser.add_argument("--nk", type=int, default=10, help="Number of cloneCount purchases")
    parser.add_argument("--sims", type=int, default=200, help="Sims per candidate in screening pass")
    parser.add_argument("--top-sims", type=int, default=5000, help="Sims for top candidate validation")
    parser.add_argument("--top", type=int, default=10, help="Number of top candidates to validate")
    parser.add_argument("--seed", type=int, default=42, help="RNG seed")
    parser.add_argument("--blocks", type=int, default=4, help="Max block depth (2, 3, or 4)")
    args = parser.parse_args()

    nc, nk = args.nc, args.nk
    N = nc + nk

    import argparse as _ap
    _p = _ap.ArgumentParser()
    _p.add_argument("--profile", "-p", default="mysko")
    _args, _ = _p.parse_known_args()
    config = load_config(profile=_args.profile)

    print("=" * 72)
    print(f"Sim-based block search: nc={nc}, nk={nk}")
    print("=" * 72)
    print(f"success_rate={config.success_rate:.0%}  "
          f"avg_success={config.avg_success_time:.1f}s  "
          f"avg_fail={config.avg_failure_time:.1f}s  "
          f"clone_dur={config.clone_course_duration:.1f}s")
    print()

    # Generate candidates
    print("Generating block-pattern candidates...")
    candidates = list(gen_2block_seqs(nc, nk))
    if args.blocks >= 3:
        candidates.extend(gen_3block_seqs(nc, nk))
    if args.blocks >= 4:
        candidates.extend(gen_4block_seqs(nc, nk))

    candidates = deduplicate(candidates, N)
    print(f"  {len(candidates)} unique candidates (up to {args.blocks}-block patterns)")
    print()

    # Screening pass
    print(f"Screening pass: {args.sims} sims each...")
    t0 = _timer.time()
    results = []
    for i, seq in enumerate(candidates):
        stats = evaluate(seq, config, args.sims, args.seed)
        results.append((stats["mean"], seq, stats))
        if (i + 1) % 100 == 0:
            print(f"  {i + 1}/{len(candidates)} evaluated...")

    results.sort(key=lambda r: r[0])
    elapsed = _timer.time() - t0
    print(f"  Screened {len(results)} candidates in {elapsed:.1f}s")
    print()

    # Show screening top
    print(f"Screening top {args.top}:")
    print(f"  {'Mean':>7}  Sequence")
    print(f"  {'-' * 50}")
    for mean, seq, _ in results[:args.top]:
        print(f"  {mean:>7.2f}  {rle(seq)}")
    print()

    # Validation pass
    top_n = min(args.top, len(results))
    print(f"Validation pass: top {top_n} with {args.top_sims} sims each...")
    validated = []
    for _, seq, _ in results[:top_n]:
        stats = evaluate(seq, config, args.top_sims, args.seed)
        validated.append((stats["mean"], seq, stats))

    validated.sort(key=lambda r: r[0])
    print()
    print(f"{'Mean':>7} {'Median':>7} {'P10':>7} {'P90':>7}  Sequence")
    print("-" * 65)
    for mean, seq, stats in validated:
        print(f"{stats['mean']:>7.2f} {stats['median']:>7.2f} "
              f"{stats['p10']:>7.2f} {stats['p90']:>7.2f}  {rle(seq)}")

    # Winner
    best_mean, best_seq, best_stats = validated[0]
    print(f"\nBEST: {rle(best_seq)}  mean={best_mean:.2f}s")
    print(f"  Full: {' → '.join(seq_to_names(best_seq))} → wallJump")


if __name__ == "__main__":
    main()
