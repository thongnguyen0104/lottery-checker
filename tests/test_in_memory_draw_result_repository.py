from datetime import date
import unittest

from lottery_checker.domain.entities import DrawResult
from lottery_checker.infrastructure.repositories.in_memory_draw_result_repository import (
    InMemoryDrawResultRepository,
)


class InMemoryRepositoryTests(unittest.TestCase):
    def test_raises_value_error_when_result_missing(self) -> None:
        repository = InMemoryDrawResultRepository(draw_results=[])

        with self.assertRaises(ValueError):
            repository.get_by_province_and_date("HCM", date(2026, 8, 23))


if __name__ == "__main__":
    unittest.main()
