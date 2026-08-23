# Clean Architecture cho Dò Vé Số

> **Mục tiêu**: thiết kế cấu trúc project dò vé số theo Clean Architecture, **tập trung duy nhất 1 tính năng** nhưng làm cho **gọn gàng, dễ maintain, dễ test, dễ thay tech** sau này. Không mở rộng sang tính năng khác.

## 1. Tại sao Clean Architecture — và khi nào KHÔNG nên

Hiện tại project gom hết Models, Services, Controllers, DbContext vào 1 project duy nhất (`LotteryChecker.Api`). Cách này nhanh cho prototype nhưng có 3 vấn đề khi lớn lên:

1. **Mọi thứ phụ thuộc vào ASP.NET Core và EF Core**. Muốn viết unit test cho `LotteryMatcher` cũng phải kéo cả `Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.AspNetCore.*` vào test project — rất nặng.
2. **Đổi tech khó**. Muốn thay SQLite → PostgreSQL, hoặc thay Tesseract → Google Vision API, phải sửa code rải rác.
3. **Logic nghiệp vụ trộn với hạ tầng**. `LotteryMatcher` nhận `AppDbContext` trực tiếp — nếu test muốn fake data, phải dùng EF InMemory provider (chậm, behavior khác production).

Clean Architecture (CA) giải quyết bằng cách chia thành **4 layer**, với **quy tắc dependency luôn đi từ ngoài vào trong**:

```
        ┌─────────────────────────────────────┐
        │   WebApi (controllers, middleware)  │
        │  ┌──────────────────────────────┐   │
        │  │ Infrastructure (EF, OCR,     │   │
        │  │ scraper, file system)        │   │
        │  │  ┌─────────────────────────┐ │   │
        │  │  │ Application (use cases, │ │   │
        │  │  │ interfaces, DTOs)       │ │   │
        │  │  │  ┌──────────────────┐   │ │   │
        │  │  │  │ Domain (entities,│   │ │   │
        │  │  │  │ value objects)   │   │ │   │
        │  │  │  └──────────────────┘   │ │   │
        │  │  └─────────────────────────┘ │   │
        │  └──────────────────────────────┘   │
        └─────────────────────────────────────┘
```

Tinh thần: **Domain không biết bất cứ gì về EF, ASP.NET, OCR, hay bất kỳ thư viện ngoài nào.** Application chỉ biết Domain. Infrastructure implement các interface mà Application định nghĩa. WebApi chỉ là 1 cách "trình bày" — có thể thay bằng gRPC, Console app, hay Worker mà không phải sửa Application.

### Khi nào KHÔNG nên dùng Clean Architecture?

- **Project < 20 endpoint, 1 developer, deadline 2 tuần**: overhead không đáng. Cứ làm 1 project flat. Bạn có thể refactor sau khi product validated.
- **Học viên mới làm quen .NET**: học CA trước khi nắm vững DI, async/await, EF Core là quá tải.
- **Prototype, demo, MVP**: tốc độ ship quan trọng hơn cấu trúc.

### Khi nào NÊN?
- Project sẽ tồn tại > 1 năm, có maintain dài hạn.
- Team ≥ 2 người.
- Cần unit test logic nghiệp vụ riêng, không chạm DB/HTTP.
- Có khả năng đổi tech: DB, OCR provider, hosting...
- **Project portfolio để xin việc** ← lý do hợp lý cho project này.

Vé số app của bạn nằm ở vùng "có thể không cần" nhưng nếu mục tiêu có **portfolio để show cho nhà tuyển dụng**, thì refactor sang CA là đáng — vì 90% tin tuyển dụng .NET enterprise đều yêu cầu kinh nghiệm CA.

> ⚠️ **Lưu ý về MediatR**: nhiều tutorial Clean Architecture trên mạng (đặc biệt template `Ardalis.CleanArchitecture` và `JasonTaylorDev/CleanArchitecture`) dùng MediatR cho CQRS. Tuy nhiên **từ tháng 4/2025 MediatR đã chuyển sang license thương mại**. Project mới năm 2025+ nên dùng **manual handler pattern** (interface đơn giản, không lệ thuộc thư viện) — xu hướng mới của cộng đồng .NET. Hướng dẫn này đi theo cách đó.

---

## 2. Cấu trúc solution mới

```
lottery-checker/
├── backend/
│   ├── LotteryChecker.sln
│   ├── src/
│   │   ├── LotteryChecker.Domain/              ← KHÔNG ref project nào khác
│   │   ├── LotteryChecker.Application/         ← ref Domain
│   │   ├── LotteryChecker.Infrastructure/      ← ref Domain + Application
│   │   └── LotteryChecker.WebApi/              ← ref Application + Infrastructure
│   └── tests/
│       ├── LotteryChecker.Domain.UnitTests/
│       ├── LotteryChecker.Application.UnitTests/
│       ├── LotteryChecker.Infrastructure.IntegrationTests/
│       └── LotteryChecker.WebApi.FunctionalTests/
└── frontend/
    └── ... (giữ nguyên)
```

**Quy tắc dependency** (kiểm tra bằng `dotnet list reference`):

| Project | Được phép ref | KHÔNG được ref |
|---|---|---|
| `Domain` | (không gì) | tất cả |
| `Application` | `Domain` | `Infrastructure`, `WebApi`, EF, ASP.NET |
| `Infrastructure` | `Domain`, `Application` | `WebApi` |
| `WebApi` | `Application`, `Infrastructure` | (không hạn chế) |

Quy tắc này nghiêm ngặt nhưng đơn giản — nếu cố add reference sai chiều, build sẽ fail.

---

## 3. Project Domain — lõi nghiệp vụ thuần

**Mục đích**: chứa **entities**, **value objects**, **domain exceptions**, **domain events**. KHÔNG biết đến EF, HTTP, OCR, JSON, hay bất kỳ thứ gì cụ thể.

### 3.1 csproj
File `LotteryChecker.Domain.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```
Chú ý: **không có** `PackageReference`. Đây là layer thuần POCO, không thư viện ngoài.

### 3.2 Cấu trúc thư mục
```
LotteryChecker.Domain/
├── Entities/
│   ├── LotteryResult.cs
│   └── ScanHistory.cs
├── ValueObjects/
│   ├── TicketNumber.cs
│   ├── Province.cs
│   └── PrizeAmount.cs
├── Enums/
│   ├── Region.cs
│   └── PrizeTier.cs
├── Exceptions/
│   ├── DomainException.cs
│   └── InvalidTicketNumberException.cs
└── Events/
    └── TicketWonEvent.cs
```

### 3.3 Code mẫu

**`ValueObjects/TicketNumber.cs`** — value object validate khi tạo:
```csharp
namespace LotteryChecker.Domain.ValueObjects;

public readonly record struct TicketNumber
{
    public string Value { get; }

    public TicketNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidTicketNumberException("Số vé không được rỗng");
        if (value.Length != 6)
            throw new InvalidTicketNumberException($"Số vé phải đúng 6 chữ số, hiện có {value.Length}");
        if (!value.All(char.IsDigit))
            throw new InvalidTicketNumberException("Số vé chỉ chứa chữ số");
        Value = value;
    }

    public string Last(int n) => Value[^Math.Min(n, Value.Length)..];
    public override string ToString() => Value;

    // Cho phép implicit convert sang string khi ghi log, debug
    public static implicit operator string(TicketNumber t) => t.Value;
}
```

