"""Finite State Machine for the game simulation.

States:
  RUNNING      Player is running the course
  AT_EXIT      Player just finished, standing at exit gate
  AT_BOX       Player is at an upgrade box (just bought or about to)
  AT_ENTRANCE  Player is at the level entrance, about to start

Transitions (with measured durations):
  RUNNING     → AT_EXIT:      run_time (2.2s success / 1.0s fail)
  AT_EXIT     → AT_ENTRANCE:  0.75s (no buy, go straight back)
  AT_EXIT     → AT_BOX:       2.0s (walk to upgrade box)
  AT_BOX      → AT_BOX:       0.75s (buy another at the box)
  AT_BOX      → AT_ENTRANCE:  2.5s (walk from box to entrance)
  AT_ENTRANCE → RUNNING:      0s (start the run immediately)

Policy is consulted at AT_EXIT and AT_BOX.
"""
from enum import Enum, auto


class State(Enum):
    RUNNING = auto()
    AT_EXIT = auto()
    AT_BOX = auto()
    AT_ENTRANCE = auto()
    DONE = auto()


# (from_state, to_state) -> duration in seconds
TRANSITIONS: dict[tuple[State, State], float] = {
    # AT_EXIT choices
    (State.AT_EXIT, State.AT_ENTRANCE): 0.75,
    (State.AT_EXIT, State.AT_BOX): 2.0,

    # AT_BOX choices
    (State.AT_BOX, State.AT_BOX): 0.75,
    (State.AT_BOX, State.AT_ENTRANCE): 2.5,

    # AT_ENTRANCE → RUNNING is instant
    (State.AT_ENTRANCE, State.RUNNING): 0.0,
}


def transition_time(from_state: State, to_state: State) -> float:
    key = (from_state, to_state)
    if key not in TRANSITIONS:
        raise ValueError(f"Invalid transition: {from_state.name} → {to_state.name}")
    return TRANSITIONS[key]
