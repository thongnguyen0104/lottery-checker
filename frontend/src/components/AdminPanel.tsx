import { useEffect, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import {
  adjustBalance, getAdminFeatures, getAdminOverview, getAdminTransactions, getAdminUser, getAdminUsers, resetUserPassword,
  setAdminFeature, setUserAdmin, type AdminFeature, type FeatureKey,
  type AdminOverview, type AdminUserDetail, type AdminUserRow, type WalletTransaction,
} from '../api/client'
import { currentLocale } from '../i18n'
import { PASSWORD_RULES, passwordValid } from '../utils/accountRules'
import { formatDateTime } from '../utils/date'
import ConfirmDialog from './ConfirmDialog'
import Icon, { IconBadge, type IconName } from './Icon'
import AdminShops from './map/AdminShops'

const num = (n: number) => n.toLocaleString(currentLocale())
const joined = (iso: string) => new Date(iso).toLocaleDateString(currentLocale(), { day: '2-digit', month: '2-digit', year: 'numeric' })
const QUICK_AMOUNTS = [50_000, 100_000, 500_000, 1_000_000]
const MAX_AMOUNT = 100_000_000

type Sort = 'new' | 'balance'

/** Trang quản trị: tổng quan, danh sách tài khoản, chi tiết + cộng/trừ tiền, đặt lại mật khẩu, quyền admin. */
export default function AdminPanel({ me, onFeaturesChanged, onOpenShop }: {
  me: string; onFeaturesChanged: () => void
  /** Tab Điểm bán → mở điểm trên bản đồ. */
  onOpenShop: (id: string) => void
}) {
  const { t } = useTranslation('admin')
  const [section, setSection] = useState<'users' | 'features' | 'shops'>('users')
  const [overview, setOverview] = useState<AdminOverview | null>(null)
  const [selected, setSelected] = useState<number | null>(null)
  // Tăng khi có thay đổi (cộng tiền, đổi quyền) → danh sách + tổng quan tải lại cho khớp.
  const [version, setVersion] = useState(0)

  useEffect(() => { getAdminOverview().then(setOverview).catch(() => {}) }, [version])

  const select = (id: number | null) => {
    setSelected(id)
    if (window.innerWidth < 1024) window.scrollTo(0, 0)
  }

  return (
    <div className="space-y-4">
      <div>
        <h1 className="text-xl md:text-3xl font-extrabold tracking-tight flex items-center gap-2">
          <Icon name="admin" className="w-6 h-6 md:w-8 md:h-8 text-brand-700 dark:text-brand-400" /> {t('title')}
        </h1>
        <p className="text-sm md:text-base text-ink-soft mt-0.5">{t('hint')}</p>
      </div>

      {overview && <Overview o={overview} />}

      <div className="grid grid-cols-3 gap-1 p-1 rounded-xl bg-muted border border-line/60 max-w-md" role="tablist">
        {(['users', 'features', 'shops'] as const).map(s => (
          <button key={s} role="tab" aria-selected={s === section} onClick={() => setSection(s)}
                  className={`flex items-center justify-center gap-1.5 py-2 rounded-lg text-sm font-semibold transition ${s === section
                    ? 'bg-surface dark:bg-brand-500/10 shadow-sm text-brand-700 dark:text-brand-400'
                    : 'text-ink-faint hover:text-ink-soft'}`}>
            <Icon name={s === 'users' ? 'users' : s === 'shops' ? 'map' : 'sparkles'} className="w-4 h-4" /> {t(`sections.${s}`)}
          </button>
        ))}
      </div>

      {section === 'features' && <FeatureToggles onChanged={onFeaturesChanged} />}
      {section === 'shops' && <AdminShops onOpen={onOpenShop} />}

      {/* Điện thoại: danh sách HOẶC chi tiết. Màn rộng: 2 cột cạnh nhau. */}
      <div hidden={section !== 'users'} className="lg:grid lg:grid-cols-[minmax(0,1fr)_minmax(0,1.3fr)] lg:gap-4 lg:items-start">
        <div className={selected != null ? 'hidden lg:block' : ''}>
          <UserList selected={selected} onSelect={select} version={version} me={me} />
        </div>
        <div className={selected == null ? 'hidden lg:block' : ''}>
          {selected == null ? (
            <div className="card p-6 text-center text-sm text-ink-soft">
              <IconBadge name="users" tone="info" />
              <p className="mt-3">{t('pick')}</p>
            </div>
          ) : (
            <UserDetail key={selected} id={selected} me={me} onBack={() => select(null)}
                        onChanged={() => setVersion(v => v + 1)} />
          )}
        </div>
      </div>
    </div>
  )
}

/** Bật/tắt tính năng cho user thường. Tắt = ẩn hẳn ở FE và máy chủ chặn API; admin vẫn xem trước được. */
function FeatureToggles({ onChanged }: { onChanged: () => void }) {
  const { t } = useTranslation('admin')
  const [flags, setFlags] = useState<AdminFeature[] | null>(null)
  const [busy, setBusy] = useState<FeatureKey | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => { getAdminFeatures().then(setFlags).catch(e => setError(e?.message ?? '')) }, [])

  const toggle = async (f: AdminFeature) => {
    setBusy(f.key)
    setError(null)
    try {
      setFlags(await setAdminFeature(f.key, !f.enabled))
      onChanged()
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(null)
    }
  }

  return (
    <div className="space-y-3 max-w-2xl">
      <p className="text-sm text-ink-soft">{t('features.hint')}</p>
      {error && <p className="text-sm text-bad">{error}</p>}
      {!flags && !error && <div className="card h-24 motion-safe:animate-pulse" />}
      {flags?.map(f => (
        <div key={f.key} className="card p-4 flex items-start gap-4">
          <div className="min-w-0 flex-1">
            <div className="flex flex-wrap items-center gap-2">
              <span className="font-bold">{f.name}</span>
              <span className={`px-1.5 py-0.5 rounded-md text-[10px] font-bold uppercase tracking-wide
                                ${f.enabled ? 'bg-ok/15 text-ok' : 'bg-muted text-ink-faint'}`}>
                {f.enabled ? t('features.on') : t('features.off')}
              </span>
            </div>
            <p className="text-sm text-ink-soft mt-1">{f.description}</p>
            {f.updatedAt && (
              <p className="text-xs text-ink-faint mt-1">
                {t('features.updated', { by: f.updatedBy ?? '?', at: formatDateTime(f.updatedAt) })}
              </p>
            )}
          </div>
          {/* Công tắc: role=switch để trình đọc màn hình đọc đúng bật/tắt */}
          <button role="switch" aria-checked={f.enabled} aria-label={f.name} disabled={busy === f.key}
                  onClick={() => toggle(f)}
                  className={`relative shrink-0 w-12 h-7 rounded-full transition disabled:opacity-50
                              ${f.enabled ? 'bg-ok' : 'bg-line'}`}>
            <span className={`absolute top-0.5 left-0.5 w-6 h-6 rounded-full bg-white shadow transition-transform
                              ${f.enabled ? 'translate-x-5' : ''}`} />
          </button>
        </div>
      ))}
    </div>
  )
}

