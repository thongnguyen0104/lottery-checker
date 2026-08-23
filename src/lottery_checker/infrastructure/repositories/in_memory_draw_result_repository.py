from datetime import date

from lottery_checker.application.ports.draw_result_repository import DrawResultRepository
from lottery_checker.domain.entities import DrawResult


class InMemoryDrawResultRepository(DrawResultRepository):
    def __init__(self, draw_results: list[DrawResult]):
        self._data = {
            (result.province, result.draw_date): result
            for result in draw_results
        }

    def get_by_province_and_date(self, province: str, draw_date: date) -> DrawResult:
        try:
            return self._data[(province, draw_date)]
        except KeyError as error:
            raise ValueError("Draw result not found") from error
