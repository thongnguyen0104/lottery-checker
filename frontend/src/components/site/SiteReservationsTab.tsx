import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import Icon from '../Icon'
import { secondaryBtn } from '../map/Sheet'
import { formatDateTime } from '../../utils/date'
import {
  getSiteReservations, RESERVATION_STATUSES, setReservationStatus, type ReservationStatus, type SiteReservation,
} from '../../api/client'

/** Bước kế tiếp hợp lý cho từng trạng thái — đủ dùng mà khỏi bấm nhầm. */
const NEXT: Record<ReservationStatus, ReservationStatus[]> = {
  Pending: ['Confirmed', 'Cancelled'],
  Confirmed: ['Completed', 'Cancelled'],
  Completed: [],
  Cancelled: ['Pending'],
}

const TONE: Record<ReservationStatus, string> = {
  Pending: 'bg-warn/10 text-warn', Confirmed: 'bg-info/10 text-info', Completed: 'bg-ok/10 text-ok', Cancelled: 'bg-muted text-ink-faint',
}

/** Yêu cầu giữ vé khách gửi từ site: gọi lại xác nhận → khách tới quầy nhận vé, trả tiền. */
export default function SiteReservationsTab({ onPendingChange }: { onPendingChange: (n: number) => void }) {
  const { t } = useTranslation('site')
  const [filter, setFilter] = useState<ReservationStatus | null>('Pending')
  const [items, setItems] = useState<SiteReservation[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    getSiteReservations(filter ?? undefined).then(setItems).catch(e => setError((e as Error).message))
  }, [filter])

  const change = async (r: SiteReservation, status: ReservationStatus) => {
    setError(null)
    try {
      const u = await setReservationStatus(r.id, status)
      setItems(list => list?.map(x => x.id === u.id ? u : x) ?? null)
      if (r.status === 'Pending' || status === 'Pending') {
        getSiteReservations('Pending').then(p => onPendingChange(p.length)).catch(() => {})
      }
    } catch (e) {
      setError((e as Error).message)
    }
  }

  return (
    <div className="space-y-3">
      <div className="flex gap-1 flex-wrap">
        {[null, ...RESERVATION_STATUSES].map(s => (
          <button key={s ?? 'all'} onClick={() => setFilter(s)}
                  className={`px-3 py-1.5 rounded-full text-sm font-semibold ${filter === s ? 'bg-brand-500/15 text-brand-700 dark:text-brand-400' : 'bg-muted text-ink-soft'}`}>
            {s ? t(`editor.statuses.${s}`) : t('editor.reservationsAll')}
          </button>
        ))}
      </div>
      {error && <p className="text-sm text-bad">{error}</p>}
      {!items ? <div className="card h-32 animate-pulse" /> : items.length === 0 ? (
        <p className="card p-6 text-center text-ink-soft">{t('editor.reservationsEmpty')}</p>
      ) : (
        <ul className="space-y-2">
          {items.map(r => (
            <li key={r.id} className="card p-4 space-y-2">
              <div className="flex items-start gap-3">
                <div className="flex-1 min-w-0">
                  <p className="font-semibold">{r.customerName} · <a href={`tel:${r.phone}`} className="text-brand-700 dark:text-brand-400 underline">{r.phone}</a></p>
                  <p className="text-sm text-ink-soft">{r.quantity} × {r.productName ?? t('reserve.anyProduct')}</p>
                  {r.note && <p className="text-sm mt-1 whitespace-pre-line">{r.note}</p>}
                  <p className="text-xs text-ink-faint mt-1">{formatDateTime(r.createdAt)}</p>
                </div>
                <span className={`shrink-0 px-2 py-0.5 rounded-full text-xs font-semibold ${TONE[r.status]}`}>{t(`editor.statuses.${r.status}`)}</span>
              </div>
              {NEXT[r.status].length > 0 && (
                <div className="flex gap-2 flex-wrap">
                  <a href={`tel:${r.phone}`} className={secondaryBtn}><Icon name="phone" className="w-4 h-4" /></a>
                  {NEXT[r.status].map(s => (
                    <button key={s} onClick={() => change(r, s)} className={secondaryBtn}>{t(`editor.actions.${s}`)}</button>
                  ))}
                </div>
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
