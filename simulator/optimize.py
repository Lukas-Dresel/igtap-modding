#!/usr/bin/env python3
"""Meta-optimizer: runs ALL search algorithms for a profile in parallel.

Each algorithm writes candidates to a shared JSONL file as it finds them.
After all finish (or timeout), validates all candidates and reports top results.

Usage:
  python3.11 optimize.py --profile mysko
  python3.11 optimize.py --profile grill --validate-sims 5000
"""
import argparse
import json
import statistics
import time
import os
from itertools import groupby
from pathlib import Path
from threading import Thread

from config import load_config
from simulator import Simulator
from policy import FixedSequence


def summarize(seq: list[str]) -> str:
    parts = []
    for k, g in groupby(seq):
        n = len(list(g))
        parts.append(f"{n}x{k}" if n > 1 else k)
    return ", ".join(parts)


def write_candidate(jsonl_path: str, algo: str, seq: list[str], internal_score: float = None):
    """Append a candidate to the JSONL file (thread-safe via append mode)."""
    entry = {"algo": algo, "sequence": seq, "summary": summarize(seq)}
    if internal_score is not None:
        entry["internal_score"] = round(internal_score, 1)
    with open(jsonl_path, "a") as f:
        f.write(json.dumps(entry) + "\n")
    score_str = f"{internal_score:6.1f}s" if internal_score is not None else "   ???"
    print(f"    {score_str}  [{algo}] {summarize(seq)}")


# === Algorithm runners — each writes candidates to JSONL as found ===

def run_ga(config, jsonl, seed=42, pop_size=500, gens=1000, eval_sims=5):
    from genetic import GeneticSearch
    ga = GeneticSearch(config, seed=seed, pop_size=pop_size, elite_count=pop_size // 10,
                       mutation_rate=0.4, eval_sims=eval_sims)
    genome = ga.search(generations=gens, verbose=False)
    write_candidate(jsonl, "ga", genome)
    # Also write the initial best since search() only returns final
    print(f"  [GA] done: {summarize(genome)}")


def run_ga_multi(config, jsonl, gens=500):
    from genetic import GeneticSearch
    for seed in [42, 123, 456]:
        ga = GeneticSearch(config, seed=seed, pop_size=300, elite_count=30,
                           mutation_rate=0.4, eval_sims=5)
        genome = ga.search(generations=gens, verbose=False,
                           on_improvement=lambda g, t, s=seed: write_candidate(jsonl, f"ga_s{s}", g, t))


def run_lexicase(config, jsonl, gens=300):
    from lexicase import LexicaseGA
    for seed in [42, 123, 456]:
        ga = LexicaseGA(config, seed=seed, pop_size=300, n_cases=5,
                        mutation_rate=0.4, epsilon=3.0)
        genome = ga.search(generations=gens, verbose=False,
                           on_improvement=lambda g, t, s=seed: write_candidate(jsonl, f"lexicase_s{s}", g, t))


def run_novelty(config, jsonl, gens=300):
    from novelty import NoveltyGA
    for seed in [42, 123]:
        ga = NoveltyGA(config, seed=seed, pop_size=300, k_nearest=15,
                       novelty_weight=0.4, mutation_rate=0.4, n_eval_seeds=3)
        genome = ga.search(generations=gens, verbose=False,
                           on_improvement=lambda g, t, s=seed: write_candidate(jsonl, f"novelty_s{s}", g, t))


def run_graph(config, jsonl):
    from graph import solve
    sim = Simulator(config, seed=42)
    path = solve(config, verbose=False)
    seq = [x for x in path if x != "wallJump"]
    score = statistics.mean(sim.run_batch(FixedSequence(seq + ["wallJump"]), n=10))
    write_candidate(jsonl, "graph", seq, score)


def run_graph_full(config, jsonl):
    from graph_full import solve
    sim = Simulator(config, seed=42)
    path = solve(config, verbose=False)
    seq = [x for x in path if x != "wallJump"]
    score = statistics.mean(sim.run_batch(FixedSequence(seq + ["wallJump"]), n=10))
    write_candidate(jsonl, "graphfull", seq, score)


def run_mcts(config, jsonl, iters=50000):
    from mcts import MCTS
    sim = Simulator(config, seed=42)
    for seed in [42, 123]:
        mcts = MCTS(config, seed=seed)
        buys = mcts.search(iterations=iters, verbose=False)
        score = statistics.mean(sim.run_batch(FixedSequence(buys + ["wallJump"]), n=10))
        write_candidate(jsonl, f"mcts_s{seed}", buys, score)


def run_hmcts(config, jsonl, iters=30000):
    from hmcts import HierarchicalMCTS
    sim = Simulator(config, seed=42)
    for seed in [42, 123]:
        hmcts = HierarchicalMCTS(config, seed=seed, max_block=12)
        seq = hmcts.search(iterations=iters, verbose=False)
        score = statistics.mean(sim.run_batch(FixedSequence(seq + ["wallJump"]), n=10))
        write_candidate(jsonl, f"hmcts_s{seed}", seq, score)


def run_simsearch(config, jsonl):
    from sim_search import evaluate as ss_eval, gen_2block_seqs, gen_3block_seqs, gen_4block_seqs, seq_to_names, deduplicate
    print("  [SimSearch] Running...")
    candidates = []
    for nc, nk_range in [(10, range(5, 15))]:
        for nk in nk_range:
            candidates.extend(list(gen_2block_seqs(nc, nk)))
            candidates.extend(list(gen_3block_seqs(nc, nk)))
            candidates.extend(list(gen_4block_seqs(nc, nk)))
    seen = set()
    unique = []
    for seq in candidates:
        key = tuple(seq)
        if key not in seen:
            seen.add(key)
            unique.append(seq)
    candidates = unique
    print(f"  [SimSearch] {len(candidates)} candidates")
    best_time = float("inf")
    best_seq = None
    for seq in candidates:
        result = ss_eval(seq, config, n_sims=3, seed=42)
        t = result["mean"]
        if t < best_time:
            best_time = t
            best_seq = seq_to_names(seq, config)
            write_candidate(jsonl, "simsearch", best_seq, best_time)
    print(f"  [SimSearch] done: {summarize(best_seq)} ({best_time:.1f}s)")


