import { useEffect, useRef, useState } from 'react'
import CameraCapture from '../components/CameraCapture'
import ImageUpload from '../components/ImageUpload'
import TicketInfoConfirm from '../components/TicketInfoConfirm'
import ResultDisplay from '../components/ResultDisplay'
import ProcessingScreen from '../components/ProcessingScreen'
import type { ResultsFocus } from '../components/AvailableData'
import { scanImage, checkTicket, loadCompressOptions } from '../api/client'
import { ALL_PROVINCES, provinceName } from '../data/provinces'

type Stage = 'capture' | 'confirm' | 'result'
type Progress = { title: string; steps: string[]; step: number; detail?: string }

const SCAN_STEPS = ['Tải ảnh lên', 'Đọc chữ trên vé', 'Nhận diện số vé, đài và ngày']
// Máy chủ không báo tiến độ giữa chừng: upload xong thì sang "Đọc chữ", rồi tự chuyển sang
// "Nhận diện" sau chừng này ms (≈ thời gian OCR cục bộ hay gặp). Bước cuối chỉ ✓ khi có kết quả.
const OCR_STEP_MS = 1500
// Giữ màn "xong hết ✓" một nhịp cho user kịp thấy trước khi chuyển trang — 0 = chuyển ngay.
const DONE_HOLD_MS = 350

const fmtDate = (iso: string) => iso.split('-').reverse().join('/')

type Props = {
  /** false = đang ở tính năng khác (Home chỉ bị ẩn để giữ state) → tắt camera. */
  active: boolean
  /** focus = mở thẳng bảng của đài/ngày trên vé vừa dò; bỏ trống = mở danh sách. */
  onShowResults: (focus?: ResultsFocus) => void
}

export default function Home({ active, onShowResults }: Props) {
  const [stage, setStage] = useState<Stage>('capture')
  const [scanned, setScanned] = useState<any>(null)
  const [result, setResult] = useState<any>(null)
  const [progress, setProgress] = useState<Progress | null>(null)
  const [imageUrl, setImageUrl] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
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
    setProgress({ title: 'Đang đọc vé số...', steps: SCAN_STEPS, step: 0 })
    let uploaded = false
    try {
      const data = await scanImage(blob, ratio => {
        if (ratio < 1) return advanceTo(0, `${Math.round(ratio * 100)}%`)
        if (uploaded) return
        uploaded = true
        advanceTo(1)
        advanceLater(2, OCR_STEP_MS)
      })
      await finishProgress()
      setScanned(data)
      setStage('confirm')
    } catch (e: any) {
      setError(e?.message ?? 'Lỗi không xác định')
    } finally {
      clearTimers()
      setProgress(null)
    }
  }

  const handleConfirm = async (info: { ticketNumber: string; drawDate: string; province: string }) => {
    setError(null)
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
      setStage('result')
    } catch (e: any) {
      setError(e?.message ?? 'Lỗi không xác định')
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
          <div className="text-5xl mb-3">😵‍💫</div>
          <div className="text-lg font-bold mb-1">Ối, có lỗi rồi</div>
          <div className="text-sm text-bad mb-5">{error}</div>
          <button onClick={() => { setError(null); setStage('capture') }}
                  className="btn btn-primary w-full">
            🔄 Thử lại
          </button>
        </div>
      )}

      {!progress && !error && (
        <>
          {stage === 'capture' && (
            <div className="fade-up">
              <div className="mb-4 md:mb-6">
                <h1 className="text-xl md:text-3xl font-extrabold tracking-tight">
                  Chụp vé, dò liền tay <span aria-hidden>✨</span>
                </h1>
                <p className="hidden md:block text-ink-soft mt-1">
                  Đưa vé vào khung rồi bấm chụp — máy tự đọc số vé, đài và ngày để dò giải giúp bạn.
                </p>
              </div>

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
                  <button onClick={() => onShowResults()} className="btn btn-soft w-full">
                    📅 Xem kết quả xổ số
                  </button>
                  <div className="card p-4">
                    <div className="font-semibold mb-2">💡 Mẹo chụp rõ nét</div>
                    <ul className="space-y-1.5 text-sm text-ink-soft">
                      <li>☀️ Đủ sáng, tránh bóng đổ và lóa đèn</li>
                      <li>📐 Chụp thẳng, vé nằm gọn trong khung</li>
                      <li>🔍 Lấy nét vào dãy 6 số và dòng ngày</li>
                    </ul>
                  </div>
                </div>
              </div>
            </div>
          )}
          {stage === 'confirm' && (
            <div className="fade-up">
              <TicketInfoConfirm
                scanned={scanned}
                imageUrl={imageUrl}
                allProvinces={ALL_PROVINCES}
                onConfirm={handleConfirm}
                onRescan={() => setStage('capture')}
              />
            </div>
          )}
          {stage === 'result' && (
            <div className="fade-up max-w-xl mx-auto">
              <ResultDisplay result={result} onRescan={() => setStage('capture')}
                             onShowTable={onShowResults} />
            </div>
          )}
        </>
      )}
    </>
  )
}
