#!/usr/bin/env python3
"""Plot cash on hand over time for all strategies."""
import math
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt

from config import load_config, SimConfig
from state import GameState
from simulator import _clone_income_between
from policy import (
    MCTSDistilled, Tomjon6, Lukas, PreTomjon6, GreedyROI,
    ClonesFirst, CheapestFirst,
)


def trace_run(config: SimConfig, policy, seed: int = 42) -> tuple[list[float], list[float]]:
    """Run one simulation, recording (time, cash_on_hand) at each event."""
    import random
    rng = random.Random(seed)

    state = GameState(config=config)
    clone_start_time = None
    times = [0.0]
    cash_history = [0.0]

    while not state.has_terminal and state.time < 50000:
        # Player runs
        if rng.random() < config.success_rate:
            run_time = rng.choice(config.success_times)
            success = True
        else:
            run_time = rng.choice(config.failure_times)
            success = False

        # Clone income during run
        if state.clone_count > 0 and clone_start_time is not None:
            ci = _clone_income_between(state, config, clone_start_time, state.time, state.time + run_time)
            state.cash += ci

        state.time += run_time

        if success:
            state.cash += state.reward_per_completion

        times.append(state.time)
        cash_history.append(state.cash)

        # Decision
        action = policy.choose_action(state)

        if action.type == "buy":
            t_before = state.time
            state.time += config.buy_time
            if state.clone_count > 0 and clone_start_time is not None:
                ci = _clone_income_between(state, config, clone_start_time, t_before, state.time)
                state.cash += ci

            state.buy_upgrade(action.upgrade_name)

            times.append(state.time)
            cash_history.append(state.cash)  # cash AFTER spending

            if clone_start_time is None and state.clone_count > 0:
                clone_start_time = state.time

            if state.has_terminal:
                break

    return times, cash_history


def main():
    import argparse as _ap
    _p = _ap.ArgumentParser()
    _p.add_argument("--profile", "-p", default="mysko")
    _p.add_argument("--course", "-c", default="course1")
    _args, _ = _p.parse_known_args()
    config = load_config(profile=_args.profile, course=_args.course)

    policies = [
        ("MCTSDistilled", MCTSDistilled()),
        ("Tomjon6", Tomjon6()),
        ("Lukas-1", Lukas(extra_clones=1)),
        ("PreTomjon6", PreTomjon6()),
        ("GreedyROI", GreedyROI()),
        ("ClonesFirst(5)", ClonesFirst(clone_target=5)),
        ("CheapestFirst", CheapestFirst()),
    ]

    fig, ax = plt.subplots(figsize=(14, 8))

    for name, policy in policies:
        times, cash = trace_run(config, policy, seed=42)
        lw = 2.5 if name == "MCTSDistilled" else 1.3
        line, = ax.plot(times, cash, label=name, linewidth=lw)
        # Mark endpoint (wallJump purchased)
        ax.plot(times[-1], cash[-1], 'o', color=line.get_color(), markersize=8 if name == "MCTSDistilled" else 5)

    ax.set_xlabel('Time (seconds)', fontsize=12)
    ax.set_ylabel('Cash On Hand', fontsize=12)
    ax.set_title('Cash On Hand Over Time by Strategy', fontsize=14)
    ax.legend(fontsize=10, loc='upper left')
    ax.grid(True, alpha=0.3)

    plt.tight_layout()
    plt.savefig('data/strategy_comparison.png', dpi=150)
    print("Saved to data/strategy_comparison.png")


if __name__ == "__main__":
    main()
