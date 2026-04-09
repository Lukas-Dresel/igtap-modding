"""Event types and priority queue for the event-driven simulation."""
from dataclasses import dataclass, field
from heapq import heappush, heappop
from typing import Any


@dataclass(order=True)
class Event:
    time: float
    event_type: str = field(compare=False)
    data: Any = field(default=None, compare=False)


# Event type constants
PLAYER_COMPLETES = "player_completes"
PLAYER_FAILS = "player_fails"
CLONE_COMPLETES = "clone_completes"


class EventQueue:
    def __init__(self):
        self._heap: list[Event] = []

    def push(self, time: float, event_type: str, data: Any = None):
        heappush(self._heap, Event(time, event_type, data))

    def pop(self) -> Event:
        return heappop(self._heap)

    def peek(self) -> Event | None:
        return self._heap[0] if self._heap else None

    def __len__(self) -> int:
        return len(self._heap)

    def __bool__(self) -> bool:
        return len(self._heap) > 0

    def clear(self):
        self._heap.clear()

    def clone(self) -> "EventQueue":
        eq = EventQueue()
        eq._heap = list(self._heap)
        return eq
