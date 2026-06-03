using System.Text.RegularExpressions;
using LotteryChecker.Api.Models;
using Tesseract;

namespace LotteryChecker.Api.Services;

public class OcrService
{
    private readonly string _tessDataPath;
    private readonly ProvinceMatcher _provinces;

    public OcrService(IConfiguration config, ProvinceMatcher provinces)
    {
        _tessDataPath = config["Tesseract:DataPath"] ?? "./tessdata";
        _provinces = provinces;
    }

    public TicketInfo Extract(byte[] imageBytes)
    {
        using var engine = new TesseractEngine(_tessDataPath, "vie+eng", EngineMode.Default);
        using var img = Pix.LoadFromMemory(imageBytes);
        using var page = engine.Process(img);

        var text = page.GetText();
        var confidence = page.GetMeanConfidence();

        return new TicketInfo
        {
            RawText = text,
            TicketNumber = ExtractTicketNumber(text),
            DrawDate = ExtractDate(text),
            Province = _provinces.FindBestMatch(text),
            OcrConfidence = confidence
        };
    }

    // Số vé: 6 chữ số liên tục. Heuristic loại 19xx/20xx (đó là năm).
    private static string? ExtractTicketNumber(string text)
    {
        var candidates = Regex.Matches(text, @"\b\d{6}\b")
                              .Select(m => m.Value)
                              .Distinct()
                              .ToList();
        if (candidates.Count == 0) return null;
        return candidates.FirstOrDefault(c => !c.StartsWith("19") && !c.StartsWith("20"))
               ?? candidates.First();
    }

    // Ngày: 28-05-2026, 28/05/2026, 28.05.2026, "ngày 28 tháng 5 năm 2026"
    private static DateOnly? ExtractDate(string text)
    {
        var m1 = Regex.Match(text, @"(\d{1,2})[-/.](\d{1,2})[-/.](\d{4})");
        if (m1.Success && TryBuildDate(m1.Groups[1].Value, m1.Groups[2].Value,
                                       m1.Groups[3].Value, out var d1))
            return d1;

        var m2 = Regex.Match(text,
            @"ng[àa]y\s*(\d{1,2}).*?th[áa]ng\s*(\d{1,2}).*?n[ăa]m\s*(\d{4})",
            RegexOptions.IgnoreCase);
        if (m2.Success && TryBuildDate(m2.Groups[1].Value, m2.Groups[2].Value,
                                       m2.Groups[3].Value, out var d2))
            return d2;

        return null;
    }

    private static bool TryBuildDate(string d, string m, string y, out DateOnly result)
    {
        result = default;
        if (int.TryParse(d, out var dd) && int.TryParse(m, out var mm)
            && int.TryParse(y, out var yy)
            && dd is >= 1 and <= 31 && mm is >= 1 and <= 12
            && yy is >= 2020 and <= 2099)
        {
            try { result = new DateOnly(yy, mm, dd); return true; }
            catch { return false; }
        }
        return false;
    }
}
