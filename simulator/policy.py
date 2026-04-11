"""Policy interface and built-in policies."""
from abc import ABC, abstractmethod
from dataclasses import dataclass

from state import GameState


@dataclass
class Action:
    type: str  # "run" or "buy"
    upgrade_name: str | None = None

    @staticmethod
    def run() -> "Action":
        return Action(type="run")

    @staticmethod
    def buy(name: str) -> "Action":
        return Action(type="buy", upgrade_name=name)


class Policy(ABC):
    """Base class for all policies. Maps game state to an action."""

    @abstractmethod
    def choose_action(self, state: GameState) -> Action:
        ...

    def compatible_with(self, config) -> bool:
        """Return True if this policy can run on the given config (level)."""
        return True

    @property
    def name(self) -> str:
        return self.__class__.__name__


class SaveForWallJump(Policy):
    """Baseline: never buy anything except the terminal upgrade."""

    def choose_action(self, state: GameState) -> Action:
        terminal = state.config.terminal_upgrade
        if state.can_afford(terminal):
            return Action.buy(terminal)
        return Action.run()


class CheapestFirst(Policy):
    """Always buy the cheapest affordable upgrade."""

    def choose_action(self, state: GameState) -> Action:
        affordable = state.affordable_upgrades()
        if not affordable:
            return Action.run()
        cheapest = min(affordable, key=lambda n: state.upgrade_cost(n))
        return Action.buy(cheapest)


class ClonesFirst(Policy):
    """Prioritize clone upgrade, then income upgrade, then terminal."""

    def __init__(self, clone_target: int = 10):
        self.clone_target = clone_target

    def choose_action(self, state: GameState) -> Action:
        cfg = state.config
        if state.clone_count < self.clone_target and state.can_afford(cfg.clone_upgrade):
            return Action.buy(cfg.clone_upgrade)
        if state.can_afford(cfg.income_upgrade):
            return Action.buy(cfg.income_upgrade)
        if state.can_afford(cfg.terminal_upgrade):
            return Action.buy(cfg.terminal_upgrade)
        return Action.run()


class CashFirst(Policy):
    """Prioritize income upgrade, then clones, then terminal."""

    def __init__(self, cash_target: int = 10):
        self.cash_target = cash_target

    def choose_action(self, state: GameState) -> Action:
        cfg = state.config
        if state.cash_per_loop < self.cash_target and state.can_afford(cfg.income_upgrade):
            return Action.buy(cfg.income_upgrade)
        if state.can_afford(cfg.clone_upgrade):
            return Action.buy(cfg.clone_upgrade)
        if state.can_afford(cfg.terminal_upgrade):
            return Action.buy(cfg.terminal_upgrade)
        return Action.run()


class GreedyROI(Policy):
    """Buy the upgrade with the highest marginal income increase per cost.
    If terminal ROI beats everything, save for it."""

    def choose_action(self, state: GameState) -> Action:
        terminal = state.config.terminal_upgrade
        affordable = state.affordable_upgrades()
        if not affordable:
            return Action.run()

        best_name = None
        best_roi = -1.0

        for name in affordable:
            cost = state.upgrade_cost(name)
            if cost <= 0:
                continue

            if name == terminal:
                continue  # evaluate separately below

            old_income = _expected_income_per_second(state)
            test_state = state.clone()
            test_state.buy_upgrade(name)
            new_income = _expected_income_per_second(test_state)
            marginal = new_income - old_income

            roi = marginal / cost if cost > 0 else 0
            if roi > best_roi:
                best_roi = roi
                best_name = name

        if terminal in affordable:
            wj_cost = state.upgrade_cost(terminal)
            current_income = _expected_income_per_second(state)
            if current_income > 0:
                time_to_wj = wj_cost / current_income
                if best_name and best_roi > 0:
                    payback_time = 1.0 / best_roi
                    if payback_time < time_to_wj * 0.5:
                        return Action.buy(best_name)
            return Action.buy(terminal)

        if best_name:
            return Action.buy(best_name)
        return Action.run()


def _expected_income_per_second(state: GameState) -> float:
    """Estimate expected income per second from player runs + clones."""
    cfg = state.config
    avg_run_time = (cfg.success_rate * cfg.avg_success_time +
                    (1 - cfg.success_rate) * cfg.avg_failure_time)
    if avg_run_time <= 0:
        return 0.0

    player_income_per_run = state.reward_per_completion * cfg.success_rate
    player_ips = player_income_per_run / avg_run_time

    clone_ips = 0.0
    if state.clone_count > 0 and cfg.clone_course_duration > 0:
        clone_ips = (state.clone_count * state.clone_reward_per_completion /
                     cfg.clone_course_duration)

    return player_ips + clone_ips


