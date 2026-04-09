#!/usr/bin/env python3
"""
Z3-based optimizer: find the optimal upgrade purchase order for IGTAP Level 1.

Minimizes expected time to buy wallJump by choosing the interleaving of
cashPerLoop and cloneCount purchases.

Model:
  - Player income:  success_rate * ceil(base * (cash_level+1)) / cycle_time
  - Clone income:   clone_count * ceil(player_reward * 0.1) / clone_duration
  - Per-purchase:   grind until affordable, walk to box, buy, walk back
  - Clone income accrues during ALL travel (walk-back carry-over + walk-to-box)

Usage:
  python3 z3_optimizer.py [--bruteforce] [--nc NC --nk NK] [--validate]
"""

import math
import sys
import itertools
import time as _timer
import argparse

# ── Game parameters (from config + fsm) ─────────────────────────────
SUCCESS_RATE     = 0.66
AVG_SUCCESS_TIME = 2.2
AVG_FAILURE_TIME = 1.0
CLONE_DURATION   = 2.1
BASE_REWARD      = 1
CLONE_MULT       = 0.1
WALLJUMP_COST    = 700

EXIT_TO_ENTRANCE = 0.75
EXIT_TO_BOX      = 2.0
BOX_TO_BOX       = 0.75
BOX_TO_ENTRANCE  = 2.5
TRAVEL_TIME      = EXIT_TO_BOX + BOX_TO_ENTRANCE   # 4.5s per purchase trip

AVG_RUN_TIME   = SUCCESS_RATE * AVG_SUCCESS_TIME + (1 - SUCCESS_RATE) * AVG_FAILURE_TIME
AVG_CYCLE_TIME = AVG_RUN_TIME + EXIT_TO_ENTRANCE  # grind cycle (run + return)


# ── Cost tables ──────────────────────────────────────────────────────
def _cost_sequence(base, scale, power, add, n):
    costs = []
    cost = base
    for _ in range(n):
        costs.append(cost)
        cost += add
        cost = cost ** power
        cost = math.ceil(cost * scale)
    return costs

MAX_CASH  = 20
MAX_CLONE = 18
CASH_COSTS  = _cost_sequence(1, 1.1, 1, 0, MAX_CASH)    # cashPerLoop
CLONE_COSTS = _cost_sequence(8, 1.16, 1, 0, MAX_CLONE)   # cloneCount


# ── Income model ─────────────────────────────────────────────────────
def player_reward(c):
    return math.ceil(BASE_REWARD * (c + 1))

def clone_reward_per_completion(c):
    return math.ceil(player_reward(c) * CLONE_MULT)

def income_rates(c, k):
    """Return (player_ips, clone_ips) at cash_level=c, clone_level=k."""
    r = player_reward(c)
    p_ips = SUCCESS_RATE * r / AVG_CYCLE_TIME
    c_ips = k * clone_reward_per_completion(c) / CLONE_DURATION if k > 0 else 0.0
    return p_ips, c_ips


# ── Expected-time evaluator (ground truth, pure Python) ─────────────
def eval_sequence(seq):
    """
    Compute expected total time for a purchase sequence.
    Tracks cash exactly, including clone startup delay and walk-back carry-over.

    seq: list of 0 (cashPerLoop) or 1 (cloneCount), ending implicitly with wallJump.
    """
    c, k = 0, 0
    t = 0.0
    cash = 0.0
    clone_start_t = None   # when first clone was bought (for startup delay)

    for action in list(seq) + [2]:  # 2 = wallJump sentinel
        if action == 0:
            cost = CASH_COSTS[c]
        elif action == 1:
            cost = CLONE_COSTS[k]
        else:
            cost = WALLJUMP_COST

        p_ips, c_ips = income_rates(c, k)
        total_ips = p_ips + c_ips

        # Clone income during walk to box (EXIT_TO_BOX = 2.0s)
        travel_to_box_income = EXIT_TO_BOX * c_ips
        needed = cost - travel_to_box_income - cash

        if needed > 0 and total_ips > 0:
            grind_t = needed / total_ips
            cash += grind_t * total_ips
            t += grind_t
        elif total_ips <= 0:
            return float('inf')

        # Walk to box
        cash += travel_to_box_income
        t += EXIT_TO_BOX

        # Buy
        cash -= cost
        if action == 0:
            c += 1
        elif action == 1:
            k += 1
            if clone_start_t is None:
                clone_start_t = t

        if action == 2:
            break  # wallJump — game ends at box

        # Walk back to entrance (BOX_TO_ENTRANCE = 2.5s)
        _, c_ips_new = income_rates(c, k)

        # Handle clone startup delay: first clone needs CLONE_DURATION before producing
        if clone_start_t is not None and t < clone_start_t + CLONE_DURATION:
            travel_back_income = 0.0
            activate_at = clone_start_t + CLONE_DURATION
            if activate_at < t + BOX_TO_ENTRANCE:
                active_time = (t + BOX_TO_ENTRANCE) - activate_at
                travel_back_income = active_time * c_ips_new
        else:
            travel_back_income = BOX_TO_ENTRANCE * c_ips_new

        cash += travel_back_income
        t += BOX_TO_ENTRANCE

    return t


