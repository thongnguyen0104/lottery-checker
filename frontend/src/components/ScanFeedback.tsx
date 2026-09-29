import type { CloudProvider, ScanResponse, ScanTimings } from '../api/client'
import { provinceName } from '../data/provinces'
import Icon from './Icon'

const fmtDate = (iso: string | null) => {
  if (!iso) return null
  const [y, m, d] = iso.split('-')
  return `${d}/${m}/${y}`
}

// < 1s giữ nguyên ms (so sánh chặng nhanh cho dễ), từ 1s trở lên đổi ra giây kiểu VN (2,4s).
const fmtMs = (ms: number) =>
  ms < 1000 ? `${Math.round(ms)}ms` : `${(ms / 1000).toFixed(1).replace('.', ',')}s`

const fmtBytes = (b: number) =>
  b < 1024 * 1024 ? `${Math.round(b / 1024)}KB` : `${(b / 1024 / 1024).toFixed(1).replace('.', ',')}MB`

// Chặng nối tiếp, khớp key backend trả về (ScanController). Cộng lại đúng bằng timings.total.
// Chặng cloud = null khi OCR cục bộ đã đủ tin → hiện "bỏ qua" cho thấy rõ là KHÔNG gọi cloud.
const STAGES: { key: Exclude<keyof ScanTimings, 'total'>; label: string }[] = [
  { key: 'upload',       label: 'Nhận ảnh (upload)' },
  { key: 'preprocess',   label: 'Tiền xử lý ảnh' },
  { key: 'localOcr',     label: 'Đọc chữ trên máy chủ' },
  { key: 'localRetry',   label: 'Đọc lại (ảnh tăng tương phản)' },
  { key: 'cloudPrepare', label: 'Chuẩn bị ảnh cho AI' },
  { key: 'cloudOcr',     label: 'AI đọc' },   // thêm tên nguồn (Gemini/OCR.space) lúc hiển thị
  { key: 'merge',        label: 'Gộp kết quả' },
]

const PROVIDER_NAMES: Record<CloudProvider, string> = { gemini: 'Gemini', ocrspace: 'OCR.space' }

// Mã lỗi của một lượt gọi cloud (ScanController.CloudAttempt) → vì sao phải thử nguồn tiếp theo.
const CLOUD_ERRORS: Record<string, string> = {
  timeout: 'quá lâu không trả lời',
  http_429: 'hết lượt miễn phí (429)',
  rate_limited: 'đang nhiều người dùng, tạm nhường lượt',
  http_503: 'máy chủ AI đang quá tải (503)',
  empty: 'không trả nội dung',
  bad_json: 'trả dữ liệu hỏng',
  network: 'lỗi mạng',
  failed: 'không đọc được',
}
const cloudErrorText = (e: string) =>
  CLOUD_ERRORS[e] ?? (e.startsWith('http_') ? `lỗi HTTP ${e.slice(5)}` : e)

// Mã lý do từ TicketResultValidator → câu dễ hiểu (vì sao phải nhờ AI đọc lại).
const REASONS: Record<string, string> = {
  number_missing: 'không thấy số vé',
  number_ambiguous: 'nhiều số vé khác nhau',
  number_normalized: 'số vé bị mờ/nhầm ký tự',
  date_missing: 'không thấy ngày',
  date_out_of_range: 'ngày bất thường',
  province_missing: 'không thấy đài',
  province_fuzzy: 'tên đài chưa rõ',
  province_ambiguous: 'thấy nhiều tên đài',
  low_confidence: 'ảnh chưa đủ rõ',
}

