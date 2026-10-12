namespace LotteryChecker.Api.Services;

/// <summary>
/// Mô phỏng minigame đua xe né chướng ngại — server chạy lại ván từ seed + chuỗi thao tác để tự tính điểm
/// (không tin điểm client gửi). PHẢI giống hệt frontend/src/minigame/sim.ts: chỉ số nguyên, cùng PRNG,
/// cùng thứ tự gọi PRNG. Đổi gì ở đây thì đổi cả bên kia và sinh lại minigame-golden.json.
/// </summary>
public sealed class MiniGameSim
{
    public const int Lanes = 3;
    public const int TicksPerSecond = 30;
    public const int MaxTicks = 90 * TicksPerSecond;
    public const int FirstRowAt = 18_000;
    public const int RowGap = 12_000;

    /// <summary>Quãng đường đi trong tick này — tăng dần từ 300 tới 750.</summary>
    public static int Speed(int tick) => 300 + tick / 6;

    /// <summary>Một hàng chướng ngại: Blocked là bitmask làn bị chặn (luôn chừa ≥ 1 làn); CoinLane −1 = không có xu.</summary>
    public readonly record struct Row(int Blocked, int CoinLane);

    private uint _rng;
    private readonly List<Row> _rows = [];

    public int Tick { get; private set; }
    public int Lane { get; private set; } = 1;
    public long Distance { get; private set; }
    public int Score { get; private set; }
    public int Coins { get; private set; }
    public bool Crashed { get; private set; }
    public bool Over => Crashed || Tick >= MaxTicks;
    private int _nextRow;

    public MiniGameSim(uint seed) => _rng = seed;

    private uint Next()
    {
        unchecked
        {
            _rng += 0x6D2B79F5;
            var t = _rng;
            t = (t ^ (t >> 15)) * (t | 1);
            t ^= t + (t ^ (t >> 7)) * (t | 61);
            return t ^ (t >> 14);
        }
    }

    private int NextInt(int n) => (int)(Next() % (uint)n);

    public static long RowY(int i) => FirstRowAt + (long)i * RowGap;

    /// <summary>Hàng thứ i — sinh lần lượt theo thứ tự để PRNG khớp client.</summary>
    public Row GetRow(int i)
    {
        while (_rows.Count <= i)
        {
            var idx = _rows.Count;
            int blocked;
            if (NextInt(100) < Math.Min(60, 20 + idx))
                blocked = 7 & ~(1 << NextInt(Lanes));   // chặn 2 làn
            else
                blocked = 1 << NextInt(Lanes);           // chặn 1 làn
            var coin = -1;
            if (NextInt(2) == 0)
            {
                var free = Enumerable.Range(0, Lanes).Where(l => (blocked & (1 << l)) == 0).ToArray();
                coin = free[NextInt(free.Length)];
            }
            _rows.Add(new Row(blocked, coin));
        }
        return _rows[i];
    }

    /// <summary>Chạy 1 tick; dir −1/0/+1 = đổi làn trước khi chạy (ra ngoài đường thì bỏ qua).</summary>
    public void Step(int dir)
    {
        if (Over) return;
        Lane = Math.Clamp(Lane + dir, 0, Lanes - 1);
        Distance += Speed(Tick);
        Tick++;
        while (RowY(_nextRow) <= Distance)
        {
            var row = GetRow(_nextRow++);
            if ((row.Blocked & (1 << Lane)) != 0) { Crashed = true; return; }
            Score++;
            if (row.CoinLane == Lane) { Score++; Coins++; }
        }
    }

    /// <summary>Kết quả chạy lại: dừng ở tick đâm hoặc hết giờ; thao tác sau đó bị bỏ qua.</summary>
    public record Result(int Score, int Coins, int Ticks, bool Crashed);

    /// <summary>inputs: (tick, dir) đã kiểm tra hợp lệ — tick tăng dần, dir ±1.</summary>
    public static Result Replay(uint seed, IReadOnlyList<(int Tick, int Dir)> inputs)
    {
        var sim = new MiniGameSim(seed);
        var i = 0;
        while (!sim.Over)
        {
            var dir = 0;
            if (i < inputs.Count && inputs[i].Tick == sim.Tick) dir = inputs[i++].Dir;
            sim.Step(dir);
        }
        return new Result(sim.Score, sim.Coins, sim.Tick, sim.Crashed);
    }
}
