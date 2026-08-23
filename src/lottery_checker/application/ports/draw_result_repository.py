from abc import ABC, abstractmethod
from datetime import date

from lottery_checker.domain.entities import DrawResult


class DrawResultRepository(ABC):
    @abstractmethod
    def get_by_province_and_date(self, province: str, draw_date: date) -> DrawResult:
        raise NotImplementedError
