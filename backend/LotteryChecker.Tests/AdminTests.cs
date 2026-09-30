using FluentAssertions;
using LotteryChecker.Api.Data;
using LotteryChecker.Api.Migrations;
using LotteryChecker.Api.Models;
using LotteryChecker.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LotteryChecker.Tests;

// Trang quản trị: tài khoản admin tạo sẵn trong migration, cộng/trừ số dư, tìm user.
public class AdminTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact(DisplayName = "Migration tren SQLite that tao superadmin: dang nhap duoc bang mat khau mac dinh, bat doi mat khau")]
    public void Migration_SeedsAdmin()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options);
        db.Database.Migrate();

        var admin = db.Users.Single(x => x.Username == AddAdmin.SeedUsername);
        admin.Should().Match<User>(u => u.IsAdmin && u.MustChangePassword && u.Balance == 0);
        new PasswordHasher<User>().VerifyHashedPassword(admin, admin.PasswordHash, "Welcom@123")
            .Should().NotBe(PasswordVerificationResult.Failed);
        AccountRules.UsernameError(admin.Username).Should().BeNull();
        AccountRules.PasswordError("Welcom@123").Should().BeNull();
    }

    [Fact(DisplayName = "Admin cong/tru so du: ghi TopUp/Adjust, khong cho tru qua so du, so tien 0 bi tu choi")]
    public async Task Adjust()
    {
        using var db = NewDb();
        var u = new User { Username = "someone1234", Balance = 50_000 };
        db.Users.Add(u);
        db.SaveChanges();
        var wallet = new Wallet(db, TimeProvider.System);

        (await wallet.AdjustAsync(u.Id, 100_000, "tặng", default)).Should().Be(new Wallet.AdjustOutcome(150_000, Wallet.AdjustError.None));
        (await wallet.AdjustAsync(u.Id, -40_000, "sửa nhầm", default)).Should().Be(new Wallet.AdjustOutcome(110_000, Wallet.AdjustError.None));
        (await wallet.AdjustAsync(u.Id, -200_000, null, default)).Error.Should().Be(Wallet.AdjustError.InsufficientBalance);
        (await wallet.AdjustAsync(u.Id, 0, null, default)).Error.Should().Be(Wallet.AdjustError.BadAmount);
        (await wallet.AdjustAsync(u.Id, Wallet.MaxTopUp + 1, null, default)).Error.Should().Be(Wallet.AdjustError.BadAmount);
        (await wallet.AdjustAsync(999, 1, null, default)).Error.Should().Be(Wallet.AdjustError.NoAccount);

        db.WalletTransactions.OrderBy(x => x.Id).Select(x => new { x.Kind, x.Amount, x.BalanceAfter }).ToList()
            .Should().Equal(new { Kind = WalletTransactionKind.TopUp, Amount = 100_000L, BalanceAfter = 150_000L },
                            new { Kind = WalletTransactionKind.Adjust, Amount = -40_000L, BalanceAfter = 110_000L });
    }

    [Fact(DisplayName = "Danh sach user: tim theo mot phan username (khong phan biet hoa thuong), sap theo so du")]
    public async Task List_SearchAndSort()
    {
        using var db = NewDb();
        db.Users.AddRange(
            new User { Username = "alice.nguyen", Balance = 10 },
            new User { Username = "bob.tran123", Balance = 30 },
            new User { Username = "alice.le2026", Balance = 20 });
        db.SaveChanges();
        var admin = new AdminUsers(db);

        var found = await admin.ListAsync("ALICE", null, 1, default);
        found.Total.Should().Be(2);
        found.Items.Select(x => x.Username).Should().Equal("alice.le2026", "alice.nguyen");   // mới đăng ký trước

        (await admin.ListAsync(null, "balance", 1, default)).Items.Select(x => x.Balance).Should().Equal(30, 20, 10);
    }

    [Fact(DisplayName = "Dat lai mat khau: mat khau moi dung duoc va bat buoc doi lai")]
    public async Task ResetPassword()
    {
        using var db = NewDb();
        var u = new User { Username = "someone1234", PasswordHash = "x" };
        db.Users.Add(u);
        db.SaveChanges();

        (await new AdminUsers(db).ResetPasswordAsync(u.Id, "Tam@12345", default)).Should().BeTrue();
        u.MustChangePassword.Should().BeTrue();
        new PasswordHasher<User>().VerifyHashedPassword(u, u.PasswordHash, "Tam@12345")
            .Should().NotBe(PasswordVerificationResult.Failed);
    }
}
