"""Behavior metric extraction via SimObserver.

`simulate_with_metrics(config, sequence, seed)` runs a FixedSequence policy
through the simulator with a MetricsObserver attached and returns a feature
dict used by lexicase/novelty selection.
"""
from simulator import Simulator, SimObserver
from policy import FixedSequence


INF = 100000.0


class MetricsObserver(SimObserver):
    """Records behavior metrics during a simulation run."""

    def __init__(self):
        self.total_player = 0.0
        self.total_clone = 0.0
        self.max_cash_on_hand = 0.0
        self.buy_count = 0
        self.milestones = {}  # name -> time
        self.total_transition_time = 0.0
        self.last_buy_time = 0.0
        self.buy_gaps = []
        self.first_clone_income_time = None
        self.income_snapshots = []  # list of (time, total_earned)
        self.clone_income_at_50 = 0.0
        self.clone_income_at_100 = 0.0
        self.total_income_at_50 = 0.0
        self.total_income_at_100 = 0.0

    def _record_milestone(self, name, time):
        if name not in self.milestones:
            self.milestones[name] = time

    def on_start(self, state):
        pass

    def on_transition(self, state, from_loc, to_loc, duration, clone_income):
        self.total_clone += clone_income
        self.total_transition_time += duration
        if clone_income > 0 and self.first_clone_income_time is None:
            self.first_clone_income_time = state.time
        if state.cash > self.max_cash_on_hand:
            self.max_cash_on_hand = state.cash

    def on_run_complete(self, state, run_time, success, player_reward, clone_income):
        self.total_player += player_reward
        self.total_clone += clone_income
        if clone_income > 0 and self.first_clone_income_time is None:
            self.first_clone_income_time = state.time
        if state.cash > self.max_cash_on_hand:
            self.max_cash_on_hand = state.cash

        total_earned = self.total_player + self.total_clone
        if total_earned > 0:
            self.income_snapshots.append((state.time, total_earned))

        # Snapshots at fixed time markers
        if state.time >= 50 and self.total_income_at_50 == 0:
            self.total_income_at_50 = total_earned
            self.clone_income_at_50 = self.total_clone
        if state.time >= 100 and self.total_income_at_100 == 0:
            self.total_income_at_100 = total_earned
            self.clone_income_at_100 = self.total_clone

    def on_buy(self, state, upgrade_name):
        self.buy_count += 1
        self.buy_gaps.append(state.time - self.last_buy_time)
        self.last_buy_time = state.time

        for n in range(1, 10):
            if state.clone_count >= n:
                self._record_milestone(f"clone_{n}", state.time)
        for mult in [2, 4, 6, 8, 10]:
            if state.cash_per_loop + 1 >= mult:
                self._record_milestone(f"mult_{mult}x", state.time)
        total_earned = self.total_player + self.total_clone
        if total_earned >= 100:
            self._record_milestone("cash_100", state.time)
        if total_earned >= 500:
            self._record_milestone("cash_500", state.time)

    def on_finish(self, state):
        pass


def simulate_with_metrics(config, sequence: list[str], rng_seed: int) -> dict[str, float]:
    """Run a FixedSequence policy and return behavior metrics.

    The terminal upgrade is appended automatically if not already present.
    """
    if not sequence or sequence[-1] != config.terminal_upgrade:
        sequence = list(sequence) + [config.terminal_upgrade]

    sim = Simulator(config, seed=rng_seed)
    obs = MetricsObserver()
    state = sim.run(FixedSequence(sequence), observer=obs)

    finish_time = state.time if state.has_terminal else INF
    total_earned = obs.total_player + obs.total_clone

    features = {
        "time_to_terminal": finish_time,
        "buy_count": float(obs.buy_count),
        "neg_max_cash_on_hand": -obs.max_cash_on_hand,
    }

    for n in range(1, 10):
        features[f"time_to_clone_{n}"] = obs.milestones.get(f"clone_{n}", INF)
    for mult in [2, 4, 6, 8, 10]:
        features[f"time_to_mult_{mult}x"] = obs.milestones.get(f"mult_{mult}x", INF)
    features["time_to_cash_100"] = obs.milestones.get("cash_100", INF)
    features["time_to_cash_500"] = obs.milestones.get("cash_500", INF)

    # Tension 1: clone share / utilization
    features["first_clone_income_time"] = obs.first_clone_income_time or INF
    features["neg_clone_share_at_50"] = (
        -(obs.clone_income_at_50 / obs.total_income_at_50) if obs.total_income_at_50 > 0 else 0
    )
    features["neg_clone_share_at_100"] = (
        -(obs.clone_income_at_100 / obs.total_income_at_100) if obs.total_income_at_100 > 0 else 0
    )
    features["neg_clone_utilization"] = (
        -(obs.total_clone / total_earned) if total_earned > 0 else 0
    )

    # Tension 2: transition overhead
    features["transition_overhead"] = obs.total_transition_time
    features["longest_buy_gap"] = max(obs.buy_gaps) if obs.buy_gaps else INF
    features["avg_buy_gap"] = (sum(obs.buy_gaps) / len(obs.buy_gaps)) if obs.buy_gaps else INF
    features["buy_gap_variance"] = (
        sum((g - features["avg_buy_gap"]) ** 2 for g in obs.buy_gaps) / len(obs.buy_gaps)
        if len(obs.buy_gaps) > 1 else 0
    )

    # Tension 3: cash income shape
    features["income_doubling_time"] = INF  # not tracked anymore (was unreliable anyway)
    for pct in [25, 50, 75]:
        t_target = finish_time * pct / 100
        earned_at = 0
        for t, e in obs.income_snapshots:
            if t <= t_target:
                earned_at = e
        features[f"neg_cps_at_{pct}pct"] = -(earned_at / t_target) if t_target > 0 else 0

    features["cash_waste_at_terminal"] = state.cash if state.has_terminal else INF

    return features
