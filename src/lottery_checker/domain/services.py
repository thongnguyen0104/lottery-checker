from .entities import CheckResult, DrawResult, LotteryTicket


class TicketMatcher:
    """Domain service for ticket matching rules."""

    def match(self, ticket: LotteryTicket, result: DrawResult) -> CheckResult:
        matched = ticket.number == result.special_prize_number
        return CheckResult(
            matched=matched,
            ticket_number=ticket.number,
            winning_number=result.special_prize_number,
        )
