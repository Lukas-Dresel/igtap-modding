"""MCTS over game states. Branches ONLY on buy decisions.

Run actions are collapsed: between buy decisions, the simulator runs
courses until the player can afford at least one upgrade. Then the
tree branches on which upgrade to buy.
"""
import math
import random
from dataclasses import dataclass, field

from config import SimConfig
from state import GameState
from fsm import State, transition_time
from simulator import _clone_income_between


def advance(state: GameState, config: SimConfig, duration: float, clone_start: float | None):
    if duration <= 0:
        state.time += duration
        return
    if state.clone_count > 0 and clone_start is not None:
        state.cash += _clone_income_between(state, config, clone_start, state.time, state.time + duration)
    state.time += duration


def run_until_can_buy(state: GameState, config: SimConfig, rng: random.Random,
                      clone_start: float | None, fsm_state: State,
                      max_time: float = 100000) -> tuple[float | None, State]:
    """Run courses until the player can afford at least one upgrade.
    Mutates state. Returns (clone_start, fsm_state)."""
    while state.time < max_time:
        if state.affordable_upgrades():
            return clone_start, fsm_state

        # Need to run a course to earn more
        if fsm_state == State.AT_EXIT:
            advance(state, config, transition_time(config, State.AT_EXIT, State.AT_ENTRANCE), clone_start)
            fsm_state = State.AT_ENTRANCE
        elif fsm_state == State.AT_BOX:
            advance(state, config, transition_time(config, State.AT_BOX, State.AT_ENTRANCE), clone_start)
            fsm_state = State.AT_ENTRANCE

        if fsm_state == State.AT_ENTRANCE:
            advance(state, config, transition_time(config, State.AT_ENTRANCE, State.RUNNING), clone_start)

        # Run course
        if rng.random() < config.success_rate:
            rt = rng.choice(config.success_times)
            advance(state, config, rt, clone_start)
            state.cash += state.reward_per_completion
        else:
            rt = rng.choice(config.failure_times)
            advance(state, config, rt, clone_start)
        fsm_state = State.AT_EXIT

    return clone_start, fsm_state


def do_buy(state: GameState, config: SimConfig, upgrade: str,
           clone_start: float | None, fsm_state: State) -> tuple[float | None, State]:
    """Execute a buy action. Mutates state."""
    if fsm_state == State.AT_EXIT:
        advance(state, config, transition_time(config, State.AT_EXIT, State.AT_BOX), clone_start)
    elif fsm_state == State.AT_BOX:
        advance(state, config, transition_time(config, State.AT_BOX, State.AT_BOX), clone_start)

    state.buy_upgrade(upgrade)
    if clone_start is None and state.clone_count > 0:
        clone_start = state.time
    return clone_start, State.AT_BOX


@dataclass
class Node:
    state: GameState
    clone_start: float | None
    fsm_state: State
    upgrade_bought: str | None = None  # what buy led here
    parent: "Node | None" = None
    children: dict[str, "Node"] = field(default_factory=dict)
    visits: int = 0
    total_reward: float = 0.0
    untried: list[str] | None = None

    @property
    def is_terminal(self) -> bool:
        return self.state.has_terminal

    def ucb1(self, c: float = 1.414) -> float:
        if self.visits == 0:
            return float("inf")
        exploit = self.total_reward / self.visits
        explore = c * math.sqrt(math.log(self.parent.visits) / self.visits)
        return exploit + explore


