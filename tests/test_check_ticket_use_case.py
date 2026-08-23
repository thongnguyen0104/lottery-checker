from datetime import date
import unittest

from lottery_checker.application.use_cases.check_ticket import (
    CheckTicketCommand,
    CheckTicketUseCase,
)
from lottery_checker.domain.entities import DrawResult
from lottery_checker.infrastructure.repositories.in_memory_draw_result_repository import (
    InMemoryDrawResultRepository,
)


class CheckTicketUseCaseTests(unittest.TestCase):
    def setUp(self) -> None:
        self.draw_date = date(2026, 8, 23)
        self.repository = InMemoryDrawResultRepository(
            draw_results=[
                DrawResult(
                    province="HCM",
                    draw_date=self.draw_date,
                    special_prize_number="123456",
                )
            ]
        )
        self.use_case = CheckTicketUseCase(repository=self.repository)

    def test_returns_match_when_ticket_equals_special_prize(self) -> None:
        result = self.use_case.execute(
            CheckTicketCommand(
                province="HCM",
                draw_date=self.draw_date,
                ticket_number="123456",
            )
        )

        self.assertTrue(result.matched)

    def test_returns_not_match_when_ticket_differs(self) -> None:
        result = self.use_case.execute(
            CheckTicketCommand(
                province="HCM",
                draw_date=self.draw_date,
                ticket_number="000000",
            )
        )

        self.assertFalse(result.matched)


if __name__ == "__main__":
    unittest.main()