**`Enums/PrizeTier.cs`**:
```csharp
namespace LotteryChecker.Domain.Enums;

public enum PrizeTier
{
    DacBiet = 0,      // Đặc biệt
    Nhat = 1,
    Nhi = 2,
    Ba = 3,
    Tu = 4,
    Nam = 5,
    Sau = 6,
    Bay = 7,
    Tam = 8
}

public static class PrizeTierExtensions
{
    public static int CompareLength(this PrizeTier tier) => tier switch
    {
        PrizeTier.DacBiet => 6,
        PrizeTier.Nhat or PrizeTier.Nhi or PrizeTier.Ba or PrizeTier.Tu => 5,
        PrizeTier.Nam or PrizeTier.Sau => 4,
        PrizeTier.Bay => 3,
        PrizeTier.Tam => 2,
        _ => 6
    };

    public static decimal DefaultPrize(this PrizeTier tier) => tier switch
    {
        PrizeTier.DacBiet => 2_000_000_000m,
        PrizeTier.Nhat => 30_000_000m,
        PrizeTier.Nhi => 15_000_000m,
        PrizeTier.Ba => 10_000_000m,
        PrizeTier.Tu => 3_000_000m,
        PrizeTier.Nam => 1_000_000m,
        PrizeTier.Sau => 400_000m,
        PrizeTier.Bay => 200_000m,
        PrizeTier.Tam => 100_000m,
        _ => 0m
    };
}
```

**`Entities/LotteryResult.cs`** — entity thuần, không attribute EF:
```csharp
using LotteryChecker.Domain.Enums;

namespace LotteryChecker.Domain.Entities;

public class LotteryResult
{
    public int Id { get; private set; }
    public DateOnly DrawDate { get; private set; }
    public Region Region { get; private set; }
    public string Province { get; private set; } = "";
    public PrizeTier PrizeTier { get; private set; }
    public string Number { get; private set; } = "";
    public DateTime CreatedAt { get; private set; }

    // EF Core cần ctor không tham số (private)
    private LotteryResult() { }

    // Factory method — ép tạo entity qua đây để validate
    public static LotteryResult Create(DateOnly drawDate, Region region,
        string province, PrizeTier tier, string number)
    {
        if (string.IsNullOrWhiteSpace(province))
            throw new ArgumentException("Province required", nameof(province));
        if (string.IsNullOrWhiteSpace(number))
            throw new ArgumentException("Number required", nameof(number));

        return new LotteryResult
        {
            DrawDate = drawDate,
            Region = region,
            Province = province,
            PrizeTier = tier,
            Number = number,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// So sánh xem vé có trúng giải này không.
    /// Logic thuần Domain — không chạm DB.
    /// </summary>
    public bool Matches(string ticketNumber)
    {
        if (ticketNumber.Length < Number.Length) return false;
        var compareLen = PrizeTier.CompareLength();
        var ticketTail = ticketNumber[^compareLen..];
        var numberTail = Number[^Math.Min(compareLen, Number.Length)..];
        return ticketTail == numberTail;
    }
}
```

**`Exceptions/DomainException.cs`**:
```csharp
namespace LotteryChecker.Domain.Exceptions;

public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message) { }
}

public class InvalidTicketNumberException : DomainException
{
    public InvalidTicketNumberException(string message) : base(message) { }
}
```

---

## 4. Project Application — use cases và interfaces (ports)

**Mục đích**: định nghĩa các **use case** ("dò 1 vé", "scan ảnh", "lấy kết quả ngày X"), và **interface** mô tả cái gì Infrastructure phải cung cấp (repository, OCR, scraper). KHÔNG biết tech cụ thể nào implement.

### 4.1 csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\LotteryChecker.Domain\LotteryChecker.Domain.csproj" />
    <!-- Chỉ thêm thư viện độc lập với hạ tầng -->
    <PackageReference Include="FluentValidation" Version="11.10.0" />
  </ItemGroup>
</Project>
```

> Lưu ý: KHÔNG add `Microsoft.EntityFrameworkCore.*` hay `Microsoft.AspNetCore.*` ở đây. FluentValidation là OK vì nó độc lập với hạ tầng.

### 4.2 Cấu trúc thư mục
```
LotteryChecker.Application/
├── Abstractions/
│   ├── Messaging/
│   │   ├── ICommand.cs
│   │   ├── IQuery.cs
│   │   ├── ICommandHandler.cs
│   │   └── IQueryHandler.cs
│   ├── Persistence/
│   │   ├── ILotteryResultRepository.cs
│   │   └── IUnitOfWork.cs
│   ├── Ocr/
│   │   ├── IOcrService.cs
│   │   └── IImagePreprocessor.cs
│   └── Scraping/
│       └── IResultScraper.cs
├── Common/
│   ├── Result.cs
│   └── Error.cs
├── Features/
│   ├── ScanTicket/
│   │   ├── ScanTicketCommand.cs
│   │   ├── ScanTicketHandler.cs
│   │   └── ScanTicketResult.cs
│   ├── CheckTicket/
│   │   ├── CheckTicketCommand.cs
│   │   ├── CheckTicketHandler.cs
│   │   ├── CheckTicketValidator.cs
│   │   └── CheckTicketResult.cs
│   └── FetchDailyResults/
│       ├── FetchDailyResultsCommand.cs
│       └── FetchDailyResultsHandler.cs
└── DependencyInjection.cs
```

> **Tại sao tổ chức theo Feature** thay vì theo loại (Commands/, Queries/, Validators/...)? Khi project lớn, đi tìm 1 file của feature "ScanTicket" phải nhảy qua nhiều folder. Gom theo feature, mọi thứ liên quan đến "scan ticket" nằm trong 1 thư mục — dễ nắm, dễ delete cả feature khi cần. Đây là phong cách **Vertical Slice** lai với Clean Architecture, đang là xu hướng .NET 2024+.

### 4.3 Code mẫu — Messaging abstractions (manual mediator)

**`Abstractions/Messaging/ICommand.cs`**:
```csharp
namespace LotteryChecker.Application.Abstractions.Messaging;

// Command: thay đổi state
public interface ICommand { }
public interface ICommand<TResponse> { }

// Query: chỉ đọc
public interface IQuery<TResponse> { }
```

**`Abstractions/Messaging/ICommandHandler.cs`**:
```csharp
using LotteryChecker.Application.Common;

namespace LotteryChecker.Application.Abstractions.Messaging;

public interface ICommandHandler<TCommand>
    where TCommand : ICommand
{
    Task<Result> Handle(TCommand command, CancellationToken ct);
}

public interface ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> Handle(TCommand command, CancellationToken ct);
}

public interface IQueryHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    Task<Result<TResponse>> Handle(TQuery query, CancellationToken ct);
}
```

### 4.4 Result pattern (thay throw exception)

**`Common/Error.cs`**:
```csharp
namespace LotteryChecker.Application.Common;

public sealed record Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);
    public static readonly Error NullValue = new("Error.NullValue", "Null value provided");

    public static Error Validation(string code, string msg) => new($"Validation.{code}", msg);
    public static Error NotFound(string code, string msg) => new($"NotFound.{code}", msg);
    public static Error Conflict(string code, string msg) => new($"Conflict.{code}", msg);
}
```

**`Common/Result.cs`** — handle thành công/thất bại không cần try-catch:
```csharp
namespace LotteryChecker.Application.Common;

public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None) throw new InvalidOperationException();
        if (!isSuccess && error == Error.None) throw new InvalidOperationException();
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) => new(false, error);
    public static Result<T> Success<T>(T value) => new(value, true, Error.None);
    public static Result<T> Failure<T>(Error error) => new(default!, false, error);
}

public class Result<T> : Result
{
    private readonly T _value;
    internal Result(T value, bool isSuccess, Error error) : base(isSuccess, error) => _value = value;

    public T Value => IsSuccess
        ? _value
        : throw new InvalidOperationException("Cannot access value of failed result");
}
```

### 4.5 Interfaces (ports) — Application định nghĩa, Infrastructure implement

**`Abstractions/Persistence/ILotteryResultRepository.cs`**:
```csharp
using LotteryChecker.Domain.Entities;

