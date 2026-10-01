import { useEffect, useRef, useState } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import type { HubConnection } from '@microsoft/signalr'
import Icon, { type IconName } from './Icon'
import { getNotifications, markNotificationsRead, NOTIFICATION_HUB_URL, type AppNotification } from '../api/client'
import i18n from '../i18n'
import { provinceName } from '../data/provinces'
import { formatDate, timeAgo } from '../utils/date'

/** Kết nối lần đầu hỏng (mạng / máy chủ đang khởi động lại) thì thử lại sau chừng này. */
const RETRY_MS = 15_000

const KIND_ICON: Record<AppNotification['kind'], IconName> = {
  PostComment: 'comment',
  CommentReply: 'reply',
  ShopReview: 'star',
  ShopWin: 'trophy',
}

/** Dòng trích dưới thông báo. Vé trúng: máy chủ gửi "đài|giải|yyyy-MM-dd" để FE dịch. */
function snippet(n: AppNotification) {
  if (n.kind === 'ShopWin') {
    const [province, tier, date] = n.snippet.split('|')
    return i18n.t('map:wins.line', {
      tier: i18n.t(`map:wins.tiers.${tier}`, { defaultValue: tier }),
      province: province === 'Vietlott' ? 'Vietlott' : provinceName(province),
      date: date ? formatDate(date) : '',
    })
  }
  return n.snippet ? `“${n.snippet}”` : ''
}

/**
 * Chuông thông báo trên header (chỉ hiện khi đã đăng nhập): số chưa đọc + danh sách. Thông báo mới đẩy
 * realtime qua SignalR; mất kết nối rồi nối lại thì tải lại danh sách để bù những cái lỡ mất.
 */