class FixedSequence(Policy):
    """Follow a fixed sequence of upgrade purchases. Run course when can't afford next.
    Tracks position by counting total upgrades bought, so it's stateless per sim.

    Subclass and set SEQUENCE to define a named fixed-sequence policy."""

    SEQUENCE: list[str] = []

    def __init__(self, sequence: list[str] | None = None):
        self._sequence = list(sequence) if sequence is not None else list(self.SEQUENCE)

    def compatible_with(self, config) -> bool:
        return all(name in config.upgrades for name in self._sequence)

    def choose_action(self, state: GameState) -> Action:
        terminal = state.config.terminal_upgrade
        total_bought = sum(state.upgrades.values())
        if total_bought >= len(self._sequence):
            if state.can_afford(terminal):
                return Action.buy(terminal)
            return Action.run()

        target = self._sequence[total_bought]
        if state.can_afford(target):
            return Action.buy(target)
        return Action.run()

    @property
    def name(self) -> str:
        return self.__class__.__name__


class PreTomjon6(FixedSequence):
    """Human-discovered policy: 10c (11x mult) then 10cl, walljump.

    Original cash-first version. Same 10 cash + 10 clones end state as Tomjon6,
    but doesn't kick off clone income early — all cash bought before any clones.
    Tomjon6 refines this by buying the first clone after only 7 cash.
    """

    SEQUENCE = (
        ["cashPerLoop"] * 10 +
        ["cloneCount"] * 10 +
        ["wallJump"]
    )


class Tomjon6(FixedSequence):
    """Human-discovered policy: 7c (8x mult), 1cl (kick off income), 3c (11x mult), 9cl (10 total), walljump.

    Refinement of PreTomjon6: same 10 cash + 10 clones end state, but kicks off
    clone income earlier with 1 clone after 7 cash, instead of waiting for full 11x mult.
    """

    SEQUENCE = (
        ["cashPerLoop"] * 7 +
        ["cloneCount"] * 1 +
        ["cashPerLoop"] * 3 +
        ["cloneCount"] * 9 +
        ["wallJump"]
    )


class Lukas(Policy):
    """Human-discovered policy: cashPerLoop to 4x, save up to buy clone at cost 8,
    then buy N more clones, cashPerLoop to 11x, clones to 10, then greedy.
    Variants: Lukas-1 (1 extra clone), Lukas-2 (2), Lukas-3 (3)."""

    def __init__(self, extra_clones: int = 1):
        self._extra_clones = extra_clones
        self._greedy = GreedyROI()

    def choose_action(self, state: GameState) -> Action:
        clones = state.clone_count
        cash_level = state.cash_per_loop

        # Phase 1: cashPerLoop to 3 purchases (= 4x multiplier)
        if cash_level < 3 and clones == 0:
            if state.can_afford("cashPerLoop"):
                return Action.buy("cashPerLoop")
            return Action.run()

        # Phase 2: save up and buy first clone (cost 8)
        if clones == 0:
            if state.can_afford("cloneCount"):
                return Action.buy("cloneCount")
            return Action.run()

        # Phase 3: buy extra clones (1/2/3 more)
        if clones < 1 + self._extra_clones:
            if state.can_afford("cloneCount"):
                return Action.buy("cloneCount")
            return Action.run()

        # Phase 4: cashPerLoop to 10 purchases (= 11x multiplier)
        if cash_level < 10:
            if state.can_afford("cashPerLoop"):
                return Action.buy("cashPerLoop")
            return Action.run()

        # Phase 5: clones to 10
        if clones < 10:
            if state.can_afford("cloneCount"):
                return Action.buy("cloneCount")
            if state.can_afford("cashPerLoop"):
                return Action.buy("cashPerLoop")
            return Action.run()

        # Phase 6: greedy
        return self._greedy.choose_action(state)

    @property
    def name(self) -> str:
        return f"Lukas-{self._extra_clones}"




class MyskoSub15(FixedSequence):
    """Mysko sub-15 candidate: 3c, cl, 7c, 9cl, 10c + wallJump."""

    SEQUENCE = (
        ["cashPerLoop"] * 3 +
        ["cloneCount"] * 1 +
        ["cashPerLoop"] * 7 +
        ["cloneCount"] * 9 +
        ["cashPerLoop"] * 10 +
        ["wallJump"]
    )


