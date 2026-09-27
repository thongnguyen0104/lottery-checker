using System.Collections.Concurrent;
using Tesseract;

namespace LotteryChecker.Api.Services;

/// <summary>
/// Kho engine Tesseract dùng lại được.
///
/// Vì sao cần: mỗi <c>new TesseractEngine</c> phải nạp lại vie+eng traineddata từ đĩa và dựng
/// mạng LSTM — vài trăm ms, làm lại ở MỌI request là lãng phí thuần tuý. Nhưng engine KHÔNG
/// thread-safe nên cũng không thể dùng chung một cái: pool cho mượn–trả, giới hạn số engine
/// sống cùng lúc để không phình RAM (mỗi engine ~30–50MB).
///
/// Singleton: engine tạo ra sống suốt vòng đời app, request sau mượn lại engine của request trước.
/// </summary>
public sealed class TesseractEnginePool : IDisposable
{
    private readonly ConcurrentBag<TesseractEngine> _idle = new();
    private readonly SemaphoreSlim _slots;
    private readonly string _dataPath;
    private readonly string _languages;
    private volatile bool _disposed;

    public TesseractEnginePool(IConfiguration config)
    {
        _dataPath = config["Tesseract:DataPath"] ?? "./tessdata";
        // ĐỪNG rút xuống "vie" cho nhanh: đo trên ảnh mẫu thì Tesseract nhanh hơn ~1s nhưng đọc
        // SAI ngày (08-05 thay vì 06-05) và mất luôn số vé. Gói eng gánh phần chữ số/latinh.
        _languages = config["Tesseract:Languages"] ?? "vie+eng";

        // Mặc định = số core: nhiều engine hơn số core chỉ tốn RAM chứ không nhanh thêm,
        // vì OCR là việc nặng CPU. Tối thiểu 2 để 1 request vẫn chạy được vài lượt PSM song song.
        var maxEngines = config.GetValue<int?>("Tesseract:MaxEngines")
                         ?? Math.Max(2, Environment.ProcessorCount);
        _slots = new SemaphoreSlim(maxEngines, maxEngines);
    }

    /// <summary>
    /// Mượn một engine; chờ nếu đã hết chỗ. Nhớ <c>using</c> để trả engine về kho —
    /// quên trả thì pool cạn dần và request sau treo vô hạn.
    /// </summary>
    public Lease Rent(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _slots.Wait(ct);
        try
        {
            if (!_idle.TryTake(out var engine))
                engine = new TesseractEngine(_dataPath, _languages, EngineMode.Default);
            return new Lease(this, engine);
        }
        catch
        {
            _slots.Release();   // tạo engine lỗi → trả chỗ, nếu không pool rò rỉ slot
            throw;
        }
    }

    /// <summary>
    /// Tạo sẵn vài engine để request ĐẦU TIÊN không phải trả giá nạp traineddata (~1s mỗi engine).
    /// Giữ hết lease rồi mới trả: nếu mượn–trả lần lượt thì lần sau lại lấy đúng engine cũ ra,
    /// kho vẫn chỉ có 1 cái.
    /// </summary>
    public void Warmup(int count)
    {
        var leases = new List<Lease>(count);
        try
        {
            for (var i = 0; i < count; i++) leases.Add(Rent());
        }
        finally
        {
            foreach (var lease in leases) lease.Dispose();
        }
    }

    private void Return(TesseractEngine engine)
    {
        if (_disposed) engine.Dispose();
        else _idle.Add(engine);
        _slots.Release();
    }

    public void Dispose()
    {
        _disposed = true;
        while (_idle.TryTake(out var engine)) engine.Dispose();
        _slots.Dispose();
    }

    /// <summary>Quyền dùng engine trong một khoảng; Dispose = trả về kho (không huỷ engine).</summary>
    public sealed class Lease : IDisposable
    {
        private readonly TesseractEnginePool _pool;
        private TesseractEngine? _engine;

        internal Lease(TesseractEnginePool pool, TesseractEngine engine)
        {
            _pool = pool; _engine = engine;
        }

        public TesseractEngine Engine =>
            _engine ?? throw new ObjectDisposedException(nameof(Lease));

        public void Dispose()
        {
            var engine = Interlocked.Exchange(ref _engine, null);
            if (engine != null) _pool.Return(engine);
        }
    }
}
