using LotteryChecker.Api.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace LotteryChecker.Api.Services;

/// <summary>
/// Engine đọc chữ trên ảnh vé đã tiền xử lý. Có nhiều bản cài (Tesseract, ONNX PP-OCRv5) để
/// đổi qua lại bằng cấu hình <c>Ocr:Engine</c> mà không phải sửa code — đo được engine nào
/// nhanh/chính xác hơn trên máy thật thay vì đoán.
/// </summary>
public interface ITicketOcrEngine
{
    /// <summary>Tên engine, đưa vào log để biết kết quả là do ai đọc.</summary>
    string Name { get; }

    /// <summary>Đọc ảnh đã encode (PNG/JPEG) → thông tin vé, CHƯA merge cloud OCR.</summary>
    TicketInfo Extract(byte[] imageBytes);

    /// <summary>
    /// Đọc ảnh đang nằm trong RAM. Mặc định encode PNG rồi gọi bản byte[]; engine nào nhận
    /// thẳng pixel được (ONNX) thì override để bỏ qua vòng encode → decode vô ích.
    /// </summary>
    TicketInfo Extract(Image<Rgba32> image) => Extract(ImagePreprocessor.EncodePng(image));
}
