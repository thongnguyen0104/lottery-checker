import { formatDate as fmtDay } from '../utils/date'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import Confetti from './Confetti'
import ScratchCard from './ScratchCard'
import Icon, { IconBadge, type IconName } from './Icon'
import type { MultiTicket } from '../api/client'
import { provinceName } from '../data/provinces'
import i18n, { currentLocale } from '../i18n'

type Props = {
  tickets: MultiTicket[]
  imageUrl?: string | null
  /** Bấm một vé: đã dò thì mở chi tiết, chưa dò (còn nghi ngờ) thì mở form sửa. */
  onOpen: (index: number) => void
  onRescan: () => void
}

// Ảnh nhiều vé đã cào rồi thì quay lại từ màn chi tiết không bắt cào lần nữa (khóa theo mảng tickets).
const scratched = new WeakSet<MultiTicket[]>()

const formatVND = (n: number) =>
  i18n.t('check:money', { amount: n.toLocaleString(currentLocale(), { maximumFractionDigits: 0 }) })

const formatDate = (iso: string | null) => iso ? fmtDay(iso) : '—'

type Tone = 'ok' | 'bad' | 'warn' | 'info'
const BADGE: Record<Tone, string> = {
  ok: 'bg-ok/15 text-ok',
  bad: 'bg-bad/10 text-bad',
  warn: 'bg-warn/15 text-warn',
  info: 'bg-info/15 text-info',
}

/** Nhãn trạng thái ngắn cho 1 dòng vé — đủ ý như các StatusCard ở ResultDisplay. */
function statusOf(t: MultiTicket): { tone: Tone; icon: IconName; label: string; iconOnly?: boolean } {
  const r = t.result
  if (!r) return { tone: 'warn', icon: 'edit', label: i18n.t('check:multi.needReview') }
  switch (r.status) {
    case 'Expired': return { tone: 'bad', icon: 'expired', label: i18n.t('check:multi.expired') }
    case 'NotDrawnYet': return { tone: 'warn', icon: 'clock', label: i18n.t('check:multi.notDrawn') }
    case 'NoData': return { tone: 'info', icon: 'empty', label: i18n.t('check:multi.noData') }
    default: return r.isWinner
      ? { tone: 'ok', icon: 'trophy', label: formatVND(r.totalPrize) }
      : { tone: 'bad', icon: 'close', label: i18n.t('check:multi.lose'), iconOnly: true }
  }
}