function Overview({ o }: { o: AdminOverview }) {
  const { t } = useTranslation('admin')
  const tiles: [IconName, string, string, string][] = [
    ['users', t('overview.users'), num(o.users), t('overview.admins', { count: o.admins })],
    ['wallet', t('overview.balance'), t('money', { amount: num(o.totalBalance) }), t('overview.topUp', { amount: num(o.totalTopUp) })],
    ['ticket', t('overview.tickets'), num(o.ticketsSold), t('overview.won', { count: o.ticketsWon })],
    ['award', t('overview.paidOut'), t('money', { amount: num(o.totalPaidOut) }), ''],
  ]
  return (
    <dl className="grid grid-cols-2 md:grid-cols-4 gap-2">
      {tiles.map(([icon, label, value, sub]) => (
        <div key={icon} className="card px-3 py-3">
          <dt className="flex items-center gap-1.5 text-xs text-ink-faint">
            <Icon name={icon} className="w-3.5 h-3.5 shrink-0" /> <span className="truncate">{label}</span>
          </dt>
          <dd className="mt-1 text-lg font-extrabold tabular-nums text-brand-700 dark:text-brand-400 truncate">{value}</dd>
          {sub && <dd className="text-[11px] text-ink-faint truncate">{sub}</dd>}
        </div>
      ))}
    </dl>
  )
}

