"""Comprehensive behavior metrics for lexicase/novelty selection.

Three key tensions:
1. Early clone income vs late cash multiplier
2. Low transition overhead vs low buy drought
3. High clone utilization vs fast income doubling

Plus standard milestones.
"""
import math
import random

import sys, os
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from config import SimConfig
from fsm import State
from sim import SimState, step, clone_income


def simulate_with_metrics(config: SimConfig, genome: list[str], rng: random.Random) -> dict[str, float]:
    full = genome + ["wallJump"]
    state = SimState(config)

    INF = 100000.0

    # Tracking vars
    total_earned = 0.0
    total_clone_earned = 0.0
    max_cash_on_hand = 0.0
    buy_count = 0
    milestones = {}

    # Tension 2: transition overhead vs buy drought
    total_transition_time = 0.0
    last_buy_time = 0.0
    buy_gaps = []
    runs_while_affordable = 0

    # Tension 3: income rate tracking
    income_snapshots = []  # (time, total_earned) for computing rates
    first_income_rate = None
    income_doubled_time = None

    # Tension 1: clone vs player income
    first_clone_income_time = None
    clone_income_at_50 = 0.0
    clone_income_at_100 = 0.0
    total_income_at_50 = 0.0
    total_income_at_100 = 0.0

    prev_time = 0.0
    prev_cash = 0.0
    prev_fsm = State.AT_ENTRANCE

    def record(name):
        if name not in milestones:
            milestones[name] = state.game.time

    for action in full:
        if state.done:
            break

        prev_time = state.game.time
        prev_cash = state.game.cash
        prev_clone_earned = total_clone_earned
        prev_fsm = state.fsm
        prev_loc = state.location

        step(state, action, rng)

        dt = state.game.time - prev_time
        cash_delta = state.game.cash - prev_cash

        # Track clone income separately
        if action == "run" and state.game.clone_count > 0 and state.clone_start is not None:
            # Clone income during this step
            ci = clone_income(state.game, config, state.clone_start, prev_time, state.game.time)
            total_clone_earned += ci
            if ci > 0 and first_clone_income_time is None:
                first_clone_income_time = state.game.time

        if action == "run":
            player_earned = max(0, cash_delta - (total_clone_earned - prev_clone_earned))
            total_earned += max(0, cash_delta)
        else:
            # Buy action — cash decreased
            buy_count += 1
            buy_gaps.append(state.game.time - last_buy_time)
            last_buy_time = state.game.time

        # Transition overhead: time spent moving (not running the course)
        # Use the location delta to compute travel cost
        if action == "run":
            if prev_loc != "entrance":
                total_transition_time += config.travel_time(prev_loc, "entrance")
        else:
            # Buy: travel from previous location to the named box
            total_transition_time += config.travel_time(prev_loc, action)

        # Runs while affordable
        if action == "run" and state.game.affordable_upgrades():
            runs_while_affordable += 1

        max_cash_on_hand = max(max_cash_on_hand, state.game.cash)

        # Income snapshots for doubling time
        if total_earned > 0:
            income_snapshots.append((state.game.time, total_earned))
            if first_income_rate is None and state.game.time > 5:
                first_income_rate = total_earned / state.game.time
            if first_income_rate and income_doubled_time is None:
                current_rate = total_earned / state.game.time if state.game.time > 0 else 0
                if current_rate >= 2 * first_income_rate:
                    income_doubled_time = state.game.time

        # Clone/total income at timestamps
        if state.game.time >= 50 and total_income_at_50 == 0:
            total_income_at_50 = total_earned
            clone_income_at_50 = total_clone_earned
        if state.game.time >= 100 and total_income_at_100 == 0:
            total_income_at_100 = total_earned
            clone_income_at_100 = total_clone_earned

        # Standard milestones
        for n in range(1, 10):
            if state.game.clone_count >= n:
                record(f"clone_{n}")
        for mult in [2, 4, 6, 8, 10]:
            if state.game.cash_per_loop + 1 >= mult:
                record(f"mult_{mult}x")
        if total_earned >= 100:
            record("cash_100")
        if total_earned >= 500:
            record("cash_500")

    # Finish: run until wallJump
    while not state.done and state.game.time < INF:
        prev_time = state.game.time
        step(state, "run", rng)
        total_earned += max(0, state.game.cash - prev_cash)
        prev_cash = state.game.cash
        if state.game.can_afford("wallJump"):
            step(state, "wallJump", rng)

    finish_time = state.game.time if state.done else INF

    # === Build feature dict (lower = better for all, negate "higher is better") ===

    features = {
        # Main objective
        "time_to_terminal": finish_time,

        # Standard milestones
        "buy_count": float(buy_count),
        "neg_max_cash_on_hand": -max_cash_on_hand,
    }

    for n in range(1, 10):
        features[f"time_to_clone_{n}"] = milestones.get(f"clone_{n}", INF)
    for mult in [2, 4, 6, 8, 10]:
        features[f"time_to_mult_{mult}x"] = milestones.get(f"mult_{mult}x", INF)
    features["time_to_cash_100"] = milestones.get("cash_100", INF)
    features["time_to_cash_500"] = milestones.get("cash_500", INF)

    # --- Tension 1: Early clone income vs late cash multiplier ---
    features["first_clone_income_time"] = first_clone_income_time or INF
    features["neg_clone_share_at_50"] = -(clone_income_at_50 / total_income_at_50) if total_income_at_50 > 0 else 0
    features["neg_clone_share_at_100"] = -(clone_income_at_100 / total_income_at_100) if total_income_at_100 > 0 else 0
    features["neg_clone_utilization"] = -(total_clone_earned / total_earned) if total_earned > 0 else 0

    # --- Tension 2: Low transition overhead vs low buy drought ---
    features["transition_overhead"] = total_transition_time
    features["longest_buy_gap"] = max(buy_gaps) if buy_gaps else INF
    features["avg_buy_gap"] = (sum(buy_gaps) / len(buy_gaps)) if buy_gaps else INF
    features["buy_gap_variance"] = (sum((g - features["avg_buy_gap"])**2 for g in buy_gaps) / len(buy_gaps)) if len(buy_gaps) > 1 else 0
    features["runs_while_affordable"] = float(runs_while_affordable)

    # --- Tension 3: High clone utilization vs fast income doubling ---
    features["income_doubling_time"] = income_doubled_time or INF
    # Cash per second at 25/50/75% of finish time
    for pct in [25, 50, 75]:
        t_target = finish_time * pct / 100
        earned_at = 0
        for t, e in income_snapshots:
            if t <= t_target:
                earned_at = e
        features[f"neg_cps_at_{pct}pct"] = -(earned_at / t_target) if t_target > 0 else 0

    # Time to afford wallJump (even if you keep investing)
    features["time_to_afford_wj"] = milestones.get("cash_500", INF)  # rough proxy

    # Cash waste at end
    features["cash_waste_at_wj"] = state.game.cash if state.done else INF

    return features
