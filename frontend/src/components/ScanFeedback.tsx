import type { CloudProvider, ScanResponse, ScanTimings } from '../api/client'
import { provinceName } from '../data/provinces'
import { useTranslation } from 'react-i18next'
import Icon from './Icon'
import i18n, { currentLocale } from '../i18n'

const fmtDate = (iso: string | null) => {
  if (!iso) return null
  const [y, m, d] = iso.split('-')
  return `${d}/${m}/${y}`
}

// 1 chữ số thập phân theo locale đang dùng (VN: 2,4 · EN: 2.4).
const fmt1 = (n: number) =>
  n.toLocaleString(currentLocale(), { minimumFractionDigits: 1, maximumFractionDigits: 1 })

// < 1s giữ nguyên ms (so sánh chặng nhanh cho dễ), từ 1s trở lên đổi ra giây (2,4s).
const fmtMs = (ms: number) =>
  ms < 1000 ? `${Math.round(ms)}ms` : `${fmt1(ms / 1000)}s`

const fmtBytes = (b: number) =>
  b < 1024 * 1024 ? `${Math.round(b / 1024)}KB` : `${fmt1(b / 1024 / 1024)}MB`

// Chặng nối tiếp, khớp key backend trả về (ScanController). Cộng lại đúng bằng timings.total.
// Chặng cloud = null khi OCR cục bộ đã đủ tin → hiện "bỏ qua" cho thấy rõ là KHÔNG gọi cloud.
// Nhãn: check.json → feedback.stage.<key>; cloudOcr thêm tên nguồn (Gemini/OCR.space) lúc hiển thị.
const STAGES: Exclude<keyof ScanTimings, 'total' | 'ocrDetect' | 'ocrRecognize' | 'ocrLines'>[] = [
  'upload', 'preprocess', 'localOcr', 'localRetry', 'cloudPrepare', 'cloudOcr', 'merge',
]

const PROVIDER_NAMES: Record<CloudProvider, string> = { gemini: 'Gemini', ocrspace: 'OCR.space' }

// Mã lỗi của một lượt gọi cloud (ScanController.CloudAttempt) → vì sao phải thử nguồn tiếp theo.
// Nội dung: check.json → feedback.cloudErr.<mã>.
const CLOUD_ERRORS = new Set(['timeout', 'http_429', 'rate_limited', 'http_503', 'empty', 'bad_json', 'network', 'failed'])
const cloudErrorText = (e: string) =>
  CLOUD_ERRORS.has(e) ? i18n.t(`check:feedback.cloudErr.${e as 'timeout'}`)
    : e.startsWith('http_') ? i18n.t('check:feedback.cloudErr.http', { code: e.slice(5) }) : e

// Mã lý do từ TicketResultValidator → câu dễ hiểu (vì sao phải nhờ AI đọc lại).
// Nội dung: check.json → feedback.reason.<mã>.
const REASONS = new Set(['number_missing', 'number_ambiguous', 'number_normalized', 'date_missing',
  'date_out_of_range', 'province_missing', 'province_fuzzy', 'province_ambiguous', 'low_confidence'])
const reasonText = (r: string) =>
  REASONS.has(r) ? i18n.t(`check:feedback.reason.${r as 'number_missing'}`) : r

