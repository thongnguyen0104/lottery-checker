import { useEffect, useRef, useState } from 'react'
import CameraCapture from '../components/CameraCapture'
import ImageUpload from '../components/ImageUpload'
import TicketInfoConfirm from '../components/TicketInfoConfirm'
import ResultDisplay from '../components/ResultDisplay'
import ProcessingScreen from '../components/ProcessingScreen'
import MultiResultDisplay from '../components/MultiResultDisplay'
import Icon, { IconBadge, type IconName } from '../components/Icon'
import type { ResultsFocus } from '../components/AvailableData'
import {
  scanImage, scanMultiImage, checkTicket, loadCompressOptions,
  type CheckResult, type MultiTicket, type ScanResponse, type TicketQuery,
} from '../api/client'
import { ALL_PROVINCES, provinceName } from '../data/provinces'

type Stage = 'capture' | 'confirm' | 'result' | 'multi'

const MULTI_STEPS = ['Tải ảnh lên', 'AI tìm và đọc từng vé', 'Dò kết quả các vé']
// Nhiều vé: Gemini đọc cả ảnh thường mất vài giây — tự sang bước "Dò" sau chừng này ms.
const MULTI_READ_MS = 3500

/**
 * Vé trong ảnh nhiều vé → dạng ScanResponse để dùng lại form xác nhận (đánh dấu trường thiếu/nghi
 * ngờ). Chỉ các trường form đọc là có nghĩa; phần số liệu quét được ẩn (hideFeedback).
 */
const toScanResponse = (t: MultiTicket): ScanResponse => ({
  ticketNumber: t.ticketNumber, drawDate: t.drawDate, province: t.province,
  confidence: null, lowConfidence: false, ticketNumberFromCloud: t.ticketNumber != null,
  allProvinces: null, warning: null, needsReview: t.needsReview,
  clientMs: 0, upload: { originalBytes: 0, sentBytes: 0, compressMs: 0, compressed: false },
})
type Progress = { title: string; steps: string[]; step: number; detail?: string }

const SCAN_STEPS = ['Tải ảnh lên', 'Đọc chữ trên vé', 'Nhận diện số vé, đài và ngày']
// Máy chủ không báo tiến độ giữa chừng: upload xong thì sang "Đọc chữ", rồi tự chuyển sang
// "Nhận diện" sau chừng này ms (≈ thời gian OCR cục bộ hay gặp). Bước cuối chỉ ✓ khi có kết quả.
const OCR_STEP_MS = 1500
// Giữ màn "xong hết ✓" một nhịp cho user kịp thấy trước khi chuyển trang — 0 = chuyển ngay.
const DONE_HOLD_MS = 350

const fmtDate = (iso: string) => iso.split('-').reverse().join('/')

const errorText = (e: unknown) => (e instanceof Error && e.message) || 'Lỗi không xác định'

/** Máy chủ chắc cả 3 trường → thông tin để dò luôn; null = phải hỏi lại user trên form. */
function autoQuery(s: ScanResponse): TicketQuery | null {
  if (!s.autoCheck || !s.ticketNumber || !s.drawDate || !s.province) return null
  return { ticketNumber: s.ticketNumber, drawDate: s.drawDate, province: s.province }
}

const TIPS: [IconName, string][] = [
  ['sun', 'Đủ sáng, tránh bóng đổ và lóa đèn'],
  ['frame', 'Chụp thẳng, vé nằm gọn trong khung'],
  ['focus', 'Lấy nét vào dãy 6 số và dòng ngày'],
]

type Props = {
  /** false = đang ở tính năng khác (Home chỉ bị ẩn để giữ state) → tắt camera. */
  active: boolean
  /** Mở thẳng bảng kết quả của đài/ngày trên vé vừa dò (danh sách đài thì mở từ menu Kết quả). */
  onShowResults: (focus: ResultsFocus) => void
}

