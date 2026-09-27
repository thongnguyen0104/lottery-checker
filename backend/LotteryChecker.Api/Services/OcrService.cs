using System.Text;
using LotteryChecker.Api.Models;
using Tesseract;

namespace LotteryChecker.Api.Services;

/// <summary>Engine OCR cục bộ dùng Tesseract (vie+eng), chạy nhiều lượt PageSegMode rồi gộp text.</summary>
public class OcrService : ITicketOcrEngine
{
    private readonly TesseractEnginePool _engines;
    private readonly TicketTextParser _parser;

    public OcrService(TesseractEnginePool engines, TicketTextParser parser)
    {
        _engines = engines;
        _parser = parser;
    }

    public string Name => "tesseract";

    public TicketInfo Extract(byte[] imageBytes) => Extract(imageBytes, null);

    public TicketInfo Extract(byte[] imageBytes, PageSegMode? psm)
    {
        // Vé số có bố cục phức tạp (hình, QR, số cách điệu) → 1 PSM không đủ.
        // Chạy nhiều lượt PSM rồi gộp text để trích trường tốt nhất.
        // (psm != null: chỉ 1 lượt — dùng cho endpoint debug.)
        var modes = psm.HasValue
            ? new[] { psm.Value }
            : new[] { PageSegMode.SingleColumn, PageSegMode.SingleBlock, PageSegMode.SparseText };

        // Chạy song song: mỗi lượt là một lần OCR đầy đủ (~2s trên ảnh 1600px), nối tiếp thì
        // cộng dồn. Ghi kết quả theo đúng chỉ số của modes để text gộp ra GIỐNG HỆT bản tuần tự —
        // thứ tự đổi là PickTicketNumber có thể chọn khác số khi hoà phiếu.
        var texts = new string[modes.Length];
        var confidences = new float[modes.Length];
        Parallel.For(0, modes.Length,
            new ParallelOptions
            {
                // Không vượt quá số core: OCR nặng CPU, mở thêm luồng chỉ làm các lượt tranh nhau chậm đi.
                // (Đã thử ép OMP_THREAD_LIMIT=1 để các lượt "thật sự" song song: chậm hơn 50% —
                //  Tesseract tự đa luồng bên trong một lượt hiệu quả hơn ta chia tay.)
                MaxDegreeOfParallelism = Math.Min(modes.Length, Environment.ProcessorCount)
            },
            i =>
            {
                using var lease = _engines.Rent();
                // Mỗi luồng tự nạp Pix riêng: leptonica Pix không đảm bảo an toàn khi 2 engine
                // cùng đọc một ảnh. Giải mã lại ảnh đã nhị phân hoá chỉ tốn vài chục ms.
                using var img = Pix.LoadFromMemory(imageBytes);
                using var page = lease.Engine.Process(img, modes[i]);
                texts[i] = page.GetText();
                confidences[i] = page.GetMeanConfidence();
            });

        var sb = new StringBuilder();
        foreach (var t in texts) sb.AppendLine(t);

        return _parser.Parse(sb.ToString(), confidences.Max());
    }
}
