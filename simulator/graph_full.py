"""Full expanded graph. Cash × 3 to make 2/3 success rate integer.

State: (n_cash, n_clone, cash_x3, at_box, prev_type)
Each edge = one player run cycle.
Player earns 2*reward per cycle (in x3 space: reward*2/3*3 = 2*reward).
Clone income: 3 * floor(cycle_time * n_clone / clone_dur) * clone_reward.
All costs multiplied by 3.
"""
import math
import heapq
from itertools import groupby

from config import SimConfig, load_config
from fsm import State, transition_time

SCALE = 3


def solve(config: SimConfig, verbose: bool = True) -> list[str]:
    cash_cap = config.upgrades["cashPerLoop"].cap
    clone_cap = config.upgrades["cloneCount"].cap
    wj_cost = int(config.upgrades["wallJump"].cost_at(0)) * SCALE

    avg_run = config.success_rate * config.avg_success_time + (1 - config.success_rate) * config.avg_failure_time
    transition_back = transition_time(State.AT_EXIT, State.AT_ENTRANCE)
    cycle_time = avg_run + transition_back

    max_cash = wj_cost + 100 * SCALE

    INF = float("inf")
    dist = {}
    prev = {}
    action = {}

    start = (0, 0, 0, False, 0)
    dist[start] = 0.0
    pq = [(0.0, start)]

    best_wj_time = INF
    best_wj_state = None
    explored = 0

    while pq:
        d, state = heapq.heappop(pq)
        if d > dist.get(state, INF):
            continue
        if d > best_wj_time:
            continue

        explored += 1
        if explored % 500000 == 0 and verbose:
            print(f"  explored {explored}, queue={len(pq)}, best wj={best_wj_time:.1f}s")

        n_cash, n_clone, cash, at_box, prev_type = state

        reward = math.ceil(config.base_reward * (n_cash + 1))
        clone_reward = math.ceil(reward * config.clone_base_multiplier)

        # === Buy wallJump ===
        if cash >= wj_cost:
            if at_box:
                trip = transition_time(State.AT_BOX, State.AT_BOX)
            else:
                trip = transition_time(State.AT_EXIT, State.AT_BOX)
            wj_time = d + trip
            if wj_time < best_wj_time:
                best_wj_time = wj_time
                best_wj_state = state
                if verbose:
                    print(f"  wj best: {wj_time:.1f}s c={n_cash} cl={n_clone}")
            continue

        # === Buy upgrades ===
        def try_buy(name, n_c, n_cl, type_id):
            uc = config.upgrades[name]
            count = n_c if name == "cashPerLoop" else n_cl
            if count >= uc.cap:
                return
            cost = int(uc.cost_at(count)) * SCALE
            if cash < cost:
                return

            if at_box and prev_type == type_id:
                trip = transition_time(State.AT_BOX, State.AT_BOX)
            elif at_box:
                trip = (transition_time(State.AT_BOX, State.AT_ENTRANCE) +
                        transition_time(State.AT_EXIT, State.AT_BOX))
            else:
                trip = transition_time(State.AT_EXIT, State.AT_BOX)

            new_cash = cash - cost
            # Clone income during trip
            if n_clone > 0:
                interval = config.clone_course_duration / n_clone
                cc = int(trip / interval)
                new_cash += cc * clone_reward * SCALE
            new_cash = min(new_cash, max_cash)

            if name == "cashPerLoop":
                ns = (n_c + 1, n_cl, new_cash, True, type_id)
            else:
                ns = (n_c, n_cl + 1, new_cash, True, type_id)

            new_d = d + trip
            if new_d < dist.get(ns, INF):
                dist[ns] = new_d
                prev[ns] = state
                action[ns] = name
                heapq.heappush(pq, (new_d, ns))

        try_buy("cashPerLoop", n_cash, n_clone, 1)
        try_buy("cloneCount", n_cash, n_clone, 2)

        # === Run one cycle ===
        if at_box:
            pre = transition_time(State.AT_BOX, State.AT_ENTRANCE)
        else:
            pre = transition_time(State.AT_EXIT, State.AT_ENTRANCE)

        step_time = pre + cycle_time

        # Player: 2/3 success → in x3 space, earn 2*reward per cycle
        player_earn = 2 * reward

        # Clones: discrete completions during this step
        clone_earn = 0
        if n_clone > 0:
            interval = config.clone_course_duration / n_clone
            cc = int(step_time / interval)
            clone_earn = cc * clone_reward * SCALE

        new_cash = min(cash + player_earn + clone_earn, max_cash)
        ns = (n_cash, n_clone, new_cash, False, 0)
        new_d = d + step_time
        if new_d < dist.get(ns, INF):
            dist[ns] = new_d
            prev[ns] = state
            action[ns] = "run"
            heapq.heappush(pq, (new_d, ns))

        # === Leave box ===
        if at_box:
            lt = transition_time(State.AT_BOX, State.AT_ENTRANCE)
            ce = 0
            if n_clone > 0:
                interval = config.clone_course_duration / n_clone
                ce = int(lt / interval) * clone_reward * SCALE
            ns = (n_cash, n_clone, min(cash + ce, max_cash), False, 0)
            new_d = d + lt
            if new_d < dist.get(ns, INF):
                dist[ns] = new_d
                prev[ns] = state
                action[ns] = "leave_box"
                heapq.heappush(pq, (new_d, ns))

    if verbose:
        print(f"\nExplored {explored} states")

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
        print(f"Optimal time (full graph): {best_wj_time:.1f}s")
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
