import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import Icon from '../Icon'
import { secondaryBtn } from '../map/Sheet'
import { getAdminSites, setAdminSiteStatus, type AdminSite } from '../../api/client'
import { formatDateTime } from '../../utils/date'
import { sitePath } from '../../views'

/** Tab Quản trị → Website: mọi website con, bị báo cáo lên đầu; ẩn / hiện lại. */
export default function AdminSites() {
  const { t } = useTranslation('site')
  const [items, setItems] = useState<AdminSite[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState<string | null>(null)

  const load = useCallback(() => getAdminSites().then(setItems).catch(e => setError((e as Error).message)), [])
  useEffect(() => { load() }, [load])

  const setStatus = async (s: AdminSite, status: AdminSite['status']) => {
    setBusy(s.id)
    setError(null)
    try {
      await setAdminSiteStatus(s.id, status)
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
            <Icon name="site" className="w-5 h-5 mt-0.5 shrink-0 text-brand-700 dark:text-brand-400" />
            <div className="flex-1 min-w-0">
              <h3 className="font-bold break-words">{s.name}</h3>
              <p className="text-xs text-ink-faint">/s/{s.slug} · {t('admin.by', { name: s.owner })} · {formatDateTime(s.createdAt)}</p>
            </div>
            <span className={`shrink-0 px-2 py-0.5 rounded-full text-xs font-bold
                              ${s.status === 'Hidden' ? 'bg-warn/15 text-warn' : s.reportCount ? 'bg-bad/10 text-bad' : 'bg-muted text-ink-soft'}`}>
              {s.status === 'Hidden' ? t('admin.hidden') : s.reportCount ? t('admin.reports', { count: s.reportCount })
                : !s.published ? t('admin.draft') : '✓'}
            </span>
          </header>
          {s.reports.length > 0 && (
            <ul className="space-y-1 text-sm">
              {s.reports.map((r, i) => (
                <li key={i} className="flex gap-2">
                  <Icon name="flag" className="w-4 h-4 mt-0.5 shrink-0 text-bad" />
                  <span className="min-w-0">
                    <b className="font-semibold">{t(`public.reportReasons.${r.reason}`)}</b>
                    {r.note && <span className="text-ink-soft"> — {r.note}</span>}
                    <span className="block text-xs text-ink-faint">{r.username} · {formatDateTime(r.createdAt)}</span>
                  </span>
                </li>
              ))}
            </ul>
          )}
          <div className="flex flex-wrap gap-2">
            {s.published && (
              <a href={sitePath(s.slug)} target="_blank" rel="noopener" className={secondaryBtn}>
                <Icon name="external" className="w-4 h-4" /> {t('admin.open')}
              </a>
            )}
            {s.status === 'Hidden' || s.reportCount > 0 ? (
              <button onClick={() => setStatus(s, 'Active')} disabled={busy === s.id} className={secondaryBtn}>
                <Icon name="show" className="w-4 h-4" /> {t('admin.show')}
              </button>
            ) : null}
            {s.status === 'Active' && (
              <button onClick={() => setStatus(s, 'Hidden')} disabled={busy === s.id} className={secondaryBtn}>
                <Icon name="hide" className="w-4 h-4" /> {t('admin.hide')}
              </button>
            )}
          </div>
        </article>
      ))}
    </div>
  )
}
