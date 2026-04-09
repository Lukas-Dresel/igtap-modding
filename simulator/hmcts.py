"""Hierarchical MCTS for upgrade sequence optimization.

Two levels:
  High level: decides BLOCKS — "buy N cashPerLoop" or "buy M cloneCount"
  Low level: evaluates a full block sequence via simulation

Each high-level action is a (type, count) pair like ("cashPerLoop", 3).
The sequence is a list of blocks. wallJump is implicit at the end.

This compresses the search space dramatically:
  - Regular MCTS: each node is one purchase (branching ~2-3, depth ~20)
  - Hierarchical: each node is a block (branching ~20, depth ~4-6)
"""
import math
import random
from dataclasses import dataclass, field
from itertools import groupby

from config import SimConfig
from simulator import Simulator
from policy import FixedSequence


@dataclass
class HNode:
    blocks: list[tuple[str, int]]  # [(type, count), ...]
    parent: "HNode | None" = None
    children: dict[str, "HNode"] = field(default_factory=dict)
    visits: int = 0
    total_reward: float = 0.0
    untried: list[tuple[str, int]] | None = None

    def sequence(self) -> list[str]:
        """Flatten blocks into a purchase sequence."""
        seq = []
        for typ, count in self.blocks:
            seq.extend([typ] * count)
        return seq

    def total_of(self, typ: str) -> int:
        return sum(c for t, c in self.blocks if t == typ)

    def ucb1(self, c: float = 1.414) -> float:
        if self.visits == 0:
            return float("inf")
        exploit = self.total_reward / self.visits
        explore = c * math.sqrt(math.log(self.parent.visits) / self.visits)
        return exploit + explore


def get_block_actions(node: HNode, config: SimConfig, max_block: int = 10) -> list[tuple[str, int]]:
    """Possible next blocks: (type, 1..max_block) for each type with remaining cap."""
    actions = []
    cash_so_far = node.total_of("cashPerLoop")
    clone_so_far = node.total_of("cloneCount")
    cash_cap = config.upgrades["cashPerLoop"].cap
    clone_cap = config.upgrades["cloneCount"].cap

    cash_remaining = cash_cap - cash_so_far
    clone_remaining = clone_cap - clone_so_far

    for n in range(1, min(max_block, cash_remaining) + 1):
        actions.append(("cashPerLoop", n))
    for n in range(1, min(max_block, clone_remaining) + 1):
        actions.append(("cloneCount", n))

    return actions


class HierarchicalMCTS:
    def __init__(self, config: SimConfig, seed: int = 42, max_block: int = 10):
        self.config = config
        self.rng = random.Random(seed)
        self.sim = Simulator(config, seed=seed)
        self.max_block = max_block

    def evaluate(self, sequence: list[str]) -> float:
        policy = FixedSequence(sequence + ["wallJump"])
        state = self.sim.run(policy)
        return state.time

    def search(self, iterations: int = 50000, verbose: bool = True) -> list[str]:
        root = HNode(blocks=[])

        best_time = float("inf")
        best_seq = None

        for i in range(iterations):
            node = self._select(root)
            node = self._expand(node)
            time, seq = self._rollout(node)
            self._backprop(node, -time)

            if time < best_time:
                best_time = time
                best_seq = seq
                if verbose and (i < 100 or i % 2000 == 0):
                    parts = []
                    for k, g in groupby(best_seq):
                        n = len(list(g))
                        parts.append(f"{n}x{k}" if n > 1 else k)
                    print(f"  iter {i}: best {best_time:.1f}s ({len(best_seq)} buys) {','.join(parts)}")

        if verbose:
            print(f"  Final best: {best_time:.1f}s")
        return best_seq

    def _select(self, node: HNode) -> HNode:
        while True:
            if node.untried is None:
                node.untried = get_block_actions(node, self.config, self.max_block)
            if node.untried:
                return node
            if not node.children:
                return node
            node = max(node.children.values(), key=lambda n: n.ucb1())

    def _expand(self, node: HNode) -> HNode:
        if node.untried is None:
            node.untried = get_block_actions(node, self.config, self.max_block)
        if not node.untried:
            return node

        action = node.untried.pop()
        child = HNode(blocks=node.blocks + [action], parent=node)
        key = f"{action[0]}:{action[1]}"
        node.children[key] = child
        return child

    def _rollout(self, node: HNode) -> tuple[float, list[str]]:
        """Random block completion + evaluate."""
        blocks = list(node.blocks)

        # Randomly add more blocks with increasing stop probability
        for _ in range(20):
            actions = get_block_actions(
                HNode(blocks=blocks), self.config, self.max_block)
            if not actions:
                break
            stop_prob = sum(c for _, c in blocks) / 25.0
            if self.rng.random() < stop_prob:
                break
            blocks.append(self.rng.choice(actions))

        seq = []
        for typ, count in blocks:
            seq.extend([typ] * count)

        time = self.evaluate(seq)
        return time, seq

    def _backprop(self, node: HNode, reward: float):
        while node is not None:
            node.visits += 1
            node.total_reward += reward
            node = node.parent
