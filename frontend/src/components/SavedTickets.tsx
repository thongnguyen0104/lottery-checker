import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import {
  deleteSavedTicket, getSavedTickets, saveTicket, setTicketClaimed, type SavedTicket, type SavedTicketStatus,
} from '../api/client'
import { ALL_PROVINCES, isSouth, provinceName } from '../data/provinces'
import { currentLocale } from '../i18n'
import { prizeName } from '../i18n/prizes'
import { formatDate } from '../utils/date'
import type { ResultsFocus } from './AvailableData'
import ConfirmDialog from './ConfirmDialog'
import Icon, { IconBadge } from './Icon'

const FILTERS: SavedTicketStatus[] = ['Waiting', 'Won', 'Lost', 'Expired']

const TONE: Record<SavedTicketStatus, string> = {
  Waiting: 'bg-info/10 text-info ring-info/25',
  Won: 'bg-ok/10 text-ok ring-ok/25',
  Lost: 'bg-muted text-ink-faint ring-line',
  Expired: 'bg-bad/10 text-bad ring-bad/25',
}

const num = (n: number) => n.toLocaleString(currentLocale())

// Ngày hôm nay giờ VN 'YYYY-MM-DD' (en-CA cho sẵn định dạng ISO).
const todayVn = () => new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Ho_Chi_Minh' }).format(new Date())
const daysBetween = (from: string, to: string) =>
  Math.round((Date.parse(`${to}T00:00:00Z`) - Date.parse(`${from}T00:00:00Z`)) / 86_400_000)

type Props = {
  onShowResults: (focus: ResultsFocus) => void
  /** Số vé trúng chưa lĩnh thay đổi (badge trên tab). */
  onUnclaimedChange?: () => void
}

/** Tab "Vé đã lưu" ở trang Tài khoản: vé chờ kết quả, vé trúng (công tắc Đã lĩnh, hạn lĩnh), thêm vé tay. */
export default function SavedTickets({ onShowResults, onUnclaimedChange }: Props) {
  const { t } = useTranslation('profile')
  const [filter, setFilter] = useState<SavedTicketStatus>('Waiting')
  const [items, setItems] = useState<SavedTicket[]>([])
  const [page, setPage] = useState(1)
  const [hasMore, setHasMore] = useState(false)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [reload, setReload] = useState(0)
  const [adding, setAdding] = useState(false)

  useEffect(() => {
    let alive = true
    getSavedTickets(filter, page)
      .then(d => {
        if (!alive) return
        setItems(prev => page === 1 ? d.items : [...prev, ...d.items.filter(x => !prev.some(p => p.id === x.id))])
        setHasMore(d.hasMore)
      })
      .catch(e => alive && setError(e?.message ?? ''))
      .finally(() => alive && setLoading(false))
    return () => { alive = false }
  }, [filter, page, reload])

  const load = (f: SavedTicketStatus, p: number) => {
    setLoading(true)
    setError(null)
    setFilter(f)
    setPage(p)
    setReload(n => n + 1)
  }

  const onChanged = (x: SavedTicket) => {
    setItems(list => list.map(i => i.id === x.id ? x : i))
    onUnclaimedChange?.()
  }

  const [deleting, setDeleting] = useState<SavedTicket | null>(null)
  const closeDelete = useCallback(() => setDeleting(null), [])
  const onDelete = async (x: SavedTicket) => {
    setDeleting(null)
    try {
      await deleteSavedTicket(x.id)
      setItems(list => list.filter(i => i.id !== x.id))
      onUnclaimedChange?.()
    } catch (e) {
      alert((e as Error)?.message ?? '')
    }
  }

  const onAdded = (x: SavedTicket) => {
    setAdding(false)
    onUnclaimedChange?.()
    load(x.status, 1)
  }

  return (
    <div className="space-y-3">
      <div className="flex items-center gap-2 overflow-x-auto pb-1" role="radiogroup">
        {FILTERS.map(f => (
          <button key={f} role="radio" aria-checked={f === filter} onClick={() => load(f, 1)}
                  className={`shrink-0 px-3 py-1.5 rounded-full text-sm font-semibold ring-1 transition active:scale-95 ${f === filter
                    ? 'bg-brand-500/15 text-brand-700 ring-brand-500/30 dark:text-brand-400'
                    : 'text-ink-faint ring-line hover:text-ink-soft'}`}>
            {t(`saved.filter.${f}`)}
          </button>
        ))}
        <button onClick={() => setAdding(a => !a)} className="ml-auto shrink-0 btn btn-soft !py-1.5">
          <Icon name="plus" className="w-4 h-4" /> {t('saved.add')}
        </button>
      </div>

      {adding && <AddForm onDone={onAdded} onCancel={() => setAdding(false)} />}

      {error ? (
        <div className="card p-6 text-center space-y-3">
          <IconBadge name="error" tone="bad" />
          <div className="text-sm text-bad">{error}</div>
          <button onClick={() => load(filter, page)} className="btn btn-soft">
            <Icon name="retry" className="w-4 h-4" /> {t('retry')}
          </button>
        </div>
      ) : !loading && items.length === 0 ? (
        <div className="card p-6 text-center space-y-2">
          <IconBadge name="bookmark" tone="info" />
          <p className="text-sm text-ink-soft">{t(`saved.empty.${filter}`)}</p>
        </div>
      ) : (
        <ul className="card divide-y divide-line/70 overflow-hidden">
          {items.map(x => <Row key={x.id} item={x} onChanged={onChanged} onDelete={setDeleting} onShowResults={onShowResults} />)}
          {loading && page === 1 && Array.from({ length: 3 }, (_, i) => (
            <li key={i} className="px-4 py-3 flex justify-between motion-safe:animate-pulse">
              <div className="space-y-2">
                <div className="h-5 w-24 rounded-full bg-muted" />
                <div className="h-3 w-32 rounded-full bg-muted" />
              </div>
              <div className="h-6 w-20 rounded-full bg-muted" />
            </li>
          ))}
        </ul>
      )}
      {hasMore && !error && (
        <button onClick={() => load(filter, page + 1)} disabled={loading} className="btn btn-soft w-full">
          {loading ? t('loading') : t('loadMore')}
        </button>
      )}
      {deleting && (
        <ConfirmDialog title={t('saved.confirmDelete', { number: deleting.ticketNumber })} confirmLabel={t('saved.delete')}
                       onConfirm={() => onDelete(deleting)} onClose={closeDelete} />
      )}
    </div>
  )
}