function UserList({ selected, onSelect, version, me }: {
  selected: number | null; onSelect: (id: number) => void; version: number; me: string
}) {
  const { t } = useTranslation('admin')
  const [search, setSearch] = useState('')
  const [query, setQuery] = useState('')   // search sau khi ngừng gõ 300ms
  const [sort, setSort] = useState<Sort>('new')
  const [retry, setRetry] = useState(0)
  const [page, setPage] = useState(1)
  const [items, setItems] = useState<AdminUserRow[]>([])
  const [total, setTotal] = useState(0)
  const [hasMore, setHasMore] = useState(false)
  // Đang tải / lỗi suy ra từ khoá request: request hiện tại khác request đã xong = đang tải.
  const requestKey = `${query}|${sort}|${page}|${version}|${retry}`
  const [doneKey, setDoneKey] = useState<string | null>(null)
  const [failed, setFailed] = useState<{ key: string; message: string } | null>(null)
  const loading = doneKey !== requestKey
  const error = failed?.key === requestKey ? failed.message : null

  useEffect(() => {
    const timer = setTimeout(() => { setQuery(search.trim()); setPage(1) }, 300)
    return () => clearTimeout(timer)
  }, [search])

  useEffect(() => {
    let alive = true
    getAdminUsers(query, sort, page)
      .then(d => {
        if (!alive) return
        setItems(prev => page === 1 ? d.items : [...prev, ...d.items.filter(x => !prev.some(p => p.id === x.id))])
        setHasMore(d.hasMore)
        setTotal(d.total)
      })
      .catch(e => alive && setFailed({ key: requestKey, message: e?.message ?? '' }))
      .finally(() => alive && setDoneKey(requestKey))
    return () => { alive = false }
  }, [query, sort, page, requestKey])

  return (
    <div className="space-y-3">
      <div className="flex gap-2">
        <label className="relative flex-1">
          <Icon name="find" className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-ink-faint" />
          <input value={search} onChange={e => setSearch(e.target.value)} placeholder={t('search')}
                 autoCapitalize="none" spellCheck={false} className="field pl-9" aria-label={t('search')} />
        </label>
      </div>
      <div className="flex items-center justify-between gap-2">
        <div className="grid grid-cols-2 gap-1 p-1 rounded-xl bg-muted border border-line/60" role="tablist">
          {(['new', 'balance'] as const).map(s => (
            <button key={s} role="tab" aria-selected={s === sort} onClick={() => { setSort(s); setPage(1) }}
                    className={`px-3 py-1.5 rounded-lg text-sm font-semibold transition ${s === sort
                      ? 'bg-surface dark:bg-brand-500/10 shadow-sm text-brand-700 dark:text-brand-400'
                      : 'text-ink-faint hover:text-ink-soft'}`}>
              {t(`sort.${s}`)}
            </button>
          ))}
        </div>
        <span className="text-xs text-ink-faint">{t('total', { count: total })}</span>
      </div>

      {error ? (
        <div className="card p-4 text-center space-y-2">
          <p className="text-sm text-bad">{error}</p>
          <button onClick={() => setRetry(n => n + 1)} className="btn btn-soft">{t('retry')}</button>
        </div>
      ) : !loading && items.length === 0 ? (
        <div className="card p-6 text-center text-sm text-ink-soft">{t('empty')}</div>
      ) : (
        <ul className="card divide-y divide-line/70 overflow-hidden">
          {items.map(u => (
            <li key={u.id}>
              <button onClick={() => onSelect(u.id)} aria-current={u.id === selected ? 'true' : undefined}
                      className={`w-full px-4 py-3 flex items-center gap-3 text-left transition
                                  ${u.id === selected ? 'bg-brand-500/10' : 'hover:bg-muted/60'}`}>
                <Avatar name={u.username} />
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-1.5 font-semibold">
                    <span className="truncate">{u.username}</span>
                    {u.isAdmin && <Badge tone="brand">{t('badge.admin')}</Badge>}
                    {u.username === me && <Badge tone="muted">{t('badge.you')}</Badge>}
                  </div>
                  <div className="text-xs text-ink-faint truncate">
                    {t('joined', { date: joined(u.createdAt) })} · {t('ticketsCount', { count: u.tickets })}
                  </div>
                </div>
                <span className="shrink-0 font-bold tabular-nums text-brand-700 dark:text-brand-400">
                  {t('money', { amount: num(u.balance) })}
                </span>
              </button>
            </li>
          ))}
          {loading && page === 1 && Array.from({ length: 4 }, (_, i) => (
            <li key={i} className="px-4 py-3 flex items-center gap-3 motion-safe:animate-pulse">
              <div className="w-10 h-10 rounded-full bg-muted" />
              <div className="flex-1 space-y-2">
                <div className="h-4 w-32 rounded-full bg-muted" />
                <div className="h-3 w-24 rounded-full bg-muted" />
              </div>
            </li>
          ))}
        </ul>
      )}

      {hasMore && !error && (
        <button onClick={() => setPage(p => p + 1)} disabled={loading} className="btn btn-soft w-full">
          {loading ? t('loading') : t('loadMore')}
        </button>
      )}
    </div>
  )
}

