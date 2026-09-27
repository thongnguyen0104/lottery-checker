import { useState } from 'react'
import ScanFeedback from './ScanFeedback'
import type { ScanResponse } from '../api/client'
import { CLAIM_DAYS, claimDeadline, isExpired } from '../utils/claim'

const fmtDate = (iso: string) => iso.split('-').reverse().join('/')

type Props = {
  // Dùng thẳng kiểu của API: thêm trường mới (vd timings) là tự chảy tới ScanFeedback,
  // khỏi phải khai lại ở đây rồi quên mất một chỗ.
  scanned: ScanResponse
  /** Ảnh vé vừa quét (object URL) — hiện kèm để user so từng số khi sửa tay. */
  imageUrl?: string | null
  allProvinces: { code: string; name: string }[]
  onConfirm: (data: { ticketNumber: string; drawDate: string; province: string }) => void
  onRescan: () => void
}

export default function TicketInfoConfirm({ scanned, imageUrl, allProvinces, onConfirm, onRescan }: Props) {
  const [ticket, setTicket] = useState(scanned.ticketNumber ?? '')
  const [date, setDate] = useState(scanned.drawDate ?? new Date().toISOString().slice(0, 10))
  const [province, setProvince] = useState(scanned.province ?? '')
  // HEIC trên Chrome không giải mã được → ẩn ô ảnh thay vì hiện icon ảnh vỡ.
  const [imgFailed, setImgFailed] = useState(false)

  // Đỏ = OCR không đọc được; vàng = đọc được nhưng chưa chắc (backend needsReview) — nhất là khi
  // máy chủ KHÔNG gọi AI đọc lại cho đài/ngày, user chính là người xác nhận cuối cùng.
  const review = new Set(scanned.needsReview ?? [])
  // .field có sẵn bg-surface: không đặt nền thì iOS tô xám ô ngày và ô chọn đài, lệch tông ô số vé.
  const fieldClass = (missing: boolean, uncertain = false) =>
    `field ${missing ? 'border-bad/60 bg-bad/5' : uncertain ? 'border-warn/70 bg-warn/5' : ''}`
  const reviewHint = (show: boolean) => show && (
    <span className="block text-xs text-warn mt-1">⚠️ Máy đọc chưa chắc — kiểm tra lại với vé</span>
  )

  return (
    <div className="space-y-4">
      <div>
        <h1 className="text-xl md:text-3xl font-extrabold tracking-tight">Kiểm tra lại vé nhé 👀</h1>
        <p className="text-sm md:text-base text-ink-soft mt-0.5">Máy đọc sai chỗ nào thì sửa, rồi bấm Dò ngay.</p>
      </div>

      <div className="space-y-4 md:space-y-0 md:grid md:grid-cols-2 md:gap-6 md:items-start">
        <div className="space-y-4">
          {imageUrl && !imgFailed && (
            <div className="card p-2">
              <img src={imageUrl} alt="Ảnh vé vừa quét" decoding="async" onError={() => setImgFailed(true)}
                   className="block w-full max-h-48 md:max-h-80 object-contain rounded-2xl bg-gray-900" />
            </div>
          )}
          <ScanFeedback scanned={scanned} />
        </div>

        <div className="card p-4 md:p-5 space-y-4">
          <label className="block">
            <span className="text-sm font-medium text-ink-soft">Số vé (6 chữ số)</span>
            <input value={ticket}
                   onChange={e => setTicket(e.target.value.replace(/\D/g, '').slice(0, 6))}
                   className={`${fieldClass(!scanned.ticketNumber, review.has('number'))} mt-1
                               text-center text-2xl font-extrabold tracking-[.3em] tabular-nums`}
                   inputMode="numeric" placeholder="123456" />
            {reviewHint(!!scanned.ticketNumber && review.has('number'))}
          </label>

          <label className="block">
            <span className="text-sm font-medium text-ink-soft">Ngày mở thưởng</span>
            <input type="date" value={date} onChange={e => setDate(e.target.value)}
                   className={`${fieldClass(!scanned.drawDate, review.has('date'))} mt-1`} />
            {reviewHint(!!scanned.drawDate && review.has('date'))}
          </label>

          {/* Báo sớm ngay khi ngày trên vé (đọc được hoặc sửa tay) đã quá hạn — khỏi bấm dò mới biết */}
          {isExpired(date) && (
            <div className="alert bg-bad/10 border-bad/30 text-bad">
              ⌛ <b>Vé hết hạn</b>: đã quá {CLAIM_DAYS} ngày kể từ ngày mở thưởng
              (hạn lĩnh thưởng đến hết ngày {fmtDate(claimDeadline(date))}).
            </div>
          )}

          <label className="block">
            <span className="text-sm font-medium text-ink-soft">Đài</span>
            <select value={province} onChange={e => setProvince(e.target.value)}
                    className={`${fieldClass(!scanned.province, review.has('province'))} mt-1`}>
              <option value="">-- Chọn đài --</option>
              {allProvinces.map(p => (
                <option key={p.code} value={p.code}>{p.name}</option>
              ))}
            </select>
            {reviewHint(!!scanned.province && review.has('province'))}
          </label>

          <div className="flex gap-3 pt-1">
            <button onClick={onRescan} className="btn btn-soft flex-1">
              📷 Chụp lại
            </button>
            <button
              onClick={() => onConfirm({ ticketNumber: ticket, drawDate: date, province })}
              disabled={!ticket || ticket.length !== 6 || !province}
              className="btn btn-primary flex-[1.4]">
              ✅ Dò ngay
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}