class GraphFullV1(FixedSequence):
    """graphfull's optimize winner (mysko course1, post-refactor): 164.1s mean.

    2c, 3cl, 8c, 6cl, 10c + wallJump. The trailing 10x cash is the graphfull
    over-investment artifact — it buys past the wallJump cost because the search
    doesn't penalize wasted upgrades.
    """

    SEQUENCE = (
        ["cashPerLoop"] * 2 +
        ["cloneCount"] * 3 +
        ["cashPerLoop"] * 8 +
        ["cloneCount"] * 6 +
        ["cashPerLoop"] * 10 +
        ["wallJump"]
    )


class MyskoFast1(FixedSequence):
    """Lexicase winner for mysko-1.6s-clones: 141.4s mean.

    2c, 3cl, 1c, 1cl, 7c, 5cl, 1c, 1cl + wallJump = 10 cash + 10 clones.
    Heavy interleaving pays off when clones are fast.
    """

    SEQUENCE = (
        ["cashPerLoop"] * 2 +
        ["cloneCount"] * 3 +
        ["cashPerLoop"] * 1 +
        ["cloneCount"] * 1 +
        ["cashPerLoop"] * 7 +
        ["cloneCount"] * 5 +
        ["cashPerLoop"] * 1 +
        ["cloneCount"] * 1 +
        ["wallJump"]
    )


class MyskoFast1Variant(FixedSequence):
    """User-suggested variant: 3c, 4cl, 7c, 6cl + wallJump = 10c + 10cl, 4 blocks."""

    SEQUENCE = (
        ["cashPerLoop"] * 3 +
        ["cloneCount"] * 4 +
        ["cashPerLoop"] * 7 +
        ["cloneCount"] * 6 +
        ["wallJump"]
    )


class MCTSDistilledV4(FixedSequence):
    """Best MCTS find: 168.3s. 2c,3cl,c,2cl,3c,2cl,4c,2cl,c + wallJump."""

    SEQUENCE = [
        "cashPerLoop", "cashPerLoop",
        "cloneCount", "cloneCount", "cloneCount",
        "cashPerLoop",
        "cloneCount", "cloneCount",
        "cashPerLoop", "cashPerLoop", "cashPerLoop",
        "cloneCount", "cloneCount",
        "cashPerLoop", "cashPerLoop", "cashPerLoop", "cashPerLoop",
        "cloneCount", "cloneCount",
        "cashPerLoop",
        "wallJump",
    ]


class MCTSDistilledV5(FixedSequence):
    """Hierarchical MCTS find: 167.6s. 3c,2cl,7c,7cl,3c + wallJump."""

    SEQUENCE = (
        ["cashPerLoop"] * 3 +
        ["cloneCount"] * 2 +
        ["cashPerLoop"] * 7 +
        ["cloneCount"] * 7 +
        ["cashPerLoop"] * 3 +
        ["wallJump"]
    )


class MCTSDistilledV6(FixedSequence):
    """GA find: 164.7s. 3c,3cl,c,cl,6c,5cl + wallJump."""

    SEQUENCE = (
        ["cashPerLoop"] * 3 +
        ["cloneCount"] * 3 +
        ["cashPerLoop"] +
        ["cloneCount"] +
        ["cashPerLoop"] * 6 +
        ["cloneCount"] * 5 +
        ["wallJump"]
    )


class MCTSDistilledV7(FixedSequence):
    """GA gen45 find: 164.3s. 2c,3cl,8c,6cl,c + wallJump."""

    SEQUENCE = (
        ["cashPerLoop"] * 2 +
        ["cloneCount"] * 3 +
        ["cashPerLoop"] * 8 +
        ["cloneCount"] * 6 +
        ["cashPerLoop"] +
        ["wallJump"]
    )


class MCTSDistilledV8(FixedSequence):
    """GA gen145 find: 163.9s. 2c,3cl,8c,7cl + wallJump."""

    SEQUENCE = (
        ["cashPerLoop"] * 2 +
        ["cloneCount"] * 3 +
        ["cashPerLoop"] * 8 +
        ["cloneCount"] * 7 +
        ["wallJump"]
    )


