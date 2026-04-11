"""Trace observer + cash/income plotting for the simulator."""
from dataclasses import dataclass, field

from simulator import SimObserver
from state import GameState


@dataclass
class TraceSample:
    time: float
    cash: float
    player_ips: float       # instantaneous player income rate (cash/sec)
    clone_ips: float        # instantaneous clone income rate (cash/sec)
    total_player: float     # cumulative player reward earned
    total_clone: float      # cumulative clone reward earned


@dataclass
class TraceObserver(SimObserver):
    """Records every event into a list of TraceSample."""
    samples: list[TraceSample] = field(default_factory=list)
    total_player: float = 0.0
    total_clone: float = 0.0

    def _record(self, state: GameState):
        cfg = state.config
        avg_run_time = (cfg.success_rate * cfg.avg_success_time +
                        (1 - cfg.success_rate) * cfg.avg_failure_time)
        if avg_run_time > 0:
            player_ips = state.reward_per_completion * cfg.success_rate / avg_run_time
        else:
            player_ips = 0.0
        if state.clone_count > 0 and cfg.clone_course_duration > 0:
            clone_ips = (state.clone_count * state.clone_reward_per_completion /
                         cfg.clone_course_duration)
        else:
            clone_ips = 0.0
        self.samples.append(TraceSample(
            time=state.time,
            cash=state.cash,
            player_ips=player_ips,
            clone_ips=clone_ips,
            total_player=self.total_player,
            total_clone=self.total_clone,
        ))

    def on_start(self, state):
        self._record(state)

    def on_transition(self, state, from_state, to_state, duration, clone_income):
        self.total_clone += clone_income
        self._record(state)

    def on_run_complete(self, state, run_time, success, player_reward, clone_income):
        self.total_clone += clone_income
        self.total_player += player_reward
        self._record(state)

    def on_buy(self, state, upgrade_name):
        self._record(state)

    def on_finish(self, state):
        self._record(state)


def trace_policy(config, policy, seed: int = 42) -> tuple[float, list[TraceSample]]:
    """Run policy once with tracing, return (finish_time, samples)."""
    from simulator import Simulator
    sim = Simulator(config, seed=seed)
    obs = TraceObserver()
    state = sim.run(policy, observer=obs)
    return state.time, obs.samples


def trace_policy_batch(config, policy, n_sims: int, seed: int = 42) -> list[list[TraceSample]]:
    """Run policy n_sims times with tracing, return all sample lists."""
    from simulator import Simulator
    traces = []
    for i in range(n_sims):
        sim = Simulator(config, seed=seed + i)
        obs = TraceObserver()
        sim.run(policy, observer=obs)
        traces.append(obs.samples)
    return traces


# === Plotting ===

def _resample(traces: list[list[TraceSample]], field_name: str, n_grid: int = 200):
    """Step-resample the named field of each trace onto a common time grid.
    Returns (grid, resampled[n_traces, n_grid])."""
    import numpy as np
    if not traces:
        return None, None
    max_t = max(t[-1].time for t in traces if t)
    grid = np.linspace(0, max_t, n_grid)
    out = np.zeros((len(traces), n_grid))
    for i, trace in enumerate(traces):
        ts = np.array([s.time for s in trace])
        vals = np.array([getattr(s, field_name) for s in trace])
        idx = np.clip(np.searchsorted(ts, grid, side="right") - 1, 0, len(vals) - 1)
        out[i] = vals[idx]
        # Freeze at last value past trace end
        out[i][grid > trace[-1].time] = vals[-1]
    return grid, out


def _plot_band(ax, name, mean_finish, grid, values, color=None):
    """Plot median + P10-P90 band."""
    import numpy as np
    median = np.median(values, axis=0)
    p10 = np.percentile(values, 10, axis=0)
    p90 = np.percentile(values, 90, axis=0)
    line, = ax.plot(grid, median, label=f"{name} (mean={mean_finish:.1f}s)",
                    linewidth=1.8, color=color)
    ax.fill_between(grid, p10, p90, alpha=0.15, color=line.get_color())
    return line.get_color()


def plot_traces(config, policies, n_sims: int = 100, seed: int = 42,
                out_path: str = "trace.png"):
    """Generate a 4-panel plot: cash, player IPS, clone IPS, cumulative income split."""
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    # Collect all traces per policy
    policy_data = []
    for policy in policies:
        traces = trace_policy_batch(config, policy, n_sims, seed=seed)
        finish_times = [t[-1].time for t in traces if t]
        if not finish_times:
            continue
        mean_finish = sum(finish_times) / len(finish_times)
        policy_data.append((policy.name, mean_finish, traces))

    policy_data.sort(key=lambda x: x[1])

    fig, axes = plt.subplots(2, 2, figsize=(16, 10))
    ax_cash, ax_player_ips = axes[0]
    ax_clone_ips, ax_cum = axes[1]

    color_for = {}
    for name, mean_finish, traces in policy_data:
        grid, cash = _resample(traces, "cash")
        c = _plot_band(ax_cash, name, mean_finish, grid, cash)
        color_for[name] = c

        _, pips = _resample(traces, "player_ips")
        _plot_band(ax_player_ips, name, mean_finish, grid, pips, color=c)

        _, kips = _resample(traces, "clone_ips")
        _plot_band(ax_clone_ips, name, mean_finish, grid, kips, color=c)

        # Cumulative split: stacked total_player + total_clone
        _, tp = _resample(traces, "total_player")
        _, tk = _resample(traces, "total_clone")
        import numpy as np
        total_median = np.median(tp + tk, axis=0)
        ax_cum.plot(grid, total_median, label=f"{name} total", linewidth=1.8, color=c)
        ax_cum.plot(grid, np.median(tk, axis=0), linewidth=1.0, linestyle="--", color=c, alpha=0.7)

    ax_cash.set_title(f"Cash on hand (median + P10-P90)")
    ax_cash.set_xlabel("Time (s)")
    ax_cash.set_ylabel("Cash")
    ax_cash.legend(fontsize=8, loc="upper left")
    ax_cash.grid(True, alpha=0.3)

    ax_player_ips.set_title("Player income rate (cash/sec)")
    ax_player_ips.set_xlabel("Time (s)")
    ax_player_ips.set_ylabel("Player IPS")
    ax_player_ips.grid(True, alpha=0.3)

    ax_clone_ips.set_title("Clone income rate (cash/sec)")
    ax_clone_ips.set_xlabel("Time (s)")
    ax_clone_ips.set_ylabel("Clone IPS")
    ax_clone_ips.grid(True, alpha=0.3)

    ax_cum.set_title("Cumulative income — solid=total, dashed=clone share")
    ax_cum.set_xlabel("Time (s)")
    ax_cum.set_ylabel("Cumulative cash earned")
    ax_cum.grid(True, alpha=0.3)

    fig.suptitle(f"Strategy comparison ({n_sims} sims/policy)", fontsize=14)
    plt.tight_layout()
    plt.savefig(out_path, dpi=150)
    print(f"Saved trace plot to {out_path}")
