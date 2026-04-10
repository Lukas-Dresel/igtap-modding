"""Shortest-path upgrade optimizer with discrete earning and batching.

State: (n_cash, n_clone, leftover_cash, at_box, prev_type)
  - Tracks leftover cash between purchases
  - Tracks whether we're at the box (can batch same-type for 0.75s)
  - prev_type: what we last bought (for batch detection)

Dijkstra finds the optimal path.
"""
import math
import heapq
from itertools import groupby

from config import SimConfig, load_config
from fsm import State, transition_time


def time_to_earn(config: SimConfig, n_cash: int, n_clone: int, amount: float,
                  clone_producing: bool = True) -> tuple[float, float]:
    """Simulate discrete earning to accumulate `amount` cash.
    Returns (time_taken, leftover_cash)."""
    if amount <= 0:
        return 0.0, -amount

    reward = math.ceil(config.base_reward * (n_cash + 1))
    clone_reward = math.ceil(reward * config.clone_base_multiplier)
    sr = config.success_rate
    avg_run = sr * config.avg_success_time + (1 - sr) * config.avg_failure_time
    transition_back = transition_time(State.AT_EXIT, State.AT_ENTRANCE)
    cycle_time = avg_run + transition_back

    player_per_cycle = reward * sr

    clone_per_cycle = 0.0
    if n_clone > 0 and clone_producing:
        interval = config.clone_course_duration / n_clone
        clone_per_cycle = (cycle_time / interval) * clone_reward

    income_per_cycle = player_per_cycle + clone_per_cycle
    if income_per_cycle <= 0:
        return float("inf"), 0.0

    cycles = math.ceil(amount / income_per_cycle)
    total_earned = cycles * income_per_cycle
    leftover = total_earned - amount
    return cycles * cycle_time, leftover


def solve(config: SimConfig, verbose: bool = True) -> list[str]:
    """Dijkstra with batching support.

    State: (n_cash, n_clone, leftover_int, at_box, prev_type)
      prev_type: 0=none, 1=cash, 2=clone
    """
    cash_cap = config.upgrades["cashPerLoop"].cap
    clone_cap = config.upgrades["cloneCount"].cap
    wj_cost = config.upgrades["wallJump"].cost_at(0)
    max_leftover = int(wj_cost) + 1

    INF = float("inf")
    dist = {}
    prev = {}
    action = {}

    start = (0, 0, 0, False, 0)
    dist[start] = 0.0
    pq = [(0.0, start)]

    best_wj_time = INF
    best_wj_state = None

    while pq:
        d, state = heapq.heappop(pq)
        if d > dist.get(state, INF):
            continue

        n_cash, n_clone, leftover, at_box, prev_type = state
        clone_producing = n_clone > 0

        # === Buy wallJump ===
        need = max(0, wj_cost - leftover)
        if at_box:
            # Earn first (leave box, run, come back)
            earn_t, _ = time_to_earn(config, n_cash, n_clone, need, clone_producing)
            trip = transition_time(State.AT_BOX, State.AT_ENTRANCE) + earn_t + transition_time(State.AT_EXIT, State.AT_BOX)
        else:
            earn_t, _ = time_to_earn(config, n_cash, n_clone, need, clone_producing)
            trip = earn_t + transition_time(State.AT_EXIT, State.AT_BOX)
        wj_time = d + trip
        if wj_time < best_wj_time:
            best_wj_time = wj_time
            best_wj_state = state

        def try_buy(upgrade_name, n_c, n_cl, type_id):
            uc = config.upgrades[upgrade_name]
            count = n_c if upgrade_name == "cashPerLoop" else n_cl
            if count >= uc.cap:
                return
            cost = uc.cost_at(count)
            need = max(0, cost - leftover)

            if at_box and prev_type == type_id:
                # Batch: same type, stay at box. 0.75s + earn time
                # If we have leftover >= cost, no earning needed
                earn_t, earn_left = time_to_earn(config, n_cash, n_clone, need, clone_producing)
                trip_t = transition_time(State.AT_BOX, State.AT_BOX)
                new_d = d + earn_t + trip_t
                new_left = int(min(earn_left, max_leftover))
            elif at_box:
                # Different type at box: leave box, earn, come back
                earn_t, earn_left = time_to_earn(config, n_cash, n_clone, need, clone_producing)
                trip_t = (transition_time(State.AT_BOX, State.AT_ENTRANCE) +
                          earn_t +
                          transition_time(State.AT_EXIT, State.AT_BOX))
                new_d = d + trip_t
                new_left = int(min(earn_left, max_leftover))
            else:
                # Not at box: earn then buy trip
                earn_t, earn_left = time_to_earn(config, n_cash, n_clone, need, clone_producing)
                trip_t = earn_t + transition_time(State.AT_EXIT, State.AT_BOX)
                new_d = d + trip_t
                new_left = int(min(earn_left, max_leftover))

            if upgrade_name == "cashPerLoop":
                new_state = (n_c + 1, n_cl, new_left, True, type_id)
            else:
                new_state = (n_c, n_cl + 1, new_left, True, type_id)

            if new_d < dist.get(new_state, INF):
                dist[new_state] = new_d
                prev[new_state] = state
                action[new_state] = upgrade_name
                heapq.heappush(pq, (new_d, new_state))

        try_buy("cashPerLoop", n_cash, n_clone, 1)
        try_buy("cloneCount", n_cash, n_clone, 2)

        # === Leave box (go run without buying) ===
        if at_box:
            leave_t = transition_time(State.AT_BOX, State.AT_ENTRANCE)
            new_state = (n_cash, n_clone, leftover, False, 0)
            new_d = d + leave_t
            if new_d < dist.get(new_state, INF):
                dist[new_state] = new_d
                prev[new_state] = state
                action[new_state] = "leave_box"
                heapq.heappush(pq, (new_d, new_state))

    # Reconstruct path
    path = []
    state = best_wj_state
    while state in prev:
        act = action[state]
        if act in ("cashPerLoop", "cloneCount"):
            path.append(act)
        state = prev[state]
    path.reverse()
    path.append("wallJump")

    if verbose:
        print(f"Optimal time (graph): {best_wj_time:.1f}s")
        parts = []
        for k, g in groupby(path):
            n = len(list(g))
            parts.append(f"{n}x{k}" if n > 1 else k)
        print(f"Summary: {', '.join(parts)}")
        print(f"Totals: {path.count('cashPerLoop')} cash + {path.count('cloneCount')} clone + wallJump")

    return path


if __name__ == "__main__":
    import argparse as _ap
    _p = _ap.ArgumentParser()
    _p.add_argument("--profile", "-p", default="mysko")
    _args, _ = _p.parse_known_args()
    config = load_config(profile=_args.profile)
    path = solve(config)

    from simulator import Simulator
    from policy import FixedSequence
    import statistics

    sim = Simulator(config, seed=42)
    times = sim.run_batch(FixedSequence(path), n=10000)
    times.sort()
    print(f"\nValidated (10000 sims): Mean={statistics.mean(times):.1f}  Median={statistics.median(times):.1f}  P10={times[int(len(times)*0.1)]:.1f}  P90={times[int(len(times)*0.9)]:.1f}")
