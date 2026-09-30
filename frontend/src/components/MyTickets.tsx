import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  buyTickets, getMyTickets, getTicketShop, scratchTicket, type ScratchTicket, type TicketShop,
} from '../api/client'
import { provinceName } from '../data/provinces'
import { currentLocale } from '../i18n'
import { formatDate, formatDay } from '../utils/date'
import Icon, { IconBadge } from './Icon'
import ScratchCard from './ScratchCard'

const num = (n: number) => n.toLocaleString(currentLocale())
/** Giờ VN máy chủ gửi dạng 'YYYY-MM-DDTHH:mm:ss' (không múi giờ) → 'HH:mm'. */
const hhmm = (vn: string) => vn.slice(11, 16)
/** Đã tới mốc giờ VN này chưa — so theo giờ thật, không phụ thuộc múi giờ của máy. */
const reached = (vn: string) => new Date(`${vn.slice(0, 19)}+07:00`).getTime() <= Date.now()

/** Vé của tôi: quầy mua vé cào 2 số + các vé đã mua (cào xem kết quả sau giờ xổ). */
export default function MyTickets({ onBalance }: { onBalance: (balance: number) => void }) {
  const { t } = useTranslation('profile')
  const [shop, setShop] = useState<TicketShop | null>(null)
  const [tickets, setTickets] = useState<ScratchTicket[]>([])
  const [page, setPage] = useState(1)
  const [hasMore, setHasMore] = useState(false)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [reload, setReload] = useState(0)

  // Lấy vé trước (máy chủ chốt luôn vé đã có kết quả, cộng tiền trúng) rồi mới lấy số dư cho đúng.
  useEffect(() => {
    let alive = true
    getMyTickets(page)
      .then(async d => {
        if (!alive) return
        setTickets(prev => page === 1 ? d.items : [...prev, ...d.items.filter(x => !prev.some(p => p.id === x.id))])
        setHasMore(d.hasMore)
        if (page === 1) {
          const s = await getTicketShop()
          if (!alive) return
          setShop(s)
          onBalance(s.balance)
        }
      })
      .catch(e => alive && setError(e?.message ?? ''))
      .finally(() => alive && setLoading(false))
    return () => { alive = false }
  }, [page, reload, onBalance])

  const load = (p: number) => {
    setLoading(true)
    setError(null)
    setPage(p)
    setReload(n => n + 1)
  }

  const refreshShop = () => getTicketShop().then(s => { setShop(s); onBalance(s.balance) }).catch(() => {})

  if (error) return (
    <div className="card p-6 text-center space-y-3">
      <IconBadge name="error" tone="bad" />
      <div className="text-sm text-bad">{error}</div>
      <button onClick={() => load(page)} className="btn btn-soft">
        <Icon name="retry" className="w-4 h-4" /> {t('retry')}
      </button>
    </div>
  )

  return (
    <div className="space-y-4">
      {shop ? (
        <Shop shop={shop} onBought={(bought, balance) => {
          setTickets(ts => [...bought, ...ts])
          setShop({ ...shop, balance })
          onBalance(balance)
        }} onFailed={refreshShop} />
      ) : (
        <div className="card p-4 space-y-3 motion-safe:animate-pulse">
          <div className="h-5 w-40 rounded-full bg-muted" />
          <div className="h-10 w-full rounded-xl bg-muted" />
          <div className="h-11 w-full rounded-xl bg-muted" />
        </div>
      )}

      {!loading && tickets.length === 0 && (
        <div className="card p-6 text-center space-y-2">
          <IconBadge name="tickets" tone="info" />
          <p className="text-sm text-ink-soft pt-1">{t('ticket.empty')}</p>
        </div>
      )}

      <ul className="grid md:grid-cols-2 gap-3">
        {tickets.map(x => <TicketCard key={x.id} ticket={x} />)}
      </ul>

      {hasMore && (
        <button onClick={() => load(page + 1)} disabled={loading} className="btn btn-soft w-full">
          {loading ? t('loading') : t('loadMore')}
        </button>
      )}
    </div>
  )
}

