import type { ReactNode } from 'react'
import Confetti from './Confetti'
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
  const won = status === 'Checked' && isWinner

  return (
    <div className="space-y-4">
      {won && <Confetti />}

      <div className="ticket-shadow">
        <div className="ticket-shape relative overflow-hidden rounded-3xl bg-gradient-to-br from-brand-600 to-accent
                        text-white px-6 py-5 text-center">
          {/* Vân sáng chéo cho tấm vé đỡ phẳng */}
          <div aria-hidden className="absolute -top-16 -right-10 w-48 h-48 rounded-full bg-white/10" />
          <div className="relative">
            <div className="text-xs font-bold uppercase tracking-[.25em] text-white/75">Vé số của bạn</div>
            <div className="flex justify-center gap-1.5 my-3" aria-label={extractedNumber}>
              {extractedNumber.split('').map((d, i) => (
                <span key={i} aria-hidden
                      className="w-10 h-12 sm:w-11 sm:h-14 rounded-xl bg-white/20 ring-1 ring-white/30
                                 flex items-center justify-center text-3xl sm:text-4xl font-extrabold tabular-nums">
                  {d}
                </span>
              ))}
            </div>
            <div className="border-t border-dashed border-white/40 pt-3 text-sm font-medium text-white/90">
              📍 {province ? provinceName(province) : '—'} · 🗓️ {formatDate(drawDate)}
            </div>
          </div>
        </div>
      </div>

      {status === 'Expired' ? (
        <StatusCard tone="bad" icon="⌛" title="Vé hết hạn">
          Vé mở thưởng ngày <b>{formatDate(drawDate)}</b> — hạn lĩnh thưởng {CLAIM_DAYS} ngày
          (đến hết ngày <b>{formatDate(result.claimDeadline ?? (drawDate ? claimDeadline(drawDate) : null))}</b>) đã qua.
        </StatusCard>
      ) : status === 'NotDrawnYet' ? (
        <StatusCard tone="warn" icon="⏳" title="Vé chưa đến giờ xổ">
          {province ? provinceName(province) : 'Đài này'} xổ lúc{' '}
          <b>{formatTime(drawsAt) ?? '16:15'}</b> ngày <b>{formatDate(drawDate)}</b>. Quay lại sau nhé!
        </StatusCard>
      ) : status === 'NoData' ? (
        <StatusCard tone="info" icon="📭" title="Chưa có kết quả để dò">
          Hệ thống chưa tải được kết quả của {province ? provinceName(province) : 'đài này'} ngày{' '}
          {formatDate(drawDate)}. Kiểm tra lại ngày/đài, hoặc thử lại sau ít phút —
          <b> chưa kết luận được vé trúng hay không</b>.
        </StatusCard>
      ) : isWinner ? (
        <>
          <div className="relative overflow-hidden rounded-3xl p-6 text-center text-amber-950 shadow-lg shadow-amber-500/30
                          bg-gradient-to-br from-amber-200 via-yellow-300 to-orange-300">
            <div className="text-5xl mb-1 motion-safe:animate-bounce" aria-hidden>🎉</div>
            <div className="font-bold">Chúc mừng! Vé trúng</div>
            <div className="text-4xl sm:text-5xl font-extrabold tracking-tight mt-1">{formatVND(totalPrize)}</div>
            {winnings.length > 1 && (
              <div className="text-xs font-medium text-amber-900/70 mt-2">
                ({winnings.length} giải cộng dồn)
              </div>
            )}
          </div>

          <ul className="card divide-y divide-line overflow-hidden">
            {winnings.map((w, i) => (
              <li key={i} className="flex items-center justify-between gap-3 p-4">
                <span className="font-medium">🏆 {w.tierName}</span>
                <span className="text-brand-600 dark:text-brand-300 font-bold">{formatVND(w.amount)}</span>
              </li>
            ))}
          </ul>
        </>
      ) : (
        <div className="card p-6 text-center">
          <div className="text-4xl mb-2">🍀</div>
          <div className="font-bold text-lg">Tiếc quá, vé không trúng giải nào</div>
          <div className="text-sm text-ink-soft mt-1">Chúc bạn may mắn lần sau!</div>
        </div>
      )}

      <p className="text-xs text-ink-faint text-center px-2">
        Kết quả do AI đọc và có thể mắc sai sót, chúng tôi không chịu trách nhiệm nếu bạn hủy vé.
      </p>

      <div className="grid sm:grid-flow-col sm:auto-cols-fr gap-3">
        {/* Chỉ 'Checked' mới chắc có bảng trong DB: chưa xổ/chưa cào thì chưa có, còn vé hết hạn
            thì kết quả >30 ngày đã bị scraper dọn. */}
        {status === 'Checked' && drawDate && province && (
          <button onClick={() => onShowTable({ drawDate, province, ticketNumber: extractedNumber })}
                  className="btn btn-soft">
            📋 Xem bảng kết quả đài này
          </button>
        )}
        <button onClick={onRescan} className="btn btn-primary">
          🔄 Dò vé khác
        </button>
      </div>
    </div>
  )
}

const TONES = {
  bad: { box: 'bg-bad/10 border-bad/30', title: 'text-bad' },
  warn: { box: 'bg-warn/10 border-warn/30', title: 'text-warn' },
  info: { box: 'bg-info/10 border-info/30', title: 'text-info' },
}

function StatusCard({ tone, icon, title, children }: {
  tone: keyof typeof TONES; icon: string; title: string; children: ReactNode
}) {
  return (
    <div className={`rounded-3xl border p-6 text-center ${TONES[tone].box}`}>
      <div className="text-4xl mb-2">{icon}</div>
      <div className={`font-bold text-lg ${TONES[tone].title}`}>{title}</div>
      <div className="text-sm text-ink-soft mt-1">{children}</div>
    </div>
  )
}
