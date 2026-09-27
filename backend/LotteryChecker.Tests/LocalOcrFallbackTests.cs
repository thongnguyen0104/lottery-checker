using FluentAssertions;
using LotteryChecker.Api.Models;
using LotteryChecker.Api.Services;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace LotteryChecker.Tests;

// Luật quyết định "OCR cục bộ đủ tin → KHÔNG gọi OCR.space". Sai ở đây nghĩa là hoặc chậm
// (gọi cloud thừa), hoặc tệ hơn: trả số vé sai mà không cho cloud cơ hội sửa.
public class LocalOcrFallbackTests
{
    private static readonly DateOnly Today = new(2026, 6, 10);

    private static TicketResultValidator Validator(double minConfidence = 0.80) =>
        new(new FixedTimeProvider(new DateTimeOffset(Today.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero)),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ocr:Validation:MinConfidence"] = minConfidence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            }).Build());

    private static readonly TicketTextParser Parser = new(new ProvinceMatcher());

    private const string ClearTicket =
        "XO SO KIEN THIET BINH DUONG\n288921\nMo thuong ngay 05-06-2026\n288921 D\nGia 10.000d";

    [Fact(DisplayName = "1. Vé rõ: đủ số/ngày/đài + confidence cao → PASS, không cần cloud")]
    public void ClearTicket_Passes()
    {
        var check = Validator().Validate(Parser.Parse(ClearTicket, 0.93));
        check.Passed.Should().BeTrue();
        check.Reasons.Should().BeEmpty();
    }

    [Fact(DisplayName = "2. Confidence thấp dù đủ trường → FAIL (low_confidence)")]
    public void LowConfidence_Fails()
    {
        var check = Validator().Validate(Parser.Parse(ClearTicket, 0.6));
        check.Passed.Should().BeFalse();
        check.Reasons.Should().Equal("low_confidence");
        check.NumberOk.Should().BeTrue();
    }

    [Fact(DisplayName = "3. Hai số 6 chữ số hoà phiếu → FAIL (number_ambiguous)")]
    public void TiedNumbers_Fail()
    {
        var info = Parser.Parse("BINH DUONG 05-06-2026\n288921\n268921", 0.95);
        info.TicketNumberAmbiguous.Should().BeTrue();
        Validator().Validate(info).Reasons.Should().Contain("number_ambiguous");
    }

    [Fact(DisplayName = "4. Chỉ đọc được số khi sửa O→0/l→1 → có số để điền sẵn nhưng FAIL (number_normalized)")]
    public void NormalizedNumber_PrefillsButFails()
    {
        var info = Parser.Parse("BINH DUONG 05-06-2026\n28892l\n2O8921x", 0.95);
        info.TicketNumber.Should().Be("288921");
        info.TicketNumberNormalized.Should().BeTrue();
        Validator().Validate(info).Reasons.Should().Contain("number_normalized");
    }

    [Theory(DisplayName = "5. Không sửa ký tự nhầm khi token không chắc là số")]
    [InlineData("SOLIS1")]      // chỉ 1 chữ số — là chữ, không phải số bị đọc nhầm
    [InlineData("288921D")]     // có bản đọc sạch rồi thì không cần đoán (và dính chữ)
    [InlineData("A28892l")]     // dính chữ cái phía trước
    public void Normalization_IsConservative(string token)
    {
        TicketTextParser.NormalizedCandidates(token).Should().BeEmpty();
    }

    [Fact(DisplayName = "6. Ngày lệch quá xa hôm nay → FAIL (date_out_of_range)")]
    public void FarDate_Fails()
    {
        var info = Parser.Parse("BINH DUONG 288921\n05-06-2028", 0.95);
        Validator().Validate(info).Reasons.Should().Equal("date_out_of_range");
    }

    [Fact(DisplayName = "7. Đài chỉ khớp gần đúng → FAIL (province_fuzzy); thiếu đài → province_missing")]
    public void Province_MustBeExact()
    {
        // Đúng lý do phải bác khớp gần đúng: "DAKLAX" (định đọc DakLak) lại khớp "da lat" trước.
        var fuzzy = Parser.Parse("XSKT DAKLAX 288921 05-06-2026", 0.95);
        fuzzy.Province.Should().NotBeNull();
        fuzzy.ProvinceExact.Should().BeFalse();
        Validator().Validate(fuzzy).Reasons.Should().Equal("province_fuzzy");

        Validator().Validate(Parser.Parse("288921 05-06-2026", 0.95)).Reasons.Should().Equal("province_missing");
    }

    [Fact(DisplayName = "8. Merge: local chỉ thiếu đài → giữ số vé local, lấy đài của cloud")]
    public void Merge_KeepsValidLocalNumber()
    {
        var local = Parser.Parse("288921 05-06-2026 288921", 0.95);
        var check = Validator().Validate(local);

        var merged = Merge(local, check, "XSKT BINH DUONG\n268921");

        merged.TicketNumber.Should().Be("288921");
        merged.TicketNumberFromCloud.Should().BeFalse();
        merged.Province.Should().Be("BinhDuong");
    }

    [Fact(DisplayName = "9. Merge: số vé local không hợp lệ → lấy số vé cloud; ngày bị bác → lấy ngày cloud")]
    public void Merge_ReplacesInvalidFields()
    {
        var local = Parser.Parse("BINH DUONG 288921 268921 05-06-2028", 0.95);
        var check = Validator().Validate(local);

        var merged = Merge(local, check, "288921 D\n05-06-2026");

        merged.TicketNumber.Should().Be("288921");
        merged.TicketNumberFromCloud.Should().BeTrue();
        merged.DrawDate.Should().Be(new DateOnly(2026, 6, 5));
        Validator().Validate(merged).Passed.Should().BeTrue();
    }

    [Fact(DisplayName = "9b. Vé cũ thật: ngày local đúng nhưng ngoài khoảng, cloud đọc lệch cũng ngoài khoảng → giữ ngày local")]
    public void Merge_DoesNotReplaceDateWithImplausibleCloudDate()
    {
        // Tình huống thật (Image.jfif): local 05-6-2026 đúng, cloud đọc 04-6-2026 → trước đây bị đè.
        var local = Parser.Parse("BINH DUONG 288921 288921 05-03-2026", 0.95);
        var check = Validator().Validate(local);
        check.Reasons.Should().Equal("date_out_of_range");

        Merge(local, check, "288921 D\n04-03-2026").DrawDate.Should().Be(new DateOnly(2026, 3, 5));
    }

    [Fact(DisplayName = "19. Vé hết hạn, ngày đọc khớp ≥2 chỗ, số + đài chắc → bỏ qua cloud")]
    public void Expired_ConsistentDate_SkipsCloud()
    {
        var info = Parser.Parse("BINH DUONG 288921 288921 05-03-2026\nMo ngay 05-03-2026", 0.95);
        var check = Validator().Validate(info);
        check.Reasons.Should().Equal("date_out_of_range");
        Validator().IsConfidentlyExpired(info, check).Should().BeTrue();
    }

    [Fact(DisplayName = "19b. Vé thật Bình Dương cũ: ngày đầy đủ 1 chỗ + mảnh '6-2026' xác nhận → 2 phiếu")]
    public void RealTicket_OldBinhDuong_MonthYearCorroborates()
    {
        const string text = "XÓ\nSÔ\nKIEN\nTHIÊT\nBINH\nDUONG\n10.0000\nSoan tin: XSBD gÜi dén 997\n288921\nD\n" +
                            "288921\n05-6-2026\n06K23\n6-2026\nCTYTR\n*MB ng.0562028:\nDUONG\n926";
        TicketTextParser.AnalyzeDate(text).Should().Be((new DateOnly(2026, 6, 5), 2));
        // Không có mảnh nào khác → ngày đầy đủ không tự xác nhận chính nó.
        TicketTextParser.AnalyzeDate("05-6-2026").Votes.Should().Be(1);
        // Mảnh lệch năm (đọc nhầm) không được tính.
        TicketTextParser.AnalyzeDate("05-6-2026\n6-2025").Votes.Should().Be(1);
    }

    [Fact(DisplayName = "20. Ngày cũ chỉ đọc được 1 chỗ (có thể đọc nhầm năm) → vẫn gọi cloud")]
    public void Expired_SingleDateReading_StillUsesCloud()
    {
        var info = Parser.Parse("BINH DUONG 288921 288921 05-06-2025", 0.95);
        var check = Validator().Validate(info);
        check.Reasons.Should().Equal("date_out_of_range");
        Validator().IsConfidentlyExpired(info, check).Should().BeFalse();
    }

    [Fact(DisplayName = "21. Ngày quá cũ nhưng còn lỗi khác (thiếu đài) hoặc ngày ở tương lai → vẫn gọi cloud")]
    public void Expired_OtherProblems_StillUsesCloud()
    {
        var noProvince = Parser.Parse("288921 288921 05-03-2026 05-03-2026", 0.95);
        Validator().IsConfidentlyExpired(noProvince, Validator().Validate(noProvince)).Should().BeFalse();

        var future = Parser.Parse("BINH DUONG 288921 288921 05-06-2028 05-06-2028", 0.95);
        Validator().IsConfidentlyExpired(future, Validator().Validate(future)).Should().BeFalse();
    }

    [Fact(DisplayName = "22. Số vé chắc, chỉ đài/ngày chưa chắc → KHÔNG gọi cloud; form đánh dấu đúng trường")]
    public void OnlyProvinceOrDateUncertain_NoCloud_MarksFields()
    {
        var noProvince = Parser.Parse("288921 288921 05-06-2026", 0.95);
        TicketResultValidator.CloudCanHelp(Validator().Validate(noProvince)).Should().BeFalse();
        Validator().FieldsToReview(noProvince).Should().Equal("province");

        var futureDate = Parser.Parse("BINH DUONG 288921 288921 05-06-2028", 0.95);
        TicketResultValidator.CloudCanHelp(Validator().Validate(futureDate)).Should().BeFalse();
        Validator().FieldsToReview(futureDate).Should().Equal("date");
    }

    [Fact(DisplayName = "23. Số vé có rủi ro (mơ hồ / confidence thấp) → vẫn gọi cloud")]
    public void TicketNumberRisk_UsesCloud()
    {
        var tied = Parser.Parse("BINH DUONG 05-06-2026 288921 268921", 0.95);
        TicketResultValidator.CloudCanHelp(Validator().Validate(tied)).Should().BeTrue();
        Validator().FieldsToReview(tied).Should().Equal("number");

        var lowConf = Parser.Parse(ClearTicket, 0.6);
        TicketResultValidator.CloudCanHelp(Validator().Validate(lowConf)).Should().BeTrue();
    }

    [Fact(DisplayName = "24. Vé cũ (ngày quá khứ ngoài khoảng) không bị đánh dấu ngày — đã có banner 'Vé hết hạn'")]
    public void OldPastDate_NotMarkedForReview()
    {
        var old = Parser.Parse("BINH DUONG 288921 288921 05-03-2026", 0.95);
        Validator().FieldsToReview(old).Should().BeEmpty();
    }

    // ---- Dò luôn (bỏ qua form xác nhận): sai ở đây là dò vé bằng số/đài/ngày máy đọc nhầm ----

    [Fact(DisplayName = "25. Dò luôn: vé rõ, OCR cục bộ qua hết luật")]
    public void AutoCheck_ClearLocalTicket()
    {
        var info = Parser.Parse(ClearTicket, 0.93);
        Validator().CanAutoCheck(info, Validator().Validate(info)).Should().BeTrue();
    }

    [Fact(DisplayName = "26. Không dò luôn: đủ trường nhưng confidence thấp, cloud lỗi — dù needsReview rỗng")]
    public void AutoCheck_LowConfidenceWithoutCloud()
    {
        var info = Parser.Parse(ClearTicket, 0.6);
        Validator().FieldsToReview(info).Should().BeEmpty();
        Validator().CanAutoCheck(info, Validator().Validate(info)).Should().BeFalse();
    }

    [Fact(DisplayName = "27. Dò luôn: confidence local thấp nhưng số vé đã được cloud đọc lại")]
    public void AutoCheck_NumberFromCloud()
    {
        var local = Parser.Parse(ClearTicket, 0.6);
        var check = Validator().Validate(local);
        var merged = Merge(local, check, "XO SO KIEN THIET BINH DUONG 288921 05-06-2026");
        merged.TicketNumberFromCloud.Should().BeTrue();
        Validator().CanAutoCheck(merged, check).Should().BeTrue();

        // Cloud trả lời mà không có số vé → vẫn là số local confidence thấp → không dò luôn.
        var noNumber = Parser.Parse(ClearTicket, 0.6);
        var noNumberCheck = Validator().Validate(noNumber);
        Validator().CanAutoCheck(Merge(noNumber, noNumberCheck, "BINH DUONG"), noNumberCheck).Should().BeFalse();
    }

    [Fact(DisplayName = "28. Không dò luôn: thiếu đài / đài gần đúng / ngày tương lai xa")]
    public void AutoCheck_UncertainProvinceOrDate()
    {
        foreach (var text in new[]
                 {
                     "288921 288921 05-06-2026",                    // thiếu đài
                     "vinh long 288921 can tho 05-06-2026",         // hai đài ngang điểm
                     "BINH DUONG 288921 288921 05-06-2028",         // ngày ngoài khoảng ở tương lai
                 })
        {
            var info = Parser.Parse(text, 0.95);
            Validator().CanAutoCheck(info, Validator().Validate(info)).Should().BeFalse(text);
        }
    }

    [Fact(DisplayName = "29. Ngày cũ: khớp ≥2 chỗ → dò luôn (ra 'Vé hết hạn'); 1 chỗ có thể nhầm năm → hỏi lại")]
    public void AutoCheck_OldDateNeedsTwoReadings()
    {
        var sure = Parser.Parse("BINH DUONG 288921 288921 05-03-2026 05-03-2026", 0.95);
        Validator().CanAutoCheck(sure, Validator().Validate(sure)).Should().BeTrue();

        var single = Parser.Parse("BINH DUONG 288921 288921 05-06-2025", 0.95);
        Validator().FieldsToReview(single).Should().BeEmpty();
        Validator().CanAutoCheck(single, Validator().Validate(single)).Should().BeFalse();
    }

    [Fact(DisplayName = "30. Dò luôn: chỉ AI đọc (OCR cục bộ tắt), đủ trường hợp lệ")]
    public void AutoCheck_CloudOnly()
    {
        var info = new TicketInfo
        {
            TicketNumber = "288921", TicketNumberFromCloud = true,
            DrawDate = new DateOnly(2026, 6, 5), DrawDateVotes = 1,
            Province = "BinhDuong", ProvinceExact = true, OcrConfidence = GeminiTicketReader.AssumedConfidence,
        };
        Validator().CanAutoCheck(info, localCheck: null).Should().BeTrue();

        info.Province = null;
        Validator().CanAutoCheck(info, localCheck: null).Should().BeFalse();
    }

    // Gọi merge đúng như ScanController.
    private static TicketInfo Merge(TicketInfo local, TicketValidation check, string cloudText) =>
        Parser.MergeFromCloudText(local, cloudText,
            preferCloudNumber: !check.NumberOk || !check.ConfidenceOk,
            replaceDateIf: check.DateOk ? null : Validator().IsPlausibleDrawDate);

    // ---- Hồi quy từ text OCR (PP-OCRv5) của vé thật trong backend/TestData ----

    [Fact(DisplayName = "11. Vé Vĩnh Long in ở Cần Thơ → VinhLong, không phải CanTho (falsePass cũ)")]
    public void RealTicket_VinhLong_NotPrinterProvince()
    {
        const string text = "XÖ SÔ KIÊN THIET VINH LONG\n10000!\nKIEN THIÉT VINA LONG\nBón MQt By\nNam Hai Ba\n" +
                            "N TAICTY CÓ PHÁN IN TÓNG HOP CÁN THO\nTHIE\n417523\n417523\n25-09-2026\n" +
                            "Soan: XSVL gú1 997 nhan KQXS Vinh Long\n17523\nLoai 47 VL 39\nmó ngày thú sáu\n25109//2026";
        var info = Parser.Parse(text, 0.92);
        info.Province.Should().Be("VinhLong");
        info.ProvinceAmbiguous.Should().BeFalse();
        info.TicketNumber.Should().Be("417523");
        info.DrawDate.Should().Be(new DateOnly(2026, 9, 25));
    }

    [Fact(DisplayName = "12. Tên đài bị OCR tách 2 dòng (BINH\\nDUONG) vẫn khớp")]
    public void RealTicket_ProvinceSplitAcrossLines()
    {
        Parser.Parse("XÓ\nSO\nKIÊN\nTHIÊT\nBINH\nDUONG\n10.0000", 0.9).Province.Should().Be("BinhDuong");
    }

    [Fact(DisplayName = "13. Ngày đọc nhầm '-' thành ':' (25:9-2026) vẫn parse được")]
    public void RealTicket_DateWithColon()
    {
        TicketTextParser.ExtractDate("467534\n2-09K39\n25-9-20\n-2026\nDUONG\n\"Ma: nday 25:9-2026:")
            .Should().Be(new DateOnly(2026, 9, 25));
    }

    [Fact(DisplayName = "14. Ngày: bản đọc đúng xuất hiện nhiều hơn thắng bản đọc lệch đứng trước")]
    public void Date_MajorityWins()
    {
        TicketTextParser.ExtractDate("05-6-2025\n05-6-2026\nMo ngay 05-6-2026").Should().Be(new DateOnly(2026, 6, 5));
    }

    [Theory(DisplayName = "15. Tên đài phải là từ trọn vẹn — không khớp giữa từ khác")]
    [InlineData("serial number 288921")]   // "mb" trong "number"
    [InlineData("thue VAT 10%")]           // "hue" trong "thue"
    public void Province_RequiresWordBoundary(string text)
    {
        new ProvinceMatcher().Match(text).Should().BeNull();
    }

    [Fact(DisplayName = "16. Hai đài ngang điểm → Ambiguous → FAIL (province_ambiguous)")]
    public void Province_TieIsAmbiguous()
    {
        var info = Parser.Parse("vinh long 288921 can tho 05-09-2026", 0.95);
        info.ProvinceAmbiguous.Should().BeTrue();
        Validator().Validate(info).Reasons.Should().Contain("province_ambiguous");
    }

    [Fact(DisplayName = "17. Vé Đồng Tháp đọc lệch 'DÔNG THIÁP' → gần đúng DongThap, không phải MB (từ 'Hai' ≈ 'hanoi')")]
    public void RealTicket_DongThap_FuzzyNotMienBac()
    {
        const string text = "XÓ SO KIEN THIET DÔNG THIÁP\n10.000d\nDÔNG\n801024\nGAIDBET 6CHÜ SÓ\nV38\nS\n2IY\nIDONG\n" +
                            "21-09-2026\nTám Không Môt Không Hai Bön\nHI\n00\nKy vé V38\n801024\nMô ngày thú hai\n21-09-2026";
        var m = new ProvinceMatcher().Match(text);
        m.Should().NotBeNull();
        m!.Code.Should().Be("DongThap");
        m.Exact.Should().BeFalse();       // vẫn là gần đúng → validator vẫn nhờ cloud xác nhận
    }

    [Fact(DisplayName = "18. Tên tỉnh ở dòng nhà in không được dùng kể cả khi khớp gần đúng")]
    public void Fuzzy_IgnoresPrinterLine()
    {
        new ProvinceMatcher().Match("GIAI DAC BIET 912990\nIn tai XN In Tai Chinh TP HCM").Should().BeNull();
    }

    // ---- Lượt đọc lại trên ảnh lọc khác (ScanController → FillMissingFromRetry) ----

    private static IReadOnlyList<string> Fill(TicketInfo primary, TicketInfo retry)
    {
        var validator = Validator();
        return TicketTextParser.FillMissingFromRetry(primary, retry, validator.Validate(primary),
                                                     validator.IsPlausibleDrawDate, validator.IsPossibleDrawDate);
    }

    [Fact(DisplayName = "19. Đọc lại: năm bị cụt ('05-06-202') → lấp ngày từ lượt lại, số vé giữ nguyên, PASS")]
    public void Retry_FillsTruncatedDate()
    {
        // Như vé Vĩnh Long thật: ảnh gốc đọc "25-09-202", ảnh tăng tương phản đọc đủ "25-09-2026".
        var primary = Parser.Parse("XO SO KIEN THIET BINH DUONG\n288921\nMo thuong ngay 05-06-202\n288921 D", 0.93);
        primary.DrawDate.Should().BeNull();
        var retry = Parser.Parse(ClearTicket.Replace("288921", "288927"), 0.90);

        Fill(primary, retry).Should().Equal("date");

        primary.DrawDate.Should().Be(new DateOnly(2026, 6, 5));
        primary.TicketNumber.Should().Be("288921");     // số vé luôn của lượt chính
        primary.OcrConfidence.Should().Be(0.93);
        Validator().Validate(primary).Passed.Should().BeTrue();
    }

    [Fact(DisplayName = "20. Đọc lại: không đè ngày đã hợp lệ (vé Bình Dương thật: ảnh tương phản đọc 2026 → 2028)")]
    public void Retry_NeverOverwritesValidDate()
    {
        var primary = Parser.Parse("XO SO KIEN THIET DONG THAP\n801024\n05-06-2026", 0.93);
        var retry = Parser.Parse("XO SO KIEN THIET DONG THAP\n801024\n05-06-2028", 0.93);
        Validator().Validate(primary).DateOk.Should().BeTrue();

        Fill(primary, retry).Should().NotContain("date");
        primary.DrawDate.Should().Be(new DateOnly(2026, 6, 5));
    }

    [Theory(DisplayName = "21. Đọc lại: ngày lượt chính ngoài khoảng chỉ bị thay bằng ngày HỢP LÝ")]
    [InlineData("05-06-2026", "2026-06-05")]   // lượt lại hợp lý → thay
    [InlineData("05-06-2021", "2020-06-05")]   // lượt lại cũng ngoài khoảng → giữ ngày lượt chính
    public void Retry_ReplacesOutOfRangeDateOnlyWithPlausible(string retryDate, string expected)
    {
        var primary = Parser.Parse("XO SO KIEN THIET BINH DUONG\n288921\n05-06-2020", 0.93);
        Validator().Validate(primary).Reasons.Should().Contain("date_out_of_range");

        Fill(primary, Parser.Parse($"XO SO KIEN THIET BINH DUONG\n288921\n{retryDate}", 0.93));

        primary.DrawDate.Should().Be(DateOnly.Parse(expected));
    }

    [Theory(DisplayName = "23. Đọc lại: lượt chính không có ngày → nhận ngày cũ (vé hết hạn) nhưng KHÔNG nhận ngày tương lai xa")]
    [InlineData("05-06-2026", "2026-06-05")]   // hợp lý
    [InlineData("05-06-2025", "2025-06-05")]   // vé cũ thật → vẫn lấy để báo hết hạn
    [InlineData("05-06-2096", null)]           // vé 288921 thật: cắt dòng đọc 2026 → 2096 → bỏ, để lượt sau còn lấp được
    public void Retry_RejectsImpossibleFutureDate(string retryDate, string? expected)
    {
        var primary = Parser.Parse("XO SO KIEN THIET BINH DUONG\n288921", 0.93);
        primary.DrawDate.Should().BeNull();

        Fill(primary, Parser.Parse($"XO SO KIEN THIET BINH DUONG\n288921\n{retryDate}", 0.93));

        primary.DrawDate.Should().Be(expected is null ? null : DateOnly.Parse(expected));
    }

    [Fact(DisplayName = "22. Đọc lại: đài gần đúng → thay bằng đài khớp nguyên văn; lượt lại không có đài → giữ")]
    public void Retry_ProvinceOnlyWhenSurer()
    {
        const string fuzzyDongThap = "XÓ SO KIEN THIET DÔNG THIÁP\n801024\n05-06-2026";

        var primary = Parser.Parse(fuzzyDongThap, 0.93);
        Validator().Validate(primary).Reasons.Should().Equal("province_fuzzy");
        Fill(primary, Parser.Parse("XO SO KIEN THIET DONG THAP\n801024\n05-06-2026", 0.93)).Should().Equal("province");
        primary.ProvinceExact.Should().BeTrue();
        Validator().Validate(primary).Passed.Should().BeTrue();

        var kept = Parser.Parse(fuzzyDongThap, 0.93);
        Fill(kept, Parser.Parse("801024\n05-06-2026", 0.93)).Should().BeEmpty();
        kept.Province.Should().Be("DongThap");
        kept.ProvinceExact.Should().BeFalse();
    }

    [Fact(DisplayName = "10. JPEG cho cloud: ảnh nhỏ encode 1 lần; ảnh nhiễu to vẫn ép được dưới giới hạn")]
    public void CloudJpeg_StaysUnderLimit()
    {
        using var small = new Image<Rgba32>(800, 600, Color.White);
        ImagePreprocessor.EncodeJpegUnderLimit(small).Length.Should().BeLessThan(1_000_000);

        // Nhiễu ngẫu nhiên = trường hợp xấu nhất cho JPEG: q85 vượt 1MB → buộc tìm chất lượng thấp hơn.
        using var noisy = new Image<Rgba32>(1600, 1000);
        var rnd = new Random(42);
        noisy.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
                foreach (ref var p in rows.GetRowSpan(y))
                    p = new Rgba32((byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256));
        });
        ImagePreprocessor.EncodeJpegUnderLimit(noisy).Length.Should().BeLessThan(1_000_000);
    }
}
