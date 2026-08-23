from dataclasses import dataclass
from datetime import date

from lottery_checker.application.ports.draw_result_repository import DrawResultRepository
from lottery_checker.domain.entities import CheckResult, LotteryTicket
from lottery_checker.domain.services import TicketMatcher


@dataclass(frozen=True)
class CheckTicketCommand:
    province: str
    draw_date: date
    ticket_number: str


class CheckTicketUseCase:
    def __init__(self, repository: DrawResultRepository, matcher: TicketMatcher | None = None):
        self._repository = repository
        self._matcher = matcher or TicketMatcher()

    def execute(self, command: CheckTicketCommand) -> CheckResult:
        draw_result = self._repository.get_by_province_and_date(
            province=command.province,
            draw_date=command.draw_date,
        )
        ticket = LotteryTicket(number=command.ticket_number)
        return self._matcher.match(ticket=ticket, result=draw_result)
