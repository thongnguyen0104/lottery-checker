import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  getCheckHistory, getProfile, type CheckHistoryItem, type FeatureKey, type Features, type Profile as ProfileData,
} from '../api/client'
import { provinceName } from '../data/provinces'
import { currentLocale } from '../i18n'
import { formatDate, formatDateTime } from '../utils/date'
import type { ResultsFocus } from './AvailableData'
import Icon, { IconBadge, type IconName } from './Icon'
import MyTickets from './MyTickets'

type Tab = 'tickets' | 'history'

/** Mỗi tab thuộc 1 cờ tính năng — cờ tắt thì tab ẩn. */
const TAB_FEATURE: Record<Tab, FeatureKey> = { tickets: 'scratchTickets', history: 'checkHistory' }

type Props = {
  onShowResults: (focus: ResultsFocus) => void
  onGoCheck: () => void
  features: Features
}

const num = (n: number) => n.toLocaleString(currentLocale())

/**
 * Trang Tài khoản (đã đăng nhập): số dư, Vé của tôi, Lịch sử dò vé — phần nào hiện tuỳ cờ tính năng.
 * Mount lại mỗi lần mở để lịch sử luôn mới.
 */
export default function Profile({ onShowResults, onGoCheck, features }: Props) {
  const { t } = useTranslation('profile')
  const [profile, setProfile] = useState<ProfileData | null>(null)
  const tabs = (['tickets', 'history'] as const).filter(x => features.available[TAB_FEATURE[x]])
  const [picked, setPicked] = useState<Tab>('tickets')
  // Tab đang chọn vừa bị tắt (admin đổi cờ) → về tab đầu còn lại.
  const tab = tabs.includes(picked) ? picked : tabs[0]
  const setTab = setPicked
  const tickets = features.available.scratchTickets
  const [error, setError] = useState<string | null>(null)
  const [reload, setReload] = useState(0)
  // Ổn định để MyTickets không tải lại mỗi lần Profile render.
  const onBalance = useCallback((balance: number) => setProfile(p => p && { ...p, balance }), [])

  useEffect(() => {
    let alive = true
    getProfile()
      .then(p => alive && setProfile(p))
      .catch(e => alive && setError(e?.message ?? ''))
    return () => { alive = false }
  }, [reload])

  if (error) return (
    <div className="card p-6 text-center space-y-3 max-w-2xl mx-auto">
      <IconBadge name="error" tone="bad" />
      <div className="text-sm text-bad">{error}</div>
      <button onClick={() => { setError(null); setReload(n => n + 1) }} className="btn btn-soft">
        <Icon name="retry" className="w-4 h-4" /> {t('retry')}
      </button>
    </div>
  )

  return (
    <div className="space-y-4 max-w-2xl mx-auto">
      {/* Tên + ngày tham gia */}
      <div className="flex items-center gap-3">
        <span className="shrink-0 w-14 h-14 rounded-2xl flex items-center justify-center text-2xl font-extrabold uppercase
                         bg-gradient-to-br from-primary to-primary-end text-on-primary shadow-md shadow-primary/25">
          {profile?.username[0] ?? <Icon name="user" className="w-7 h-7" />}
        </span>
        <div className="min-w-0">
          <h1 className="text-xl md:text-3xl font-extrabold tracking-tight truncate">
            {profile?.username ?? <span className="inline-block h-6 w-40 rounded-full bg-muted motion-safe:animate-pulse" />}
          </h1>
          <p className="text-sm text-ink-soft mt-0.5">
            {profile ? t('memberSince', { date: new Date(profile.createdAt).toLocaleDateString(currentLocale(),
              { day: '2-digit', month: '2-digit', year: 'numeric' }) }) : t('title')}
          </p>
        </div>
      </div>

      {/* Số dư tài khoản — chỉ dùng để mua vé cào nên đi cùng cờ vé cào */}
      {tickets && <div className="card p-5 flex items-center gap-4">
        <span className="shrink-0 w-12 h-12 rounded-2xl ring-1 flex items-center justify-center
                         bg-brand-500/10 text-brand-700 ring-brand-500/25 dark:text-brand-400">
          <Icon name="wallet" className="w-6 h-6" />
        </span>
        <div className="min-w-0">
          <div className="text-sm text-ink-faint">{t('balance')}</div>
          <div className="text-2xl md:text-3xl font-extrabold tabular-nums text-brand-700 dark:text-brand-400 truncate">
            {profile ? t('money', { amount: num(profile.balance) })
                     : <span className="inline-block h-7 w-32 rounded-full bg-muted motion-safe:animate-pulse" />}
          </div>
          <div className="text-xs text-ink-faint mt-0.5">{t('balanceNote')}</div>
          {!features.enabled.scratchTickets && <PreviewBadge />}
        </div>
      </div>}

      {tabs.length === 0 && (
        <div className="card p-6 text-center text-sm text-ink-soft">{t('nothingYet')}</div>
      )}

      {tabs.length > 0 && (
        <div className="grid gap-1 p-1 rounded-xl bg-muted border border-line/60" role="tablist"
             style={{ gridTemplateColumns: `repeat(${tabs.length}, minmax(0, 1fr))` }}>
          {tabs.map(x => (
            <button key={x} role="tab" aria-selected={x === tab} onClick={() => setTab(x)}
                    className={`py-2.5 rounded-lg text-sm font-semibold transition active:scale-95 ${x === tab
                      ? 'tab-pop bg-surface dark:bg-brand-500/10 shadow-sm text-brand-700 dark:text-brand-400'
                      : 'text-ink-faint hover:text-ink-soft'}`}>
              {t(`tabs.${x}`)}
              {!features.enabled[TAB_FEATURE[x]] && <span className="ml-1 text-warn" aria-label={t('previewOnly')}>●</span>}
            </button>
          ))}
        </div>
      )}

      {/* Vé của tôi: mua vé cào 2 số + các vé đã mua. Mua/trúng đổi số dư → cập nhật thẻ số dư ở trên. */}
      {tickets && (
        <div hidden={tab !== 'tickets'} className="space-y-3">
          {!features.enabled.scratchTickets && <PreviewBadge />}
          <MyTickets onBalance={onBalance} />
        </div>
      )}

      {features.available.checkHistory && (
        <div hidden={tab !== 'history'} className="space-y-3">
          {!features.enabled.checkHistory && <PreviewBadge />}
          {profile && <Summary checks={profile.checks} />}
          <History onShowResults={onShowResults} onGoCheck={onGoCheck} />
        </div>
      )}
    </div>
  )
}

