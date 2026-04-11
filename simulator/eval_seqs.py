#!/usr/bin/env python3
"""Evaluate a JSON array of sequences.

Usage:
  python3.11 eval_seqs.py sequences.json [n_sims]
  echo '[["cashPerLoop","cloneCount","wallJump"]]' | python3.11 eval_seqs.py - [n_sims]

Input JSON format: array of arrays of upgrade names.
wallJump is appended automatically if not present.
"""
import sys
import json
import statistics
from itertools import groupby

from config import load_config
from simulator import Simulator
from policy import FixedSequence


def main():
    # Read input
    if len(sys.argv) < 2:
        print("Usage: eval_seqs.py <sequences.json | -> [n_sims]")
        sys.exit(1)

    src = sys.argv[1]
    n_sims = int(sys.argv[2]) if len(sys.argv) > 2 else 2000

    if src == "-":
        data = json.load(sys.stdin)
    else:
        with open(src) as f:
            data = json.load(f)

    import argparse as _ap
    _p = _ap.ArgumentParser()
    _p.add_argument("--profile", "-p", default="mysko")
    _p.add_argument("--course", "-c", default="course1")
    _args, _ = _p.parse_known_args()
    config = load_config(profile=_args.profile, course=_args.course)
    sim = Simulator(config, seed=42)

    print(f"Evaluating {len(data)} sequences @ {n_sims} sims each\n")
    print(f"{'#':<4} {'Mean':>8} {'Median':>8} {'P10':>8} {'P90':>8}  {'Buys':>4}  Summary")
    print("-" * 90)

    results = []
    for i, seq in enumerate(data):
        if not seq or seq[-1] != "wallJump":
            seq = seq + ["wallJump"]

        times = sim.run_batch(FixedSequence(seq), n=n_sims)
        times.sort()
        mean = statistics.mean(times)
        median = statistics.median(times)
        p10 = times[int(len(times) * 0.1)]
        p90 = times[min(len(times) - 1, int(len(times) * 0.9))]

        parts = []
        for k, g in groupby(seq[:-1]):  # exclude wallJump from summary
            n = len(list(g))
            parts.append(f"{n}x{k}" if n > 1 else k)
        summary = ", ".join(parts)

        results.append((mean, i, median, p10, p90, len(seq) - 1, summary))

    results.sort()
    for mean, i, median, p10, p90, buys, summary in results:
        print(f"{i:<4} {mean:>8.1f} {median:>8.1f} {p10:>8.1f} {p90:>8.1f}  {buys:>4}  {summary}")


if __name__ == "__main__":
    main()