function Row({ item, onChanged, onDelete, onShowResults }: {
  item: SavedTicket
  onChanged: (x: SavedTicket) => void
  onDelete: (x: SavedTicket) => void
  onShowResults: Props['onShowResults']
}) {
  const { t } = useTranslation('profile')
  const [busy, setBusy] = useState(false)
  const today = todayVn()
  const daysLeft = daysBetween(today, item.claimDeadline)
  const settled = item.status === 'Won' || item.status === 'Lost'

  const toggleClaimed = async () => {
    setBusy(true)
    try { onChanged(await setTicketClaimed(item.id, !item.claimedAt)) }
    catch (e) { alert((e as Error)?.message ?? '') }
    finally { setBusy(false) }
  }

  return (
    <li className="px-4 py-3 space-y-2">
      <div className="flex items-start justify-between gap-3">
        <button disabled={!settled} title={settled ? t('viewResults', { province: provinceName(item.province), date: formatDate(item.drawDate) }) : undefined}
                onClick={() => onShowResults({ drawDate: item.drawDate, province: item.province, ticketNumber: item.ticketNumber, from: 'profile' })}
                className="min-w-0 text-left enabled:hover:opacity-80">
          <div className="font-mono text-xl font-bold tabular-nums tracking-wider">{item.ticketNumber}</div>
          <div className="text-xs text-ink-faint truncate">{provinceName(item.province)} · {formatDate(item.drawDate)}</div>
          {item.note && <div className="text-xs text-ink-soft truncate">{item.note}</div>}
        </button>
        <div className="shrink-0 flex items-start gap-1">
          <div className="text-right space-y-1">
            <span className={`inline-block px-2 py-0.5 rounded-full ring-1 text-xs font-semibold ${TONE[item.status]}`}>
              {item.status === 'Won' ? t('status.win', { amount: num(item.prize) }) : t(`saved.status.${item.status}`)}
            </span>
            {item.status === 'Waiting' && (
              <div className="text-[11px] text-ink-faint tabular-nums">
                {item.drawDate > today ? t('saved.drawsOn', { time: item.drawsAt.slice(11, 16), date: formatDate(item.drawDate) })
                                       : t('saved.drawsAt', { time: item.drawsAt.slice(11, 16) })}
              </div>
            )}
          </div>
          <button onClick={() => onDelete(item)} aria-label={t('saved.delete')} title={t('saved.delete')}
                  className="p-1.5 rounded-lg text-ink-faint hover:text-bad hover:bg-muted transition">
            <Icon name="trash" className="w-4 h-4" />
          </button>
        </div>
      </div>

      {item.status === 'Won' && (
        <div className="flex flex-wrap items-center justify-between gap-2 text-xs">
          <span className="text-ink-soft">
            {item.prizeTiers.map(prizeName).join(' + ')}
            {' · '}
            {item.claimedAt ? t('saved.claimedOn', { date: formatDate(item.claimedAt.slice(0, 10)) })
              : daysLeft < 0 ? <b className="text-bad">{t('saved.claimExpired')}</b>
              : <b className={daysLeft <= 5 ? 'text-warn' : ''}>{t('saved.daysLeft', { count: daysLeft, date: formatDate(item.claimDeadline) })}</b>}
          </span>
          <label className="inline-flex items-center gap-2 font-semibold cursor-pointer select-none">
            <input type="checkbox" checked={!!item.claimedAt} disabled={busy} onChange={toggleClaimed}
                   className="w-4 h-4" />
            {t('saved.claimed')}
          </label>
        </div>
      )}
    </li>
  )
}