export default function MultiResultDisplay({ tickets, imageUrl, onOpen, onRescan }: Props) {
  const { t: tr } = useTranslation('check')
  const won = tickets.filter(t => t.result?.status === 'Checked' && t.result.isWinner)
  const total = won.reduce((s, t) => s + t.result!.totalPrize, 0)
  const pending = tickets.filter(t => !t.result).length
  // Chỉ phải cào khi có vé đã dò được kết quả thật; vé chưa xổ/hết hạn... không có gì để giấu.
  const [needScratch] = useState(() => !scratched.has(tickets) && tickets.some(t => t.result?.status === 'Checked'))
  const [revealed, setRevealed] = useState(!needScratch)
  const onReveal = () => { scratched.add(tickets); setRevealed(true) }

  if (tickets.length === 0)
    return (
      <div className="card p-8 text-center">
        <IconBadge name="empty" tone="info" />
        <div className="text-lg font-bold mt-4 mb-1">{tr('multi.emptyTitle')}</div>
        <div className="text-sm text-ink-soft mb-5">{tr('multi.emptyBody')}</div>
        <button onClick={onRescan} className="btn btn-primary w-full"><Icon name="retry" /> {tr('multi.rescan')}</button>
      </div>
    )

  const summary = (
      <div className={`rounded-2xl border p-5 text-center shadow-soft bg-surface bg-gradient-to-b
                       ${won.length ? 'border-ok/35 from-ok/15' : 'border-line from-transparent'}`}>
        {won.length ? (
          <>
            <div className="font-bold text-ok">{tr('multi.wonSummary', { won: won.length, total: tickets.length })}</div>
            <div className="text-3xl sm:text-4xl font-extrabold tracking-tight tabular-nums mt-1
                            text-brand-700 dark:text-brand-400">
              {formatVND(total)}
            </div>
          </>
        ) : (
          <div className="font-bold">{tr('multi.noneWon')}{pending ? tr('multi.stillPending') : ''}</div>
        )}
        {pending > 0 && (
          <div className="text-xs text-warn mt-2 flex items-center justify-center gap-1">
            <Icon name="warn" className="w-3.5 h-3.5" /> {tr('multi.pendingHint', { count: pending })}
          </div>
        )}
      </div>
  )

  return (
    <div className="space-y-4">
      {won.length > 0 && revealed && <Confetti />}

      <div>
        <h1 className="text-xl md:text-3xl font-extrabold tracking-tight">{tr('multi.heading', { count: tickets.length })}</h1>
        <p className="text-sm md:text-base text-ink-soft mt-0.5">{tr('multi.subtitle')}</p>
      </div>

      {/* Tổng kết: trúng thì khung xanh + tổng tiền màu vàng brand như màn 1 vé */}
      {needScratch ? <ScratchCard onReveal={onReveal}>{summary}</ScratchCard> : summary}

      {imageUrl && (
        <div className="card p-2">
          <img src={imageUrl} alt={tr('multi.imageAlt')} decoding="async"
               className="block w-full max-h-56 object-contain rounded-xl bg-slate-900" />
        </div>
      )}

      <ul className="space-y-3">
        {tickets.map((t, i) => {
          // Chưa cào thì giấu nhãn trúng/trượt của vé đã dò — chỉ hiện sau khi cào tổng kết
          const s = !revealed && t.result?.status === 'Checked'
            ? { tone: 'info' as Tone, icon: 'ticket' as IconName, label: tr('multi.scratchToSee') }
            : statusOf(t)
          return (
            <li key={i}>
              <button onClick={() => onOpen(i)}
                      className="card w-full p-4 flex items-center gap-3 text-left transition hover:border-brand-500/60">
                <span className="w-8 h-8 shrink-0 rounded-full bg-primary text-on-primary font-bold
                                 flex items-center justify-center text-sm">{i + 1}</span>
                <span className="flex-1 min-w-0">
                  <span className={`block text-xl font-extrabold tracking-[.15em] tabular-nums
                                    ${t.ticketNumber ? '' : 'text-ink-faint'}`}>
                    {t.ticketNumber ?? '––––––'}
                  </span>
                  <span className="flex flex-wrap gap-x-3 text-xs text-ink-soft mt-0.5">
                    <span className="inline-flex items-center gap-1">
                      <Icon name="pin" className="w-3.5 h-3.5" /> {t.province ? provinceName(t.province) : '—'}
                    </span>
                    <span className="inline-flex items-center gap-1">
                      <Icon name="calendar" className="w-3.5 h-3.5" /> {formatDate(t.drawDate)}
                    </span>
                  </span>
                </span>
                <span className={`shrink-0 max-w-[8.5rem] sm:max-w-none inline-flex items-center gap-1 rounded-2xl px-2.5 py-1
                                  text-xs font-bold leading-tight
                                  ${BADGE[s.tone]}`}>
                  <Icon name={s.icon} className={s.iconOnly ? 'w-4 h-4' : 'w-3.5 h-3.5'} strokeWidth={s.iconOnly ? 3 : undefined} /> {s.iconOnly ? <span className="sr-only">{s.label}</span> : s.label}
                </span>
                <Icon name="next" className="w-4 h-4 shrink-0 text-ink-faint" />
              </button>
            </li>
          )
        })}
      </ul>

      <p className="text-xs text-ink-faint text-center px-2">
        {tr('result.disclaimer')}
      </p>

      <button onClick={onRescan} className="btn btn-primary w-full">
        <Icon name="retry" /> {tr('multi.checkAnother')}
      </button>
    </div>
  )
}
