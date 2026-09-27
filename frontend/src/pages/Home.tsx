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
        <ProcessingScreen title={progress.title} imageUrl={imageUrl} steps={progress.steps}
                          current={progress.step} detail={progress.detail} />
      )}

      {!progress && error && (
        <div className="p-8 text-center text-red-600">
          <div className="mb-2">❌ {error}</div>
          <button onClick={() => { setError(null); setStage('capture') }}
                  className="bg-blue-600 text-white px-4 py-2 rounded">
            Thử lại
          </button>
        </div>
      )}

      {!progress && !error && (
        <>
          {stage === 'capture' && (
            <>
              {active && <CameraCapture onCapture={handleCapture} />}
              <div className="my-4 text-center text-gray-500">— hoặc —</div>
              <ImageUpload onSelect={f => handleCapture(f)} />
              <button onClick={() => onShowResults()}
                      className="mt-4 w-full border border-gray-300 text-gray-700 py-2.5 rounded-lg">
                📅 Xem kết quả xổ số
              </button>
            </>
          )}
          {stage === 'confirm' && (
            <TicketInfoConfirm
              scanned={scanned}
              allProvinces={ALL_PROVINCES}
              onConfirm={handleConfirm}
              onRescan={() => setStage('capture')}
            />
          )}
          {stage === 'result' && (
            <ResultDisplay result={result} onRescan={() => setStage('capture')}
                           onShowTable={onShowResults} />
          )}
        </>
      )}
    </>
  )
}