namespace LotteryChecker.Application.Abstractions.Persistence;

public interface ILotteryResultRepository
{
    Task<IReadOnlyList<LotteryResult>> GetByDateAndProvinceAsync(
        DateOnly drawDate, string province, CancellationToken ct);

    Task AddRangeAsync(IEnumerable<LotteryResult> results, CancellationToken ct);

    Task<bool> ExistsForDateAndProvinceAsync(
        DateOnly drawDate, string province, CancellationToken ct);
}
```

**`Abstractions/Persistence/IUnitOfWork.cs`** — commit transaction:
```csharp
namespace LotteryChecker.Application.Abstractions.Persistence;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct);
}
```

**`Abstractions/Ocr/IOcrService.cs`**:
```csharp
namespace LotteryChecker.Application.Abstractions.Ocr;

public interface IOcrService
{
    Task<OcrExtractedInfo> ExtractAsync(byte[] imageBytes, CancellationToken ct);
}

public sealed record OcrExtractedInfo(
    string? TicketNumber,
    DateOnly? DrawDate,
    string? Province,
    double Confidence,
    string RawText);
```

**`Abstractions/Ocr/IImagePreprocessor.cs`**:
```csharp
namespace LotteryChecker.Application.Abstractions.Ocr;

public interface IImagePreprocessor
{
    Task<byte[]> PreprocessAsync(Stream input, CancellationToken ct);
}
```

**`Abstractions/Scraping/IResultScraper.cs`**:
```csharp
using LotteryChecker.Domain.Entities;

namespace LotteryChecker.Application.Abstractions.Scraping;

public interface IResultScraper
{
    Task<IReadOnlyList<LotteryResult>> FetchAsync(DateOnly date, CancellationToken ct);
}
```

### 4.6 Use case: ScanTicket (chỉ OCR, không dò)

**`Features/ScanTicket/ScanTicketCommand.cs`**:
```csharp
using LotteryChecker.Application.Abstractions.Messaging;

namespace LotteryChecker.Application.Features.ScanTicket;

public sealed record ScanTicketCommand(Stream Image) : ICommand<ScanTicketResult>;
```

**`Features/ScanTicket/ScanTicketResult.cs`**:
```csharp
namespace LotteryChecker.Application.Features.ScanTicket;

public sealed record ScanTicketResult(
    string? TicketNumber,
    DateOnly? DrawDate,
    string? Province,
    double Confidence,
    bool LowConfidence,
    string? Warning);
```

**`Features/ScanTicket/ScanTicketHandler.cs`**:
```csharp
using LotteryChecker.Application.Abstractions.Messaging;
using LotteryChecker.Application.Abstractions.Ocr;
using LotteryChecker.Application.Common;

namespace LotteryChecker.Application.Features.ScanTicket;

public sealed class ScanTicketHandler : ICommandHandler<ScanTicketCommand, ScanTicketResult>
{
    private readonly IImagePreprocessor _preprocessor;
    private readonly IOcrService _ocr;

    public ScanTicketHandler(IImagePreprocessor preprocessor, IOcrService ocr)
    {
        _preprocessor = preprocessor;
        _ocr = ocr;
    }

    public async Task<Result<ScanTicketResult>> Handle(
        ScanTicketCommand command, CancellationToken ct)
    {
        var processed = await _preprocessor.PreprocessAsync(command.Image, ct);
        var info = await _ocr.ExtractAsync(processed, ct);

        var missing = new List<string>();
        if (info.TicketNumber is null) missing.Add("số vé");
        if (info.DrawDate is null) missing.Add("ngày mở thưởng");
        if (info.Province is null) missing.Add("đài");

        var warning = missing.Count > 0
            ? $"Không tự đọc được: {string.Join(", ", missing)}. Vui lòng kiểm tra/điền tay."
            : null;

        return Result.Success(new ScanTicketResult(
            info.TicketNumber, info.DrawDate, info.Province,
            info.Confidence, info.Confidence < 0.55, warning));
    }
}
```

### 4.7 Use case: CheckTicket (dò vé với info đã xác nhận)

**`Features/CheckTicket/CheckTicketCommand.cs`**:
```csharp
using LotteryChecker.Application.Abstractions.Messaging;

namespace LotteryChecker.Application.Features.CheckTicket;

public sealed record CheckTicketCommand(
    string TicketNumber,
    DateOnly DrawDate,
    string Province) : ICommand<CheckTicketResult>;
```

**`Features/CheckTicket/CheckTicketValidator.cs`**:
```csharp
using FluentValidation;

namespace LotteryChecker.Application.Features.CheckTicket;

public sealed class CheckTicketValidator : AbstractValidator<CheckTicketCommand>
{
    public CheckTicketValidator()
    {
        RuleFor(x => x.TicketNumber)
            .NotEmpty().WithMessage("Số vé bắt buộc")
            .Length(6).WithMessage("Số vé phải đúng 6 chữ số")
            .Matches(@"^\d{6}$").WithMessage("Số vé chỉ chứa chữ số");

        RuleFor(x => x.Province)
            .NotEmpty().WithMessage("Đài bắt buộc");

        RuleFor(x => x.DrawDate)
            .LessThanOrEqualTo(DateOnly.FromDateTime(DateTime.Today))
            .WithMessage("Ngày mở thưởng không thể trong tương lai");
    }
}
```

**`Features/CheckTicket/CheckTicketResult.cs`**:
```csharp
using LotteryChecker.Domain.Enums;

namespace LotteryChecker.Application.Features.CheckTicket;

public sealed record CheckTicketResult(
    string TicketNumber,
    DateOnly DrawDate,
    string Province,
    bool IsWinner,
    PrizeTier? WinningTier,
    decimal PrizeAmount);
```

**`Features/CheckTicket/CheckTicketHandler.cs`**:
```csharp
using FluentValidation;
using LotteryChecker.Application.Abstractions.Messaging;
using LotteryChecker.Application.Abstractions.Persistence;
using LotteryChecker.Application.Common;
using LotteryChecker.Domain.Enums;
using LotteryChecker.Domain.ValueObjects;

namespace LotteryChecker.Application.Features.CheckTicket;

public sealed class CheckTicketHandler : ICommandHandler<CheckTicketCommand, CheckTicketResult>
{
    private readonly ILotteryResultRepository _repo;
    private readonly IValidator<CheckTicketCommand> _validator;

    public CheckTicketHandler(ILotteryResultRepository repo, IValidator<CheckTicketCommand> validator)
    {
        _repo = repo;
        _validator = validator;
    }

    public async Task<Result<CheckTicketResult>> Handle(
        CheckTicketCommand cmd, CancellationToken ct)
    {
        var validation = await _validator.ValidateAsync(cmd, ct);
        if (!validation.IsValid)
            return Result.Failure<CheckTicketResult>(
                Error.Validation("CheckTicket", validation.Errors[0].ErrorMessage));

        var ticket = new TicketNumber(cmd.TicketNumber);  // throws nếu invalid

        var results = await _repo.GetByDateAndProvinceAsync(cmd.DrawDate, cmd.Province, ct);
        if (results.Count == 0)
            return Result.Failure<CheckTicketResult>(Error.NotFound("LotteryResult",
                $"Chưa có kết quả cho {cmd.Province} ngày {cmd.DrawDate:dd-MM-yyyy}"));

        // Tìm giải cao nhất (DacBiet = 0, Tam = 8 — số nhỏ = giải cao)
        var winning = results
            .Where(r => r.Matches(ticket.Value))
            .OrderBy(r => (int)r.PrizeTier)
            .FirstOrDefault();

        return Result.Success(new CheckTicketResult(
            ticket.Value,
            cmd.DrawDate,
            cmd.Province,
            IsWinner: winning is not null,
            WinningTier: winning?.PrizeTier,
            PrizeAmount: winning?.PrizeTier.DefaultPrize() ?? 0m));
    }
}
```

### 4.8 DependencyInjection.cs

Mỗi project có 1 extension method để wire up DI — gom hết logic register vào 1 chỗ.

**`Application/DependencyInjection.cs`**:
```csharp
using FluentValidation;
using LotteryChecker.Application.Abstractions.Messaging;
using LotteryChecker.Application.Features.CheckTicket;
using LotteryChecker.Application.Features.FetchDailyResults;
using LotteryChecker.Application.Features.ScanTicket;
using Microsoft.Extensions.DependencyInjection;