function Shop({ shop, onBought, onFailed }: {
  shop: TicketShop
  onBought: (tickets: ScratchTicket[], balance: number) => void
  onFailed: () => void
}) {
  const { t } = useTranslation('profile')
  const [province, setProvince] = useState(shop.provinces[0] ?? '')
  const [quantity, setQuantity] = useState(1)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState<string | null>(null)
  // Qua giờ ngừng bán, máy chủ chuyển sang ngày mai với danh sách đài khác → chọn lại đài đầu tiên.
  const selected = shop.provinces.includes(province) ? province : shop.provinces[0] ?? ''
  const total = quantity * shop.price
  const short = total > shop.balance

  const buy = async () => {
    if (!selected || busy || short) return
    setBusy(true)
    setError(null)
    setDone(null)
    try {
      const r = await buyTickets({ province: selected, drawDate: shop.drawDate, quantity })
      onBought(r.tickets, r.balance)
      setDone(t('shop.bought', { count: r.tickets.length, time: hhmm(shop.drawsAt) }))
    } catch (e) {
      setError((e as Error).message)
      onFailed()   // hết giờ bán / số dư đổi → lấy lại ngày bán + số dư mới
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="card p-4 md:p-5 space-y-4">
      <div>
        <h2 className="font-extrabold text-lg flex items-center gap-2">
          <Icon name="ticket" className="w-5 h-5 text-brand-700 dark:text-brand-400" /> {t('shop.title')}
        </h2>
        <p className="text-sm text-ink-soft mt-1">
          {t('shop.rule', { prize: num(shop.prize), multiplier: shop.payoutMultiplier })}
        </p>
        <div className="flex flex-wrap gap-x-3 gap-y-1 mt-2 text-xs font-semibold">
          <span className="inline-flex items-center gap-1 text-brand-700 dark:text-brand-400">
            <Icon name="calendar" className="w-3.5 h-3.5" /> {t('shop.draw', { day: formatDay(shop.drawDate) })}
          </span>
          <span className="inline-flex items-center gap-1 text-ink-faint">
            <Icon name="clock" className="w-3.5 h-3.5" /> {t('shop.closes', { time: hhmm(shop.closesAt) })}
          </span>
        </div>
      </div>

      <fieldset>
        <legend className="text-sm font-semibold mb-1.5">{t('shop.province')}</legend>
        <div role="radiogroup" className="flex flex-wrap gap-2">
          {shop.provinces.map(p => (
            <button key={p} type="button" role="radio" aria-checked={p === selected} onClick={() => { setProvince(p); setDone(null) }}
                    className={`px-4 py-2 rounded-full text-sm font-semibold border transition active:scale-95
                                ${p === selected
                                  ? 'bg-gradient-to-r from-primary to-primary-end text-on-primary border-transparent shadow-md shadow-primary/25'
                                  : 'bg-surface border-line text-ink-soft hover:bg-muted'}`}>
              {provinceName(p)}
            </button>
          ))}
        </div>
      </fieldset>

      <div className="flex items-center justify-between gap-3">
        <span className="text-sm font-semibold">{t('shop.quantity')}</span>
        <div className="flex items-center gap-1 p-1 rounded-xl bg-muted border border-line/60">
          <button type="button" onClick={() => { setQuantity(q => Math.max(1, q - 1)); setDone(null) }} disabled={quantity <= 1}
                  aria-label={t('shop.less')}
                  className="w-9 h-9 rounded-lg text-lg font-bold text-ink-soft hover:bg-surface disabled:opacity-40 transition">−</button>
          <span className="w-10 text-center font-extrabold tabular-nums" aria-live="polite">{quantity}</span>
          <button type="button" onClick={() => { setQuantity(q => Math.min(shop.maxQuantity, q + 1)); setDone(null) }}
                  disabled={quantity >= shop.maxQuantity} aria-label={t('shop.more')}
                  className="w-9 h-9 rounded-lg text-lg font-bold text-ink-soft hover:bg-surface disabled:opacity-40 transition">+</button>
        </div>
      </div>

      <div className="space-y-2">
        <button onClick={buy} disabled={!selected || busy || short} className="btn btn-primary w-full">
          {busy ? t('shop.buying') : t('shop.buy', { count: quantity, total: num(total) })}
        </button>
        {short && <p className="text-sm text-warn text-center">{t('shop.insufficient')}</p>}
        {error && <p className="text-sm text-bad text-center">{error}</p>}
        {done && (
          <p className="text-sm text-ok text-center flex items-center justify-center gap-1.5">
            <Icon name="ok" className="w-4 h-4" /> {done}
          </p>
        )}
      </div>
    </div>
  )
}

function TicketCard({ ticket }: { ticket: ScratchTicket }) {
  const { t } = useTranslation('profile')
  const { status } = ticket
  const settled = status === 'Won' || status === 'Lost'

  const result = settled && (
    // border chứ không ring: ring (box-shadow) vẽ ra ngoài khung, lớp cào không che → lộ màu trúng/trượt.
    <div className={`min-h-28 rounded-2xl p-4 flex flex-col items-center justify-center text-center border
                     ${status === 'Won' ? 'bg-ok/10 border-ok/25' : 'bg-muted border-line'}`}>
      <div className="text-xs text-ink-faint">
        {t('ticket.eighth')}: <b className="font-mono text-base text-ink tabular-nums">{ticket.winningNumber}</b>
      </div>
      {status === 'Won' ? (
        <>
          <div className="mt-1 flex items-center gap-1.5 text-xl font-extrabold text-ok">
            <Icon name="trophy" className="w-5 h-5" /> {t('ticket.won', { amount: num(ticket.prize) })}
          </div>
          <div className="text-xs text-ink-faint mt-0.5">{t('ticket.credited')}</div>
        </>
      ) : (
        <div className="mt-1 text-lg font-bold text-ink-soft">{t('ticket.lost')}</div>
      )}
    </div>
  )

  return (
    <li className="card p-4 space-y-3">
      <div className="flex items-center gap-3">
        <span className="shrink-0 w-14 h-14 rounded-full flex items-center justify-center font-mono text-2xl font-extrabold
                         bg-gradient-to-br from-primary to-primary-end text-on-primary shadow-md shadow-primary/25"
              aria-label={`${t('ticket.number')} ${ticket.number}`}>
          {ticket.number}
        </span>
        <div className="min-w-0">
          <div className="font-semibold truncate">{provinceName(ticket.province)}</div>
          <div className="text-xs text-ink-faint">{formatDate(ticket.drawDate)} · {num(ticket.price)} ₫</div>
        </div>
      </div>

      {status === 'Pending' ? (
        <div className="rounded-xl bg-info/10 text-info ring-1 ring-info/25 px-3 py-2.5 text-sm font-medium flex items-center gap-2">
          <Icon name="clock" className="w-4 h-4 shrink-0" />
          {reached(ticket.drawsAt)
            ? t('ticket.waitingResult')
            : t('ticket.waiting', { time: hhmm(ticket.drawsAt), date: formatDate(ticket.drawDate) })}
        </div>
      ) : status === 'Refunded' ? (
        <div className="rounded-xl bg-warn/10 text-warn ring-1 ring-warn/25 px-3 py-2.5 text-sm font-medium flex items-center gap-2">
          <Icon name="retry" className="w-4 h-4 shrink-0" /> {t('ticket.refunded', { amount: num(ticket.price) })}
        </div>
      ) : ticket.scratched ? result : (
        // Tiền đã cộng lúc chốt; cào chỉ để xem — báo máy chủ để lần sau khỏi cào lại.
        <ScratchCard onReveal={() => { scratchTicket(ticket.id).catch(() => {}) }}>{result}</ScratchCard>
      )}
    </li>
  )
}
