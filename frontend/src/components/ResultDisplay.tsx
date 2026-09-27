import { provinceName } from '../data/provinces'
import { CLAIM_DAYS, claimDeadline } from '../utils/claim'

type Winning = { tierName: string; amount: number }

// 'Checked' = đã đối chiếu kết quả thật; các trạng thái còn lại KHÔNG kết luận trúng/trượt.
type Status = 'Checked' | 'NotDrawnYet' | 'NoData' | 'Expired'

type Props = {
  result: {
    extractedNumber: string
    drawDate: string | null
    province: string | null
    status?: Status
    drawsAt?: string | null   // ISO không timezone, giờ VN (vd "2026-08-23T16:15:00")
    claimDeadline?: string | null  // 'YYYY-MM-DD' — hạn cuối lĩnh thưởng, chỉ có khi Expired
    isWinner: boolean
    winnings: Winning[]
    totalPrize: number
  }
  onRescan: () => void
  /** Mở bảng kết quả đầy đủ của đài/ngày trên vé (chỉ hiện nút khi đã có kết quả để dò). */
  onShowTable: (focus: { drawDate: string; province: string; ticketNumber: string }) => void
}

const formatVND = (n: number) =>
  n.toLocaleString('vi-VN', { style: 'currency', currency: 'VND', maximumFractionDigits: 0 })

const formatDate = (iso: string | null | undefined) => {
  if (!iso) return null
  const [y, m, d] = iso.slice(0, 10).split('-')
  return `${d}/${m}/${y}`
}

// Giờ xổ lấy trực tiếp từ chuỗi, KHÔNG qua new Date() — tránh browser lệch múi giờ.
const formatTime = (iso: string | null | undefined) => iso?.slice(11, 16) ?? null

export default function ResultDisplay({ result, onRescan, onShowTable }: Props) {
  const { isWinner, winnings, totalPrize, extractedNumber, drawDate, province, drawsAt } = result
  const status: Status = result.status ?? 'Checked'

  return (
    <div className="p-4 space-y-4">
      <div className="bg-white rounded-2xl shadow p-5 text-center">
        <div className="text-sm text-gray-500">Vé số</div>
        <div className="text-3xl font-bold tracking-widest my-1">{extractedNumber}</div>
        <div className="text-sm text-gray-500">
          {province ? provinceName(province) : '—'} — {formatDate(drawDate)}
        </div>
      </div>

      {status === 'Expired' ? (
        <div className="bg-red-50 border border-red-300 rounded-2xl p-6 text-center">
          <div className="text-2xl mb-2">⌛</div>
          <div className="font-medium text-red-800">Vé hết hạn</div>
          <div className="text-sm text-red-700/80 mt-1">
            Vé mở thưởng ngày <b>{formatDate(drawDate)}</b> — hạn lĩnh thưởng {CLAIM_DAYS} ngày
            (đến hết ngày <b>{formatDate(result.claimDeadline ?? (drawDate ? claimDeadline(drawDate) : null))}</b>) đã qua.
          </div>
        </div>
      ) : status === 'NotDrawnYet' ? (
        <div className="bg-amber-50 border border-amber-300 rounded-2xl p-6 text-center">
          <div className="text-2xl mb-2">⏳</div>
          <div className="font-medium text-amber-800">Vé chưa đến giờ xổ</div>
          <div className="text-sm text-amber-700/80 mt-1">
            {province ? provinceName(province) : 'Đài này'} xổ lúc{' '}
            <b>{formatTime(drawsAt) ?? '16:15'}</b> ngày <b>{formatDate(drawDate)}</b>. Quay lại sau nhé!
          </div>
        </div>
      ) : status === 'NoData' ? (
        <div className="bg-blue-50 border border-blue-300 rounded-2xl p-6 text-center">
          <div className="text-2xl mb-2">📭</div>
          <div className="font-medium text-blue-800">Chưa có kết quả để dò</div>
          <div className="text-sm text-blue-700/80 mt-1">
            Hệ thống chưa tải được kết quả của {province ? provinceName(province) : 'đài này'} ngày{' '}
            {formatDate(drawDate)}. Kiểm tra lại ngày/đài, hoặc thử lại sau ít phút —
            <b> chưa kết luận được vé trúng hay không</b>.
          </div>
        </div>
      ) : isWinner ? (
        <>
          <div className="bg-green-50 border border-green-300 rounded-2xl p-5 text-center">
            <div className="text-green-700 font-medium mb-1">🎉 Chúc mừng! Vé trúng:</div>
            <div className="text-4xl font-bold text-green-700">{formatVND(totalPrize)}</div>
            {winnings.length > 1 && (
              <div className="text-xs text-green-700/70 mt-2">
                ({winnings.length} giải cộng dồn)
              </div>
            )}
          </div>

          <ul className="bg-white rounded-2xl shadow divide-y">
            {winnings.map((w, i) => (
              <li key={i} className="flex items-center justify-between p-4">
                <span className="font-medium">{w.tierName}</span>
                <span className="text-brand-600 font-semibold">{formatVND(w.amount)}</span>
              </li>
            ))}
          </ul>
        </>
      ) : (
        <div className="bg-gray-50 border border-gray-200 rounded-2xl p-6 text-center">
          <div className="text-2xl mb-2">😔</div>
          <div className="font-medium">Tiếc quá, vé không trúng giải nào</div>
          <div className="text-sm text-gray-500 mt-1">Chúc bạn may mắn lần sau!</div>
        </div>
      )}

      <p className="text-xs text-gray-500 text-center px-2">
        Kết quả do AI đọc và có thể mắc sai sót, chúng tôi không chịu trách nhiệm nếu bạn hủy vé.
      </p>

      {/* Chỉ 'Checked' mới chắc có bảng trong DB: chưa xổ/chưa cào thì chưa có, còn vé hết hạn
          thì kết quả >30 ngày đã bị scraper dọn. */}
      {status === 'Checked' && drawDate && province && (
        <button onClick={() => onShowTable({ drawDate, province, ticketNumber: extractedNumber })}
                className="w-full border border-gray-300 text-gray-700 bg-white py-3 rounded-lg">
          📋 Xem bảng kết quả đài này
        </button>
      )}

      <button onClick={onRescan}
              className="w-full bg-blue-600 text-white py-3 rounded-lg">
        🔄 Dò vé khác
      </button>
    </div>
  )
}
