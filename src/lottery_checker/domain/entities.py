from dataclasses import dataclass
from datetime import date


@dataclass(frozen=True)
class LotteryTicket:
    number: str


@dataclass(frozen=True)
class DrawResult:
    province: str
    draw_date: date
    special_prize_number: str


@dataclass(frozen=True)
class CheckResult:
    matched: bool
    ticket_number: str
    winning_number: str