function UserDetail({ id, me, onBack, onChanged }: {
  id: number; me: string; onBack: () => void; onChanged: () => void
}) {
  const { t } = useTranslation('admin')
  const [user, setUser] = useState<AdminUserDetail | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [reload, setReload] = useState(0)

  useEffect(() => {
    let alive = true
    getAdminUser(id).then(u => alive && setUser(u)).catch(e => alive && setError(e?.message ?? ''))
    return () => { alive = false }
  }, [id, reload])

  const changed = () => { setReload(n => n + 1); onChanged() }

  const back = (
    <button onClick={onBack} className="lg:hidden mb-3 flex items-center gap-1 text-sm font-semibold text-ink-soft hover:text-ink">
      <Icon name="back" className="w-4 h-4" /> {t('back')}
    </button>
  )

  if (error) return (
    <div>{back}<div className="card p-6 text-center text-sm text-bad">{error}</div></div>
  )
  if (!user) return (
    <div>
      {back}
      <div className="card p-5 space-y-3 motion-safe:animate-pulse">
        <div className="h-6 w-40 rounded-full bg-muted" />
        <div className="h-10 w-32 rounded-full bg-muted" />
        <div className="h-20 w-full rounded-xl bg-muted" />
      </div>
    </div>
  )

  const stats: [string, string][] = [
    [t('stats.bought'), num(user.tickets.bought)],
    [t('stats.won'), num(user.tickets.won)],
    [t('stats.pending'), num(user.tickets.pending)],
    [t('stats.checks'), num(user.checks)],
    [t('stats.spent'), t('money', { amount: num(user.tickets.spent) })],
    [t('stats.winnings'), t('money', { amount: num(user.tickets.winnings) })],
  ]

  return (
    <div>
      {back}
      <div className="space-y-3">
      <div className="card p-4 md:p-5 space-y-4">
        <div className="flex items-center gap-3">
          <Avatar name={user.username} big />
          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-1.5">
              <h2 className="text-lg font-extrabold truncate">{user.username}</h2>
              {user.isAdmin && <Badge tone="brand">{t('badge.admin')}</Badge>}
              {user.mustChangePassword && <Badge tone="warn">{t('badge.mustChange')}</Badge>}
            </div>
            <div className="text-xs text-ink-faint">{t('joined', { date: joined(user.createdAt) })} · #{user.id}</div>
          </div>
        </div>
        <div>
          <div className="text-sm text-ink-faint">{t('balanceLabel')}</div>
          <div className="text-3xl font-extrabold tabular-nums text-brand-700 dark:text-brand-400">
            {t('money', { amount: num(user.balance) })}
          </div>
        </div>
        <dl className="grid grid-cols-3 gap-2">
          {stats.map(([label, value]) => (
            <div key={label} className="rounded-xl bg-muted/60 border border-line/60 px-2 py-2 text-center">
              <dt className="text-[11px] text-ink-faint truncate">{label}</dt>
              <dd className="font-bold tabular-nums truncate">{value}</dd>
            </div>
          ))}
        </dl>
      </div>

      <BalanceForm user={user} onDone={changed} />
      <AccountActions user={user} me={me} onDone={changed} />
      <Transactions key={reload} userId={user.id} />
      </div>
    </div>
  )
}

