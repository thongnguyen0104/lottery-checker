import { useState } from 'react'
import ScanFeedback from './ScanFeedback'
import type { ScanResponse } from '../api/client'
import { CLAIM_DAYS, claimDeadline, isExpired } from '../utils/claim'

const fmtDate = (iso: string) => iso.split('-').reverse().join('/')

type Props = {
  // Dùng thẳng kiểu của API: thêm trường mới (vd timings) là tự chảy tới ScanFeedback,
  // khỏi phải khai lại ở đây rồi quên mất một chỗ.
  scanned: ScanResponse
  allProvinces: { code: string; name: string }[]
  onConfirm: (data: { ticketNumber: string; drawDate: string; province: string }) => void
  onRescan: () => void
}

export default function TicketInfoConfirm({ scanned, allProvinces, onConfirm, onRescan }: Props) {
  const [ticket, setTicket] = useState(scanned.ticketNumber ?? '')
  const [date, setDate] = useState(scanned.drawDate ?? new Date().toISOString().slice(0, 10))
  const [province, setProvince] = useState(scanned.province ?? '')

  // Đỏ = OCR không đọc được; vàng = đọc được nhưng chưa chắc (backend needsReview) — nhất là khi
  // máy chủ KHÔNG gọi AI đọc lại cho đài/ngày, user chính là người xác nhận cuối cùng.
  const review = new Set(scanned.needsReview ?? [])
  const fieldClass = (missing: boolean, uncertain = false) =>
    `w-full p-3 border rounded-lg ${missing ? 'border-red-400 bg-red-50'
      : uncertain ? 'border-amber-400 bg-amber-50' : 'border-gray-300 bg-white'}`
  // bg-white: không đặt thì iOS tô nền xám cho ô ngày và ô chọn đài, lệch tông với ô số vé.
  const reviewHint = (show: boolean) => show && (
    <span className="block text-xs text-amber-700 mt-1">⚠️ Máy đọc chưa chắc — kiểm tra lại với vé</span>
  )

  return (
    <div className="space-y-4 p-4">
      <ScanFeedback scanned={scanned} />

      <label className="block">
        <span className="text-sm text-gray-600">Số vé (6 chữ số)</span>
        <input value={ticket}
               onChange={e => setTicket(e.target.value.replace(/\D/g, '').slice(0, 6))}
               className={fieldClass(!scanned.ticketNumber, review.has('number'))}
               inputMode="numeric" placeholder="VD: 123456" />
        {reviewHint(!!scanned.ticketNumber && review.has('number'))}
      </label>

      <label className="block">
        <span className="text-sm text-gray-600">Ngày mở thưởng</span>
        <input type="date" value={date} onChange={e => setDate(e.target.value)}
               className={fieldClass(!scanned.drawDate, review.has('date'))} />
        {reviewHint(!!scanned.drawDate && review.has('date'))}
      </label>

      {/* Báo sớm ngay khi ngày trên vé (đọc được hoặc sửa tay) đã quá hạn — khỏi bấm dò mới biết */}
      {isExpired(date) && (
        <div className="bg-red-50 border border-red-300 rounded-xl p-3 text-sm text-red-800">
          ⌛ <b>Vé hết hạn</b>: đã quá {CLAIM_DAYS} ngày kể từ ngày mở thưởng
          (hạn lĩnh thưởng đến hết ngày {fmtDate(claimDeadline(date))}).
        </div>
      )}

      <label className="block">
        <span className="text-sm text-gray-600">Đài</span>
        <select value={province} onChange={e => setProvince(e.target.value)}
                className={fieldClass(!scanned.province, review.has('province'))}>
          <option value="">-- Chọn đài --</option>
          {allProvinces.map(p => (
            <option key={p.code} value={p.code}>{p.name}</option>
          ))}
        </select>
        {reviewHint(!!scanned.province && review.has('province'))}
      </label>

      <div className="flex gap-2">
        <button onClick={onRescan}
                className="flex-1 border border-gray-300 py-3 rounded-lg">
          📷 Chụp lại
        </button>
        <button
          onClick={() => onConfirm({ ticketNumber: ticket, drawDate: date, province })}
          disabled={!ticket || ticket.length !== 6 || !province}
          className="flex-1 bg-blue-600 text-white py-3 rounded-lg disabled:bg-gray-300">
          ✅ Dò ngay
        </button>
      </div>
    </div>
  )
}