function Summary({ checks }: { checks: ProfileData['checks'] }) {
  const { t } = useTranslation('profile')
  const short = (n: number) => n.toLocaleString(currentLocale(), { maximumFractionDigits: 1 })
  // Ô nhỏ không đủ chỗ cho 2.000.000.000 ₫ → 2 tỷ / 150 triệu; dưới 1 triệu ghi đủ.
  const money = (n: number) =>
    n >= 1e9 ? t('summary.billion', { n: short(n / 1e9) })
    : n >= 1e6 ? t('summary.million', { n: short(n / 1e6) })
    : t('money', { amount: num(n) })
  const tiles: [IconName, string, string][] = [
    ['ticket', t('summary.checks'), num(checks.checks)],
    ['trophy', t('summary.winners'), num(checks.winners)],
    ['award', t('summary.prize'), money(checks.totalPrize)],
  ]
  return (
    <dl className="grid grid-cols-3 gap-2">
      {tiles.map(([icon, label, value]) => (
        <div key={icon} className="card px-2 py-2.5 text-center">
          <dt className="flex items-center justify-center gap-1 text-[11px] text-ink-faint">
            <Icon name={icon} className="w-3.5 h-3.5 shrink-0" /> <span className="truncate">{label}</span>
          </dt>
          <dd className="mt-0.5 font-extrabold tabular-nums text-brand-700 dark:text-brand-400 truncate">{value}</dd>
        </div>
      ))}
    </dl>
  )
}

