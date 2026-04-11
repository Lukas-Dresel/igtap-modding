"""Finite State Machine for the game simulation.

The simulator tracks a `location: str` (one of "entrance", "exit", or an upgrade
name) plus a high-level FSM phase. Travel time between any two locations comes
from `config.travel_time(from_loc, to_loc)`.

Phases:
  AT_ENTRANCE  Player is at the start gate, about to run.
  RUNNING      Player is running the course.
  AT_EXIT      Player just finished a run, at the end gate.
  AT_BOX       Player is standing at an upgrade box (location field says which).
  DONE         Terminal upgrade acquired.

Decisions are made at AT_EXIT and AT_BOX.
"""
from enum import Enum, auto


class State(Enum):
    RUNNING = auto()
    AT_EXIT = auto()
    AT_BOX = auto()
    AT_ENTRANCE = auto()
    DONE = auto()
