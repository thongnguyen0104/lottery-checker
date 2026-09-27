using System.Diagnostics;

namespace LotteryChecker.Api.Services;

/// <summary>
/// Đo thời gian từng chặng của một request rồi gộp thành 1 dòng log + 1 object JSON.
///
/// Vì sao không chỉ đo tổng: khi user báo "dò ảnh chậm", con số tổng không nói được chậm
/// ở đâu — nhận ảnh từ điện thoại (mạng yếu), tiền xử lý ImageSharp (CPU), Tesseract cục bộ
/// (CPU), hay gọi OCR.space (mạng ngoài, không kiểm soát được). Ba nguyên nhân đó sửa theo
/// ba cách hoàn toàn khác nhau, nên phải tách ra mới biết chỉnh chỗ nào.
///
/// Hai kiểu chặng:
/// <list type="bullet">
/// <item><see cref="Mark"/> — chặng NỐI TIẾP: tính từ mốc trước, cộng lại đúng bằng tổng.</item>
/// <item><see cref="TrackAsync"/> — nhánh chạy SONG SONG: đo riêng, cố ý chồng lấn nhau nên
/// cộng lại sẽ lớn hơn tổng. Đây là số liệu để biết nhánh nào ghìm chân nhánh kia.</item>
/// </list>
/// </summary>
public sealed class StageTimer
{
    private readonly long _startTicks;
    private readonly Lock _lock = new();
    private long _lastTicks;
    private readonly List<KeyValuePair<string, double?>> _stages = [];

    /// <param name="startTicks">
    /// Mốc bắt đầu, lấy bằng <c>Stopwatch.GetTimestamp()</c>. Truyền mốc lúc Kestrel nhận
    /// request (xem <c>HttpContext.RequestStartTicks()</c>) để chặng đầu tiên gồm cả thời
    /// gian upload ảnh; bỏ trống thì tính từ lúc tạo timer.
    /// </param>
    public StageTimer(long? startTicks = null)
    {
        _startTicks = startTicks ?? Stopwatch.GetTimestamp();
        _lastTicks = _startTicks;
    }

    /// <summary>Chốt một chặng nối tiếp: thời gian (ms) từ mốc trước tới giờ.</summary>
    public double Mark(string name)
    {
        var now = Stopwatch.GetTimestamp();
        double ms;
        lock (_lock)
        {
            ms = Round(Stopwatch.GetElapsedTime(_lastTicks, now).TotalMilliseconds);
            _lastTicks = now;
            _stages.Add(new KeyValuePair<string, double?>(name, ms));
        }
        return ms;
    }

    /// <summary>
    /// Chạy một nhánh và ghi riêng thời gian của nhánh đó. KHÔNG dời mốc nối tiếp, nên vẫn
    /// <see cref="Mark"/> được cả cụm song song sau khi chờ xong tất cả nhánh.
    /// </summary>
    public async Task<T> TrackAsync<T>(string name, Func<Task<T>> work)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            return await work();
        }
        finally
        {
            var ms = Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            lock (_lock) _stages.Add(new KeyValuePair<string, double?>(name, ms));
        }
    }

    /// <summary>
    /// Ghi một số đo con có sẵn (vd thời gian bên trong engine OCR) — KHÔNG dời mốc nối tiếp, nên
    /// không làm lệch tổng; chỉ là chi tiết của một chặng đã <see cref="Mark"/>.
    /// </summary>
    public void Record(string name, double value)
    {
        lock (_lock) _stages.Add(new KeyValuePair<string, double?>(name, Round(value)));
    }

    /// <summary>
    /// Ghi một chặng KHÔNG chạy (vd cloud OCR khi local đã qua validate) thành null, để JSON
    /// luôn đủ key — frontend/benchmark phân biệt được "không chạy" với "chạy mất 0ms".
    /// Không dời mốc nối tiếp.
    /// </summary>
    public void Skip(string name)
    {
        lock (_lock) _stages.Add(new KeyValuePair<string, double?>(name, null));
    }

    /// <summary>Tổng thời gian (ms) từ mốc bắt đầu tới giờ.</summary>
    public double TotalMs => Round(Stopwatch.GetElapsedTime(_startTicks).TotalMilliseconds);

    /// <summary>Object trả về cho frontend: từng chặng + <c>total</c>, đơn vị ms.</summary>
    public Dictionary<string, double?> ToTimings()
    {
        lock (_lock)
        {
            var timings = new Dictionary<string, double?>(_stages.Count + 1);
            foreach (var (name, ms) in _stages)
                timings[name] = ms;
            timings["total"] = TotalMs;
            return timings;
        }
    }

    /// <summary>Một dòng gọn cho log: "receive 12,3ms, preprocess 180,4ms = 2103,7ms".</summary>
    public override string ToString()
    {
        lock (_lock)
            return string.Join(", ", _stages.Select(s => s.Value is { } v ? $"{s.Key} {v}ms" : $"{s.Key} -"))
                   + $" = {TotalMs}ms";
    }

    // Làm tròn 1 chữ số thập phân: đủ để so sánh chặng nào chậm, mà log không đầy số rác.
    private static double Round(double ms) => Math.Round(ms, 1);
}