function BalanceForm({ user, onDone }: { user: AdminUserDetail; onDone: () => void }) {
  const { t } = useTranslation('admin')
  const [mode, setMode] = useState<'add' | 'subtract'>('add')
  const [amount, setAmount] = useState(0)
  const [note, setNote] = useState('')
  const [confirming, setConfirming] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState<string | null>(null)

  const signed = mode === 'add' ? amount : -amount
  const tooMuch = mode === 'subtract' && amount > user.balance
  const valid = amount > 0 && amount <= MAX_AMOUNT && !tooMuch

  const submit = (e: FormEvent) => { e.preventDefault(); if (valid && !busy) setConfirming(true) }

  const apply = async () => {
    setConfirming(false)
    setBusy(true)
    setError(null)
    setDone(null)
    try {
      const r = await adjustBalance(user.id, signed, note.trim())
      setDone(t('balance.done', { balance: num(r.balance) }))
      setAmount(0)
      setNote('')
      onDone()
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={submit} className="card p-4 md:p-5 space-y-3">
      <h3 className="font-bold flex items-center gap-2">
        <Icon name="wallet" className="w-5 h-5 text-brand-700 dark:text-brand-400" /> {t('balance.title')}
      </h3>
      <div className="grid grid-cols-2 gap-1 p-1 rounded-xl bg-muted border border-line/60" role="radiogroup">
        {(['add', 'subtract'] as const).map(m => (
          <button key={m} type="button" role="radio" aria-checked={m === mode} onClick={() => { setMode(m); setDone(null) }}
                  className={`py-2 rounded-lg text-sm font-semibold transition ${m === mode
                    ? `bg-surface shadow-sm ${m === 'add' ? 'text-ok' : 'text-bad'}`
                    : 'text-ink-faint hover:text-ink-soft'}`}>
            {m === 'add' ? '+ ' : '− '}{t(`balance.${m}`)}
          </button>
        ))}
      </div>
      <label className="block">
        <span className="text-sm font-semibold">{t('balance.amount')}</span>
        <input inputMode="numeric" value={amount ? num(amount) : ''} placeholder="0"
               onChange={e => { setAmount(Math.min(MAX_AMOUNT, Number(e.target.value.replace(/\D/g, '')) || 0)); setDone(null) }}
               className="field mt-1 text-lg font-bold tabular-nums" />
      </label>
      <div className="flex flex-wrap gap-2">
        {QUICK_AMOUNTS.map(a => (
          <button key={a} type="button" onClick={() => { setAmount(a); setDone(null) }}
                  className={`px-3 py-1.5 rounded-full text-sm font-semibold border transition
                              ${a === amount ? 'bg-brand-500/15 border-brand-500/40 text-brand-700 dark:text-brand-400'
                                             : 'border-line text-ink-soft hover:bg-muted'}`}>
            {num(a)}
          </button>
        ))}
      </div>
      <label className="block">
        <span className="text-sm font-semibold">{t('balance.note')}</span>
        <input value={note} onChange={e => setNote(e.target.value)} maxLength={150}
               placeholder={t('balance.notePlaceholder')} className="field mt-1" />
      </label>
      {tooMuch && <p className="text-sm text-bad">{t('balance.tooMuch', { balance: num(user.balance) })}</p>}
      {error && <p className="text-sm text-bad">{error}</p>}
      {done && <p className="text-sm text-ok flex items-center gap-1.5"><Icon name="ok" className="w-4 h-4" /> {done}</p>}
      <button type="submit" disabled={!valid || busy} className="btn btn-primary w-full">
        {busy ? t('loading') : t(mode === 'add' ? 'balance.submitAdd' : 'balance.submitSubtract', { amount: num(amount) })}
      </button>

      {confirming && (
        <ConfirmDialog title={t(mode === 'add' ? 'balance.confirmAdd' : 'balance.confirmSubtract',
                                { amount: num(amount), username: user.username })}
                       message={t('balance.confirmBody', { after: num(user.balance + signed) })}
                       confirmLabel={t('confirm')} onConfirm={apply} onClose={() => setConfirming(false)} />
      )}
    </form>
  )
}

function AccountActions({ user, me, onDone }: { user: AdminUserDetail; me: string; onDone: () => void }) {
  const { t } = useTranslation('admin')
  const { t: tc } = useTranslation()
  const [password, setPassword] = useState('')
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null)
  const [confirmRole, setConfirmRole] = useState(false)
  const self = user.username === me

  const run = async (fn: () => Promise<void>, ok: string) => {
    setBusy(true)
    setMessage(null)
    try {
      await fn()
      setMessage({ ok: true, text: ok })
      onDone()
    } catch (err) {
      setMessage({ ok: false, text: (err as Error).message })
    } finally {
      setBusy(false)
    }
  }

  const reset = (e: FormEvent) => {
    e.preventDefault()
    if (!passwordValid(password) || busy) return
    run(async () => { await resetUserPassword(user.id, password); setPassword('') }, t('account.resetDone'))
  }

  return (
    <div className="card p-4 md:p-5 space-y-4">
      <h3 className="font-bold flex items-center gap-2">
        <Icon name="user" className="w-5 h-5 text-brand-700 dark:text-brand-400" /> {t('account.title')}
      </h3>

      <form onSubmit={reset} className="space-y-2">
        <label className="block">
          <span className="text-sm font-semibold">{t('account.tempPassword')}</span>
          <input value={password} onChange={e => setPassword(e.target.value)} maxLength={64}
                 autoComplete="off" spellCheck={false} className="field mt-1 font-mono" />
        </label>
        {password && (
          <ul className="grid grid-cols-2 gap-1 text-xs">
            {PASSWORD_RULES.map(r => (
              <li key={r.key} className={`flex items-center gap-1.5 ${r.test(password) ? 'text-ok' : 'text-ink-faint'}`}>
                <Icon name={r.test(password) ? 'ok' : 'fail'} className="w-3.5 h-3.5 shrink-0" /> {tc(`auth.rules.${r.key}`)}
              </li>
            ))}
          </ul>
        )}
        <p className="text-xs text-ink-faint">{t('account.resetHint')}</p>
        <button type="submit" disabled={!passwordValid(password) || busy} className="btn btn-soft w-full">
          <Icon name="key" className="w-4 h-4" /> {t('account.resetPassword')}
        </button>
      </form>

      <div className="pt-3 border-t border-line/70">
        <button onClick={() => setConfirmRole(true)} disabled={self || busy} title={self ? t('account.selfRole') : undefined}
                className="btn btn-soft w-full">
          <Icon name="admin" className="w-4 h-4" /> {user.isAdmin ? t('account.revokeAdmin') : t('account.grantAdmin')}
        </button>
        {self && <p className="mt-1 text-xs text-ink-faint text-center">{t('account.selfRole')}</p>}
      </div>

      {message && <p className={`text-sm ${message.ok ? 'text-ok' : 'text-bad'}`}>{message.text}</p>}

      {confirmRole && (
        <ConfirmDialog title={t(user.isAdmin ? 'account.revokeConfirm' : 'account.grantConfirm', { username: user.username })}
                       message={t('account.roleBody')} confirmLabel={t('confirm')}
                       onClose={() => setConfirmRole(false)}
                       onConfirm={() => {
                         setConfirmRole(false)
                         run(() => setUserAdmin(user.id, !user.isAdmin),
                             user.isAdmin ? t('account.revokeAdmin') : t('account.grantAdmin'))
                       }} />
      )}
    </div>
  )
}