namespace LotteryChecker.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // FluentValidation tự scan
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        // Đăng ký từng handler — hoặc dùng Scrutor để tự scan
        services.AddScoped<ICommandHandler<ScanTicketCommand, ScanTicketResult>, ScanTicketHandler>();
        services.AddScoped<ICommandHandler<CheckTicketCommand, CheckTicketResult>, CheckTicketHandler>();
        services.AddScoped<ICommandHandler<FetchDailyResultsCommand>, FetchDailyResultsHandler>();

        return services;
    }
}
```

> **Pro tip**: nếu lười đăng ký từng handler, thêm package `Scrutor` rồi:
> ```csharp
> services.Scan(scan => scan
>     .FromAssembliesOf(typeof(DependencyInjection))
>     .AddClasses(c => c.AssignableTo(typeof(ICommandHandler<,>)))
>     .AsImplementedInterfaces()
>     .WithScopedLifetime());
> ```

---

## 5. Project Infrastructure — implement các interface của Application

**Mục đích**: chứa code dính líu tới tech cụ thể — EF Core, Tesseract, HtmlAgilityPack, HTTP client, file system.

### 5.1 csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\LotteryChecker.Domain\LotteryChecker.Domain.csproj" />
    <ProjectReference Include="..\LotteryChecker.Application\LotteryChecker.Application.csproj" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.8" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.8" />
    <PackageReference Include="Tesseract" Version="5.2.0" />
    <PackageReference Include="SixLabors.ImageSharp" Version="3.1.5" />
    <PackageReference Include="HtmlAgilityPack" Version="1.11.65" />
  </ItemGroup>
  <ItemGroup>
    <None Update="tessdata\**\*.*">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
</Project>
```

### 5.2 Cấu trúc thư mục
```
LotteryChecker.Infrastructure/
├── Persistence/
│   ├── AppDbContext.cs
│   ├── UnitOfWork.cs
│   ├── Configurations/
│   │   └── LotteryResultConfiguration.cs
│   ├── Repositories/
│   │   └── LotteryResultRepository.cs
│   └── Migrations/                  ← do EF tự sinh
├── Ocr/
│   ├── TesseractOcrService.cs
│   ├── ImageSharpPreprocessor.cs
│   └── ProvinceMatcher.cs
├── Scraping/
│   └── MinhNgocResultScraper.cs
├── BackgroundServices/
│   └── DailyResultFetchWorker.cs
├── tessdata/
│   └── vie.traineddata
└── DependencyInjection.cs
```

### 5.3 EF Configuration — tách metadata khỏi entity

**`Persistence/Configurations/LotteryResultConfiguration.cs`**:
```csharp
using LotteryChecker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LotteryChecker.Infrastructure.Persistence.Configurations;

internal sealed class LotteryResultConfiguration : IEntityTypeConfiguration<LotteryResult>
{
    public void Configure(EntityTypeBuilder<LotteryResult> b)
    {
        b.ToTable("LotteryResults");
        b.HasKey(x => x.Id);

        b.Property(x => x.Region).HasConversion<string>().HasMaxLength(8);
        b.Property(x => x.Province).HasMaxLength(32).IsRequired();
        b.Property(x => x.PrizeTier).HasConversion<string>().HasMaxLength(8);
        b.Property(x => x.Number).HasMaxLength(8).IsRequired();

        b.HasIndex(x => new { x.DrawDate, x.Province });
        b.HasIndex(x => x.Number);
    }
}
```

> **Tại sao tách Configuration thay vì gắn attribute lên Entity?** Entity ở Domain layer, không được biết gì về EF. Tách Configuration ra Infrastructure giữ được Domain thuần.

### 5.4 DbContext
**`Persistence/AppDbContext.cs`**:
```csharp
using LotteryChecker.Application.Abstractions.Persistence;
using LotteryChecker.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LotteryChecker.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext, IUnitOfWork
{
    public AppDbContext(DbContextOptions<AppDbContext> opt) : base(opt) { }

    public DbSet<LotteryResult> LotteryResults => Set<LotteryResult>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    Task<int> IUnitOfWork.SaveChangesAsync(CancellationToken ct) => base.SaveChangesAsync(ct);
}
```

### 5.5 Repository implementation
**`Persistence/Repositories/LotteryResultRepository.cs`**:
```csharp
using LotteryChecker.Application.Abstractions.Persistence;
using LotteryChecker.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LotteryChecker.Infrastructure.Persistence.Repositories;

internal sealed class LotteryResultRepository : ILotteryResultRepository
{
    private readonly AppDbContext _db;
    public LotteryResultRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<LotteryResult>> GetByDateAndProvinceAsync(
        DateOnly drawDate, string province, CancellationToken ct)
    {
        return await _db.LotteryResults
            .Where(r => r.DrawDate == drawDate && r.Province == province)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public Task AddRangeAsync(IEnumerable<LotteryResult> results, CancellationToken ct)
    {
        _db.LotteryResults.AddRange(results);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsForDateAndProvinceAsync(
        DateOnly drawDate, string province, CancellationToken ct)
    {
        return _db.LotteryResults.AnyAsync(r =>
            r.DrawDate == drawDate && r.Province == province, ct);
    }
}
```

### 5.6 OCR implementation
**`Ocr/TesseractOcrService.cs`**:
```csharp
using LotteryChecker.Application.Abstractions.Ocr;
using Microsoft.Extensions.Options;
using Tesseract;

namespace LotteryChecker.Infrastructure.Ocr;

public sealed class TesseractOcrOptions
{
    public string DataPath { get; set; } = "./tessdata";
}

internal sealed class TesseractOcrService : IOcrService
{
    private readonly TesseractOcrOptions _opt;
    private readonly ProvinceMatcher _provinces;

    public TesseractOcrService(IOptions<TesseractOcrOptions> opt, ProvinceMatcher provinces)
    {
        _opt = opt.Value;
        _provinces = provinces;
    }

    public Task<OcrExtractedInfo> ExtractAsync(byte[] imageBytes, CancellationToken ct)
    {
        // Tesseract sync nên chạy trong Task.Run để không block thread pool nếu host gọi sync
        return Task.Run(() =>
        {
            using var engine = new TesseractEngine(_opt.DataPath, "vie+eng", EngineMode.Default);
            using var img = Pix.LoadFromMemory(imageBytes);
            using var page = engine.Process(img);

            var text = page.GetText();
            return new OcrExtractedInfo(
                ExtractTicketNumber(text),
                ExtractDate(text),
                _provinces.FindBestMatch(text),
                page.GetMeanConfidence(),
                text);
        }, ct);
    }

    // ... ExtractTicketNumber, ExtractDate giữ nguyên logic regex từ trước
}
```