const SOUTH = ALL_PROVINCES.filter(p => isSouth(p.code))

function AddForm({ onDone, onCancel }: { onDone: (x: SavedTicket) => void; onCancel: () => void }) {
  const { t } = useTranslation('profile')
  const [ticketNumber, setNumber] = useState('')
  const [drawDate, setDate] = useState(todayVn())
  const [province, setProvince] = useState('')
  const [note, setNote] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try { onDone(await saveTicket({ ticketNumber, drawDate, province, note: note.trim() || null })) }
    catch (err) { setError((err as Error)?.message ?? '') }
    finally { setBusy(false) }
  }

  return (
    <form onSubmit={submit} className="card p-4 space-y-3">
      <div className="grid grid-cols-2 gap-3">
        <label className="col-span-2 space-y-1">
          <span className="text-xs font-semibold text-ink-faint">{t('saved.form.number')}</span>
          <input value={ticketNumber} onChange={e => setNumber(e.target.value.replace(/\D/g, '').slice(0, 6))}
                 inputMode="numeric" pattern="\d{6}" required placeholder="123456"
                 className="field font-mono text-lg tracking-widest" />
        </label>
        <label className="space-y-1">
          <span className="text-xs font-semibold text-ink-faint">{t('saved.form.date')}</span>
          <input type="date" value={drawDate} onChange={e => setDate(e.target.value)} required className="field" />
        </label>
        <label className="space-y-1">
          <span className="text-xs font-semibold text-ink-faint">{t('saved.form.province')}</span>
          <select value={province} onChange={e => setProvince(e.target.value)} required className="field">
            <option value="" disabled>{t('saved.form.pickProvince')}</option>
            {SOUTH.map(p => <option key={p.code} value={p.code}>{provinceName(p.code)}</option>)}
          </select>
        </label>
        <label className="col-span-2 space-y-1">
          <span className="text-xs font-semibold text-ink-faint">{t('saved.form.note')}</span>
          <input value={note} onChange={e => setNote(e.target.value.slice(0, 100))} maxLength={100}
                 placeholder={t('saved.form.notePlaceholder')} className="field" />
        </label>
      </div>
      {error && <p role="alert" className="text-sm text-bad">{error}</p>}
      <div className="flex gap-2 justify-end">
        <button type="button" onClick={onCancel} className="btn btn-soft">{t('saved.form.cancel')}</button>
        <button type="submit" disabled={busy || ticketNumber.length !== 6 || !province} className="btn btn-primary">
          <Icon name="bookmark" className="w-4 h-4" /> {busy ? t('saved.form.saving') : t('saved.form.save')}
        </button>
      </div>
    </form>
  )
}
