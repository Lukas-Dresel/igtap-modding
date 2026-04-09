"""Simulation engine driven by an explicit FSM.

The simulator walks through states, consulting the policy at decision
points (AT_EXIT, AT_BOX). Clone income accrues during every timed transition.
"""
import random
import math

from state import GameState
from config import SimConfig
from fsm import State, transition_time


class Simulator:
    def __init__(self, config: SimConfig, seed: int | None = None):
        self.config = config
        self.rng = random.Random(seed)

    def sample_player_run(self) -> tuple[float, bool]:
        if self.rng.random() < self.config.success_rate:
            return self.rng.choice(self.config.success_times), True
        else:
            return self.rng.choice(self.config.failure_times), False

    def advance_time(self, state: GameState, duration: float, clone_start_time: float | None):
        if duration <= 0:
            return
        if state.clone_count > 0 and clone_start_time is not None:
            state.cash += _clone_income_between(
                state, self.config, clone_start_time,
                state.time, state.time + duration)
        state.time += duration

    def run(self, policy, max_time: float = 100000.0) -> GameState:
        state = GameState(config=self.config)
        clone_start_time = None
        fsm_state = State.AT_ENTRANCE

        iters = 0
        while state.time < max_time and not state.has_wall_jump:
            iters += 1
            if iters > 100000:
                break

            if fsm_state == State.AT_ENTRANCE:
                # AT_ENTRANCE → RUNNING (instant)
                self.advance_time(state, transition_time(State.AT_ENTRANCE, State.RUNNING), clone_start_time)

                # RUNNING: simulate the course
                run_time, success = self.sample_player_run()
                self.advance_time(state, run_time, clone_start_time)
                if success:
                    state.cash += state.reward_per_completion

                # RUNNING → AT_EXIT (instant, run_time already accounted for)
                fsm_state = State.AT_EXIT

            elif fsm_state == State.AT_EXIT:
                # Decision point: go to box or go to entrance?
                action = policy.choose_action(state)

                if action.type == "buy" and state.can_afford(action.upgrade_name):
                    # AT_EXIT → AT_BOX
                    self.advance_time(state, transition_time(State.AT_EXIT, State.AT_BOX), clone_start_time)
                    state.buy_upgrade(action.upgrade_name)
                    if clone_start_time is None and state.clone_count > 0:
                        clone_start_time = state.time
                    fsm_state = State.AT_BOX
                else:
                    # AT_EXIT → AT_ENTRANCE
                    self.advance_time(state, transition_time(State.AT_EXIT, State.AT_ENTRANCE), clone_start_time)
                    fsm_state = State.AT_ENTRANCE

            elif fsm_state == State.AT_BOX:
                if state.has_wall_jump:
                    break

                # Decision point: buy another or go to entrance?
                action = policy.choose_action(state)

                if action.type == "buy" and state.can_afford(action.upgrade_name):
                    # AT_BOX → AT_BOX
                    self.advance_time(state, transition_time(State.AT_BOX, State.AT_BOX), clone_start_time)
                    state.buy_upgrade(action.upgrade_name)
                    if clone_start_time is None and state.clone_count > 0:
                        clone_start_time = state.time
                    # stay AT_BOX
                else:
                    # AT_BOX → AT_ENTRANCE
                    self.advance_time(state, transition_time(State.AT_BOX, State.AT_ENTRANCE), clone_start_time)
                    fsm_state = State.AT_ENTRANCE

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
