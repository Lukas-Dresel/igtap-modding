"""MCTS over upgrade purchase sequences.

Each node is a partial sequence of upgrade names.
Actions: append cashPerLoop, cloneCount, or wallJump (terminal).
Evaluation: run FixedSequence through the simulator.
No game state in nodes. No "run" action. Simple.
"""
import math
import random
from dataclasses import dataclass, field

from config import SimConfig
from simulator import Simulator
from policy import FixedSequence


@dataclass
class Node:
    sequence: list[str]
    parent: "Node | None" = None
    children: dict[str, "Node"] = field(default_factory=dict)
    visits: int = 0
    total_reward: float = 0.0
    untried: list[str] | None = None

    @property
    def is_terminal(self) -> bool:
        # Terminal when no more actions possible (both capped)
        # wallJump is always appended implicitly during evaluation
        return False  # nodes are never terminal; rollout decides when to stop

    def ucb1(self, c: float = 1.414) -> float:
        if self.visits == 0:
            return float("inf")
        exploit = self.total_reward / self.visits
        explore = c * math.sqrt(math.log(self.parent.visits) / self.visits)
        return exploit + explore


def get_actions(sequence: list[str], config: SimConfig) -> list[str]:
    """Only cashPerLoop and cloneCount. wallJump is implicit at end."""
    actions = []
    cash_count = sequence.count("cashPerLoop")
    clone_count = sequence.count("cloneCount")
    if cash_count < config.upgrades["cashPerLoop"].cap:
        actions.append("cashPerLoop")
    if clone_count < config.upgrades["cloneCount"].cap:
        actions.append("cloneCount")
    return actions


class MCTS:
    def __init__(self, config: SimConfig, seed: int = 42):
        self.config = config
        self.rng = random.Random(seed)
        self.sim = Simulator(config, seed=seed)

    def evaluate(self, sequence: list[str]) -> float:
        """Run one simulation of this sequence + wallJump at end."""
        policy = FixedSequence(sequence + ["wallJump"])
        state = self.sim.run(policy)
        return state.time

    def search(self, iterations: int = 10000, verbose: bool = True) -> list[str]:
        root = Node(sequence=[])

        best_time = float("inf")
        best_seq = None

        for i in range(iterations):
            node = self._select(root)
            if not node.is_terminal:
                node = self._expand(node)
            time, seq = self._rollout(node)
            self._backprop(node, -time)

            if time < best_time:
                best_time = time
                best_seq = seq
                if verbose and (i < 100 or i % 1000 == 0):
                    print(f"  iter {i}: best {best_time:.1f}s ({len(best_seq)} buys)")

        if verbose:
            print(f"  Final best: {best_time:.1f}s")
        return best_seq

    def _select(self, node: Node) -> Node:
        while True:
            if node.untried is None:
                node.untried = get_actions(node.sequence, self.config)
            if node.untried:
                return node
            if not node.children:
                return node  # leaf, no more actions
            node = max(node.children.values(), key=lambda n: n.ucb1())

    def _expand(self, node: Node) -> Node:
        if node.untried is None:
            node.untried = get_actions(node.sequence, self.config)
        if not node.untried:
            return node

        action = node.untried.pop()
        child = Node(sequence=node.sequence + [action], parent=node)
        node.children[action] = child
        return child

    def _rollout(self, node: Node) -> tuple[float, list[str]]:
        """Random completion + evaluate. Randomly decides when to stop buying and go for wallJump."""
        seq = list(node.sequence)

        # Randomly add more upgrades, with increasing chance to stop
        for i in range(50):
            actions = get_actions(seq, self.config)
            if not actions:
                break
            # Increasing probability to stop as sequence gets longer
            stop_prob = len(seq) / 40.0  # ~50% chance to stop at length 20
            if self.rng.random() < stop_prob:
                break
            seq.append(self.rng.choice(actions))

        # Evaluate: sequence + wallJump at end
        time = self.evaluate(seq)
        return time, seq

    def _backprop(self, node: Node, reward: float):
        while node is not None:
            node.visits += 1
            node.total_reward += reward
            node = node.parent
