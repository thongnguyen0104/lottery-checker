namespace LotteryChecker.Api.Services;

// Giờ xổ XSKT theo miền + quy ước "ngày mới nhất đã có kết quả trên web".
// LƯU Ý múi giờ: server prod (Oracle Cloud) chạy UTC — nếu dùng DateTime.Now thì 10:00 UTC
// bị hiểu là "chưa xổ" trong khi VN đã 17:00. Nên mọi so sánh giờ đều quy về giờ VN.
public static class DrawSchedule
{
    private static readonly TimeOnly MnDraw = new(16, 15);   // Miền Nam
    private static readonly TimeOnly MtDraw = new(17, 15);   // Miền Trung
    private static readonly TimeOnly MbDraw = new(18, 15);   // Miền Bắc

    // Kết quả lên web trễ hơn giờ xổ vài chục phút (xổ xong mới nhập đủ 18 số/đài).
    private static readonly TimeSpan PublishDelay = TimeSpan.FromMinutes(30);

    private static readonly HashSet<string> MienTrung = new(StringComparer.OrdinalIgnoreCase)
    {
        "PhuYen", "Hue", "DakLak", "QuangNam", "KhanhHoa", "DaNang", "BinhDinh",
        "QuangTri", "QuangBinh", "GiaLai", "NinhThuan", "KonTum", "QuangNgai",
    };

    public static readonly TimeZoneInfo VietnamZone = ResolveVietnamZone();

    /// <summary>"MN" | "MT" | "MB" từ code đài (mặc định MN).</summary>
    public static string RegionOf(string province) =>
        string.Equals(province, "MB", StringComparison.OrdinalIgnoreCase) ? "MB"
        : MienTrung.Contains(province) ? "MT"
        : "MN";

    public static TimeOnly DrawTimeOf(string province) => RegionOf(province) switch
    {
        "MB" => MbDraw,
        "MT" => MtDraw,
        _    => MnDraw,
    };

    /// <summary>Giờ VN hiện tại (TimeProvider để test bơm giờ giả).</summary>
    public static DateTime NowVn(TimeProvider? time = null) =>
        TimeZoneInfo.ConvertTimeFromUtc(
            (time ?? TimeProvider.System).GetUtcNow().UtcDateTime, VietnamZone);

    /// <summary>Thời điểm xổ của (ngày, đài) — giờ VN.</summary>
    public static DateTime DrawMoment(DateOnly date, string province) =>
        date.ToDateTime(DrawTimeOf(province));

    /// <summary>Vé của (ngày, đài) này đã đến giờ xổ chưa?</summary>
    public static bool HasDrawn(DateOnly date, string province, DateTime nowVn) =>
        nowVn >= DrawMoment(date, province);

    /// <summary>Ngày gần nhất mà kết quả MN đã xổ VÀ đã lên web — mốc để biết cần cào tới đâu.</summary>
    public static DateOnly LatestPublishedDate(DateTime nowVn)
    {
        var today = DateOnly.FromDateTime(nowVn);
        return nowVn.TimeOfDay >= MnDraw.ToTimeSpan() + PublishDelay ? today : today.AddDays(-1);
    }

    private static TimeZoneInfo ResolveVietnamZone()
    {
        // ID kiểu IANA và kiểu Windows — .NET tự map được, nhưng thử cả 2 cho chắc.
        foreach (var id in new[] { "Asia/Ho_Chi_Minh", "SE Asia Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("VN+7", TimeSpan.FromHours(7), "Vietnam", "Vietnam");
    }
}
