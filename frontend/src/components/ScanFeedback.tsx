import { provinceName } from '../data/provinces'

type Scanned = {
  ticketNumber: string | null
  drawDate: string | null
  province: string | null
  confidence: number
  lowConfidence: boolean
  ticketNumberFromCloud?: boolean
}

const fmtDate = (iso: string | null) => {
  if (!iso) return null
  const [y, m, d] = iso.split('-')
  return `${d}/${m}/${y}`
}

// Hiển thị kết quả OCR theo từng trường: đọc được gì (✅) / chưa đọc được gì (❌) + gợi ý chụp lại.
export default function ScanFeedback({ scanned }: { scanned: Scanned }) {
  const fields = [
    {
      label: 'Số vé',
      ok: !!scanned.ticketNumber,
      value: scanned.ticketNumber,
      tip: 'chụp rõ dãy 6 chữ số, tránh mờ/lóa',
      badge: scanned.ticketNumberFromCloud ? '🤖 AI đọc' : null,
    },
    {
      label: 'Ngày',
      ok: !!scanned.drawDate,
      value: fmtDate(scanned.drawDate),
      tip: 'lấy nét vào dòng ngày (vd 16-06-2026)',
      badge: null,
    },
    {
      label: 'Đài',
      ok: !!scanned.province,
      value: scanned.province ? provinceName(scanned.province) : null,
      tip: 'chụp rõ phần tên tỉnh/đài',
      badge: null,
    },
  ]
  const missing = fields.filter(f => !f.ok)

  return (
    <div className="space-y-3">
      <div className="bg-white rounded-2xl shadow p-4 space-y-2">
        <div className="text-sm font-semibold text-gray-700">Kết quả đọc tự động</div>
        {fields.map(f => (
          <div key={f.label} className="flex items-start gap-2 text-sm">
            <span>{f.ok ? '✅' : '❌'}</span>
            <div>
              <span className="font-medium">{f.label}:</span>{' '}
              {f.ok
                ? <span className="text-green-700 font-semibold">{f.value}</span>
                : <span className="text-red-600">chưa rõ — {f.tip}</span>}
              {f.ok && f.badge && (
                <span className="ml-2 text-[11px] bg-blue-50 text-blue-600 rounded px-1.5 py-0.5">
                  {f.badge}
                </span>
              )}
            </div>
          </div>
        ))}
        <div className="text-xs text-gray-500 pt-1">
          Độ tin cậy OCR: {Math.round(scanned.confidence * 100)}%
          {scanned.lowConfidence && ' (thấp — nên kiểm tra kỹ)'}
        </div>
      </div>

      {missing.length > 0 && (
        <div className="bg-yellow-50 border border-yellow-300 rounded-xl p-3 text-sm text-yellow-800">
          📷 Chưa đọc được: <b>{missing.map(f => f.label.toLowerCase()).join(', ')}</b>.
          Bạn có thể điền tay bên dưới, hoặc <b>chụp lại rõ hơn</b>: đủ sáng, chụp thẳng
          (không nghiêng), tránh bóng/lóa, lấy nét vào dãy số và chữ.
        </div>
      )}
    </div>
  )
}