def sequence_to_names(seq):
    return ["cashPerLoop" if v == 0 else "cloneCount" for v in seq]


def run_length_encode(seq):
    """Compact display: '3C 8K 7C WJ'."""
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


# ── Brute-force solver ───────────────────────────────────────────────
def brute_force(nc, nk, verbose=False):
    """Exhaustive search over all C(nc+nk, nc) orderings."""
    N = nc + nk
    best_t = float('inf')
    best_seq = None
    count = 0

    for cash_positions in itertools.combinations(range(N), nc):
        seq = [1] * N
        for p in cash_positions:
            seq[p] = 0
        t = eval_sequence(seq)
        if t < best_t:
            best_t = t
            best_seq = list(seq)
        count += 1

    if verbose:
        print(f"  brute force: checked {count} orderings")
    return best_seq, best_t


# ── Z3 optimizer ─────────────────────────────────────────────────────
def solve_z3(nc, nk, verbose=False):
    """
    Find optimal ordering of nc cashPerLoop + nk cloneCount using Z3 Optimize.

    The Z3 model uses a continuous expected-value approximation:
      - At state (c, k), income = player_ips(c) + clone_ips(c, k)
      - For step i (i>0), clone income during both the previous walk-back
        and the current walk-to-box reduces grind: deduction = TRAVEL_TIME * c_ips
      - For step 0, only walk-to-box deduction (but c_ips=0 anyway)
      - All grind times precomputed per (c, k, action) → piecewise-constant ITE

    This closely matches eval_sequence except for the one-time clone startup delay.
    """
    from z3 import Optimize, Int, Real, RealVal, If, And, Sum, sat

    N = nc + nk
    if N == 0:
        return [], eval_sequence([])

    opt = Optimize()

    # Decision variables: x[i] = 0 (cashPerLoop) or 1 (cloneCount)
    x = [Int(f'x_{i}') for i in range(N)]
    for i in range(N):
        opt.add(x[i] >= 0, x[i] <= 1)

    # Exactly nc cashPerLoop purchases
    opt.add(Sum([If(x[i] == 0, 1, 0) for i in range(N)]) == nc)

    # Cumulative levels: c[i] = cash bought before step i, k[i] = clones before step i
    c = [Int(f'c_{i}') for i in range(N + 1)]
    k = [Int(f'k_{i}') for i in range(N + 1)]
    opt.add(c[0] == 0, k[0] == 0)
    for i in range(N):
        opt.add(c[i + 1] == c[i] + If(x[i] == 0, 1, 0))
        opt.add(k[i + 1] == k[i] + If(x[i] == 1, 1, 0))
        opt.add(c[i] + k[i] == i)
    opt.add(c[N] + k[N] == N)

    # Precomputed purchase time for each (step_index, c, k, action):
    #
    # For step i > 0 at state (cv, kv):
    #   effective_cost = purchase_cost - TRAVEL_TIME * c_ips(cv, kv)
    #   grind = max(0, effective_cost / total_ips(cv, kv))
    #   step_time = grind + TRAVEL_TIME
    #
    # For step 0 (no prior walk-back, and c_ips(0,0)=0):
    #   effective_cost = purchase_cost  (since c_ips = 0 at k=0)
    #   grind = effective_cost / p_ips(0, 0)
    #   step_time = grind + TRAVEL_TIME
    #
    # The TRAVEL_TIME deduction for i>0 captures:
    #   - EXIT_TO_BOX * c_ips: clone income while walking TO box for this purchase
    #   - BOX_TO_ENTRANCE * c_ips: clone income while walking BACK from previous purchase
    # These sum to TRAVEL_TIME * c_ips (valid when c_ips doesn't change between steps,
    # which is exact here since we use the BEFORE-purchase rates for both).

    def purchase_time_z3(cost, cv, kv, is_first_step=False):
        """Precomputed time for one purchase at state (cv, kv)."""
        p_ips, c_ips = income_rates(cv, kv)
        total = p_ips + c_ips
        if total <= 0:
            return 1e9
        if is_first_step:
            deduction = EXIT_TO_BOX * c_ips  # no walk-back from previous
        else:
            deduction = TRAVEL_TIME * c_ips   # walk-back + walk-to-box
        effective = cost - deduction
        grind = max(0.0, effective / total)
        return grind + TRAVEL_TIME

    obj_terms = []

    for i in range(N):
        step_expr = RealVal(0)
        for cv in range(min(i, nc) + 1):
            kv = i - cv
            if kv < 0 or kv > nk:
                continue

            is_first = (i == 0)

            # Time if buying cashPerLoop
            if cv < nc:
                t_cash = purchase_time_z3(CASH_COSTS[cv], cv, kv, is_first)
            else:
                t_cash = 1e9

            # Time if buying cloneCount
            if kv < nk:
                t_clone = purchase_time_z3(CLONE_COSTS[kv], cv, kv, is_first)
            else:
                t_clone = 1e9

            step_val = If(x[i] == 0, RealVal(t_cash), RealVal(t_clone))
            step_expr = If(And(c[i] == cv, k[i] == kv), step_val, step_expr)

        obj_terms.append(step_expr)

    # WallJump: grind at final rates, game ends at box (no walk-back)
    # Deduction includes BOX_TO_ENTRANCE from last upgrade's walk-back
    p_final, c_final = income_rates(nc, nk)
    total_final = p_final + c_final
    if total_final > 0:
        wj_deduction = TRAVEL_TIME * c_final  # walk-back from last upgrade + walk to box
        wj_effective = WALLJUMP_COST - wj_deduction
        wj_grind = max(0.0, wj_effective / total_final)
    else:
        wj_grind = 1e9
    wj_time = wj_grind + EXIT_TO_BOX  # only walk to box, game ends there

    total_obj = Sum(obj_terms) + RealVal(wj_time)
    opt.minimize(total_obj)

    if verbose:
        print(f"  Z3 model: {N} vars, solving nc={nc} nk={nk} ...")

    if opt.check() == sat:
        m = opt.model()
        seq = [m.eval(x[i]).as_long() for i in range(N)]
        time_val = float(m.eval(total_obj).as_decimal(10).rstrip('?'))
        return seq, time_val
    else:
        return None, float('inf')


