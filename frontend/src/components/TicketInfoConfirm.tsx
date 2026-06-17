import { useState } from 'react'
import ScanFeedback from './ScanFeedback'

type Props = {
  scanned: {
    ticketNumber: string | null
    drawDate: string | null
    province: string | null
    confidence: number
    lowConfidence: boolean
    warning?: string | null
  }
  allProvinces: { code: string; name: string }[]
  onConfirm: (data: { ticketNumber: string; drawDate: string; province: string }) => void
  onRescan: () => void
}

export default function TicketInfoConfirm({ scanned, allProvinces, onConfirm, onRescan }: Props) {
  const [ticket, setTicket] = useState(scanned.ticketNumber ?? '')
  const [date, setDate] = useState(scanned.drawDate ?? new Date().toISOString().slice(0, 10))
  const [province, setProvince] = useState(scanned.province ?? '')

  const fieldClass = (missing: boolean) =>
    `w-full p-3 border rounded-lg ${missing ? 'border-red-400 bg-red-50' : 'border-gray-300'}`

  return (
    <div className="space-y-4 p-4">
      <ScanFeedback scanned={scanned} />

      <label className="block">
        <span className="text-sm text-gray-600">Số vé (6 chữ số)</span>
        <input value={ticket}
               onChange={e => setTicket(e.target.value.replace(/\D/g, '').slice(0, 6))}
               className={fieldClass(!scanned.ticketNumber)}
               inputMode="numeric" placeholder="VD: 123456" />
      </label>

      <label className="block">
        <span className="text-sm text-gray-600">Ngày mở thưởng</span>
        <input type="date" value={date} onChange={e => setDate(e.target.value)}
               className={fieldClass(!scanned.drawDate)} />
      </label>

      <label className="block">
        <span className="text-sm text-gray-600">Đài</span>
        <select value={province} onChange={e => setProvince(e.target.value)}
                className={fieldClass(!scanned.province)}>
          <option value="">-- Chọn đài --</option>
          {allProvinces.map(p => (
            <option key={p.code} value={p.code}>{p.name}</option>
          ))}
        </select>
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