### 5.7 DI registration
**`Infrastructure/DependencyInjection.cs`**:
```csharp
using LotteryChecker.Application.Abstractions.Ocr;
using LotteryChecker.Application.Abstractions.Persistence;
using LotteryChecker.Application.Abstractions.Scraping;
using LotteryChecker.Infrastructure.BackgroundServices;
using LotteryChecker.Infrastructure.Ocr;
using LotteryChecker.Infrastructure.Persistence;
using LotteryChecker.Infrastructure.Persistence.Repositories;
using LotteryChecker.Infrastructure.Scraping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LotteryChecker.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration config)
    {
        // Persistence
        services.AddDbContext<AppDbContext>(opt =>
            opt.UseSqlite(config.GetConnectionString("Default")));
        services.AddScoped<ILotteryResultRepository, LotteryResultRepository>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());

        // OCR
        services.Configure<TesseractOcrOptions>(config.GetSection("Tesseract"));
        services.AddSingleton<ProvinceMatcher>();
        services.AddScoped<IOcrService, TesseractOcrService>();
        services.AddScoped<IImagePreprocessor, ImageSharpPreprocessor>();

        // Scraping
        services.AddHttpClient<IResultScraper, MinhNgocResultScraper>();

        // Background services
        services.AddHostedService<DailyResultFetchWorker>();

        return services;
    }
}
```

---

## 6. Project WebApi — chỉ có Controllers, middleware, config

**Mục đích**: HTTP transport. KHÔNG chứa business logic — chỉ nhận request, gọi handler, trả response.

### 6.1 csproj
```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\LotteryChecker.Application\LotteryChecker.Application.csproj" />
    <ProjectReference Include="..\LotteryChecker.Infrastructure\LotteryChecker.Infrastructure.csproj" />
    <PackageReference Include="Serilog.AspNetCore" Version="10.0.0" />
    <PackageReference Include="Scalar.AspNetCore" Version="2.1.0" />
  </ItemGroup>
</Project>
```

### 6.2 Controller mỏng

**`Controllers/ScanController.cs`**:
```csharp
using LotteryChecker.Application.Abstractions.Messaging;
using LotteryChecker.Application.Features.CheckTicket;
using LotteryChecker.Application.Features.ScanTicket;
using Microsoft.AspNetCore.Mvc;

namespace LotteryChecker.WebApi.Controllers;

[ApiController]
public class ScanController : ControllerBase
{
    private readonly ICommandHandler<ScanTicketCommand, ScanTicketResult> _scanHandler;
    private readonly ICommandHandler<CheckTicketCommand, CheckTicketResult> _checkHandler;

    public ScanController(
        ICommandHandler<ScanTicketCommand, ScanTicketResult> scanHandler,
        ICommandHandler<CheckTicketCommand, CheckTicketResult> checkHandler)
    {
        _scanHandler = scanHandler;
        _checkHandler = checkHandler;
    }

    [HttpPost("/api/scan")]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> Scan(IFormFile image, CancellationToken ct)
    {
        if (image is null || image.Length == 0)
            return BadRequest(new { error = "Chưa có ảnh" });

        using var stream = image.OpenReadStream();
        var result = await _scanHandler.Handle(new ScanTicketCommand(stream), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : BadRequest(new { error = result.Error.Message });
    }

    [HttpPost("/api/check")]
    public async Task<IActionResult> Check([FromBody] CheckTicketCommand cmd, CancellationToken ct)
    {
        var result = await _checkHandler.Handle(cmd, ct);
        return result.IsSuccess ? Ok(result.Value) : MapError(result.Error);
    }

    private IActionResult MapError(Application.Common.Error err) => err.Code switch
    {
        var c when c.StartsWith("Validation") => BadRequest(new { error = err.Message }),
        var c when c.StartsWith("NotFound") => NotFound(new { error = err.Message }),
        _ => StatusCode(500, new { error = err.Message })
    };
}
```

### 6.3 Program.cs

```csharp
using LotteryChecker.Application;
using LotteryChecker.Infrastructure;
using LotteryChecker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console());

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// Auto migrate ở dev
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseSerilogRequestLogging();
app.UseCors();
app.MapControllers();
app.Run();
```

> Chú ý độ gọn của Program.cs — gần như chỉ wire up. Mọi logic ở `AddApplication()` và `AddInfrastructure()`. Đó là kiểu code có thể test, có thể swap, có thể grow lên 200 endpoint mà file này vẫn dưới 50 dòng.

---

## 7. Test projects

Một trong những lợi ích lớn nhất của CA là **test dễ**. Mỗi layer có test project riêng:

### 7.1 Domain.UnitTests — test value object, entity logic
Không cần mock gì cả vì Domain không phụ thuộc gì.

```csharp
using FluentAssertions;
using LotteryChecker.Domain.Entities;
using LotteryChecker.Domain.Enums;
using Xunit;

public class LotteryResultTests
{
    [Theory]
    [InlineData("123456", "123456", PrizeTier.DacBiet, true)]
    [InlineData("123456", "123457", PrizeTier.DacBiet, false)]
    [InlineData("123456", "23456", PrizeTier.Nhat, true)]   // 5 chữ số cuối khớp
    [InlineData("123456", "56", PrizeTier.Tam, true)]       // 2 chữ số cuối khớp
    public void Matches_should_compare_by_tier_length(
        string ticket, string winningNumber, PrizeTier tier, bool expected)
    {
        var result = LotteryResult.Create(
            DateOnly.FromDateTime(DateTime.Today),
            Region.MN, "TPHCM", tier, winningNumber);

        result.Matches(ticket).Should().Be(expected);
    }
}
```

### 7.2 Application.UnitTests — test handler với mock interfaces

```csharp
using FluentAssertions;
using LotteryChecker.Application.Abstractions.Persistence;
using LotteryChecker.Application.Features.CheckTicket;
using LotteryChecker.Domain.Entities;
using LotteryChecker.Domain.Enums;
using NSubstitute;
using Xunit;

public class CheckTicketHandlerTests
{
    [Fact]
    public async Task Should_return_winner_when_ticket_matches_DacBiet()
    {
        // Arrange
        var repo = Substitute.For<ILotteryResultRepository>();
        repo.GetByDateAndProvinceAsync(default, default!, default).ReturnsForAnyArgs(new[]
        {
            LotteryResult.Create(new DateOnly(2026,5,28), Region.MN, "TPHCM",
                                 PrizeTier.DacBiet, "123456")
        });
        var validator = new CheckTicketValidator();
        var handler = new CheckTicketHandler(repo, validator);

        // Act
        var result = await handler.Handle(
            new CheckTicketCommand("123456", new DateOnly(2026,5,28), "TPHCM"),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.IsWinner.Should().BeTrue();
        result.Value.WinningTier.Should().Be(PrizeTier.DacBiet);
        result.Value.PrizeAmount.Should().Be(2_000_000_000m);
    }
}
```

Test cực nhanh, không chạm DB, không cần Tesseract — chỉ test logic dò vé.

### 7.3 Infrastructure.IntegrationTests — test với SQLite thật
Spin up `AppDbContext` với SQLite in-memory, chèn data, test repository.

### 7.4 WebApi.FunctionalTests — test end-to-end với WebApplicationFactory
```csharp
public class ScanEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public ScanEndpointTests(WebApplicationFactory<Program> f) => _factory = f;

    [Fact]
    public async Task POST_check_returns_400_when_ticket_invalid()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/check", new {
            ticketNumber = "12", drawDate = "2026-05-28", province = "TPHCM"
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
```

---

## 8. Lệnh tạo solution mới từ đầu

Chạy trong PowerShell ở thư mục `backend/`:

```powershell
# Solution
dotnet new sln -n LotteryChecker

# Tạo src/
mkdir src
cd src

# 4 projects chính
dotnet new classlib -n LotteryChecker.Domain
dotnet new classlib -n LotteryChecker.Application
dotnet new classlib -n LotteryChecker.Infrastructure
dotnet new webapi -n LotteryChecker.WebApi --use-controllers

# Tạo tests/
cd ..
mkdir tests
cd tests
dotnet new xunit -n LotteryChecker.Domain.UnitTests
dotnet new xunit -n LotteryChecker.Application.UnitTests
dotnet new xunit -n LotteryChecker.Infrastructure.IntegrationTests
dotnet new xunit -n LotteryChecker.WebApi.FunctionalTests

# Quay lại root, add hết vào solution
cd ..
dotnet sln add (Get-ChildItem -Recurse -Filter *.csproj)

# Wire references (LƯU Ý: chiều dependency!)
cd src
dotnet add LotteryChecker.Application/LotteryChecker.Application.csproj reference LotteryChecker.Domain/LotteryChecker.Domain.csproj
dotnet add LotteryChecker.Infrastructure/LotteryChecker.Infrastructure.csproj reference LotteryChecker.Domain/LotteryChecker.Domain.csproj
dotnet add LotteryChecker.Infrastructure/LotteryChecker.Infrastructure.csproj reference LotteryChecker.Application/LotteryChecker.Application.csproj
dotnet add LotteryChecker.WebApi/LotteryChecker.WebApi.csproj reference LotteryChecker.Application/LotteryChecker.Application.csproj
dotnet add LotteryChecker.WebApi/LotteryChecker.WebApi.csproj reference LotteryChecker.Infrastructure/LotteryChecker.Infrastructure.csproj

# Test refs
cd ../tests
dotnet add LotteryChecker.Domain.UnitTests reference ../src/LotteryChecker.Domain
dotnet add LotteryChecker.Application.UnitTests reference ../src/LotteryChecker.Application
dotnet add LotteryChecker.Infrastructure.IntegrationTests reference ../src/LotteryChecker.Infrastructure
dotnet add LotteryChecker.WebApi.FunctionalTests reference ../src/LotteryChecker.WebApi
```

Sau đó cài packages cho từng project (đã list ở section 3-6).

---

## 9. Tooling cho code quality & maintainability

Cấu trúc đúng là chưa đủ. Project lớn lên sẽ rối nếu không có công cụ kiểm soát chất lượng code tự động. 5 file dưới đây tốn ~15 phút setup nhưng tiết kiệm hàng giờ debug sau này.

### 9.1 `global.json` — pin .NET SDK version

Tạo ở root solution (`backend/global.json`):
```json
{
  "sdk": {
    "version": "10.0.300",
    "rollForward": "latestFeature"
  }
}
```

**Lý do**: máy dev khác nhau có thể cài SDK 10.0.x khác nhau. `global.json` đảm bảo cả team build cùng version, tránh "trên máy tôi chạy được" syndrome. `rollForward: latestFeature` cho phép up nhẹ trong cùng feature band.

### 9.2 `Directory.Packages.props` — Central Package Management

Đây là tính năng quan trọng nhưng ít người biết. Thay vì pin version trong từng csproj (4-5 project × ~5 package = 20+ chỗ phải đồng bộ), pin **1 chỗ duy nhất**.

Tạo `backend/Directory.Packages.props`:
```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <!-- EF Core 10.x đồng nhất -->
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.8" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.8" />

    <!-- Validation -->
    <PackageVersion Include="FluentValidation" Version="11.10.0" />

    <!-- OCR & images -->
    <PackageVersion Include="Tesseract" Version="5.2.0" />
    <PackageVersion Include="SixLabors.ImageSharp" Version="3.1.5" />

    <!-- Scraping -->
    <PackageVersion Include="HtmlAgilityPack" Version="1.11.65" />

    <!-- Logging & API docs -->
    <PackageVersion Include="Serilog.AspNetCore" Version="10.0.0" />
    <PackageVersion Include="Scalar.AspNetCore" Version="2.1.0" />

    <!-- Testing -->
    <PackageVersion Include="xunit" Version="2.9.2" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageVersion Include="FluentAssertions" Version="6.12.2" />
    <PackageVersion Include="NSubstitute" Version="5.1.0" />
    <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.0" />
    <PackageVersion Include="NetArchTest.Rules" Version="1.3.2" />
  </ItemGroup>
</Project>
```

Sau đó trong mọi csproj **bỏ Version**:
```xml
<!-- TRƯỚC -->
<PackageReference Include="FluentValidation" Version="11.10.0" />

<!-- SAU (chỉ tên, version lấy từ Directory.Packages.props) -->
<PackageReference Include="FluentValidation" />
```

Khi cần upgrade EF Core lên 10.0.9, **sửa 1 dòng** trong `Directory.Packages.props`, tất cả project tự update khi build lại.

### 9.3 `Directory.Build.props` — common settings cho mọi project

Tạo `backend/Directory.Build.props`:
```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <WarningsNotAsErrors>CS1591</WarningsNotAsErrors>
    <LangVersion>latest</LangVersion>
    <AnalysisLevel>latest</AnalysisLevel>
    <AnalysisMode>AllEnabledByDefault</AnalysisMode>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>
</Project>
```

`TreatWarningsAsErrors=true` là tinh thần đáng giá: **1 warning = build fail**. Không bao giờ có chỗ "warning quá nhiều, không ai xử" như trên các codebase cũ. `EnforceCodeStyleInBuild=true` ép code style từ `.editorconfig` vào build pipeline.

### 9.4 `.editorconfig` — code style nhất quán

Tạo `backend/.editorconfig`:
```ini
root = true

[*]
charset = utf-8
end_of_line = lf
insert_final_newline = true
indent_style = space
trim_trailing_whitespace = true

[*.{cs,csproj,json,xml}]
indent_size = 4

[*.{js,jsx,ts,tsx,html,css,json,yml,yaml,md}]
indent_size = 2

[*.cs]
# Sort using
dotnet_sort_system_directives_first = true
dotnet_separate_import_directive_groups = false

# Style: var khi rõ kiểu
csharp_style_var_when_type_is_apparent = true:suggestion
csharp_style_var_elsewhere = false:suggestion

# File-scoped namespace (gọn hơn block-scoped)
csharp_style_namespace_declarations = file_scoped:warning

# Expression-bodied members khi có thể
csharp_style_expression_bodied_methods = when_on_single_line:suggestion
csharp_style_expression_bodied_properties = true:suggestion

# Diagnostics
dotnet_diagnostic.CA1822.severity = warning  # Mark members as static
dotnet_diagnostic.CA2007.severity = none     # ConfigureAwait - không cần với ASP.NET Core
dotnet_diagnostic.IDE0005.severity = warning # Unused using
```