# ── Simulator validation ─────────────────────────────────────────────
def simulate_policy(seq_names, n_sims=2000, seed=42):
    """Run the sequence through the actual stochastic simulator."""
    sys.path.insert(0, '.')
    from config import load_config
    from simulator import Simulator
    from policy import FixedSequence
    import statistics

    config = load_config(
        success_times_path="data/success_times.csv",
        failure_times_path="data/failure_times.csv",
        clone_course_duration=CLONE_DURATION,
    )
    full_seq = list(seq_names) + ["wallJump"]
    policy = FixedSequence(full_seq)
    sim = Simulator(config, seed=seed)
    times = sim.run_batch(policy, n=n_sims)
    times.sort()
    return {
        'mean':   statistics.mean(times),
        'median': statistics.median(times),
        'p10':    times[int(len(times) * 0.10)],
        'p90':    times[min(len(times) - 1, int(len(times) * 0.90))],
    }


# ── Main ─────────────────────────────────────────────────────────────
def main():
    parser = argparse.ArgumentParser(description="Z3 optimizer for IGTAP upgrade order")
    parser.add_argument("--nc", type=int, default=None, help="Fixed number of cashPerLoop")
    parser.add_argument("--nk", type=int, default=None, help="Fixed number of cloneCount")
    parser.add_argument("--bruteforce", action="store_true", help="Use brute force (eval_sequence)")
    parser.add_argument("--validate", action="store_true", help="Validate with stochastic simulator")
    parser.add_argument("--sims", type=int, default=2000, help="Simulations for validation")
    args = parser.parse_args()

    print("=" * 72)
    print("IGTAP Level 1 — Optimal Upgrade Order")
    print("=" * 72)
    print(f"success_rate={SUCCESS_RATE:.0%}  avg_run={AVG_RUN_TIME:.3f}s  "
          f"cycle={AVG_CYCLE_TIME:.3f}s  clone_dur={CLONE_DURATION}s")
    print(f"cashPerLoop costs: {CASH_COSTS[:12]}...")
    print(f"cloneCount  costs: {CLONE_COSTS[:12]}...")
    print(f"wallJump cost: {WALLJUMP_COST}")
    print()

    solver_name = "brute force" if args.bruteforce else "Z3"
    solver_fn = brute_force if args.bruteforce else solve_z3

    if args.nc is not None and args.nk is not None:
        # Single (nc, nk)
        t0 = _timer.time()
        seq, solver_time = solver_fn(args.nc, args.nk, verbose=True)
        elapsed = _timer.time() - t0
        if seq is None:
            print("UNSAT"); return
        names = sequence_to_names(seq)
        ev_time = eval_sequence(seq)
        print(f"\nOptimal for nc={args.nc}, nk={args.nk} ({solver_name}):")
        print(f"  Sequence:    {run_length_encode(seq)}")
        print(f"  Full:        {' → '.join(names)} → wallJump")
        print(f"  Model obj:   {solver_time:.2f}s")
        print(f"  eval_seq:    {ev_time:.2f}s")
        print(f"  Solver wall: {elapsed:.2f}s")

        if args.validate:
            print(f"\nSimulator ({args.sims} runs):")
            stats = simulate_policy(names, n_sims=args.sims)
            print(f"  Mean={stats['mean']:.1f}s  Median={stats['median']:.1f}s  "
                  f"P10={stats['p10']:.1f}s  P90={stats['p90']:.1f}s")
        return

    # ── Sweep mode ──
    # Use brute force where feasible, Z3 for larger problems
    BF_LIMIT = 500_000  # max C(N, nc) for brute force

    print("Sweeping nc=[2..14], nk=[0..12]...")
    print(f"  Using brute force where C(N,nc) < {BF_LIMIT:,}, Z3 otherwise")
    print()

    results = []
    t0 = _timer.time()

    for nc in range(2, 15):
        for nk in range(0, 13):
            N = nc + nk
            combos = math.comb(N, nc)

            if args.bruteforce and combos > BF_LIMIT:
                continue  # skip if forced brute-force and too large

            try:
                if combos <= BF_LIMIT:
                    seq, _ = brute_force(nc, nk)
                    method = "BF"
                else:
                    seq, _ = solve_z3(nc, nk)
                    method = "Z3"
            except Exception as e:
                print(f"  nc={nc} nk={nk}: FAILED ({e})")
                continue

            if seq is not None:
                ev_time = eval_sequence(seq)
                results.append((nc, nk, seq, ev_time, method))

    elapsed = _timer.time() - t0
    results.sort(key=lambda r: r[3])

    print(f"{'nc':>3} {'nk':>3} {'N':>3} {'Eval':>10} {'Via':>4}  Sequence")
    print("-" * 72)
    for nc, nk, seq, ev_t, method in results[:30]:
        print(f"{nc:>3} {nk:>3} {nc+nk:>3} {ev_t:>10.2f} {method:>4}  {run_length_encode(seq)}")

    print(f"\nSolved {len(results)} configs in {elapsed:.1f}s")

    if not results:
        return

    # ── Best result ──
    nc, nk, seq, ev_t, method = results[0]
    names = sequence_to_names(seq)
    print(f"\n{'='*72}")
    print(f"BEST:  nc={nc}  nk={nk}  eval_time={ev_t:.2f}s  (via {method})")
    print(f"  Compact:  {run_length_encode(seq)}")
    print(f"  Full:     {' → '.join(names)} → wallJump")

    # ── Compare with known strategies ──
    print(f"\n--- Comparison with known strategies ---")
    known = [
        ("MCTSDistilledV2 (3,8,7)",  [0]*3 + [1]*8 + [0]*7),
        ("Lukas-7 (3C 8K 7C 2K)",   [0]*3 + [1]*8 + [0]*7 + [1]*2),
        ("Tomjon6-style (7C 1K 3C)", [0]*7 + [1]*1 + [0]*3),
        ("Z3 OPTIMAL",               seq),
    ]
    print(f"  {'Strategy':<30} {'Eval':>10}  Sequence")
    print(f"  {'-'*66}")
    for name, kseq in known:
        kt = eval_sequence(kseq)
        print(f"  {name:<30} {kt:>10.2f}  {run_length_encode(kseq)}")

    # ── Simulator validation ──
    if args.validate:
        print(f"\n--- Simulator validation ({args.sims} runs) ---")
        for name, kseq in known:
            knames = sequence_to_names(kseq)
            stats = simulate_policy(knames, n_sims=args.sims)
            print(f"\n  {name} ({run_length_encode(kseq)}):")
            print(f"    Mean={stats['mean']:.1f}s  Median={stats['median']:.1f}s  "
                  f"P10={stats['p10']:.1f}s  P90={stats['p90']:.1f}s")


if __name__ == "__main__":
    main()
