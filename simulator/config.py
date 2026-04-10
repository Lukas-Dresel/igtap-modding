"""Load game data and time distributions."""
import json
import csv
from dataclasses import dataclass
from pathlib import Path


@dataclass
class UpgradeConfig:
    name: str
    type: str  # "local" or "movement"
    base_cost: float
    scale_factor: float
    power_scale_factor: float
    add_factor: float
    cap: int

    def cost_at(self, times_purchased: int) -> float:
        """Cost of the (times_purchased+1)-th purchase."""
        import math
        cost = self.base_cost
        for _ in range(times_purchased):
            cost += self.add_factor
            cost = cost ** self.power_scale_factor
            cost = math.ceil(cost * self.scale_factor)
        return cost


@dataclass
class SimConfig:
    base_reward: int
    clone_base_multiplier: float
    upgrades: dict[str, UpgradeConfig]
    success_times: list[float]  # human completion times for successful runs
    failure_times: list[float]  # human completion times for failed runs
    clone_course_duration: float  # fixed time for clones to complete course
    # Transition times are defined in fsm.py

    @property
    def success_rate(self) -> float:
        total = len(self.success_times) + len(self.failure_times)
        return len(self.success_times) / total if total > 0 else 1.0

    @property
    def avg_success_time(self) -> float:
        return sum(self.success_times) / len(self.success_times) if self.success_times else 10.0

    @property
    def avg_failure_time(self) -> float:
        return sum(self.failure_times) / len(self.failure_times) if self.failure_times else 5.0


def load_config(
    data_path: str = "data/level1_data.json",
    profile: str | None = None,
    success_times_path: str | None = None,
    failure_times_path: str | None = None,
    clone_course_duration: float | None = None,
) -> SimConfig:
    base = Path(__file__).parent

    with open(base / data_path) as f:
        data = json.load(f)

    upgrades = {}
    for name, u in data["upgrades"].items():
        upgrades[name] = UpgradeConfig(
            name=name,
            type=u["type"],
            base_cost=u["base_cost"],
            scale_factor=u["scale_factor"],
            power_scale_factor=u["power_scale_factor"],
            add_factor=u["add_factor"],
            cap=u["cap"],
        )

    # Load from profile if specified
    if profile:
        profile_dir = base / "profiles" / profile
        profile_json = profile_dir / "profile.json"
        with open(profile_json) as f:
            pdata = json.load(f)

        st_path = profile_dir / "success_times.csv"
        ft_path = profile_dir / "failure_times.csv"
        success_times = _load_times(st_path) if st_path.exists() else []
        failure_times = _load_times(ft_path) if ft_path.exists() else []

        if clone_course_duration is None:
            clone_course_duration = pdata.get("clone_course_duration", 2.1)
    else:
        success_times = _load_times(base / success_times_path) if success_times_path else [8.0] * 50
        failure_times = _load_times(base / failure_times_path) if failure_times_path else [4.0] * 10
        if clone_course_duration is None:
            clone_course_duration = 2.1

    return SimConfig(
        base_reward=data["course"]["base_reward"],
        clone_base_multiplier=data["clone"]["base_multiplier"],
        upgrades=upgrades,
        success_times=success_times,
        failure_times=failure_times,
        clone_course_duration=clone_course_duration,
    )


def _load_times(path: Path) -> list[float]:
    times = []
    with open(path) as f:
        reader = csv.reader(f)
        for row in reader:
            for val in row:
                val = val.strip()
                if val:
                    try:
                        times.append(float(val))
                    except ValueError:
                        pass
    return times
