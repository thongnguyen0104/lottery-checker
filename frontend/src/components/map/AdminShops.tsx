import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import Icon from '../Icon'
import ConfirmDialog from '../ConfirmDialog'
import { deleteShop, getReportedShops, setShopStatus, type ReportedShop } from '../../api/client'
import { formatDateTime } from '../../utils/date'
import { secondaryBtn } from './Sheet'
import { TYPE_ICON } from './shopUtils'

/** Tab Quản trị → Điểm bán: điểm bị báo cáo / đang ẩn, hiện lại / ẩn / xoá. */
export default function AdminShops({ onOpen }: { onOpen: (id: string) => void }) {
  const { t } = useTranslation('map')
  const [items, setItems] = useState<ReportedShop[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const [confirmDelete, setConfirmDelete] = useState<ReportedShop | null>(null)
  const closeConfirm = useCallback(() => setConfirmDelete(null), [])

  const load = useCallback(() => getReportedShops().then(setItems).catch(e => setError((e as Error).message)), [])
  useEffect(() => { load() }, [load])

  const act = async (id: string, fn: () => Promise<unknown>) => {
    setBusy(id)
    setError(null)
    try {
      await fn()
      await load()
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(null)
    }
  }

  return (
    <div className="space-y-3">
      <p className="text-sm text-ink-soft">{t('admin.hint')}</p>
      {error && <p className="text-sm text-bad">{error}</p>}
      {items?.length === 0 && <p className="card p-5 text-center text-sm text-ink-faint">{t('admin.empty')}</p>}
      {items?.map(s => (
        <article key={s.id} className="card p-4 space-y-3">
          <header className="flex items-start gap-3">
            <Icon name={TYPE_ICON[s.type]} className="w-5 h-5 mt-0.5 shrink-0 text-brand-700 dark:text-brand-400" />
            <div className="flex-1 min-w-0">
              <h3 className="font-bold break-words">{s.name}</h3>
              <p className="text-xs text-ink-faint">
                {[t(`types.${s.type}`), s.address, t('admin.by', { name: s.createdBy })].filter(Boolean).join(' · ')}
              </p>
            </div>
            <span className={`shrink-0 px-2 py-0.5 rounded-full text-xs font-bold
                              ${s.status === 'Hidden' ? 'bg-warn/15 text-warn' : 'bg-muted text-ink-soft'}`}>
              {s.status === 'Hidden' ? t('admin.hide') : t('admin.reports', { count: s.reportCount })}
            </span>
          </header>
          {s.reports.length > 0 && (
            <ul className="space-y-1 text-sm">
              {s.reports.map((r, i) => (
                <li key={i} className="flex gap-2">
                  <Icon name="flag" className="w-4 h-4 mt-0.5 shrink-0 text-bad" />
                  <span className="min-w-0">
                    <b className="font-semibold">{t(`report.reasons.${r.reason}`)}</b>
                    {r.note && <span className="text-ink-soft"> — {r.note}</span>}
                    <span className="block text-xs text-ink-faint">{r.username} · {formatDateTime(r.createdAt)}</span>
                  </span>
                </li>
              ))}
            </ul>
          )}
          <div className="flex flex-wrap gap-2">
            <button onClick={() => onOpen(s.id)} className={secondaryBtn}><Icon name="map" className="w-4 h-4" /> {t('admin.open')}</button>
            <button onClick={() => act(s.id, () => setShopStatus(s.id, 'Visible'))} disabled={busy === s.id} className={secondaryBtn}>
              <Icon name="show" className="w-4 h-4" /> {t('admin.show')}
            </button>
            {s.status === 'Visible' && (
              <button onClick={() => act(s.id, () => setShopStatus(s.id, 'Hidden'))} disabled={busy === s.id} className={secondaryBtn}>
                <Icon name="hide" className="w-4 h-4" /> {t('admin.hide')}
              </button>
            )}
            <button onClick={() => setConfirmDelete(s)} disabled={busy === s.id} className={`${secondaryBtn} hover:text-bad`}>
              <Icon name="trash" className="w-4 h-4" /> {t('detail.delete')}
            </button>
          </div>
        </article>
      ))}
      {confirmDelete && (
        <ConfirmDialog title={t('detail.deleteTitle')} message={t('detail.deleteMessage')} confirmLabel={t('detail.delete')}
                       onClose={closeConfirm}
                       onConfirm={() => { const s = confirmDelete; closeConfirm(); act(s.id, () => deleteShop(s.id)) }} />
      )}
    </div>
  )
}
