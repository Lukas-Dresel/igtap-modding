"""Load game data and time distributions."""
import json
import csv
from dataclasses import dataclass, field
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
    success_times: list[float]
    failure_times: list[float]
    clone_course_duration: float
    terminal_upgrade: str
    income_upgrade: str
    clone_upgrade: str
    course: str = "course1"
    # transitions: directional adjacency map "from -> to" -> {travel_time, ...}
    # Locations are "entrance", "exit", or upgrade names. Querying an unmodeled
    # pair raises an error — use travel_time() to query.
    transitions: dict[str, dict] = field(default_factory=dict)

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

    @property
    def buyable_upgrades(self) -> dict[str, UpgradeConfig]:
        """All upgrades except the terminal one."""
        return {k: v for k, v in self.upgrades.items() if k != self.terminal_upgrade}

    @property
    def buyable_upgrade_names(self) -> list[str]:
        """Sorted list of non-terminal upgrade names (stable ordering for search algorithms)."""
        return sorted(self.buyable_upgrades.keys())

    def travel_time(self, from_loc: str, to_loc: str) -> float:
        """Time to travel between two locations. Raises if the pair is not modeled.

        Locations: "entrance", "exit", or any upgrade name.
        """
        key = f"{from_loc} -> {to_loc}"
        entry = self.transitions.get(key)
        if entry is None:
            raise KeyError(f"No travel time defined for '{key}' (course={self.course})")
        return entry["travel_time"]


def load_config(
    data_path: str | None = None,
    course: str = "course1",
    profile: str | None = None,
    success_times_path: str | None = None,
    failure_times_path: str | None = None,
    clone_course_duration: float | None = None,
) -> SimConfig:
    base = Path(__file__).parent

    if data_path is None:
        data_path = f"data/{course}_data.json"

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

    transitions = data.get("transitions", {})

    terminal_upgrade = data.get("terminal_upgrade", "wallJump")
    income_upgrade = data.get("income_upgrade", "cashPerLoop")
    clone_upgrade = data.get("clone_upgrade", "cloneCount")

    # Load from profile if specified
    if profile:
        profile_dir = base / "profiles" / profile / course
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
        terminal_upgrade=terminal_upgrade,
        income_upgrade=income_upgrade,
        clone_upgrade=clone_upgrade,
        course=course,
        transitions=transitions,
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