export default function Home({ active, onShowResults }: Props) {
  const [stage, setStage] = useState<Stage>('capture')
  const [scanned, setScanned] = useState<ScanResponse | null>(null)
  // Thông tin vé của lượt dò gần nhất — mở lại form để sửa (từ màn kết quả, hoặc khi dò lỗi) thì
  // điền đúng những gì đã dò, kể cả chỗ user đã sửa tay, thay vì kết quả quét ban đầu.
  const [checked, setChecked] = useState<TicketQuery | null>(null)
  const [result, setResult] = useState<CheckResult | null>(null)
  const [progress, setProgress] = useState<Progress | null>(null)
  const [imageUrl, setImageUrl] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  // Chế độ chụp nhiều vé trong 1 ảnh; multiTickets = kết quả lượt quét nhiều vé gần nhất, multiIndex =
  // vé đang mở chi tiết/sửa (null = đang ở luồng 1 vé) — dò lại xong thì cập nhật đúng dòng đó.
  const [multiMode, setMultiMode] = useState(false)
  const [multiTickets, setMultiTickets] = useState<MultiTicket[] | null>(null)
  const [multiIndex, setMultiIndex] = useState<number | null>(null)
  const timers = useRef<number[]>([])

  // Hỏi sẵn cỡ ảnh cần nén trong lúc user còn đang ngắm camera — lượt quét đầu khỏi chờ thêm 1 request.
  useEffect(() => { void loadCompressOptions() }, [])

  // Nút Dò ngay / Dò vé khác nằm cuối trang dài: sang bước mới thì về đầu trang, không thì
  // màn mới mở ra ở lưng chừng (tấm vé + kết quả bị cuộn khuất dưới header).
  useEffect(() => { window.scrollTo(0, 0) }, [stage])

  const clearTimers = () => { timers.current.forEach(clearTimeout); timers.current = [] }
  // Chỉ tiến, không lùi: timer tự chuyển bước có thể bắn sau khi bước thật đã qua.
  const advanceTo = (step: number, detail?: string) =>
    setProgress(p => p && step >= p.step ? { ...p, step, detail } : p)
  const advanceLater = (step: number, ms: number) =>
    timers.current.push(window.setTimeout(() => advanceTo(step), ms))
  const finishProgress = async () => {
    clearTimers()
    setProgress(p => p && { ...p, step: p.steps.length, detail: undefined })
    if (DONE_HOLD_MS) await new Promise(r => setTimeout(r, DONE_HOLD_MS))
  }

  const handleCapture = async (blob: Blob) => {
    // Giữ ảnh để màn chờ (cả lúc quét lẫn lúc dò) hiện đúng tấm vé user vừa chụp.
    if (imageUrl) URL.revokeObjectURL(imageUrl)
    setImageUrl(URL.createObjectURL(blob))
    setError(null)
    // Xoá vé cũ: quét lỗi thì nút Thử lại quay về camera, không mở form của vé trước.
    setScanned(null)
    setChecked(null)
    setMultiTickets(null)
    setMultiIndex(null)
    if (multiMode) return scanMulti(blob)
    setProgress({ title: 'Đang đọc vé số...', steps: SCAN_STEPS, step: 0 })
    let uploaded = false
    let data: ScanResponse
    try {
      data = await scanImage(blob, ratio => {
        if (ratio < 1) return advanceTo(0, `${Math.round(ratio * 100)}%`)
        if (uploaded) return
        uploaded = true
        advanceTo(1)
        advanceLater(2, OCR_STEP_MS)
      })
      await finishProgress()
    } catch (e) {
      clearTimers()
      setProgress(null)
      setError(errorText(e))
      return
    }
    setScanned(data)
    // Đọc chắc chắn → dò luôn: màn chờ chuyển thẳng sang checklist dò (cùng ảnh vé), không lộ form.
    // Còn nghi ngờ trường nào → hiện form cho user xác nhận/sửa như cũ.
    const query = autoQuery(data)
    if (query) return runCheck(query)
    setProgress(null)
    setStage('confirm')
  }

  // Ảnh nhiều vé: 1 request vừa đọc vừa dò luôn các vé đọc chắc → thẳng màn danh sách.
  const scanMulti = async (blob: Blob) => {
    setProgress({ title: 'Đang đọc các vé...', steps: MULTI_STEPS, step: 0 })
    let uploaded = false
    try {
      const data = await scanMultiImage(blob, ratio => {
        if (ratio < 1) return advanceTo(0, `${Math.round(ratio * 100)}%`)
        if (uploaded) return
        uploaded = true
        advanceTo(1)
        advanceLater(2, MULTI_READ_MS)
      })
      await finishProgress()
      setMultiTickets(data.tickets)
      setStage('multi')
    } catch (e) {
      setError(errorText(e))
    } finally {
      clearTimers()
      setProgress(null)
    }
  }

  // Mở 1 vé trong danh sách: đã dò → màn kết quả như 1 vé; chưa dò (còn nghi ngờ) → form sửa.
  const openMulti = (i: number) => {
    const t = multiTickets![i]
    setMultiIndex(i)
    setScanned(toScanResponse(t))
    setError(null)
    if (t.result && t.ticketNumber && t.drawDate && t.province) {
      setChecked({ ticketNumber: t.ticketNumber, drawDate: t.drawDate, province: t.province })
      setResult(t.result)
      setStage('result')
    } else {
      setChecked(null)
      setStage('confirm')
    }
  }

  const backToMulti = () => { setError(null); setMultiIndex(null); setStage('multi') }
  const inMulti = multiIndex != null && multiTickets != null

  const runCheck = async (info: TicketQuery) => {
    setError(null)
    setChecked(info)
    setProgress({
      title: 'Đang dò kết quả...',
      steps: [
        `Tìm kết quả ${provinceName(info.province)} ngày ${fmtDate(info.drawDate)}`,
        `Dò số ${info.ticketNumber} với các giải`,
        'Tổng hợp kết quả',
      ],
      step: 0,
    })
    // Dò chỉ là 1 lần tra DB, thường xong rất nhanh — timer chỉ để checklist có nhịp khi mạng chậm.
    advanceLater(1, 400)
    advanceLater(2, 900)
    try {
      const res = await checkTicket(info)
      await finishProgress()
      setResult(res)
      // Vé trong ảnh nhiều vé: ghi kết quả (và thông tin đã sửa) vào đúng dòng của danh sách.
      if (multiIndex != null)
        setMultiTickets(ts => ts && ts.map((t, i) => i === multiIndex
          ? { ...t, ...info, needsReview: [], result: res } : t))
      setStage('result')
    } catch (e) {
      setError(errorText(e))
    } finally {
      clearTimers()
      setProgress(null)
    }
  }

  return (
    <>
      {progress && (
        <div className="max-w-lg mx-auto">
          <ProcessingScreen title={progress.title} imageUrl={imageUrl} steps={progress.steps}
                            current={progress.step} detail={progress.detail} />
        </div>
      )}

      {!progress && error && (
        <div className="fade-up card max-w-md mx-auto p-8 text-center">
          <IconBadge name="error" tone="bad" />
          <div className="text-lg font-bold mt-4 mb-1">Ối, có lỗi rồi</div>
          <div className="text-sm text-bad mb-5">{error}</div>
          {/* Lỗi lúc dò (đã có thông tin vé): dò lại ngay, khỏi chụp lại — nhất là vé được dò luôn
              chưa qua form. Lỗi lúc quét: về camera. */}
          <div className="grid gap-3">
            <button onClick={() => {
                      if (checked) return runCheck(checked)
                      setError(null)
                      setStage('capture')
                    }}
                    className="btn btn-primary w-full">
              <Icon name="retry" /> Thử lại
            </button>
            {checked && scanned && (
              <button onClick={() => { setError(null); setStage('confirm') }} className="btn btn-secondary w-full">
                <Icon name="edit" /> Sửa thông tin vé
              </button>
            )}
            {inMulti && (
              <button onClick={backToMulti} className="btn btn-secondary w-full">
                <Icon name="back" /> Về danh sách vé
              </button>
            )}
          </div>
        </div>
      )}

      {!progress && !error && (
        <>
          {stage === 'capture' && (
            <div className="fade-up">
              <div className="mb-4 md:mb-6">
                {/* Nhãn nhỏ chỉ ở màn rộng: điện thoại đã có dòng mô tả dưới tên app, và cần giữ nút
                    Chụp + ô Chọn ảnh trong màn đầu tiên */}
                <p className="hidden md:flex items-center gap-1.5 mb-1 text-xs font-semibold uppercase tracking-[.18em]
                              text-brand-700 dark:text-brand-400">
                  <Icon name="sparkles" className="w-3.5 h-3.5" /> Dò vé tự động
                </p>
                <h1 className="text-xl md:text-3xl font-extrabold tracking-tight">Chụp vé, dò liền tay</h1>
                <p className="hidden md:block text-ink-soft mt-1">
                  Đưa vé vào khung rồi bấm chụp — máy tự đọc số vé, đài và ngày để dò giải giúp bạn.
                </p>
              </div>

              {/* 1 vé: đọc kỹ từng vé (OCR máy chủ + AI). Nhiều vé: AI đọc cả ảnh, dò hết một lượt. */}
              <div role="radiogroup" aria-label="Số vé trong ảnh"
                   className="grid grid-cols-2 gap-1 p-1 mb-4 rounded-xl bg-surface border border-line max-w-sm">
                {([[false, 'ticket', 'Một vé'], [true, 'tickets', 'Nhiều vé']] as const).map(([m, icon, label]) => (
                  <button key={label} role="radio" aria-checked={multiMode === m} onClick={() => setMultiMode(m)}
                          className={`flex items-center justify-center gap-1.5 py-2 rounded-lg text-sm font-semibold transition
                                      ${multiMode === m
                                        ? 'bg-primary text-on-primary shadow-soft'
                                        : 'text-ink-soft hover:text-ink'}`}>
                    <Icon name={icon} className="w-4 h-4" /> {label}
                  </button>
                ))}
              </div>
              {multiMode && (
                <p className="-mt-2 mb-4 text-xs text-ink-soft">
                  Xếp tối đa 10 vé không chồng lên nhau, thấy rõ số vé, tên đài và ngày của từng vé.
                </p>
              )}

              {/* Màn rộng: camera bên trái, chọn ảnh + mẹo bên phải. Điện thoại: xếp dọc, nút Chụp
                  và ô Chọn ảnh vẫn nằm trong màn đầu tiên không cần cuộn. */}
              <div className="md:grid md:grid-cols-[1.25fr_1fr] md:gap-6 md:items-start">
                <div className="card p-3 md:p-4">
                  {active && <CameraCapture onCapture={handleCapture} />}
                </div>

                <div className="space-y-4 mt-4 md:mt-0">
                  <div className="md:hidden flex items-center gap-3 text-xs font-medium text-ink-faint">
                    <span className="h-px flex-1 bg-line" /> hoặc <span className="h-px flex-1 bg-line" />
                  </div>
                  <ImageUpload onSelect={f => handleCapture(f)} />
                  <div className="card p-4">
                    <div className="flex items-center gap-2 font-semibold mb-3">
                      <Icon name="tip" className="w-[18px] h-[18px] text-brand-700 dark:text-brand-400" />
                      Mẹo chụp rõ nét
                    </div>
                    <ul className="space-y-2 text-sm text-ink-soft">
                      {TIPS.map(([icon, text]) => (
                        <li key={icon} className="flex items-center gap-2.5">
                          <Icon name={icon} className="w-4 h-4 shrink-0 text-ink-faint" /> {text}
                        </li>
                      ))}
                    </ul>
                  </div>
                </div>
              </div>
            </div>
          )}
          {stage === 'confirm' && scanned && (
            <div className="fade-up">
              <TicketInfoConfirm
                scanned={scanned}
                initial={checked}
                imageUrl={imageUrl}
                allProvinces={ALL_PROVINCES}
                onConfirm={runCheck}
                onRescan={inMulti ? backToMulti : () => setStage('capture')}
                hideFeedback={inMulti}
                rescanLabel={inMulti ? 'Danh sách' : undefined}
              />
            </div>
          )}
          {stage === 'result' && result && (
            <div className="fade-up max-w-xl mx-auto">
              <ResultDisplay result={result} onRescan={inMulti ? backToMulti : () => setStage('capture')}
                             rescanLabel={inMulti ? 'Về danh sách vé' : undefined}
                             onEdit={() => setStage('confirm')} onShowTable={onShowResults} />
            </div>
          )}
          {stage === 'multi' && multiTickets && (
            <div className="fade-up max-w-xl mx-auto">
              <MultiResultDisplay tickets={multiTickets} imageUrl={imageUrl} onOpen={openMulti}
                                  onRescan={() => setStage('capture')} />
            </div>
          )}
        </>
      )}
    </>
  )
}