class MCTS:
    def __init__(self, config: SimConfig, seed: int = 42):
        self.config = config
        self.rng = random.Random(seed)

    def search(self, iterations: int = 10000, verbose: bool = True) -> list[str]:
        initial_state = GameState(config=self.config)
        # Run until first buy is possible
        clone_start, fsm = run_until_can_buy(
            initial_state, self.config, self.rng, None, State.AT_ENTRANCE)

        root = Node(state=initial_state, clone_start=clone_start, fsm_state=fsm)

        best_time = float("inf")
        best_buys = None

        for i in range(iterations):
            node = self._select(root)
            if not node.is_terminal:
                node = self._expand(node)
            time, buys = self._rollout(node)
            self._backprop(node, -time)

            if time < best_time:
                best_time = time
                best_buys = buys
                if verbose and (i < 100 or i % 1000 == 0):
                    print(f"  iter {i}: best {best_time:.1f}s ({len(best_buys)} buys)")

        if verbose:
            print(f"  Final best: {best_time:.1f}s")
        return best_buys

    def _get_actions(self, node: Node) -> list[str]:
        """Affordable upgrades + 'skip' (save up and run more)."""
        actions = list(node.state.affordable_upgrades())
        if not node.state.has_terminal:
            actions.append("skip")
        return actions

    def _select(self, node: Node) -> Node:
        while not node.is_terminal:
            if node.untried is None:
                node.untried = self._get_actions(node)
            if node.untried:
                return node
            if not node.children:
                return node
            node = max(node.children.values(), key=lambda n: n.ucb1())
        return node

    def _expand(self, node: Node) -> Node:
        if node.untried is None:
            node.untried = self._get_actions(node)
        if not node.untried:
            return node

        action = node.untried.pop()
        child_state = node.state.clone()
        clone_start = node.clone_start
        fsm = node.fsm_state

        if action == "skip":
            # Run one course without buying, then run until can buy again
            if fsm == State.AT_EXIT:
                advance(child_state, self.config, transition_time(config, State.AT_EXIT, State.AT_ENTRANCE), clone_start)
            elif fsm == State.AT_BOX:
                advance(child_state, self.config, transition_time(config, State.AT_BOX, State.AT_ENTRANCE), clone_start)
            advance(child_state, self.config, transition_time(config, State.AT_ENTRANCE, State.RUNNING), clone_start)
            if self.rng.random() < self.config.success_rate:
                rt = self.rng.choice(self.config.success_times)
                advance(child_state, self.config, rt, clone_start)
                child_state.cash += child_state.reward_per_completion
            else:
                rt = self.rng.choice(self.config.failure_times)
                advance(child_state, self.config, rt, clone_start)
            fsm = State.AT_EXIT
            clone_start, fsm = run_until_can_buy(
                child_state, self.config, self.rng, clone_start, fsm)
        else:
            # Buy the upgrade
            clone_start, fsm = do_buy(child_state, self.config, action, clone_start, fsm)
            if not child_state.has_terminal:
                clone_start, fsm = run_until_can_buy(
                    child_state, self.config, self.rng, clone_start, fsm)

        child = Node(state=child_state, clone_start=clone_start, fsm_state=fsm,
                     upgrade_bought=action if action != "skip" else None, parent=node)
        node.children[action] = child
        return child

    def _rollout(self, node: Node) -> tuple[float, list[str]]:
        state = node.state.clone()
        clone_start = node.clone_start
        fsm = node.fsm_state
        buys = self._get_buys(node)

        while not state.has_terminal and state.time < 100000:
            affordable = state.affordable_upgrades()
            if not affordable:
                clone_start, fsm = run_until_can_buy(
                    state, self.config, self.rng, clone_start, fsm)
                affordable = state.affordable_upgrades()
                if not affordable:
                    break

            # Random choice: buy something or skip
            if self.rng.random() < 0.15:  # 15% chance to skip
                # Run one course without buying
                if fsm == State.AT_EXIT:
                    advance(state, self.config, transition_time(config, State.AT_EXIT, State.AT_ENTRANCE), clone_start)
                elif fsm == State.AT_BOX:
                    advance(state, self.config, transition_time(config, State.AT_BOX, State.AT_ENTRANCE), clone_start)
                advance(state, self.config, transition_time(config, State.AT_ENTRANCE, State.RUNNING), clone_start)
                if self.rng.random() < self.config.success_rate:
                    rt = self.rng.choice(self.config.success_times)
                    advance(state, self.config, rt, clone_start)
                    state.cash += state.reward_per_completion
                else:
                    rt = self.rng.choice(self.config.failure_times)
                    advance(state, self.config, rt, clone_start)
                fsm = State.AT_EXIT
            else:
                upgrade = self.rng.choice(affordable)
                clone_start, fsm = do_buy(state, self.config, upgrade, clone_start, fsm)
                buys.append(upgrade)

        return state.time, buys

    def _get_buys(self, node: Node) -> list[str]:
        path = []
        n = node
        while n.parent is not None:
            if n.upgrade_bought:
                path.append(n.upgrade_bought)
            n = n.parent
        path.reverse()
        return path

    def _backprop(self, node: Node, reward: float):
        while node is not None:
            node.visits += 1
            node.total_reward += reward
            node = node.parent
