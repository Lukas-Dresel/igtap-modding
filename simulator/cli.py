#!/usr/bin/env python3
"""CLI entry point: run simulations, compare policies."""
import sys
import statistics

from config import load_config
from simulator import Simulator
from state import GameState
from policy import (
    SaveForWallJump, CheapestFirst, ClonesFirst, CashFirst, GreedyROI, RandomPolicy,
    PreTomjon6, Tomjon6, Lukas, MCTSDistilledV1, MCTSDistilledV2, MCTSDistilledV3, MCTSDistilledV4, MCTSDistilledV5, MCTSDistilledV6, MCTSDistilledV7, MCTSDistilledV8,
    Z3OptimalV1,
    FixedSequence, CashThenClones,
)


ALL_POLICIES = {
    "cheapest": lambda: CheapestFirst(),
    "clones5": lambda: ClonesFirst(clone_target=5),
    "clones10": lambda: ClonesFirst(clone_target=10),
    "cash5": lambda: CashFirst(cash_target=5),
    "cash10": lambda: CashFirst(cash_target=10),
    "greedy": lambda: GreedyROI(),
    "pretomjon6": lambda: PreTomjon6(),
    "tomjon6": lambda: Tomjon6(),
    "lukas1": lambda: Lukas(extra_clones=1),
    "lukas2": lambda: Lukas(extra_clones=2),
    "lukas3": lambda: Lukas(extra_clones=3),
    "lukas4": lambda: Lukas(extra_clones=4),
    "lukas5": lambda: Lukas(extra_clones=5),
    "lukas6": lambda: Lukas(extra_clones=6),
    "lukas7": lambda: Lukas(extra_clones=7),
    "v1": lambda: MCTSDistilledV1(),
    "v2": lambda: MCTSDistilledV2(),
    "v3": lambda: MCTSDistilledV3(),
    "v4": lambda: MCTSDistilledV4(),
    "v5": lambda: MCTSDistilledV5(),
    "v6": lambda: MCTSDistilledV6(),
    "v7": lambda: MCTSDistilledV7(),
    "v8": lambda: MCTSDistilledV8(),
    "z3v1": lambda: Z3OptimalV1(),
    "ct1_5": lambda: CashThenClones(cash_first=1, clone_target=5),
    "ct3_5": lambda: CashThenClones(cash_first=3, clone_target=5),
}

# Top 5 from previous runs
TOP5 = ["z3v1", "v8", "v7", "v6", "v5"]


def compare_policies(n_sims: int = 1000, seed: int = 42, mcts_iters: int = 0, only: str = None):
    config = load_config(
        success_times_path="data/success_times.csv",
        failure_times_path="data/failure_times.csv",
        clone_course_duration=2.10,
    )
    sim = Simulator(config, seed=seed)

    if only == "all":
        keys = list(ALL_POLICIES.keys())
    elif only:
        # Specific keys + top5
        requested = [k.strip() for k in only.split(",")]
        keys = list(dict.fromkeys(requested + TOP5))  # dedupe, preserve order
    else:
        keys = list(ALL_POLICIES.keys())

    policies = [ALL_POLICIES[k]() for k in keys if k in ALL_POLICIES]

    print(f"Running {n_sims} simulations per policy...")
    print(f"Success rate: {config.success_rate:.1%}")
    print(f"Avg success time: {config.avg_success_time:.1f}s")
    print(f"Avg failure time: {config.avg_failure_time:.1f}s")
    print(f"Clone course duration: {config.clone_course_duration:.1f}s")
    from fsm import TRANSITIONS, State
    print(f"Transitions: exit→entrance={TRANSITIONS[(State.AT_EXIT, State.AT_ENTRANCE)]}s  exit→box={TRANSITIONS[(State.AT_EXIT, State.AT_BOX)]}s  box→box={TRANSITIONS[(State.AT_BOX, State.AT_BOX)]}s  box→entrance={TRANSITIONS[(State.AT_BOX, State.AT_ENTRANCE)]}s")
    print()

    results = []
    for policy in policies:
        n = n_sims
        times = sim.run_batch(policy, n=n)
        times.sort()
        mean = statistics.mean(times)
        median = statistics.median(times)
        p10 = times[max(0, int(len(times) * 0.1))]
        p90 = times[min(len(times) - 1, int(len(times) * 0.9))]
        results.append((policy.name, mean, median, p10, p90))

    # Sort by mean time
    results.sort(key=lambda r: r[1])

    print(f"{'Policy':<30} {'Mean':>10} {'Median':>10} {'P10':>10} {'P90':>10}")
    print("-" * 72)
    for name, mean, median, p10, p90 in results:
        print(f"{name:<30} {mean:>10.1f} {median:>10.1f} {p10:>10.1f} {p90:>10.1f}")

    # Show best policy's trace
    print(f"\n--- Best policy: {results[0][0]} ---")
    best_policy = next(p for p in policies if p.name == results[0][0])
    trace_run(config, best_policy, seed=seed)

    # Distill MCTS into a deterministic policy
    if mcts_iters > 0:
        print(f"\n--- Sequence MCTS ({mcts_iters} iterations) ---")
        from mcts import SequenceMCTS
        smcts = SequenceMCTS(config, eval_sims=30, seed=seed)
        best_seq = smcts.search(iterations=mcts_iters)
        print(f"  Sequence: {' -> '.join(best_seq)}")

        policy = FixedSequence(best_seq)
        times = Simulator(config, seed=seed).run_batch(policy, n=min(n_sims, 1000))
        times.sort()
        mean = statistics.mean(times)
        median = statistics.median(times)
        p10 = times[int(len(times) * 0.1)]
        p90 = times[min(len(times) - 1, int(len(times) * 0.9))]
        print(f"  Mean={mean:.1f}  Median={median:.1f}  P10={p10:.1f}  P90={p90:.1f}")


