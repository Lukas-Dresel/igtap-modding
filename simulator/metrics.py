"""Comprehensive behavior metrics for v1 simulator (buy-only sequences).

Three key tensions:
1. Early clone income vs late cash multiplier
2. Low transition overhead vs low buy drought
3. High clone utilization vs fast income doubling
"""
import math
import random

from config import SimConfig
from state import GameState
from fsm import State, transition_time
from simulator import Simulator, _clone_income_between


def simulate_with_metrics(config: SimConfig, sequence: list[str], rng_seed: int) -> dict[str, float]:
    """Run v1 FixedSequence simulation tracking all metrics.
    wallJump is appended automatically if not present."""
    from policy import FixedSequence

    if not sequence or sequence[-1] != "wallJump":
        sequence = list(sequence) + ["wallJump"]

    sim = Simulator(config, seed=rng_seed)
    state = GameState(config=config)
    clone_start_time = None
    fsm_state = State.AT_ENTRANCE

    INF = 100000.0
    total_earned = 0.0
    total_clone_earned = 0.0
    max_cash_on_hand = 0.0
    buy_count = 0
    milestones = {}

    total_transition_time = 0.0
    last_buy_time = 0.0
    buy_gaps = []
    runs_while_affordable = 0

    income_snapshots = []
    first_income_rate = None
    income_doubled_time = None

    first_clone_income_time = None
    clone_income_at_50 = 0.0
    clone_income_at_100 = 0.0
    total_income_at_50 = 0.0
    total_income_at_100 = 0.0

    seq_idx = 0

    def record(name):
        if name not in milestones:
            milestones[name] = state.time

    def advance(duration):
        nonlocal clone_start_time, total_clone_earned, first_clone_income_time
        if duration <= 0:
            state.time += duration
            return
        if state.clone_count > 0 and clone_start_time is not None:
            ci = _clone_income_between(state, config, clone_start_time,
                                        state.time, state.time + duration)
            state.cash += ci
            total_clone_earned += ci
            if ci > 0 and first_clone_income_time is None:
                first_clone_income_time = state.time
        state.time += duration

    def get_target():
        if seq_idx < len(sequence):
            return sequence[seq_idx]
        return None

    while state.time < INF and not state.has_wall_jump:
        if fsm_state == State.AT_ENTRANCE:
            advance(transition_time(State.AT_ENTRANCE, State.RUNNING))

            # Run course
            run_time, success = sim.sample_player_run()
            prev_clone = total_clone_earned
            advance(run_time)
            if success:
                state.cash += state.reward_per_completion
                total_earned += state.reward_per_completion
            total_earned += (total_clone_earned - prev_clone)

            max_cash_on_hand = max(max_cash_on_hand, state.cash)

            # Check runs while affordable
            target = get_target()
            if target and state.can_afford(target):
                pass  # will buy next
            elif state.affordable_upgrades():
                runs_while_affordable += 1

            # Income snapshots
            if total_earned > 0:
                income_snapshots.append((state.time, total_earned))
                if first_income_rate is None and state.time > 5:
                    first_income_rate = total_earned / state.time
                if first_income_rate and income_doubled_time is None:
                    current_rate = total_earned / state.time
                    if current_rate >= 2 * first_income_rate:
                        income_doubled_time = state.time

            # Clone/total at timestamps
            if state.time >= 50 and total_income_at_50 == 0:
                total_income_at_50 = total_earned
                clone_income_at_50 = total_clone_earned
            if state.time >= 100 and total_income_at_100 == 0:
                total_income_at_100 = total_earned
                clone_income_at_100 = total_clone_earned

            fsm_state = State.AT_EXIT

        elif fsm_state == State.AT_EXIT:
            target = get_target()
            if target and state.can_afford(target):
                total_transition_time += transition_time(State.AT_EXIT, State.AT_BOX)
                advance(transition_time(State.AT_EXIT, State.AT_BOX))
                state.buy_upgrade(target)
                seq_idx += 1
                buy_count += 1
                buy_gaps.append(state.time - last_buy_time)
                last_buy_time = state.time
                if clone_start_time is None and state.clone_count > 0:
                    clone_start_time = state.time

                # Record milestones
                for n in range(1, 10):
                    if state.clone_count >= n:
                        record(f"clone_{n}")
                for mult in [2, 4, 6, 8, 10]:
                    if state.cash_per_loop + 1 >= mult:
                        record(f"mult_{mult}x")
                if total_earned >= 100:
                    record("cash_100")
                if total_earned >= 500:
                    record("cash_500")

                fsm_state = State.AT_BOX
            else:
                total_transition_time += transition_time(State.AT_EXIT, State.AT_ENTRANCE)
                advance(transition_time(State.AT_EXIT, State.AT_ENTRANCE))
                fsm_state = State.AT_ENTRANCE

        elif fsm_state == State.AT_BOX:
            if state.has_wall_jump:
                break
            target = get_target()
            if target and state.can_afford(target):
                total_transition_time += transition_time(State.AT_BOX, State.AT_BOX)
                advance(transition_time(State.AT_BOX, State.AT_BOX))
                state.buy_upgrade(target)
                seq_idx += 1
                buy_count += 1
                buy_gaps.append(state.time - last_buy_time)
                last_buy_time = state.time
                if clone_start_time is None and state.clone_count > 0:
                    clone_start_time = state.time

                for n in range(1, 10):
                    if state.clone_count >= n:
                        record(f"clone_{n}")
                for mult in [2, 4, 6, 8, 10]:
                    if state.cash_per_loop + 1 >= mult:
                        record(f"mult_{mult}x")
                if total_earned >= 100:
                    record("cash_100")
                if total_earned >= 500:
                    record("cash_500")

                fsm_state = State.AT_BOX
            else:
                total_transition_time += transition_time(State.AT_BOX, State.AT_ENTRANCE)
                advance(transition_time(State.AT_BOX, State.AT_ENTRANCE))
                fsm_state = State.AT_ENTRANCE

    finish_time = state.time if state.has_wall_jump else INF

    # === Build features (lower = better for all) ===
    features = {
        "time_to_walljump": finish_time,
        "buy_count": float(buy_count),
        "neg_max_cash_on_hand": -max_cash_on_hand,
    }

    for n in range(1, 10):
        features[f"time_to_clone_{n}"] = milestones.get(f"clone_{n}", INF)
    for mult in [2, 4, 6, 8, 10]:
        features[f"time_to_mult_{mult}x"] = milestones.get(f"mult_{mult}x", INF)
    features["time_to_cash_100"] = milestones.get("cash_100", INF)
    features["time_to_cash_500"] = milestones.get("cash_500", INF)

    # Tension 1
    features["first_clone_income_time"] = first_clone_income_time or INF
    features["neg_clone_share_at_50"] = -(clone_income_at_50 / total_income_at_50) if total_income_at_50 > 0 else 0
    features["neg_clone_share_at_100"] = -(clone_income_at_100 / total_income_at_100) if total_income_at_100 > 0 else 0
    features["neg_clone_utilization"] = -(total_clone_earned / total_earned) if total_earned > 0 else 0

    # Tension 2
    features["transition_overhead"] = total_transition_time
    features["longest_buy_gap"] = max(buy_gaps) if buy_gaps else INF
    features["avg_buy_gap"] = (sum(buy_gaps) / len(buy_gaps)) if buy_gaps else INF
    features["buy_gap_variance"] = (sum((g - features["avg_buy_gap"])**2 for g in buy_gaps) / len(buy_gaps)) if len(buy_gaps) > 1 else 0
    features["runs_while_affordable"] = float(runs_while_affordable)

    # Tension 3
    features["income_doubling_time"] = income_doubled_time or INF
    for pct in [25, 50, 75]:
        t_target = finish_time * pct / 100
        earned_at = 0
        for t, e in income_snapshots:
            if t <= t_target:
                earned_at = e
        features[f"neg_cps_at_{pct}pct"] = -(earned_at / t_target) if t_target > 0 else 0

    features["cash_waste_at_wj"] = state.cash if state.has_wall_jump else INF

    return features
