"""Precise FSM simulator where every action (run/buy) is explicit.

No implicit "run until affordable". The caller controls every step.
Single step() function that advances state by one action.
"""
import sys
import os
import math
import random

# Add parent to path for imports
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from config import SimConfig, load_config
from state import GameState
from fsm import State


def clone_income(state: GameState, config: SimConfig, clone_start: float | None,
                 t_start: float, t_end: float) -> float:
    """Discrete clone income between t_start and t_end."""
    if state.clone_count <= 0 or clone_start is None or t_end <= t_start:
        return 0.0
    clone_dur = config.clone_course_duration
    producing_since = clone_start + clone_dur
    if t_end <= producing_since:
        return 0.0
    eff_start = max(t_start, producing_since)
    interval = clone_dur / state.clone_count
    clone_reward = math.ceil(state.reward_per_completion * config.clone_base_multiplier)
    completions = math.floor((t_end - producing_since) / interval) - \
                  math.floor((eff_start - producing_since) / interval)
    return completions * clone_reward


class SimState:
    """Full simulation state including FSM phase, current location, clone tracking."""
    __slots__ = ["game", "fsm", "location", "clone_start", "config"]

    def __init__(self, config: SimConfig):
        self.game = GameState(config=config)
        self.fsm = State.AT_ENTRANCE
        self.location = "entrance"
        self.clone_start = None
        self.config = config

    def clone(self) -> "SimState":
        s = SimState.__new__(SimState)
        s.game = self.game.clone()
        s.fsm = self.fsm
        s.location = self.location
        s.clone_start = self.clone_start
        s.config = self.config
        return s

    def _advance(self, duration: float):
        """Advance time, crediting clone income."""
        if duration <= 0:
            return
        ci = clone_income(self.game, self.config, self.clone_start,
                          self.game.time, self.game.time + duration)
        self.game.cash += ci
        self.game.time += duration

    def _move(self, to_loc: str):
        self._advance(self.config.travel_time(self.location, to_loc))
        self.location = to_loc

    @property
    def done(self) -> bool:
        return self.game.has_terminal

    def available_actions(self) -> list[str]:
        """What actions can be taken right now."""
        actions = ["run"]
        if self.fsm in (State.AT_EXIT, State.AT_BOX):
            for name in self.game.affordable_upgrades():
                actions.append(name)
        return actions


def step(state: SimState, action: str, rng: random.Random) -> None:
    """Execute one action, mutating state in place.

    Actions:
      "run" — return to entrance if needed, run the course
      <upgrade_name> — travel to that box and buy it
    """
    if action == "run":
        if state.location != "entrance":
            state._move("entrance")
        state.fsm = State.AT_ENTRANCE

        # Run the course (no travel, just sample run time)
        cfg = state.config
        if rng.random() < cfg.success_rate:
            rt = rng.choice(cfg.success_times)
            state._advance(rt)
            state.game.cash += state.game.reward_per_completion
        else:
            rt = rng.choice(cfg.failure_times)
            state._advance(rt)

        state.fsm = State.AT_EXIT
        state.location = "exit"

    else:
        # Buy action: travel to the named box
        if state.fsm == State.AT_ENTRANCE:
            return  # invalid: can't buy from entrance

        state._move(action)
        state.fsm = State.AT_BOX

        if state.game.can_afford(action):
            state.game.buy_upgrade(action)
            if state.clone_start is None and state.game.clone_count > 0:
                state.clone_start = state.game.time


def run_sequence(config: SimConfig, actions: list[str], rng: random.Random) -> SimState:
    """Run a full sequence of actions. Returns final state."""
    state = SimState(config)
    for action in actions:
        if state.done:
            break
        step(state, action, rng)
    return state


def run_sequence_mean(config: SimConfig, actions: list[str], n_sims: int = 1,
                       seed: int = 42) -> float:
    """Average completion time over n_sims runs."""
    total = 0.0
    for i in range(n_sims):
        rng = random.Random(seed + i)
        state = run_sequence(config, actions, rng)
        total += state.game.time
    return total / n_sims