VS Code (C# Dev Kit), Rider, Visual Studio đều tự đọc `.editorconfig`. Format on save tự áp dụng — không còn "PR khác nhau chỉ vì 1 người dùng tab, 1 người dùng space".

### 9.5 `NetArchTest` — enforce dependency rules bằng unit test

Cấu trúc Clean Architecture chỉ sống nếu **dependency direction được tuân thủ**. Trong project lớn, dev mới có thể add reference sai mà không ai để ý. Giải pháp: **viết test bắt lỗi này tự động**.

Tạo project test mới `tests/LotteryChecker.Architecture.Tests/`:
```csharp
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace LotteryChecker.Architecture.Tests;

public class ArchitectureTests
{
    private const string Domain = "LotteryChecker.Domain";
    private const string Application = "LotteryChecker.Application";
    private const string Infrastructure = "LotteryChecker.Infrastructure";
    private const string WebApi = "LotteryChecker.WebApi";

    [Fact]
    public void Domain_should_not_depend_on_any_other_layer()
    {
        var result = Types.InAssembly(typeof(Domain.Entities.LotteryResult).Assembly)
            .Should()
            .NotHaveDependencyOnAny(Application, Infrastructure, WebApi)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Application_should_not_depend_on_Infrastructure()
    {
        var result = Types.InAssembly(typeof(Application.DependencyInjection).Assembly)
            .Should().NotHaveDependencyOn(Infrastructure)
            .GetResult();
        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Application_should_not_depend_on_EntityFramework()
    {
        var result = Types.InAssembly(typeof(Application.DependencyInjection).Assembly)
            .Should().NotHaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();
        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Application_should_not_depend_on_AspNetCore()
    {
        var result = Types.InAssembly(typeof(Application.DependencyInjection).Assembly)
            .Should().NotHaveDependencyOn("Microsoft.AspNetCore")
            .GetResult();
        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Handlers_should_have_Handler_suffix()
    {
        var result = Types.InAssembly(typeof(Application.DependencyInjection).Assembly)
            .That().ImplementInterface(typeof(Application.Abstractions.Messaging.ICommandHandler<,>))
            .Should().HaveNameEndingWith("Handler")
            .GetResult();
        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Controllers_should_have_Controller_suffix()
    {
        var result = Types.InAssembly(typeof(WebApi.Program).Assembly)
            .That().Inherit(typeof(Microsoft.AspNetCore.Mvc.ControllerBase))
            .Should().HaveNameEndingWith("Controller")
            .GetResult();
        result.IsSuccessful.Should().BeTrue();
    }
}
```

Chạy `dotnet test`. Nếu ai cố `using LotteryChecker.Infrastructure...` từ Domain → test fail → CI block PR. **Architecture sống được lâu dài là nhờ test này** — không phải nhờ trí nhớ developer.

---

## 10. Naming conventions

Convention không bị compiler ép buộc nhưng cả team phải tuân thủ để đọc code đoán được vị trí file mà không cần search.

### 10.1 Quy tắc đặt tên class

| Loại | Quy tắc | Ví dụ |
|---|---|---|
| Entity | Singular noun | `LotteryResult` (không phải `LotteryResults`) |
| Value Object | Singular noun, ngắn | `TicketNumber`, `PrizeAmount` |
| Domain Exception | Tên hành động + `Exception` | `InvalidTicketNumberException` |
| Command | Verb-noun + `Command` | `ScanTicketCommand`, `CheckTicketCommand` |
| Query | `Get`/`List` + noun + `Query` | `GetScanHistoryQuery` |
| Handler | Cùng tên Cmd/Query + `Handler` | `ScanTicketHandler` |
| DTO Result | Cmd/Query name + `Result` | `ScanTicketResult` |
| Validator | Command name + `Validator` | `ScanTicketValidator` |
| Repo interface | `I` + entity + `Repository` | `ILotteryResultRepository` |
| Repo impl | Entity + `Repository` | `LotteryResultRepository` |
| Service interface | `I` + role + `Service` | `IOcrService` |
| Service impl | Provider + role + `Service` | `TesseractOcrService` |
| EF Configuration | Entity + `Configuration` | `LotteryResultConfiguration` |
| Controller | Plural noun + `Controller` | `ScansController` |
| Background service | Hành động + `Worker` | `DailyResultFetchWorker` |

### 10.2 Folder convention

| Layer | Folders cố định |
|---|---|
| Domain | `Entities/`, `ValueObjects/`, `Enums/`, `Exceptions/`, `Events/` |
| Application | `Abstractions/`, `Common/`, `Features/<UseCase>/` |
| Infrastructure | `Persistence/`, `Ocr/`, `Scraping/`, `BackgroundServices/` |
| WebApi | `Controllers/`, `Middleware/`, `Filters/` |

### 10.3 Code style rules (đáng ghi nhớ)

- **`var` chỉ khi rõ kiểu**: `var list = new List<int>();` ✅ nhưng `var result = repo.Get();` ❌ — viết `IReadOnlyList<LotteryResult> result = ...` để người đọc khỏi nhảy vào method.
- **File-scoped namespace**: `namespace X;` thay vì `namespace X { }` — giảm 1 cấp indent.
- **`sealed` mặc định cho class** trừ khi cần kế thừa — performance + intent rõ ràng.
- **`internal` cho mọi class implementation** trong Infrastructure — DI biết, code khác không cần ref trực tiếp.
- **`record`/`record struct` cho mọi DTO** thay vì class — equality, immutability tự động.
- **Async method luôn có suffix `Async`** và nhận `CancellationToken` là tham số cuối.
- **`IReadOnlyList<>`/`IReadOnlyCollection<>` cho return type của repository** thay vì `List<>` — caller không thể modify.

---

## 11. Cheat sheet — common tasks

Mỗi tác vụ dưới đây liệt kê **chạm file ở những layer nào**, theo thứ tự dependency đúng để dev mới làm theo không sai.

### 11.1 Thêm endpoint mới (use case mới)

Ví dụ: thêm `GET /api/scans/history` xem lịch sử dò vé.

1. **Domain** (nếu chưa có): tạo `Entities/ScanHistory.cs` với factory + private setter.
2. **Application**:
   - `Application/Features/GetScanHistory/GetScanHistoryQuery.cs` — record implement `IQuery<GetScanHistoryResult>`
   - `Application/Features/GetScanHistory/GetScanHistoryResult.cs` — record DTO
   - `Application/Features/GetScanHistory/GetScanHistoryHandler.cs` — implement `IQueryHandler<...>`, inject repo
   - Đăng ký handler trong `Application/DependencyInjection.cs`
3. **Application/Abstractions** (nếu repo cần method mới): thêm vào `IScanHistoryRepository.cs`.
4. **Infrastructure**: implement method mới vào `Persistence/Repositories/ScanHistoryRepository.cs`.
5. **WebApi**: thêm action `[HttpGet("history")]` vào `Controllers/ScansController.cs`, gọi `_handler.Handle(...)`.
6. **Tests**:
   - `Application.UnitTests/Features/GetScanHistory/GetScanHistoryHandlerTests.cs` — mock repo, test handler
   - `WebApi.FunctionalTests/ScansEndpointTests.cs` — test endpoint trả 200 + JSON shape đúng

### 11.2 Thêm entity mới

1. **Domain**: `Entities/<Name>.cs` với private setter + factory `Create(...)`. Validate trong factory.
2. **Infrastructure**: `Persistence/Configurations/<Name>Configuration.cs` implement `IEntityTypeConfiguration<>`.
3. **Infrastructure**: thêm `DbSet<Name>` vào `AppDbContext`.
4. Chạy migration:
   ```powershell
   cd backend
   dotnet ef migrations add Add<Name>Table --project src/LotteryChecker.Infrastructure --startup-project src/LotteryChecker.WebApi --output-dir Persistence/Migrations
   dotnet ef database update --project src/LotteryChecker.Infrastructure --startup-project src/LotteryChecker.WebApi
   ```
5. **Application/Abstractions**: `I<Name>Repository.cs` với các method cần.
6. **Infrastructure**: implement `<Name>Repository.cs`, đăng ký DI trong `Infrastructure/DependencyInjection.cs`.
7. **Domain.UnitTests**: test factory (validation cases) + business rules.

### 11.3 Thêm external service mới

Ví dụ: gửi SMS khi user trúng giải ĐB.

1. **Application/Abstractions**: `ISmsService.cs` với `SendAsync(phone, message, ct)`.
2. **Application**: inject `ISmsService` vào handler liên quan (vd `CheckTicketHandler`), gọi khi user trúng.
3. **Infrastructure**: `Notifications/EsmsService.cs` implement `ISmsService` (eSMS, Twilio, hoặc provider khác).
4. **Infrastructure/DependencyInjection.cs**:
   ```csharp
   services.Configure<EsmsOptions>(config.GetSection("Esms"));
   services.AddHttpClient<ISmsService, EsmsService>();
   ```
5. **appsettings.json**: thêm `"Esms": { "ApiKey": "...", "Brandname": "..." }`.
6. **Tests**:
   - Application: mock `ISmsService`, verify handler gọi `SendAsync` đúng số điện thoại + message
   - Infrastructure: integration test với HTTP mock (không gọi eSMS thật)

### 11.4 Đổi tech (SQLite → PostgreSQL)

Đây là benchmark lớn của Clean Architecture: **chỉ chạm Infrastructure**.

1. Trong `Directory.Packages.props`: bỏ `Microsoft.EntityFrameworkCore.Sqlite`, thêm:
   ```xml
   <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="9.0.x" />
   ```
2. Trong `Infrastructure.csproj`: thay tên package tương ứng (vẫn `<PackageReference>` không Version vì CPM).
3. Trong `Infrastructure/DependencyInjection.cs`: đổi `UseSqlite` → `UseNpgsql`.
4. Xóa migration cũ (SQL syntax khác), tạo lại:
   ```powershell
   dotnet ef migrations remove --project src/LotteryChecker.Infrastructure --startup-project src/LotteryChecker.WebApi
   dotnet ef migrations add InitialCreate --project src/LotteryChecker.Infrastructure --startup-project src/LotteryChecker.WebApi --output-dir Persistence/Migrations
   ```
5. Cập nhật connection string trong `appsettings.json`.
6. **Domain, Application, WebApi: KHÔNG SỬA GÌ.**

### 11.5 Đổi OCR provider (Tesseract → Google Vision)

1. `Infrastructure/Ocr/GoogleVisionOcrService.cs` implement `IOcrService`.
2. Trong `Infrastructure/DependencyInjection.cs`: đổi `AddScoped<IOcrService, TesseractOcrService>()` → `GoogleVisionOcrService`.
3. Trong `Directory.Packages.props`: bỏ `Tesseract`, thêm `Google.Cloud.Vision.V1`.
4. **Application, Domain, WebApi: KHÔNG SỬA GÌ** — đều gọi qua interface `IOcrService`.

### 11.6 Lệnh hay dùng

```powershell
# Build & test
dotnet build
dotnet test
dotnet test --filter "FullyQualifiedName~Architecture"   # chỉ run architecture tests
dotnet test --filter "FullyQualifiedName~UnitTests"      # chỉ unit tests (nhanh)

# Run với hot reload
dotnet watch --project src/LotteryChecker.WebApi run

# Format code theo .editorconfig
dotnet format

# EF migrations (chạy từ thư mục backend/)
dotnet ef migrations add <Name> --project src/LotteryChecker.Infrastructure --startup-project src/LotteryChecker.WebApi --output-dir Persistence/Migrations
dotnet ef database update --project src/LotteryChecker.Infrastructure --startup-project src/LotteryChecker.WebApi
dotnet ef migrations remove --project src/LotteryChecker.Infrastructure --startup-project src/LotteryChecker.WebApi

# NuGet (với CPM, không truyền Version)
dotnet add src/LotteryChecker.Infrastructure package <PackageName>
```

---

## 12. Tóm tắt — cấu trúc cuối cùng

```
backend/
├── global.json                          ← pin SDK 10.0.x
├── Directory.Build.props                ← TargetFramework, nullable, warnings-as-errors
├── Directory.Packages.props             ← central package version management
├── .editorconfig                        ← code style
├── LotteryChecker.sln
│
├── src/
│   ├── LotteryChecker.Domain/           ← POCO thuần, không ref ngoài
│   ├── LotteryChecker.Application/      ← use cases, interfaces, DTO
│   ├── LotteryChecker.Infrastructure/   ← EF, Tesseract, scraper
│   └── LotteryChecker.WebApi/           ← controllers, Program.cs
│
└── tests/
    ├── LotteryChecker.Architecture.Tests/      ← enforce CA rules bằng NetArchTest
    ├── LotteryChecker.Domain.UnitTests/        ← test entity, value object (nhanh)
    ├── LotteryChecker.Application.UnitTests/   ← test handler với mock repo
    ├── LotteryChecker.Infrastructure.IntegrationTests/  ← test repo với SQLite thật
    └── LotteryChecker.WebApi.FunctionalTests/  ← test endpoint end-to-end
```

**Tổng**: 4 file config root + 4 src project + 5 test project = **13 csproj**. Nghe nhiều nhưng mỗi project có trách nhiệm duy nhất, không nhầm vai trò.

### Vì sao structure này dễ maintain dài hạn

1. **Mọi câu hỏi "code này để đâu?" có 1 câu trả lời duy nhất** — nhờ folder convention ở Section 10. Dev mới onboard 1 giờ là biết.

2. **NetArchTest đảm bảo kiến trúc không bị "ăn mòn"** — không ai vô tình ref Infrastructure từ Domain. Test fail = không merge được PR.

3. **Directory.Packages.props giúp upgrade package 1 chỗ duy nhất** — sang năm EF Core 11 ra, sửa 1 dòng là cả solution lên.

4. **`TreatWarningsAsErrors=true` ép code sạch từ đầu** — không tích lũy nợ kỹ thuật theo thời gian.

5. **Test chia theo layer + tốc độ**: Domain.UnitTests chạy mỗi save (vài ms), Functional chỉ chạy trên CI (vài giây). Feedback loop nhanh.

6. **Code style nhất quán toàn solution** — không bao giờ có PR review về tab/space, vị trí brace, `var` vs explicit type.

### Khi nào nên dùng cấu trúc này

Project có ý định maintain > 6 tháng và bạn (hoặc team) sẽ làm việc trên nó nhiều lần. Nếu chỉ là prototype 1 tuần, không cần — single project nhanh hơn.

Đây cũng là cấu trúc rất tốt cho **portfolio GitHub** — đẩy lên kèm README giải thích các quyết định thiết kế (manual handler thay MediatR, Result pattern thay exception, NetArchTest enforce rules) sẽ là điểm sáng cho người tuyển dụng .NET enterprise.

---

## Phụ lục A. Migrate từ structure cũ (single-project) sang CA

Nếu đã làm theo `setup-guide.md` và có project single-flat rồi, không cần xóa làm lại. Quy trình migrate:

1. Tạo solution mới rỗng theo Section 8 — đặt cạnh project cũ trong `backend-v2/`.
2. Copy code từ project cũ sang đúng layer:
   - `Models/LotteryResult.cs` → `Domain/Entities/` (refactor thành entity factory + private setter)
   - `Models/TicketInfo.cs`, `ScanResult.cs` → `Application/Features/*/` (đổi thành record DTO)
   - `Services/OcrService.cs` → `Infrastructure/Ocr/TesseractOcrService.cs` implement `IOcrService`
   - `Services/ImagePreprocessor.cs` → `Infrastructure/Ocr/ImageSharpPreprocessor.cs`
   - `Services/LotteryMatcher.cs` → logic move vào `CheckTicketHandler` + entity method `Matches()`
   - `Services/ResultScraper.cs` → `Infrastructure/Scraping/MinhNgocResultScraper.cs`
   - `Data/AppDbContext.cs` → `Infrastructure/Persistence/AppDbContext.cs` (implement `IUnitOfWork`)
   - `Workers/DailyResultFetchWorker.cs` → `Infrastructure/BackgroundServices/`
   - `Controllers/ScanController.cs` → `WebApi/Controllers/` (refactor controller mỏng gọi handler)
3. Chạy `dotnet build` sửa từng lỗi compile (đa số là dependency direction sai → check NetArchTest).
4. Tạo lại EF migration ở Infrastructure.
5. Build xanh → xóa `backend-v1/` → rename `backend-v2/` thành `backend/`.

**Khuyên**: làm xong phiên bản đơn giản trước (theo `setup-guide.md`), validate có user thật dùng được, **rồi mới refactor lên CA**. Đừng cố CA từ ngày đầu khi chưa hiểu rõ business logic — sẽ thiết kế abstractions sai và phải refactor lại.