def trace_run(config, policy, seed=42):
    """Run a single simulation and print the event trace."""
    from simulator import Simulator
    sim = Simulator(config, seed=seed)
    state = sim.run(policy)
    print(f"Total time: {state.time:.1f}s")
    print(f"Final cash: {state.cash:.0f}")
    print(f"Upgrades: {state.upgrades}")


def distill_mcts(config, iterations=500, n_runs=10, seed=42):
    """Run MCTS multiple times, record every buy action, find the consensus sequence."""
    from mcts import MCTS
    from simulator import Simulator
    from collections import Counter

    all_sequences = []

    for run in range(n_runs):
        mcts = MCTS(config, seed=seed + run)
        sim = Simulator(config, seed=seed + run)
        state = GameState(config=config)
        clone_start_time = None
        sequence = []

        while not state.has_wall_jump and state.time < 50000:
            # Run course
            run_time, success = sim.sample_player_run()
            if state.clone_count > 0 and clone_start_time is not None:
                from simulator import _clone_income_between
                state.cash += _clone_income_between(state, config, clone_start_time,
                                                     state.time, state.time + run_time)
            state.time += run_time
            if success:
                state.cash += state.reward_per_completion

            # MCTS decides
            action = mcts.best_action(state, iterations=iterations)

            if action.type == "buy":
                t_before = state.time
                state.time += config.buy_time
                if state.clone_count > 0 and clone_start_time is not None:
                    state.cash += _clone_income_between(state, config, clone_start_time,
                                                         t_before, state.time)
                old_clones = state.clone_count
                state.buy_upgrade(action.upgrade_name)
                sequence.append(action.upgrade_name)

                if clone_start_time is None and state.clone_count > 0:
                    clone_start_time = state.time

                if state.has_wall_jump:
                    break

        all_sequences.append(sequence)
        print(f"  Run {run+1}: {' -> '.join(sequence)} ({state.time:.1f}s)")

    # Find consensus: at each position, what's the most common action?
    max_len = max(len(s) for s in all_sequences)
    consensus = []
    print(f"\n  Consensus (position-by-position majority vote):")
    for i in range(max_len):
        counts = Counter()
        for s in all_sequences:
            if i < len(s):
                counts[s[i]] += 1
        most_common = counts.most_common(1)[0]
        consensus.append(most_common[0])
        print(f"    Step {i+1}: {most_common[0]} ({most_common[1]}/{n_runs})")

    # Create and test the distilled policy
    from policy import FixedSequence
    distilled = FixedSequence(consensus)
    sim2 = Simulator(config, seed=seed)
    times = sim2.run_batch(distilled, n=500)
    times.sort()
    mean = statistics.mean(times)
    median = statistics.median(times)
    p10 = times[int(len(times) * 0.1)]
    p90 = times[int(len(times) * 0.9)]
    print(f"\n  Distilled policy: {' -> '.join(consensus)}")
    print(f"  Mean={mean:.1f}s  Median={median:.1f}s  P10={p10:.1f}s  P90={p90:.1f}s")


if __name__ == "__main__":
    n = int(sys.argv[1]) if len(sys.argv) > 1 else 500
    mcts = int(sys.argv[2]) if len(sys.argv) > 2 else 0
    only = sys.argv[3] if len(sys.argv) > 3 else None
    compare_policies(n_sims=n, mcts_iters=mcts, only=only)
