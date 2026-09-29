import { useState, type ReactNode } from 'react'
import ScratchCard from './ScratchCard'
import Confetti from './Confetti'
import Icon, { IconBadge, type IconName } from './Icon'
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
  /** Nhãn nút onRescan (mặc định "Dò vé khác") — vé trong ảnh nhiều vé thì quay về danh sách. */
  rescanLabel?: string
  /** Mở lại form với thông tin vừa dò — nhất là khi vé được dò luôn mà máy đọc nhầm. */
  onEdit: () => void
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

export default function ResultDisplay({ result, onRescan, rescanLabel, onEdit, onShowTable }: Props) {
  const { isWinner, winnings, totalPrize, extractedNumber, drawDate, province, drawsAt } = result
  const status: Status = result.status ?? 'Checked'
  const won = status === 'Checked' && isWinner
  // Cảnh báo đùa chỉ hiện khi trúng Giải Đặc Biệt (tên do backend LotteryMatcher đặt; "Giải Phụ Đặc Biệt" không tính).
  const jackpot = won && winnings.some(w => w.tierName === 'Giải Đặc Biệt')
  // Đã dò thật thì cho user tự cào lớp bạc xem trúng/trượt; hiệu ứng mừng chỉ chạy sau khi cào xong.
  const [revealed, setRevealed] = useState(status !== 'Checked')
  const onReveal = () => setRevealed(true)

  return (
    <div className="space-y-4">
      {won && revealed && <Confetti />}

      {/* Tấm vé màu primary (Hoàng kim: vàng, chữ navy); ô số dùng màu chữ pha loãng nên hợp mọi bảng màu */}
      <div className="ticket-shadow">
        <div className="ticket-shape relative overflow-hidden rounded-2xl bg-gradient-to-br from-primary to-primary-end
                        text-on-primary px-6 py-5 text-center">
          {/* Vân sáng chéo cho tấm vé đỡ phẳng, như ánh kim */}
          <div aria-hidden className="absolute -top-16 -right-10 w-48 h-48 rounded-full bg-white/15" />
          <div className="relative">
            <div className="text-xs font-bold uppercase tracking-[.25em] opacity-75">Vé số của bạn</div>
            <div className="flex justify-center gap-1.5 my-3" aria-label={extractedNumber}>
              {extractedNumber.split('').map((d, i) => (
                <span key={i} aria-hidden
                      className="w-10 h-12 sm:w-11 sm:h-14 rounded-lg bg-on-primary/10 ring-1 ring-on-primary/15
                                 flex items-center justify-center text-3xl sm:text-4xl font-extrabold tabular-nums">
                  {d}
                </span>
              ))}
            </div>
            <div className="flex items-center justify-center gap-4 border-t border-dashed border-on-primary/30 pt-3
                            text-sm font-semibold opacity-90">
              <span className="inline-flex items-center gap-1.5">
                <Icon name="pin" className="w-4 h-4" /> {province ? provinceName(province) : '—'}
              </span>
              <span className="inline-flex items-center gap-1.5">
                <Icon name="calendar" className="w-4 h-4" /> {formatDate(drawDate)}
              </span>
            </div>
          </div>
        </div>
      </div>

      {/* Ngay dưới tấm vé: vé đọc chắc thì được dò luôn không qua form — số/đài/ngày trên vé mà sai
          thì user thấy ngay ở đây và sửa được, khỏi phải chụp lại. */}
      <button onClick={onEdit}
              className="mx-auto flex items-center gap-1.5 text-sm font-medium text-ink-faint transition
                         hover:text-brand-700 dark:hover:text-brand-400">
        <Icon name="edit" className="w-4 h-4" /> Sai số vé, đài hoặc ngày? Sửa lại
      </button>

      {status === 'Expired' ? (
        <StatusCard tone="bad" icon="expired" title="Vé hết hạn">
          Vé mở thưởng ngày <b>{formatDate(drawDate)}</b> — hạn lĩnh thưởng {CLAIM_DAYS} ngày
          (đến hết ngày <b>{formatDate(result.claimDeadline ?? (drawDate ? claimDeadline(drawDate) : null))}</b>) đã qua.
        </StatusCard>
      ) : status === 'NotDrawnYet' ? (
        <StatusCard tone="warn" icon="clock" title="Vé chưa đến giờ xổ">
          {province ? provinceName(province) : 'Đài này'} xổ lúc{' '}
          <b>{formatTime(drawsAt) ?? '16:15'}</b> ngày <b>{formatDate(drawDate)}</b>. Quay lại sau nhé!
        </StatusCard>
      ) : status === 'NoData' ? (
        <StatusCard tone="info" icon="empty" title="Chưa có kết quả để dò">
          Hệ thống chưa tải được kết quả của {province ? provinceName(province) : 'đài này'} ngày{' '}
          {formatDate(drawDate)}. Kiểm tra lại ngày/đài, hoặc thử lại sau ít phút —
          <b> chưa kết luận được vé trúng hay không</b>.
        </StatusCard>
      ) : isWinner ? (
        <>
          <ScratchCard onReveal={onReveal}>
          <div className="space-y-4">
          {/* Trúng: khung xanh ngọc (trạng thái thành công), số tiền màu vàng brand */}
          <StatusCard tone="ok" icon="trophy" title="Chúc mừng! Vé trúng thưởng">
            <div className="text-4xl sm:text-5xl font-extrabold tracking-tight tabular-nums mt-1
                            text-brand-700 dark:text-brand-400">
              {formatVND(totalPrize)}
            </div>
            {winnings.length > 1 && (
              <div className="text-xs font-medium text-ink-faint mt-2">({winnings.length} giải cộng dồn)</div>
            )}
          </StatusCard>

          <ul className="card divide-y divide-line overflow-hidden">
            {winnings.map((w, i) => (
              <li key={i} className="flex items-center justify-between gap-3 p-4">
                <span className="flex items-center gap-2.5 font-medium">
                  <Icon name="award" className="w-5 h-5 shrink-0 text-ok" /> {w.tierName}
                </span>
                <span className="text-brand-700 dark:text-brand-400 font-bold tabular-nums">{formatVND(w.amount)}</span>
              </li>
            ))}
          </ul>
          </div>
          </ScratchCard>

          {/* Câu đùa cho vui lúc trúng ĐB — ghi rõ "đùa thôi" để không ai tưởng thật */}
          {jackpot && revealed && (
            <>
              <div className="alarm-frame" aria-hidden="true" />
              <div role="note" className="flex items-start gap-3 p-4 rounded-2xl border-2 border-red-600
                                          bg-red-600/15 text-red-700 dark:text-red-400">
                <Icon name="warn" className="alarm-blink w-7 h-7 shrink-0" />
                <div>
                  <div className="alarm-blink font-extrabold uppercase tracking-wide">⚠️ Cảnh báo!</div>
                  <div className="font-semibold">Chúng tôi đã biết địa chỉ IP của bạn, chiết khấu cho chúng tôi 5% nhanh! 😏</div>
                  <div className="text-xs opacity-80 mt-1">(Đùa thôi 😄 Chúc mừng bạn nha!)</div>
                </div>
              </div>
            </>
          )}
        </>
      ) : (
        <ScratchCard onReveal={onReveal}>
          <StatusCard tone="bad" icon="ticketX" title="Tiếc quá, vé không trúng giải nào">
            Chúc bạn may mắn lần sau!
          </StatusCard>
        </ScratchCard>
      )}

      <p className="text-xs text-ink-faint text-center px-2">
        Kết quả do AI đọc và có thể mắc sai sót, chúng tôi không chịu trách nhiệm nếu bạn hủy vé.
      </p>

      <div className="grid sm:grid-flow-col sm:auto-cols-fr gap-3">
        {/* Chỉ 'Checked' mới chắc có bảng trong DB: chưa xổ/chưa cào thì chưa có, còn vé hết hạn
            thì kết quả >30 ngày đã bị scraper dọn. */}
        {status === 'Checked' && drawDate && province && (
          <button onClick={() => onShowTable({ drawDate, province, ticketNumber: extractedNumber })}
                  className="btn btn-secondary">
            <Icon name="list" /> Xem bảng kết quả đài này
          </button>
        )}
        <button onClick={onRescan} className="btn btn-primary">
          <Icon name={rescanLabel ? 'back' : 'retry'} /> {rescanLabel ?? 'Dò vé khác'}
        </button>
      </div>
    </div>
  )
}

// Nền thẻ đặc (bg-surface) + dải màu nhạt từ mép trên: không lộ hình nền phía sau như nền trong suốt.
const TONES = {
  ok: { box: 'border-ok/35 from-ok/15', title: 'text-ok' },
  bad: { box: 'border-bad/30 from-bad/10', title: 'text-bad' },
  warn: { box: 'border-warn/30 from-warn/10', title: 'text-warn' },
  info: { box: 'border-info/30 from-info/10', title: 'text-info' },
}

function StatusCard({ tone, icon, title, children }: {
  tone: keyof typeof TONES; icon: IconName; title: string; children: ReactNode
}) {
  return (
    <div className={`rounded-2xl border p-6 text-center shadow-soft bg-surface bg-gradient-to-b ${TONES[tone].box}`}>
      <IconBadge name={icon} tone={tone} />
      <div className={`font-bold text-lg mt-3 ${TONES[tone].title}`}>{title}</div>
      <div className="text-sm text-ink-soft mt-1">{children}</div>
    </div>
  )
}
