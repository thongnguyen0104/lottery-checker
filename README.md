# lottery-checker

Project được tổ chức theo **Clean Architecture** cho đúng **1 tính năng duy nhất**: dò vé số theo giải đặc biệt.

## Cấu trúc

- `src/lottery_checker/domain`: entity + rule nghiệp vụ match vé số
- `src/lottery_checker/application`: use case + port trừu tượng
- `src/lottery_checker/infrastructure`: adapter triển khai port (in-memory)
- `src/lottery_checker/interfaces`: CLI adapter
- `tests`: test tập trung cho use case và repository adapter

## Chạy test

```bash
PYTHONPATH=src python -m unittest discover -s tests -p "test_*.py"
```

## Chạy thử CLI

```bash
PYTHONPATH=src python -m lottery_checker.interfaces.cli HCM 2026-08-23 123456 123456
```

Kết quả in ra:
- `MATCH` nếu trúng giải đặc biệt
- `NOT_MATCH` nếu không trúng
