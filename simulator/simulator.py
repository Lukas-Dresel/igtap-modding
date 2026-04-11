"""Simulation engine driven by an explicit FSM.

The simulator walks through phases, consulting the policy at decision
points (AT_EXIT, AT_BOX). Clone income accrues during every timed transition.

Locations are strings: "entrance", "exit", or an upgrade name. Travel time
between any two locations comes from `config.travel_time(from, to)`.

Pass an observer to subscribe to lifecycle events.
"""
import random
import math

from state import GameState
from config import SimConfig


class SimObserver:
    """Subscribe to simulation lifecycle events. Override methods to listen.

    `clone_income` is the amount of clone income credited as part of the event.
    """

    def on_start(self, state: GameState) -> None:
        """Called once before simulation begins."""

    def on_transition(self, state: GameState, from_loc: str, to_loc: str,
                      duration: float, clone_income: float) -> None:
        """Called after each travel transition (location changed)."""

    def on_run_complete(self, state: GameState, run_time: float, success: bool,
                        player_reward: float, clone_income: float) -> None:
        """Called after a course run completes (the run time has been advanced)."""

    def on_buy(self, state: GameState, upgrade_name: str) -> None:
        """Called immediately after an upgrade is purchased."""

    def on_finish(self, state: GameState) -> None:
        """Called once after the simulation ends."""


class Simulator:
    def __init__(self, config: SimConfig, seed: int | None = None):
        self.config = config
        self.rng = random.Random(seed)

    def sample_player_run(self) -> tuple[float, bool]:
        if self.rng.random() < self.config.success_rate:
            return self.rng.choice(self.config.success_times), True
        else:
            return self.rng.choice(self.config.failure_times), False

    def _credit_clone_income(self, state: GameState, duration: float,
                             clone_start_time: float | None) -> float:
        """Add clone income for the next `duration` seconds. Returns income added."""
        if duration <= 0 or state.clone_count <= 0 or clone_start_time is None:
            return 0.0
        ci = _clone_income_between(state, self.config, clone_start_time,
                                    state.time, state.time + duration)
        state.cash += ci
        return ci

    def _move(self, state: GameState, from_loc: str, to_loc: str,
              clone_start_time: float | None, observer: SimObserver | None) -> None:
        duration = self.config.travel_time(from_loc, to_loc)
        ci = self._credit_clone_income(state, duration, clone_start_time)
        state.time += duration
        if observer:
            observer.on_transition(state, from_loc, to_loc, duration, ci)

    def run(self, policy, max_time: float = 100000.0,
            observer: SimObserver | None = None) -> GameState:
        state = GameState(config=self.config)
        clone_start_time = None
        location = "entrance"

        if observer:
            observer.on_start(state)

        iters = 0
        while state.time < max_time and not state.has_terminal:
            iters += 1
            if iters > 100000:
                break

            if location == "entrance":
                # Run the course (no travel — running starts immediately)
                run_time, success = self.sample_player_run()
                ci = self._credit_clone_income(state, run_time, clone_start_time)
                state.time += run_time
                reward = 0.0
                if success:
                    reward = state.reward_per_completion
                    state.cash += reward
                if observer:
                    observer.on_run_complete(state, run_time, success, reward, ci)
                location = "exit"
                continue

            # At exit or at a specific box: ask the policy what to do
            action = policy.choose_action(state)

            if action.type == "buy" and state.can_afford(action.upgrade_name):
                next_loc = action.upgrade_name
                self._move(state, location, next_loc, clone_start_time, observer)
                state.buy_upgrade(action.upgrade_name)
                if observer:
                    observer.on_buy(state, action.upgrade_name)
                if clone_start_time is None and state.clone_count > 0:
                    clone_start_time = state.time
                location = next_loc
                if state.has_terminal:
                    break
            else:
                # Don't buy — head back to entrance and run again
                self._move(state, location, "entrance", clone_start_time, observer)
                location = "entrance"

        if observer:
            observer.on_finish(state)
        return state

    def run_batch(self, policy, n: int = 1000, max_time: float = 100000.0) -> list[float]:
        return [self.run(policy, max_time).time for _ in range(n)]


def _clone_income_between(
    state: GameState, config: SimConfig,
    clone_start_time: float, t_start: float, t_end: float,
) -> float:
    if state.clone_count <= 0:
        return 0.0

    clone_dur = config.clone_course_duration
    producing_since = clone_start_time + clone_dur

    if t_end <= producing_since:
        return 0.0

    eff_start = max(t_start, producing_since)
    eff_end = t_end

    interval = clone_dur / state.clone_count
    completions = math.floor((eff_end - producing_since) / interval) - \
                  math.floor((eff_start - producing_since) / interval)

    return completions * state.clone_reward_per_completion
