using System.Text.Json;
using FluentAssertions;
using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;
using LotteryChecker.Api.Services;
using Microsoft.EntityFrameworkCore;
using static LotteryChecker.Api.Services.MiniGameService;

namespace LotteryChecker.Tests;

// Minigame đua xe: mô phỏng khớp frontend, server tự chấm điểm, chặn gửi lại / tua nhanh / quá lượt.
public class MiniGameTests
{
    private sealed class Env
    {
        private readonly string _name = Guid.NewGuid().ToString();
        public MovableTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.FromHours(7)));
        public AppDbContext NewDb() =>
            new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_name).Options);
        public MiniGameService Service() => new(NewDb(), Clock);

        public int AddUser(long balance = 0)
        {
            using var db = NewDb();
            var u = new User { Username = $"user{Guid.NewGuid():N}"[..20], Balance = balance };
            db.Users.Add(u);
            db.SaveChanges();
            return u.Id;
        }

        public long Balance(int userId) { using var db = NewDb(); return db.Users.Single(x => x.Id == userId).Balance; }

        public void Wait(TimeSpan t) => Clock.Now += t;
    }

    private sealed record GoldenCase(uint Seed, int[][] Inputs, int Score, int Coins, int Ticks, bool Crashed);

    private static List<GoldenCase> Golden() => JsonSerializer.Deserialize<List<GoldenCase>>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "minigame-golden.json")),
        new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    /// <summary>Thao tác chơi "giỏi" cho seed — lấy từ chính mô phỏng: né hàng sắp tới.</summary>
    private static int[][] PlayWell(uint seed)
    {
        var sim = new MiniGameSim(seed);
        var inputs = new List<int[]>();
        var next = 0;
        while (!sim.Over)
        {
            while (MiniGameSim.RowY(next) <= sim.Distance) next++;
            var row = sim.GetRow(next);
            var dir = 0;
            if ((row.Blocked & (1 << sim.Lane)) != 0)
            {
                var target = Enumerable.Range(0, MiniGameSim.Lanes).First(l => (row.Blocked & (1 << l)) == 0);
                dir = Math.Sign(target - sim.Lane);
            }
            if (dir != 0) inputs.Add([sim.Tick, dir]);
            sim.Step(dir);
        }
        return inputs.ToArray();
    }

    [Fact(DisplayName = "Mo phong khop bo golden dung chung voi frontend")]
    public void Replay_MatchesGolden()
    {
        var cases = Golden();
        cases.Should().NotBeEmpty();
        foreach (var c in cases)
        {
            var r = MiniGameSim.Replay(c.Seed, ParseInputs(c.Inputs)!);
            r.Should().Be(new MiniGameSim.Result(c.Score, c.Coins, c.Ticks, c.Crashed), $"seed {c.Seed}");
        }
    }

    [Fact(DisplayName = "Moi hang chuong ngai luon chua it nhat 1 lan trong, xu nam o lan trong")]
    public void Rows_AlwaysPassable()
    {
        for (uint seed = 0; seed < 10_000; seed++)
        {
            var sim = new MiniGameSim(seed * 2654435761u);
            for (var i = 0; i < 150; i++)
            {
                var row = sim.GetRow(i);
                row.Blocked.Should().BeInRange(1, 6);
                if (row.CoinLane >= 0) (row.Blocked & (1 << row.CoinLane)).Should().Be(0);
            }
        }
    }

    [Theory(DisplayName = "Thao tac sai dinh dang bi loai ca van")]
    [InlineData("[[5,1],[5,-1]]")]     // tick không tăng
    [InlineData("[[9,1],[3,-1]]")]     // tick đi lùi
    [InlineData("[[1,2]]")]            // dir khác ±1
    [InlineData("[[2700,1]]")]         // quá tick cuối
    [InlineData("[[-1,1]]")]
    [InlineData("[[1,1,1]]")]
    public void ParseInputs_RejectsBad(string json) =>
        ParseInputs(JsonSerializer.Deserialize<int[][]>(json)).Should().BeNull();

    [Fact(DisplayName = "Choi xong: server tu tinh diem, cong thuong co tran, ghi so giao dich")]
    public async Task Finish_ServerScoresAndPays()
    {
        var env = new Env();
        var uid = env.AddUser(1_000);
        var start = (await env.Service().StartAsync(uid, default))!;
        start.Rewarded.Should().BeTrue();
        var inputs = PlayWell(start.Seed);
        var expected = MiniGameSim.Replay(start.Seed, ParseInputs(inputs)!);

        env.Wait(TimeSpan.FromSeconds(expected.Ticks / 30.0));
        var (r, err) = await env.Service().FinishAsync(uid, new FinishRequest(start.SessionId, inputs), default);

        err.Should().Be(FinishError.None);
        r!.Score.Should().Be(expected.Score);
        r.Reward.Should().Be(RewardFor(expected.Score)).And.BeLessThanOrEqualTo(MaxRewardPerPlay);
        env.Balance(uid).Should().Be(1_000 + r.Reward);
        using var db = env.NewDb();
        db.WalletTransactions.Single(x => x.UserId == uid).Kind.Should().Be(WalletTransactionKind.GameReward);
    }

    [Fact(DisplayName = "Gui lai request ket thuc: chi cong thuong 1 lan")]
    public async Task Finish_Twice_PaysOnce()
    {
        var env = new Env();
        var uid = env.AddUser();
        var start = (await env.Service().StartAsync(uid, default))!;
        var inputs = PlayWell(start.Seed);
        env.Wait(TimeSpan.FromSeconds(91));

        (await env.Service().FinishAsync(uid, new FinishRequest(start.SessionId, inputs), default)).Error.Should().Be(FinishError.None);
        var paid = env.Balance(uid);
        (await env.Service().FinishAsync(uid, new FinishRequest(start.SessionId, inputs), default)).Error
            .Should().Be(FinishError.AlreadyFinished);
        env.Balance(uid).Should().Be(paid);
    }

    [Fact(DisplayName = "Ket thuc som hon thoi gian van that (tua nhanh) bi tu choi")]
    public async Task Finish_TooFast_Rejected()
    {
        var env = new Env();
        var uid = env.AddUser();
        var start = (await env.Service().StartAsync(uid, default))!;
        var inputs = PlayWell(start.Seed);
        var ticks = MiniGameSim.Replay(start.Seed, ParseInputs(inputs)!).Ticks;
        ticks.Should().BeGreaterThan(30 * 10);

        env.Wait(TimeSpan.FromSeconds(2));
        (await env.Service().FinishAsync(uid, new FinishRequest(start.SessionId, inputs), default)).Error
            .Should().Be(FinishError.TooFast);
        env.Balance(uid).Should().Be(0);
    }

    [Fact(DisplayName = "Luot qua 3 phut thi het han, khong thuong")]
    public async Task Finish_AfterLifetime_Expired()
    {
        var env = new Env();
        var uid = env.AddUser();
        var start = (await env.Service().StartAsync(uid, default))!;
        env.Wait(TimeSpan.FromMinutes(4));
        (await env.Service().FinishAsync(uid, new FinishRequest(start.SessionId, PlayWell(start.Seed)), default)).Error
            .Should().Be(FinishError.Expired);
        env.Balance(uid).Should().Be(0);
    }

    [Fact(DisplayName = "Mo luot moi thi luot dang do het han")]
    public async Task Start_ExpiresOpenSession()
    {
        var env = new Env();
        var uid = env.AddUser();
        var first = (await env.Service().StartAsync(uid, default))!;
        await env.Service().StartAsync(uid, default);
        env.Wait(TimeSpan.FromSeconds(91));
        (await env.Service().FinishAsync(uid, new FinishRequest(first.SessionId, PlayWell(first.Seed)), default)).Error
            .Should().Be(FinishError.Expired);
    }

    [Fact(DisplayName = "Luot cua nguoi khac: khong tim thay")]
    public async Task Finish_OtherUser_NotFound()
    {
        var env = new Env();
        var a = env.AddUser();
        var b = env.AddUser();
        var start = (await env.Service().StartAsync(a, default))!;
        (await env.Service().FinishAsync(b, new FinishRequest(start.SessionId, []), default)).Error.Should().Be(FinishError.NotFound);
    }

    [Fact(DisplayName = "Het luot co thuong trong ngay: van choi duoc nhung thuong 0; sang ngay moi co lai")]
    public async Task DailyQuota()
    {
        var env = new Env();
        var uid = env.AddUser();
        for (var i = 0; i < RewardedPlaysPerDay; i++)
            (await env.Service().StartAsync(uid, default))!.Rewarded.Should().BeTrue();

        var extra = (await env.Service().StartAsync(uid, default))!;
        extra.Rewarded.Should().BeFalse();
        env.Wait(TimeSpan.FromSeconds(91));
        var (r, _) = await env.Service().FinishAsync(uid, new FinishRequest(extra.SessionId, PlayWell(extra.Seed)), default);
        r!.Score.Should().BeGreaterThan(0);
        r.Reward.Should().Be(0);
        env.Balance(uid).Should().Be(0);

        env.Wait(TimeSpan.FromHours(14));   // qua 0h giờ VN
        (await env.Service().GetStatusAsync(uid, default))!.RewardedLeft.Should().Be(RewardedPlaysPerDay);
    }

    [Fact(DisplayName = "Inputs sau tick dam xe bi bo qua, khong cuu duoc van")]
    public void InputsAfterCrash_Ignored()
    {
        var crashed = MiniGameSim.Replay(7, []);   // không đổi làn → sớm muộn cũng đâm
        crashed.Crashed.Should().BeTrue();
        var late = new List<(int, int)> { (crashed.Ticks + 1, 1), (crashed.Ticks + 2, -1) };
        MiniGameSim.Replay(7, late).Should().Be(crashed);
    }
}