class Z3OptimalV1(FixedSequence):
    """Z3 brute-force optimal (nc=10,nk=10): 2c,2cl,c,cl,3c,cl,4c,6cl + wallJump."""

    SEQUENCE = (
        ["cashPerLoop"] * 2 +
        ["cloneCount"] * 2 +
        ["cashPerLoop"] * 1 +
        ["cloneCount"] * 1 +
        ["cashPerLoop"] * 3 +
        ["cloneCount"] * 1 +
        ["cashPerLoop"] * 4 +
        ["cloneCount"] * 6 +
        ["wallJump"]
    )


class SimSearchV1(FixedSequence):
    """Sim-validated block search (nc=10,nk=10): 2c,4cl,8c,6cl + wallJump. 163.7s.

    Found via z3_optimizer.py: first used Z3/brute-force over all C(20,10)=184756
    orderings of 10 cashPerLoop + 10 cloneCount to find the optimal interleaving
    under an expected-value continuous income model (eval_sequence). That found
    2C 2K 1C 1K 3C 1K 4C 6K (Z3OptimalV1), which was slightly suboptimal in the
    stochastic simulator due to model approximation (ignoring batched box buys,
    discrete run cycles). Then searched ~417 block-pattern candidates (XC YK ZC WK
    with varying block sizes) evaluated directly in the stochastic simulator
    (200 sims each, validated top-4 at 5000 sims). This 2C 4K 8C 6K pattern won.
    """

    SEQUENCE = (
        ["cashPerLoop"] * 2 +
        ["cloneCount"] * 4 +
        ["cashPerLoop"] * 8 +
        ["cloneCount"] * 6 +
        ["wallJump"]
    )


class MCTSDistilledV1(FixedSequence):
    """First MCTS distillation (old flat cost model, interleaved)."""

    SEQUENCE = [
        "cashPerLoop", "cashPerLoop", "cashPerLoop", "cloneCount",
        "cashPerLoop", "cloneCount", "cashPerLoop", "cashPerLoop",
        "cloneCount", "cashPerLoop", "cashPerLoop", "cloneCount",
        "cashPerLoop", "cashPerLoop", "cashPerLoop", "cloneCount",
        "cloneCount", "cloneCount", "cloneCount", "cashPerLoop",
        "cloneCount", "cloneCount", "cloneCount", "cloneCount",
        "cloneCount", "cloneCount", "cloneCount", "cashPerLoop",
        "cloneCount", "cashPerLoop", "cashPerLoop", "wallJump",
    ]


class MCTSDistilledV2(FixedSequence):
    """Grid search result: batched 3cash, 8clone, 7cash."""

    SEQUENCE = (
        ["cashPerLoop"] * 3 +
        ["cloneCount"] * 8 +
        ["cashPerLoop"] * 7 +
        ["wallJump"]
    )


class MCTSDistilledV3(FixedSequence):
    """MCTS sequence search (seed=456, FSM costs)."""

    SEQUENCE = [
        "cashPerLoop", "cloneCount", "cloneCount", "cashPerLoop",
        "cashPerLoop", "cloneCount", "cashPerLoop", "cloneCount",
        "cashPerLoop", "cloneCount", "cloneCount", "cashPerLoop",
        "cashPerLoop", "cloneCount", "cashPerLoop", "cashPerLoop",
        "cashPerLoop", "cloneCount", "cashPerLoop", "cashPerLoop",
        "cloneCount", "cashPerLoop", "cashPerLoop",
        "wallJump",
    ]


class CashThenClones(Policy):
    """Buy N cashPerLoop first, then clones to target, then greedy."""

    def __init__(self, cash_first: int = 1, clone_target: int = 5):
        self._cash_first = cash_first
        self._clone_target = clone_target
        self._greedy = GreedyROI()

    def choose_action(self, state: GameState) -> Action:
        if state.cash_per_loop < self._cash_first and state.can_afford("cashPerLoop"):
            return Action.buy("cashPerLoop")
        if state.clone_count < self._clone_target and state.can_afford("cloneCount"):
            return Action.buy("cloneCount")
        return self._greedy.choose_action(state)

    @property
    def name(self) -> str:
        return f"Cash({self._cash_first})Clones({self._clone_target})"


class RandomPolicy(Policy):
    """Random: pick a random affordable action (for MCTS rollouts)."""

    def __init__(self, rng=None):
        import random
        self.rng = rng or random.Random()

    def choose_action(self, state: GameState) -> Action:
        affordable = state.affordable_upgrades()
        if not affordable:
            return Action.run()
        # 50% chance to buy, 50% chance to run
        if self.rng.random() < 0.5:
            return Action.buy(self.rng.choice(affordable))
        return Action.run()