// Hiển thị kết quả OCR theo từng trường: đọc được gì (✓ xanh) / chưa đọc được gì (✕ đỏ) + gợi ý chụp lại.
export default function ScanFeedback({ scanned }: { scanned: ScanResponse }) {
  const { t: tr } = useTranslation('check')
  const review = new Set(scanned.needsReview ?? [])
  const fields = [
    {
      label: tr('feedback.fieldNumber'),
      review: review.has('number'),
      ok: !!scanned.ticketNumber,
      value: scanned.ticketNumber,
      tip: tr('feedback.tipNumber'),
      badge: scanned.ticketNumberFromCloud ? tr('feedback.aiRead') : null,
    },
    {
      label: tr('feedback.fieldDate'),
      review: review.has('date'),
      ok: !!scanned.drawDate,
      value: fmtDate(scanned.drawDate),
      tip: tr('feedback.tipDate'),
      badge: null,
    },
    {
      label: tr('feedback.fieldProvince'),
      review: review.has('province'),
      ok: !!scanned.province,
      value: scanned.province ? provinceName(scanned.province) : null,
      tip: tr('feedback.tipProvince'),
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
        <div className="text-sm font-semibold text-ink">{tr('feedback.title')}</div>
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
                ? <span className="text-bad">{tr('feedback.unclear', { tip: f.tip })}</span>
                : f.review
                  ? <><span className="text-warn font-semibold">{f.value}</span>
                      <span className="text-warn">{tr('feedback.unsure')}</span></>
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
            {tr('feedback.confidence', { pct: Math.round(scanned.confidence * 100) })}
            {scanned.lowConfidence && tr('feedback.lowConfidence')}
          </div>
        )}

        {t && (
          // Gấp lại mặc định: user bình thường chỉ cần tổng, còn chi tiết là để soi khi chậm.
          <details className="text-xs text-ink-faint">
            <summary className="cursor-pointer select-none">
              <Icon name="timer" className="inline-block w-3.5 h-3.5 -mt-0.5 mr-1" />
              {tr('feedback.totalTime')} <b className="text-ink">{fmtMs(totalMs)}</b>
              {' '}{tr('feedback.serverTime', { ms: fmtMs(t.total) })}
            </summary>
            <div className="mt-1.5 space-y-0.5">
              <div className="flex justify-between gap-4">
                <span>
                  {up.compressed
                    ? tr('feedback.compress', { max: up.options ? ` ≤${up.options.maxWidth}px` : '',
                                                from: fmtBytes(up.originalBytes), to: fmtBytes(up.sentBytes) })
                    : tr('feedback.sendOriginal', { size: fmtBytes(up.sentBytes) })}
                </span>
                <span className="tabular-nums">{fmtMs(up.compressMs)}</span>
              </div>
              {/* Tách khâu nén ra để biết chậm ở giải mã, thu nhỏ hay mã hoá trên máy thật */}
              {up.stages && ([
                ['decode', tr('feedback.decode', { decoder: up.stages.decoder })],
                ['resize', tr('feedback.resize')],
                ['encode', tr('feedback.encode')],
              ] as const).map(([k, label]) => (
                <div key={k} className="flex justify-between gap-4 pl-3 text-ink-faint/70">
                  <span>↳ {label}</span>
                  <span className="tabular-nums">{fmtMs(up.stages![k])}</span>
                </div>
              ))}
              {/* undefined = backend cũ không có key này → ẩn; null = có nhưng không chạy → "bỏ qua" */}
              {STAGES.filter(key => t[key] !== undefined).map(key => {
                const ms = t[key]
                const s = { key, label: tr(`feedback.stage.${key}`) }
                return (
                  <div key={s.key}>
                    <div className={`flex justify-between gap-4 ${ms == null ? 'text-ink-faint/70' : ''}`}>
                      <span>
                        {s.label}
                        {s.key === 'cloudOcr' && scanned.cloudProvider && ` (${PROVIDER_NAMES[scanned.cloudProvider]})`}
                      </span>
                      <span className="tabular-nums">{ms == null ? tr('feedback.skipped') : fmtMs(ms)}</span>
                    </div>
                    {/* Chi tiết bên trong chặng OCR — chậm ở dò vùng chữ hay nhận dạng dòng */}
                    {s.key === 'localOcr' && t.ocrDetect != null && (
                      <>
                        <div className="flex justify-between gap-4 pl-3 text-ink-faint/70">
                          <span>↳ {tr('feedback.ocrDetect')}</span>
                          <span className="tabular-nums">{fmtMs(t.ocrDetect)}</span>
                        </div>
                        {t.ocrRecognize != null && (
                          <div className="flex justify-between gap-4 pl-3 text-ink-faint/70">
                            <span>↳ {t.ocrLines != null ? tr('feedback.ocrRecognizeN', { n: t.ocrLines }) : tr('feedback.ocrRecognize')}</span>
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
                          {a.retried && a.retried.length > 0 && tr('feedback.retriedAfter', { errors: a.retried.map(cloudErrorText).join(', ') })}
                          {a.error && <span className="text-warn"> — {cloudErrorText(a.error)}</span>}
                        </span>
                        <span className="tabular-nums">{fmtMs(a.ms)}</span>
                      </div>
                    ))}
                    {s.key === 'localRetry' && ms != null && scanned.localRetry && (
                      <div className="pl-3 text-ink-faint/70">
                        ↳ {scanned.localRetry.filled.length > 0
                          ? tr('feedback.filled', { fields: scanned.localRetry.filled
                              .map(f => f === 'date' ? tr('feedback.filledDate') : tr('feedback.filledProvince')).join(', ') })
                          : tr('feedback.filledNone')}
                      </div>
                    )}
                  </div>
                )
              })}
              {scanned.ocrPath === 'local-review' && (
                <div className="text-ink-faint/70">↳ {tr('feedback.localReview')}</div>
              )}
              {scanned.ocrPath === 'local-expired' && (
                <div className="text-ink-faint/70">↳ {tr('feedback.localExpired')}</div>
              )}
              {scanned.ocrPath?.startsWith('cloud') && scanned.localValidation && !scanned.localValidation.passed && (
                <div className="text-ink-faint/70">
                  ↳ {tr('feedback.cloudBecause', { reasons: scanned.localValidation.reasons.map(reasonText).join(', ') })}
                  {scanned.ocrPath === 'cloud-failed' && tr('feedback.cloudFailed')}
                </div>
              )}
              {scanned.ocrPath === 'cloud-only' && (
                <div className="text-ink-faint/70">↳ {tr('feedback.cloudOnly')}</div>
              )}
              {scanned.ocrPath === 'cloud-only-failed' && (
                <div className="text-ink-faint/70">↳ {tr('feedback.cloudOnlyFailed')}</div>
              )}
              <div className="flex justify-between gap-4">
                <span>{tr('feedback.network')}</span>
                <span className="tabular-nums">{fmtMs(networkMs)}</span>
              </div>
              <div className="flex justify-between gap-4 border-t border-line pt-0.5 font-semibold text-ink">
                <span>{tr('feedback.total')}</span>
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
            {tr('feedback.missing')} <b>{missing.map(f => f.label.toLowerCase()).join(', ')}</b>.{' '}
            {tr('feedback.missingTip1')} <b>{tr('feedback.missingTipBold')}</b>{tr('feedback.missingTip2')}
          </span>
        </div>
      )}
    </div>
  )
}
