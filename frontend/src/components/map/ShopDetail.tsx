import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import Icon from '../Icon'
import ConfirmDialog from '../ConfirmDialog'
import { ReportDialog, WinDialog } from './ShopDialogs'
import HistoryDialog from './HistoryDialog'
import { primaryBtn, secondaryBtn } from './Sheet'
import {
  confirmShop, deleteShop, deleteShopReview, deleteShopWin, getShop, shopImageUrl, upsertShopReview,
  type Account, type ShopDetail as Detail,
} from '../../api/client'
import { provinceName } from '../../data/provinces'
import { formatDate, timeAgo } from '../../utils/date'
import { shopPath, sitePath } from '../../views'
import { directionsUrl, fmtMinutes, isOpenNow, TYPE_ICON, VIETLOTT } from './shopUtils'

type Props = {
  id: string
  account: Account | null
  onRequireLogin: () => void
  onClose: () => void
  /** Tải xong — ShopMap bay tới điểm nếu mở từ link / thông báo. */
  onLoaded: (d: Detail) => void
  onEdit: (d: Detail) => void
  onDeleted: (id: string) => void
  /** Số liệu trên marker đổi (đánh giá, vé trúng, bị ẩn) → bản đồ tải lại. */
  onChanged: () => void
}

/** Chi tiết 1 điểm bán: thông tin, xác nhận còn bán, chỉ đường, chia sẻ, đánh giá, vé trúng, báo cáo. */
export default function ShopDetail({ id, account, onRequireLogin, onClose, onLoaded, onEdit, onDeleted, onChanged }: Props) {
  const { t } = useTranslation('map')
  const [d, setD] = useState<Detail | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [notice, setNotice] = useState<string | null>(null)
  const [dialog, setDialog] = useState<'win' | 'report' | 'delete' | 'history' | null>(null)
  const closeDialog = useCallback(() => setDialog(null), [])

  const load = useCallback(() =>
    getShop(id).then(x => { setD(x); setError(null); return x }).catch(e => { setError((e as Error).message); return null }),
  [id])

  // Cha đặt key theo id → đổi điểm là mount mới, không cần dọn state cũ ở đây.
  useEffect(() => {
    load().then(x => { if (x) onLoaded(x) })
    // onLoaded chỉ gọi khi tải lần đầu, không theo mỗi lần render của cha.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [load])

  const needLogin = (fn: () => void) => () => (account ? fn() : onRequireLogin())

  const run = async (fn: () => Promise<unknown>) => {
    setBusy(true)
    setNotice(null)
    try {
      await fn()
      await load()
      onChanged()
    } catch (e) {
      setNotice((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  const share = async () => {
    const url = `${location.origin}${shopPath(id)}`
    try {
      if (navigator.share) await navigator.share({ title: d?.name, url })
      else {
        await navigator.clipboard.writeText(url)
        setNotice(t('detail.copied'))
      }
    } catch { /* người dùng huỷ chia sẻ */ }
  }

  if (error) {
    return (
      <div className="card p-5 text-center space-y-3">
        <p className="text-sm text-ink-soft">{t('detail.gone')}</p>
        <button onClick={onClose} className={secondaryBtn}>{t('detail.close')}</button>
      </div>
    )
  }
  if (!d) return <div className="card p-5 animate-pulse h-48" aria-busy />

  const open = isOpenNow(d)

  return (
    <article className="card overflow-hidden">
      {d.imageUrl && <img src={shopImageUrl(d.imageUrl)} alt="" className="w-full max-h-56 object-cover" />}
      <div className="p-4 space-y-4">
        <header className="flex items-start gap-3">
          <span className="shrink-0 w-10 h-10 rounded-xl flex items-center justify-center bg-brand-500/10 text-brand-700 dark:text-brand-400">
            <Icon name={TYPE_ICON[d.type]} />
          </span>
          <div className="flex-1 min-w-0">
            <h2 className="text-lg font-bold leading-tight break-words">{d.name}</h2>
            <p className="text-xs text-ink-faint">{t(`types.${d.type}`)}</p>
          </div>
          <button onClick={onClose} aria-label={t('detail.close')}
                  className="shrink-0 w-9 h-9 -mr-1 -mt-1 rounded-xl flex items-center justify-center text-ink-faint hover:bg-muted">
            <Icon name="close" />
          </button>
        </header>

        {d.status === 'Hidden' && (
          <p className="px-3 py-2 rounded-xl bg-warn/10 text-sm text-warn flex items-center gap-2">
            <Icon name="hide" className="w-4 h-4" /> {t('detail.hidden')}
          </p>
        )}

        <ul className="space-y-1.5 text-sm">
          {d.address && (
            <li className="flex gap-2"><Icon name="pin" className="w-4 h-4 mt-0.5 shrink-0 text-ink-faint" /> {d.address}</li>
          )}
          <li className="flex gap-2">
            <Icon name="clock" className="w-4 h-4 mt-0.5 shrink-0 text-ink-faint" />
            {d.opensAtMin == null || d.closesAtMin == null ? <span className="text-ink-faint">{t('detail.hoursUnknown')}</span> : (
              <span>
                {d.opensAtMin === d.closesAtMin ? t('detail.allDay') : `${fmtMinutes(d.opensAtMin)} – ${fmtMinutes(d.closesAtMin)}`}
                {' · '}
                <span className={open ? 'text-ok font-semibold' : 'text-bad font-semibold'}>
                  {t(open ? 'detail.openNow' : 'detail.closedNow')}
                </span>
              </span>
            )}
          </li>
          {d.phone && (
            <li className="flex gap-2">
              <Icon name="phone" className="w-4 h-4 mt-0.5 shrink-0 text-ink-faint" />
              <a href={`tel:${d.phone}`} className="text-brand-700 dark:text-brand-400 font-semibold hover:underline">{d.phone}</a>
            </li>
          )}
          {d.note && (
            <li className="flex gap-2"><Icon name="sms" className="w-4 h-4 mt-0.5 shrink-0 text-ink-faint" />
              <span className="whitespace-pre-line break-words">{d.note}</span></li>
          )}
          <li className="flex gap-2 text-ink-faint">
            <Icon name="ok" className="w-4 h-4 mt-0.5 shrink-0" />
            {d.lastConfirmedAt
              ? `${t('detail.confirmCount', { count: d.confirmCount })} · ${t('detail.lastConfirmed', { ago: timeAgo(d.lastConfirmedAt) })}`
              : t('detail.neverConfirmed')}
          </li>
        </ul>

        <div className="grid grid-cols-2 gap-2">
          <button onClick={needLogin(() => run(() => confirmShop(d.id)))} disabled={busy || d.confirmedRecently || d.status === 'Hidden'}
                  className={`${primaryBtn} inline-flex items-center justify-center gap-1.5 text-sm`}>
            <Icon name="check" className="w-4 h-4" /> {t(d.confirmedRecently ? 'detail.confirmed' : 'detail.confirm')}
          </button>
          <a href={directionsUrl(d.lat, d.lng)} target="_blank" rel="noreferrer" className={`${secondaryBtn} py-2.5`}>
            <Icon name="directions" className="w-4 h-4" /> {t('detail.directions')}
          </a>
        </div>
        {d.siteSlug && (
          <a href={sitePath(d.siteSlug)} target="_blank" rel="noopener" className={`${secondaryBtn} w-full py-2.5`}>
            <Icon name="site" className="w-4 h-4" /> {t('detail.website')}
          </a>
        )}
        <div className="flex flex-wrap gap-2">
          <button onClick={share} className={secondaryBtn}><Icon name="share" className="w-4 h-4" /> {t('detail.share')}</button>
          {!d.mine && (
            <button onClick={needLogin(() => setDialog('report'))} disabled={d.reportedByMe || d.status === 'Hidden'} className={secondaryBtn}>
              <Icon name="flag" className="w-4 h-4" /> {t(d.reportedByMe ? 'detail.reported' : 'detail.report')}
            </button>
          )}
          {d.canEdit && (
            <>
              <button onClick={() => onEdit(d)} className={secondaryBtn}><Icon name="edit" className="w-4 h-4" /> {t('detail.edit')}</button>
              <button onClick={() => setDialog('delete')} className={`${secondaryBtn} hover:text-bad`}>
                <Icon name="trash" className="w-4 h-4" /> {t('detail.delete')}
              </button>
            </>
          )}
          {account?.isAdmin && (
            <button onClick={() => setDialog('history')} className={secondaryBtn}>
              <Icon name="history" className="w-4 h-4" /> {t('history.open')}
            </button>
          )}
        </div>
        {notice && <p className="text-sm text-ink-soft" role="status">{notice}</p>}
        <p className="text-xs text-ink-faint">{t('detail.createdBy', { name: d.createdBy, ago: timeAgo(d.createdAt) })}</p>

        <Wins d={d} account={account} onAdd={needLogin(() => setDialog('win'))}
              onDelete={wid => run(() => deleteShopWin(wid))} />
        {/* key: đánh giá của mình vừa thêm / xoá → form dựng lại theo giá trị mới. */}
        <Reviews key={d.myReview?.id ?? 'new'} d={d} account={account} busy={busy} onRequireLogin={onRequireLogin}
                 onSave={(stars, content) => run(() => upsertShopReview(d.id, stars, content))}
                 onDelete={rid => run(() => deleteShopReview(rid))} />
      </div>

      {dialog === 'history' && <HistoryDialog shopId={d.id} onClose={closeDialog} />}
      {dialog === 'win' && (
        <WinDialog shopId={d.id} onClose={closeDialog} onDone={() => { closeDialog(); load(); onChanged() }} />
      )}
      {dialog === 'report' && (
        <ReportDialog shopId={d.id} onClose={closeDialog}
                      onDone={hidden => { closeDialog(); setNotice(t(hidden ? 'report.hidden' : 'report.done')); load(); if (hidden) onChanged() }} />
      )}
      {dialog === 'delete' && (
        <ConfirmDialog title={t('detail.deleteTitle')} message={t('detail.deleteMessage')} confirmLabel={t('detail.delete')}
                       onClose={closeDialog}
                       onConfirm={() => {
                         closeDialog()
                         setBusy(true)
                         deleteShop(d.id).then(() => onDeleted(d.id))
                           .catch(e => { setNotice((e as Error).message); setBusy(false) })
                       }} />
      )}
    </article>
  )
}

function Wins({ d, account, onAdd, onDelete }: {
  d: Detail; account: Account | null; onAdd: () => void; onDelete: (id: number) => void
}) {
  const { t } = useTranslation('map')
  return (
    <section className="space-y-2">
      <div className="flex items-center gap-2">
        <h3 className="flex-1 font-bold flex items-center gap-1.5">
          <Icon name="trophy" className="w-4 h-4 text-warn" /> {t('wins.title')}
          {d.winCount > 0 && <span className="text-xs font-semibold text-ink-faint">({d.winCount})</span>}
        </h3>
        {d.status === 'Visible' && (
          <button onClick={onAdd} className="text-sm font-semibold text-brand-700 dark:text-brand-400 hover:underline">
            + {t('wins.add')}
          </button>
        )}
      </div>
      {d.wins.length === 0 ? <p className="text-sm text-ink-faint">{t('wins.empty')}</p> : (
        <>
          <p className="text-xs text-ink-faint">{t('wins.unverified')}</p>
          <ul className="space-y-1.5">
            {d.wins.map(w => (
              <li key={w.id} className="flex items-center gap-2 text-sm">
                {w.imageUrl && (
                  <a href={shopImageUrl(w.imageUrl)} target="_blank" rel="noreferrer" className="shrink-0">
                    <img src={shopImageUrl(w.imageUrl)} alt="" className="w-10 h-10 rounded-lg object-cover border border-line" />
                  </a>
                )}
                <span className="flex-1 min-w-0">
                  <span className="block font-semibold">
                    {t('wins.line', {
                      tier: t(`wins.tiers.${w.prizeTier}`, { defaultValue: w.prizeTier }),
                      province: w.provinceCode === VIETLOTT ? 'Vietlott' : provinceName(w.provinceCode),
                      date: formatDate(w.drawDate),
                    })}
                  </span>
                  <span className="block text-xs text-ink-faint">{w.username} · {timeAgo(w.createdAt)}</span>
                </span>
                {(w.mine || account?.isAdmin) && (
                  <button onClick={() => onDelete(w.id)} aria-label={t('wins.delete')}
                          className="shrink-0 w-8 h-8 rounded-lg flex items-center justify-center text-ink-faint hover:text-bad hover:bg-muted">
                    <Icon name="trash" className="w-4 h-4" />
                  </button>
                )}
              </li>
            ))}
          </ul>
        </>
      )}
    </section>
  )
}

function Stars({ value, onPick, size = 'w-4 h-4' }: { value: number; onPick?: (n: number) => void; size?: string }) {
  const { t } = useTranslation('map')
  return (
    <span className="inline-flex gap-0.5" role={onPick ? 'radiogroup' : undefined} aria-label={t('reviews.stars', { count: value })}>
      {[1, 2, 3, 4, 5].map(n => {
        const star = <Icon name="star" className={`${size} ${n <= value ? 'text-yellow-500 fill-yellow-400' : 'text-ink-faint'}`} />
        return onPick ? (
          <button key={n} type="button" role="radio" aria-checked={n === value} aria-label={t('reviews.stars', { count: n })}
                  onClick={() => onPick(n)} className="p-0.5 active:scale-90 transition">{star}</button>
        ) : <span key={n}>{star}</span>
      })}
    </span>
  )
}

function Reviews({ d, account, busy, onRequireLogin, onSave, onDelete }: {
  d: Detail; account: Account | null; busy: boolean; onRequireLogin: () => void
  onSave: (stars: number, content: string | null) => void; onDelete: (id: number) => void
}) {
  const { t } = useTranslation('map')
  const [stars, setStars] = useState(d.myReview?.stars ?? 0)
  const [content, setContent] = useState(d.myReview?.content ?? '')

  return (
    <section className="space-y-2">
      <h3 className="font-bold flex items-center gap-1.5">
        <Icon name="star" className="w-4 h-4 text-yellow-500" /> {t('reviews.title')}
        {d.rating != null && (
          <span className="text-sm font-semibold text-ink-soft">{d.rating.toFixed(1)} · {t('reviews.count', { count: d.ratingCount })}</span>
        )}
      </h3>

      {d.status === 'Visible' && (account ? (
        <form onSubmit={e => { e.preventDefault(); if (stars) onSave(stars, content.trim() || null) }}
              className="p-3 rounded-xl bg-muted/60 space-y-2">
          <div className="flex items-center gap-2 text-sm font-semibold">
            {t('reviews.yours')} <Stars value={stars} onPick={setStars} size="w-6 h-6" />
          </div>
          <textarea className="field min-h-[60px] text-sm" value={content} maxLength={1000} onChange={e => setContent(e.target.value)}
                    placeholder={t('reviews.placeholder')} />
          <div className="flex gap-2 justify-end">
            {d.myReview && (
              <button type="button" onClick={() => onDelete(d.myReview!.id)} disabled={busy} className={secondaryBtn}>
                {t('reviews.delete')}
              </button>
            )}
            <button type="submit" disabled={busy || !stars} className={`${primaryBtn} py-2 text-sm`}>
              {t(d.myReview ? 'reviews.update' : 'reviews.submit')}
            </button>
          </div>
        </form>
      ) : (
        <button onClick={onRequireLogin} className="text-sm font-semibold text-brand-700 dark:text-brand-400 hover:underline">
          {t('reviews.loginHint')}
        </button>
      ))}

      {d.reviews.filter(r => !r.mine).length === 0 && !d.myReview
        ? <p className="text-sm text-ink-faint">{t('reviews.empty')}</p>
        : (
          <ul className="space-y-3">
            {d.reviews.filter(r => !r.mine).map(r => (
              <li key={r.id} className="text-sm">
                <div className="flex items-center gap-2">
                  <span className="font-semibold">{r.username}</span>
                  <Stars value={r.stars} size="w-3.5 h-3.5" />
                  <span className="text-xs text-ink-faint">{timeAgo(r.updatedAt)}</span>
                  {account?.isAdmin && (
                    <button onClick={() => onDelete(r.id)} aria-label={t('reviews.delete')}
                            className="ml-auto text-ink-faint hover:text-bad"><Icon name="trash" className="w-4 h-4" /></button>
                  )}
                </div>
                {r.content && <p className="mt-0.5 text-ink-soft whitespace-pre-line break-words">{r.content}</p>}
              </li>
            ))}
          </ul>
        )}
    </section>
  )
}
