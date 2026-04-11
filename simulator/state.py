"""Game state model with reward and cost calculations."""
import math
from dataclasses import dataclass, field

from config import SimConfig


@dataclass
class GameState:
    config: SimConfig
    time: float = 0.0
    cash: float = 0.0
    upgrades: dict[str, int] = field(default_factory=dict)  # name -> times_purchased

    def __post_init__(self):
        if not self.upgrades:
            self.upgrades = {name: 0 for name in self.config.upgrades}

    def clone(self) -> "GameState":
        return GameState(
            config=self.config,
            time=self.time,
            cash=self.cash,
            upgrades=dict(self.upgrades),
        )

    # --- Derived properties ---

    @property
    def clone_count(self) -> int:
        return self.upgrades.get(self.config.clone_upgrade, 0)

    @property
    def cash_per_loop(self) -> int:
        return self.upgrades.get(self.config.income_upgrade, 0)

    @property
    def has_terminal(self) -> bool:
        return self.upgrades.get(self.config.terminal_upgrade, 0) >= 1

    @property
    def reward_per_completion(self) -> float:
        """Player reward for completing the course."""
        return math.ceil(self.config.base_reward * (self.cash_per_loop + 1))

    @property
    def clone_reward_per_completion(self) -> float:
        """Reward per clone completion."""
        return math.ceil(self.reward_per_completion * self.config.clone_base_multiplier)

    # --- Cost queries ---

    def upgrade_cost(self, name: str) -> float:
        """Current cost to buy the next level of an upgrade."""
        uc = self.config.upgrades[name]
        return uc.cost_at(self.upgrades[name])

    def can_afford(self, name: str) -> bool:
        uc = self.config.upgrades[name]
        if self.upgrades[name] >= uc.cap:
            return False
        return self.cash >= self.upgrade_cost(name)

    def affordable_upgrades(self) -> list[str]:
        """List of upgrade names the player can currently buy."""
        result = []
        for name, uc in self.config.upgrades.items():
            if self.upgrades[name] < uc.cap and self.cash >= self.upgrade_cost(name):
                result.append(name)
        return result

    # --- Actions ---

    def buy_upgrade(self, name: str):
        """Buy one level of an upgrade. Deducts cash."""
        cost = self.upgrade_cost(name)
        assert self.cash >= cost, f"Can't afford {name}: have {self.cash}, need {cost}"
        assert self.upgrades[name] < self.config.upgrades[name].cap, f"{name} is maxed"
        self.cash -= cost
        self.upgrades[name] += 1