const TX_TONE: Record<WalletTransaction['kind'], string> = {
  TopUp: 'text-ok', Win: 'text-ok', Refund: 'text-info', Purchase: 'text-ink-soft', Adjust: 'text-bad',
}

function Transactions({ userId }: { userId: number }) {
  const { t } = useTranslation('admin')
  const [items, setItems] = useState<WalletTransaction[]>([])
  const [page, setPage] = useState(1)
  const [hasMore, setHasMore] = useState(false)
  const [donePage, setDonePage] = useState(0)
  const loading = donePage !== page

  useEffect(() => {
    let alive = true
    getAdminTransactions(userId, page)
      .then(d => {
        if (!alive) return
        setItems(prev => page === 1 ? d.items : [...prev, ...d.items.filter(x => !prev.some(p => p.id === x.id))])
        setHasMore(d.hasMore)
      })
      .catch(() => {})
      .finally(() => alive && setDonePage(page))
    return () => { alive = false }
  }, [userId, page])

  return (
    <div className="card overflow-hidden">
      <h3 className="px-4 md:px-5 pt-4 pb-2 font-bold flex items-center gap-2">
        <Icon name="history" className="w-5 h-5 text-brand-700 dark:text-brand-400" /> {t('tx.title')}
      </h3>
      {!loading && items.length === 0 && <p className="px-4 md:px-5 pb-4 text-sm text-ink-soft">{t('tx.empty')}</p>}
      <ul className="divide-y divide-line/70">
        {items.map(x => (
          <li key={x.id} className="px-4 md:px-5 py-3 flex items-start justify-between gap-3">
            <div className="min-w-0">
              <div className="text-sm font-semibold">{t(`tx.kind.${x.kind}`)}</div>
              {x.note && <div className="text-xs text-ink-soft break-words">{x.note}</div>}
              <div className="text-[11px] text-ink-faint tabular-nums">{formatDateTime(x.createdAt)}</div>
            </div>
            <div className="shrink-0 text-right">
              <div className={`font-bold tabular-nums ${TX_TONE[x.kind]}`}>
                {x.amount > 0 ? '+' : '−'}{num(Math.abs(x.amount))}
              </div>
              <div className="text-[11px] text-ink-faint tabular-nums">{t('tx.after', { amount: num(x.balanceAfter) })}</div>
            </div>
          </li>
        ))}
      </ul>
      {hasMore && (
        <div className="p-3">
          <button onClick={() => setPage(p => p + 1)} disabled={loading} className="btn btn-soft w-full">
            {loading ? t('loading') : t('loadMore')}
          </button>
        </div>
      )}
    </div>
  )
}

function Avatar({ name, big }: { name: string; big?: boolean }) {
  return (
    <span className={`shrink-0 rounded-full flex items-center justify-center font-extrabold uppercase
                      bg-gradient-to-br from-primary to-primary-end text-on-primary
                      ${big ? 'w-12 h-12 text-xl' : 'w-10 h-10'}`}>
      {name[0]}
    </span>
  )
}

const BADGE = {
  brand: 'bg-brand-500/15 text-brand-700 dark:text-brand-400',
  warn: 'bg-warn/15 text-warn',
  muted: 'bg-muted text-ink-faint',
}

function Badge({ tone, children }: { tone: keyof typeof BADGE; children: React.ReactNode }) {
  return <span className={`shrink-0 px-1.5 py-0.5 rounded-md text-[10px] font-bold uppercase tracking-wide ${BADGE[tone]}`}>{children}</span>
}