export default function NotificationBell({ onOpen }: { onOpen: (n: AppNotification) => void }) {
  const { t } = useTranslation()
  const [items, setItems] = useState<AppNotification[]>([])
  const [unread, setUnread] = useState(0)
  const [error, setError] = useState(false)
  const [open, setOpen] = useState(false)
  // Tăng mỗi lần có thông báo mới → đổi key để chuông lắc lại.
  const [ring, setRing] = useState(0)
  const ref = useRef<HTMLDivElement>(null)

  const refresh = () =>
    getNotifications()
      .then(d => { setItems(d.items); setUnread(d.unread); setError(false) })
      .catch(() => setError(true))

  useEffect(() => {
    let alive = true
    let conn: HubConnection | null = null
    let retry: ReturnType<typeof setTimeout> | undefined
    refresh()

    // Tải thư viện SignalR riêng (dynamic import) — khách chưa đăng nhập không phải tải phần này.
    import('@microsoft/signalr').then(signalR => {
      if (!alive) return
      const c = new signalR.HubConnectionBuilder()
        .withUrl(NOTIFICATION_HUB_URL)
        .withAutomaticReconnect()
        .configureLogging(signalR.LogLevel.Warning)
        .build()
      conn = c
      c.on('notification', (n: AppNotification) => {
        setItems(list => list.some(x => x.id === n.id) ? list : [n, ...list])
        setUnread(u => u + 1)
        setRing(r => r + 1)
      })
      c.onreconnected(() => { refresh() })
      const start = () => c.start().catch(() => { if (alive) retry = setTimeout(start, RETRY_MS) })
      start()
    })

    return () => {
      alive = false
      clearTimeout(retry)
      conn?.stop()
    }
  }, [])

  // Chạm ra ngoài / Esc là đóng
  useEffect(() => {
    if (!open) return
    const onDown = (e: PointerEvent) => { if (!ref.current?.contains(e.target as Node)) setOpen(false) }
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setOpen(false) }
    document.addEventListener('pointerdown', onDown)
    window.addEventListener('keydown', onKey)
    return () => { document.removeEventListener('pointerdown', onDown); window.removeEventListener('keydown', onKey) }
  }, [open])

  const toggle = () => {
    if (!open) refresh()
    setOpen(o => !o)
  }

  const readAll = async () => {
    setItems(list => list.map(x => ({ ...x, isRead: true })))
    setUnread(0)
    setUnread(await markNotificationsRead().catch(() => 0))
  }

  const pick = (n: AppNotification) => {
    setOpen(false)
    if (!n.isRead) {
      setItems(list => list.map(x => x.id === n.id ? { ...x, isRead: true } : x))
      setUnread(u => Math.max(0, u - 1))
      markNotificationsRead([n.id]).then(setUnread).catch(() => {})
    }
    onOpen(n)
  }

  return (
    <div ref={ref} className="relative">
      <button onClick={toggle} aria-haspopup="menu" aria-expanded={open}
              aria-label={t('notifications.open', { count: unread })} title={t('notifications.title')}
              className={`relative w-10 h-10 rounded-xl flex items-center justify-center transition active:scale-95
                          ${open ? 'bg-muted text-brand-700 dark:text-brand-400' : 'text-ink-soft hover:bg-muted'}`}>
        <span key={ring} className={ring ? 'wiggle' : undefined}>
          <Icon name="bell" className="w-[22px] h-[22px]" />
        </span>
        {unread > 0 && (
          <span aria-hidden className="absolute top-1 right-1 min-w-[18px] h-[18px] px-1 rounded-full bg-bad text-white
                                       text-[11px] font-bold leading-[18px] text-center tabular-nums ring-2 ring-canvas">
            {unread > 99 ? '99+' : unread}
          </span>
        )}
      </button>

      {open && (
        <div role="menu"
             className="absolute right-0 top-full mt-3 w-80 max-w-[calc(100vw-2rem)] rounded-2xl bg-surface border border-line
                        shadow-2xl z-40 overflow-hidden">
          <div className="flex items-center justify-between gap-2 px-4 py-3 border-b border-line/60">
            <span className="font-bold">{t('notifications.title')}</span>
            {unread > 0 && (
              <button onClick={readAll}
                      className="inline-flex items-center gap-1 text-xs font-semibold text-brand-700 dark:text-brand-400 hover:underline">
                <Icon name="readAll" className="w-4 h-4" /> {t('notifications.readAll')}
              </button>
            )}
          </div>
          <div className="max-h-[min(70vh,28rem)] overflow-y-auto p-1.5">
            {error && items.length === 0 && <p className="px-3 py-6 text-center text-sm text-bad">{t('notifications.loadError')}</p>}
            {!error && items.length === 0 && (
              <p className="px-3 py-6 text-center text-sm text-ink-faint">{t('notifications.empty')}</p>
            )}
            {items.map(n => (
              <button key={n.id} role="menuitem" onClick={() => pick(n)}
                      className={`w-full flex gap-3 px-3 py-2.5 rounded-xl text-left transition hover:bg-muted
                                  ${n.isRead ? '' : 'bg-brand-500/5'}`}>
                <span className={`shrink-0 mt-0.5 w-8 h-8 rounded-full flex items-center justify-center
                                  ${n.isRead ? 'bg-muted text-ink-faint' : 'bg-brand-500/10 text-brand-700 dark:text-brand-400'}`}>
                  <Icon name={KIND_ICON[n.kind]} className="w-4 h-4" />
                </span>
                <span className="min-w-0 flex-1">
                  <span className={`block text-sm leading-snug break-words ${n.isRead ? 'text-ink-soft' : 'text-ink'}`}>
                    <Trans t={t} i18nKey={`notifications.${n.kind}`}
                           values={{ name: n.actorName ?? t('notifications.someone'), title: n.postTitle, shop: n.shopName, stars: n.stars }}
                           components={{ b: <b className="font-semibold" /> }} />
                  </span>
                  {snippet(n) && <span className="mt-0.5 block text-xs text-ink-faint line-clamp-2 break-words">{snippet(n)}</span>}
                  <time dateTime={n.createdAt} className="mt-0.5 block text-xs text-ink-faint">{timeAgo(n.createdAt)}</time>
                </span>
                {!n.isRead && <span aria-hidden className="shrink-0 mt-2 w-2 h-2 rounded-full bg-brand-500" />}
              </button>
            ))}
          </div>
        </div>
      )}
    </div>
  )
}