// Hiển thị kết quả OCR theo từng trường: đọc được gì (✓ xanh) / chưa đọc được gì (✕ đỏ) + gợi ý chụp lại.
export default function ScanFeedback({ scanned }: { scanned: ScanResponse }) {
  const review = new Set(scanned.needsReview ?? [])
  const fields = [
    {
      label: 'Số vé',
      review: review.has('number'),
      ok: !!scanned.ticketNumber,
      value: scanned.ticketNumber,
      tip: 'chụp rõ dãy 6 chữ số, tránh mờ/lóa',
      badge: scanned.ticketNumberFromCloud ? 'AI đọc' : null,
    },
    {
      label: 'Ngày',
      review: review.has('date'),
      ok: !!scanned.drawDate,
      value: fmtDate(scanned.drawDate),
      tip: 'lấy nét vào dòng ngày (vd 16-06-2026)',
      badge: null,
    },
    {
      label: 'Đài',
      review: review.has('province'),
      ok: !!scanned.province,
      value: scanned.province ? provinceName(scanned.province) : null,
      tip: 'chụp rõ phần tên tỉnh/đài',
      badge: null,
    },
  ]
  const missing = fields.filter(f => !f.ok)

  const t = scanned.timings
  // Phần trình duyệt chờ mà máy chủ không tính: truyền dữ liệu qua mạng (4G/tunnel).
  // Math.max(0,…) vì hai đồng hồ khác máy, chênh vài ms có thể ra số âm.
  const networkMs = t ? Math.max(0, scanned.clientMs - t.total) : 0
  const up = scanned.upload
  // Tổng user cảm nhận = nén ở trình duyệt + (mạng + máy chủ).
  const totalMs = scanned.clientMs + up.compressMs

  return (
    <div className="space-y-3">
      <div className="card p-4 space-y-2">
        <div className="text-sm font-semibold text-ink">Kết quả đọc tự động</div>
        {fields.map(f => (
          <div key={f.label} className="flex items-start gap-2 text-sm">
            {/* 3 mức: ✕ không đọc được · ⚠ đọc được nhưng chưa chắc (kiểm tra ô bên dưới) · ✓ chắc */}
            {!f.ok
              ? <Icon name="fail" className="w-[18px] h-[18px] shrink-0 text-bad" />
              : f.review
                ? <Icon name="warn" className="w-[18px] h-[18px] shrink-0 text-warn" />
                : <Icon name="ok" className="w-[18px] h-[18px] shrink-0 text-ok" />}
            <div>
              <span className="font-medium">{f.label}:</span>{' '}
              {!f.ok
                ? <span className="text-bad">chưa rõ — {f.tip}</span>
                : f.review
                  ? <><span className="text-warn font-semibold">{f.value}</span>
                      <span className="text-warn"> — chưa chắc, kiểm tra lại bên dưới</span></>
                  : <span className="text-ok font-semibold">{f.value}</span>}
              {f.ok && f.badge && (
                <span className="ml-2 inline-flex items-center gap-1 align-[1px] text-[11px] font-medium
                                 bg-info/10 text-info rounded-md px-1.5 py-0.5">
                  <Icon name="ai" className="w-3 h-3" strokeWidth={2} /> {f.badge}
                </span>
              )}
            </div>
          </div>
        ))}
        {/* null = OCR cục bộ không chạy, AI đọc thẳng — AI không trả điểm tin cậy, trường chưa chắc đã có ⚠️ */}
        {scanned.confidence != null && (
          <div className="text-xs text-ink-faint pt-1">
            Độ tin cậy OCR: {Math.round(scanned.confidence * 100)}%
            {scanned.lowConfidence && ' (thấp — nên kiểm tra kỹ)'}
          </div>
        )}

        {t && (
          // Gấp lại mặc định: user bình thường chỉ cần tổng, còn chi tiết là để soi khi chậm.
          <details className="text-xs text-ink-faint">
            <summary className="cursor-pointer select-none">
              <Icon name="timer" className="inline-block w-3.5 h-3.5 -mt-0.5 mr-1" />
              Xử lý hết <b className="text-ink">{fmtMs(totalMs)}</b>
              {' '}(máy chủ {fmtMs(t.total)}) — xem chi tiết
            </summary>
            <div className="mt-1.5 space-y-0.5">
              <div className="flex justify-between gap-4">
                <span>
                  {up.compressed
                    ? `Nén ảnh${up.options ? ` ≤${up.options.maxWidth}px` : ''} (${fmtBytes(up.originalBytes)} → ${fmtBytes(up.sentBytes)})`
                    : `Gửi ảnh gốc (${fmtBytes(up.sentBytes)})`}
                </span>
                <span className="tabular-nums">{fmtMs(up.compressMs)}</span>
              </div>
              {/* Tách khâu nén ra để biết chậm ở giải mã, thu nhỏ hay mã hoá trên máy thật */}
              {up.stages && ([
                ['decode', `Giải mã ảnh (${up.stages.decoder})`],
                ['resize', 'Thu nhỏ'],
                ['encode', 'Mã hoá JPEG'],
              ] as const).map(([k, label]) => (
                <div key={k} className="flex justify-between gap-4 pl-3 text-ink-faint/70">
                  <span>↳ {label}</span>
                  <span className="tabular-nums">{fmtMs(up.stages![k])}</span>
                </div>
              ))}
              {/* undefined = backend cũ không có key này → ẩn; null = có nhưng không chạy → "bỏ qua" */}
              {STAGES.filter(s => t[s.key] !== undefined).map(s => {
                const ms = t[s.key]
                return (
                  <div key={s.key}>
                    <div className={`flex justify-between gap-4 ${ms == null ? 'text-ink-faint/70' : ''}`}>
                      <span>
                        {s.label}
                        {s.key === 'cloudOcr' && scanned.cloudProvider && ` (${PROVIDER_NAMES[scanned.cloudProvider]})`}
                      </span>
                      <span className="tabular-nums">{ms == null ? 'bỏ qua' : fmtMs(ms)}</span>
                    </div>
                    {/* Chi tiết bên trong chặng OCR — chậm ở dò vùng chữ hay nhận dạng dòng */}
                    {s.key === 'localOcr' && t.ocrDetect != null && (
                      <>
                        <div className="flex justify-between gap-4 pl-3 text-ink-faint/70">
                          <span>↳ Dò vùng chữ</span>
                          <span className="tabular-nums">{fmtMs(t.ocrDetect)}</span>
                        </div>
                        {t.ocrRecognize != null && (
                          <div className="flex justify-between gap-4 pl-3 text-ink-faint/70">
                            <span>↳ Nhận dạng {t.ocrLines != null ? `${t.ocrLines} dòng` : 'các dòng'}</span>
                            <span className="tabular-nums">{fmtMs(t.ocrRecognize)}</span>
                          </div>
                        )}
                      </>
                    )}
                    {/* Chặng AI = tổng các lượt thử: tách ra để thấy lượt chậm là do Gemini lỗi/treo
                        rồi mới lùi về OCR.space, hay do chính nguồn đã trả lời */}
                    {s.key === 'cloudOcr' && scanned.cloudAttempts?.map((a, i) => (
                      <div key={i} className="flex justify-between gap-4 pl-3 text-ink-faint/70">
                        <span>
                          ↳ {PROVIDER_NAMES[a.provider] ?? a.provider}
                          {a.retried && a.retried.length > 0 && ` (gọi lại sau lỗi ${a.retried.map(cloudErrorText).join(', ')})`}
                          {a.error && <span className="text-warn"> — {cloudErrorText(a.error)}</span>}
                        </span>
                        <span className="tabular-nums">{fmtMs(a.ms)}</span>
                      </div>
                    ))}
                    {s.key === 'localRetry' && ms != null && scanned.localRetry && (
                      <div className="pl-3 text-ink-faint/70">
                        ↳ {scanned.localRetry.filled.length > 0
                          ? `Lấp được: ${scanned.localRetry.filled.map(f => f === 'date' ? 'ngày' : 'đài').join(', ')}`
                          : 'Không lấp thêm được trường nào'}
                      </div>
                    )}
                  </div>
                )
              })}
              {scanned.ocrPath === 'local-review' && (
                <div className="text-ink-faint/70">↳ Số vé đã chắc — không cần AI đọc lại, chỉ cần kiểm tra trường có dấu cảnh báo</div>
              )}
              {scanned.ocrPath === 'local-expired' && (
                <div className="text-ink-faint/70">↳ Vé đã hết hạn — không cần AI đọc lại</div>
              )}
              {scanned.ocrPath?.startsWith('cloud') && scanned.localValidation && !scanned.localValidation.passed && (
                <div className="text-ink-faint/70">
                  ↳ Nhờ AI đọc lại vì: {scanned.localValidation.reasons.map(r => REASONS[r] ?? r).join(', ')}
                  {scanned.ocrPath === 'cloud-failed' && ' (AI không phản hồi kịp — dùng kết quả máy chủ)'}
                </div>
              )}
              {scanned.ocrPath === 'cloud-only' && (
                <div className="text-ink-faint/70">↳ AI đọc thẳng ảnh (không chạy OCR trên máy chủ)</div>
              )}
              {scanned.ocrPath === 'cloud-only-failed' && (
                <div className="text-ink-faint/70">↳ AI không phản hồi — vui lòng điền tay bên dưới</div>
              )}
              <div className="flex justify-between gap-4">
                <span>Truyền qua mạng</span>
                <span className="tabular-nums">{fmtMs(networkMs)}</span>
              </div>
              <div className="flex justify-between gap-4 border-t border-line pt-0.5 font-semibold text-ink">
                <span>Tổng</span>
                <span className="tabular-nums">{fmtMs(totalMs)}</span>
              </div>
            </div>
          </details>
        )}
      </div>

      {missing.length > 0 && (
        <div className="alert flex gap-2 bg-warn/10 border-warn/30 text-warn">
          <Icon name="camera" className="w-4 h-4 shrink-0 mt-0.5" />
          <span>
            Chưa đọc được: <b>{missing.map(f => f.label.toLowerCase()).join(', ')}</b>.
            Bạn có thể điền tay bên dưới, hoặc <b>chụp lại rõ hơn</b>: đủ sáng, chụp thẳng
            (không nghiêng), tránh bóng/lóa, lấy nét vào dãy số và chữ.
          </span>
        </div>
      )}
    </div>
  )
}