function History({ onShowResults, onGoCheck }: Omit<Props, 'features'>) {
  const { t } = useTranslation('profile')
  const [items, setItems] = useState<CheckHistoryItem[]>([])
  const [page, setPage] = useState(1)
  const [hasMore, setHasMore] = useState(false)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [reload, setReload] = useState(0)

  useEffect(() => {
    let alive = true
    getCheckHistory(page)
      .then(d => {
        if (!alive) return
        setItems(prev => page === 1 ? d.items : [...prev, ...d.items.filter(x => !prev.some(p => p.id === x.id))])
        setHasMore(d.hasMore)
      })
      .catch(e => alive && setError(e?.message ?? ''))
      .finally(() => alive && setLoading(false))
    return () => { alive = false }
  }, [page, reload])

  const load = (p: number) => {
    setLoading(true)
    setError(null)
    setPage(p)
    setReload(n => n + 1)
  }

  if (error) return (
    <div className="card p-6 text-center space-y-3">
      <IconBadge name="error" tone="bad" />
      <div className="text-sm text-bad">{error}</div>
      <button onClick={() => load(page)} className="btn btn-soft">
        <Icon name="retry" className="w-4 h-4" /> {t('retry')}
      </button>
    </div>
  )

  if (!loading && items.length === 0) return (
    <div className="card p-6 text-center space-y-3">
      <IconBadge name="history" tone="info" />
      <p className="text-sm text-ink-soft">{t('historyEmpty')}</p>
      <button onClick={onGoCheck} className="btn btn-primary">
        <Icon name="ticket" className="w-4 h-4" /> {t('goCheck')}
      </button>
    </div>
  )

  return (
    <>
      <ul className="card divide-y divide-line/70 overflow-hidden">
        {items.map(x => <HistoryRow key={x.id} item={x} onShowResults={onShowResults} />)}
        {loading && page === 1 && Array.from({ length: 4 }, (_, i) => (
          <li key={i} className="px-4 py-3 flex justify-between motion-safe:animate-pulse">
            <div className="space-y-2">
              <div className="h-5 w-24 rounded-full bg-muted" />
              <div className="h-3 w-32 rounded-full bg-muted" />
            </div>
            <div className="h-6 w-20 rounded-full bg-muted" />
          </li>
        ))}
      </ul>
      {hasMore && (
        <button onClick={() => load(page + 1)} disabled={loading} className="btn btn-soft w-full">
          {loading ? t('loading') : t('loadMore')}
        </button>
      )}
    </>
  )
}

const STATUS_TONE = {
  win: 'bg-ok/10 text-ok ring-ok/25',
  lose: 'bg-muted text-ink-faint ring-line',
  NotDrawnYet: 'bg-info/10 text-info ring-info/25',
  NoData: 'bg-warn/10 text-warn ring-warn/25',
  Expired: 'bg-bad/10 text-bad ring-bad/25',
}

function HistoryRow({ item, onShowResults }: { item: CheckHistoryItem; onShowResults: Props['onShowResults'] }) {
  const { t } = useTranslation('profile')
  const key = item.status === 'Checked' ? (item.isWinner ? 'win' : 'lose') : item.status
  const { drawDate, province } = item
  const where = drawDate && province ? `${provinceName(province)} · ${formatDate(drawDate)}` : t('unknownDraw')
  const checkedAt = formatDateTime(item.checkedAt)

  const body = (
    <>
      <div className="min-w-0 text-left">
        <div className="font-mono text-lg font-bold tabular-nums tracking-wider">{item.ticketNumber}</div>
        <div className="text-xs text-ink-faint truncate">{where}</div>
      </div>
      <div className="shrink-0 text-right space-y-1">
        <span className={`inline-block px-2 py-0.5 rounded-full ring-1 text-xs font-semibold ${STATUS_TONE[key]}`}>
          {key === 'win' ? t('status.win', { amount: num(item.prize) }) : t(`status.${key}`)}
        </span>
        <div className="text-[11px] text-ink-faint tabular-nums">{checkedAt}</div>
      </div>
    </>
  )

  // Có đủ đài + ngày → bấm mở bảng kết quả của đài đó, tô số trùng với vé.
  return (
    <li>
      {drawDate && province ? (
        <button onClick={() => onShowResults({ drawDate, province, ticketNumber: item.ticketNumber, from: 'profile' })}
                title={t('viewResults', { province: provinceName(province), date: formatDate(drawDate) })}
                className="w-full px-4 py-3 flex items-center justify-between gap-3 hover:bg-muted/60 transition">
          {body}
        </button>
      ) : (
        <div className="px-4 py-3 flex items-center justify-between gap-3">{body}</div>
      )}
    </li>
  )
}

/** Admin đang xem trước tính năng chưa bật — user thường không thấy phần này. */
function PreviewBadge() {
  const { t } = useTranslation('profile')
  return (
    <span className="inline-flex items-center gap-1 mt-1 px-2 py-0.5 rounded-md text-[11px] font-semibold bg-warn/15 text-warn">
      <Icon name="admin" className="w-3 h-3" /> {t('previewOnly')}
    </span>
  )
}
