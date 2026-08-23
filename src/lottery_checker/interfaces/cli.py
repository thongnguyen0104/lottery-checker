import argparse
from datetime import date

from lottery_checker.application.use_cases.check_ticket import (
    CheckTicketCommand,
    CheckTicketUseCase,
)
from lottery_checker.domain.entities import DrawResult
from lottery_checker.infrastructure.repositories.in_memory_draw_result_repository import (
    InMemoryDrawResultRepository,
)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Lottery checker")
    parser.add_argument("province")
    parser.add_argument("draw_date", help="YYYY-MM-DD")
    parser.add_argument("ticket_number")
    parser.add_argument("winning_number", help="Special prize winning number")
    return parser


def main() -> int:
    args = build_parser().parse_args()
    draw_date = date.fromisoformat(args.draw_date)

    repository = InMemoryDrawResultRepository(
        draw_results=[
            DrawResult(
                province=args.province,
                draw_date=draw_date,
                special_prize_number=args.winning_number,
            )
        ]
    )
    use_case = CheckTicketUseCase(repository=repository)
    result = use_case.execute(
        CheckTicketCommand(
            province=args.province,
            draw_date=draw_date,
            ticket_number=args.ticket_number,
        )
    )

    print("MATCH" if result.matched else "NOT_MATCH")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