def validate(config, sequences: list[dict], n_sims: int = 2000, seed: int = 42):
    sim = Simulator(config, seed=seed)
    results = []
    for entry in sequences:
        seq = entry["sequence"]
        full = seq + ["wallJump"] if not seq or seq[-1] != "wallJump" else seq
        times = sim.run_batch(FixedSequence(full), n=n_sims)
        times.sort()
        mean = statistics.mean(times)
        median = statistics.median(times)
        p10 = times[int(len(times) * 0.1)]
        p90 = times[min(len(times) - 1, int(len(times) * 0.9))]
        results.append({
            "algo": entry.get("algo", "?"),
            "sequence": seq,
            "summary": summarize(seq),
            "buys": len(seq),
            "mean": round(mean, 1),
            "median": round(median, 1),
            "p10": round(p10, 1),
            "p90": round(p90, 1),
        })
    results.sort(key=lambda r: r["mean"])
    return results


def main():
    parser = argparse.ArgumentParser(description="Find optimal upgrade policy for a player profile")
    parser.add_argument("--profile", "-p", required=True)
    parser.add_argument("--ga-gens", type=int, default=500)
    parser.add_argument("--lexicase-gens", type=int, default=200)
    parser.add_argument("--novelty-gens", type=int, default=200)
    parser.add_argument("--mcts-iters", type=int, default=50000)
    parser.add_argument("--validate-sims", type=int, default=2000)
    parser.add_argument("--course", "-c", default="course1")
    parser.add_argument("--output", "-o")
    parser.add_argument("--skip", nargs="*", default=[])
    args = parser.parse_args()

    config = load_config(profile=args.profile, course=args.course)
    base = Path(__file__).parent
    jsonl_path = str(base / "profiles" / args.profile / args.course / "candidates.jsonl")

    # Clear previous candidates
    open(jsonl_path, "w").close()

    print(f"=== Optimizing for profile: {args.profile} ===")
    print(f"Success rate: {config.success_rate:.0%}")
    print(f"Avg success time: {config.avg_success_time:.1f}s")
    print(f"Avg failure time: {config.avg_failure_time:.1f}s")
    print(f"Clone duration: {config.clone_course_duration:.1f}s")
    print(f"Candidates → {jsonl_path}")
    print()

    skip = set(args.skip)
    threads = []

    algos = {
        "ga": lambda: run_ga_multi(config, jsonl_path, gens=args.ga_gens),
        "lexicase": lambda: run_lexicase(config, jsonl_path, gens=args.lexicase_gens),
        "novelty": lambda: run_novelty(config, jsonl_path, gens=args.novelty_gens),
        "graph": lambda: run_graph(config, jsonl_path),
        "graphfull": lambda: run_graph_full(config, jsonl_path),
        "mcts": lambda: run_mcts(config, jsonl_path, iters=args.mcts_iters),
        "hmcts": lambda: run_hmcts(config, jsonl_path, iters=args.mcts_iters),
        "simsearch": lambda: run_simsearch(config, jsonl_path),
    }

    for name, fn in algos.items():
        if name not in skip:
            t = Thread(target=fn, name=name, daemon=True)
            threads.append(t)

    print(f"Running {len(threads)} algorithms: {', '.join(t.name for t in threads)}\n")

    t0 = time.time()
    for t in threads:
        t.start()
    for t in threads:
        t.join()
    dt = time.time() - t0
    print(f"\nAll algorithms finished in {dt:.0f}s")

    # Read all candidates from JSONL
    candidates = []
    seen = set()
    with open(jsonl_path) as f:
        for line in f:
            line = line.strip()
            if line:
                entry = json.loads(line)
                key = tuple(entry["sequence"])
                if key not in seen:
                    seen.add(key)
                    candidates.append(entry)

    print(f"Total unique candidates: {len(candidates)}")

    # Validate
    print(f"Validating @ {args.validate_sims} sims...\n")
    results = validate(config, candidates, n_sims=args.validate_sims)

    print(f"{'Rank':<5} {'Mean':>8} {'Median':>8} {'P10':>8} {'P90':>8}  {'Buys':>4}  {'Algo':<15} Summary")
    print("-" * 100)
    for i, r in enumerate(results[:20]):
        print(f"{i+1:<5} {r['mean']:>8.1f} {r['median']:>8.1f} {r['p10']:>8.1f} {r['p90']:>8.1f}  {r['buys']:>4}  {r['algo']:<15} {r['summary']}")

    # Save
    out_path = args.output or str(base / "profiles" / args.profile / args.course / "results.json")
    output = {
        "profile": args.profile,
        "config": {
            "success_rate": config.success_rate,
            "avg_success_time": config.avg_success_time,
            "avg_failure_time": config.avg_failure_time,
            "clone_course_duration": config.clone_course_duration,
        },
        "search_time_seconds": round(dt, 1),
        "total_candidates": len(candidates),
        "top_results": results[:10],
    }
    with open(out_path, "w") as f:
        json.dump(output, f, indent=2)
    print(f"\nResults saved to {out_path}")
    print(f"Best: {results[0]['mean']:.1f}s — [{results[0]['algo']}] {results[0]['summary']}")


if __name__ == "__main__":
    main()
