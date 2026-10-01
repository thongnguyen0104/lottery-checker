import { useCallback, useEffect, useRef, useState } from 'react'
import Icon, { type IconName } from './Icon'
import ConfirmDialog from './ConfirmDialog'
import NotificationBell from './NotificationBell'
import { useTranslation } from 'react-i18next'
import { currentLang } from '../i18n'
import { VIEWS, type View } from '../views'
import type { Account, AppNotification } from '../api/client'

// Dò vé không nằm trong dãy tab — có nút primary riêng ở góc phải.
const TABS = VIEWS.filter(v => v.id !== 'check')

type Props = {
  view: View
  /** Dò vé đang quét/dò — đánh dấu tab đó khi user ở tab khác. */
  busy?: boolean
  onChange: (v: View) => void
  onOpenTheme: () => void
  onOpenDonate: () => void
  /** null = khách → mục mở form đăng nhập; có tài khoản → mục đăng xuất. */
  account: Account | null
  onOpenAuth: () => void
  onLogout: () => void
  /** Chỉ dùng khi đã đăng nhập — mở trang Tài khoản. */
  /** Có tính năng nào của trang Tài khoản đang mở cho người này không — không thì ẩn mục menu. */
  showProfile: boolean
  onOpenProfile: () => void
  /** Chỉ hiện khi account.isAdmin. */
  onOpenAdmin: () => void
  onChangePassword: () => void
  /** Bản đồ điểm bán (cờ shopMap) — không nằm trên dãy tab, mở từ menu logo. */
  showMap: boolean
  onOpenMap: () => void
  /** Bấm 1 thông báo trong chuông (chỉ hiện khi đã đăng nhập) — mở bài đó. */
  onOpenNotification: (n: AppNotification) => void
}

/** Thanh trên cùng: logo + tên tính năng đang mở; màn rộng có thêm tab chuyển tính năng
 *  (điện thoại dùng BottomNav cho vừa tầm ngón cái). Bấm logo sổ menu: ủng hộ, tài khoản, đăng nhập/xuất,
 *  ngôn ngữ, bảng màu. */
export default function AppHeader({ view, busy, onChange, onOpenTheme, onOpenDonate, account, showProfile, onOpenAuth, onLogout, onOpenProfile,
                                    onOpenAdmin, onChangePassword, showMap, onOpenMap, onOpenNotification }: Props) {
  const { t, i18n } = useTranslation()
  const lang = currentLang()
  const [confirmLogout, setConfirmLogout] = useState(false)
  const closeConfirm = useCallback(() => setConfirmLogout(false), [])
  const [menuOpen, setMenuOpen] = useState(false)
  const menuRef = useRef<HTMLDivElement>(null)
  const pick = (fn: () => void) => () => { setMenuOpen(false); fn() }

  // Chạm ra ngoài / Esc là đóng menu
  useEffect(() => {
    if (!menuOpen) return
    const onDown = (e: PointerEvent) => { if (!menuRef.current?.contains(e.target as Node)) setMenuOpen(false) }
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setMenuOpen(false) }
    document.addEventListener('pointerdown', onDown)
    window.addEventListener('keydown', onKey)
    return () => { document.removeEventListener('pointerdown', onDown); window.removeEventListener('keydown', onKey) }
  }, [menuOpen])

  return (
    <header className="sticky top-0 z-30 pt-[env(safe-area-inset-top)]
                       bg-canvas/75 backdrop-blur-xl border-b border-line/60">
      {/* Cùng bề rộng với <main> (App.tsx) để logo thẳng mép với nội dung bên dưới */}
      <div className="max-w-md md:max-w-3xl lg:max-w-5xl mx-auto h-16 px-4 flex items-center gap-3">
        <div ref={menuRef} className="relative min-w-0">
          <button onClick={() => setMenuOpen(o => !o)} aria-haspopup="menu" aria-expanded={menuOpen}
                  aria-label={t('app.menu')} className="flex items-center gap-2.5 min-w-0 text-left">
            {/* Logo vẽ bằng icon + màu primary để đổi theo bảng màu (favicon.svg cố định đỏ–cam) */}
            <span className="shrink-0 w-10 h-10 rounded-xl flex items-center justify-center
                             bg-gradient-to-br from-primary to-primary-end text-on-primary shadow-md shadow-primary/25">
              <Icon name="ticket" className="w-[22px] h-[22px]" />
            </span>
            <span className="min-w-0">
              <span className="block truncate text-lg font-extrabold leading-tight bg-gradient-to-r from-brand-700 to-accent
                               bg-clip-text text-transparent dark:from-brand-300 dark:to-brand-500">
                {t('app.name')}
              </span>
              {/* Điện thoại không có tab ở trên → ghi tính năng đang mở ngay dưới tên app */}
              <span className="block text-xs text-ink-faint truncate md:hidden">{t(`views.${view}.hint`)}</span>
              <span className="hidden md:block text-xs text-ink-faint">{t('app.tagline')}</span>
            </span>
            <Icon name="down" className={`shrink-0 w-4 h-4 text-ink-faint transition-transform ${menuOpen ? 'rotate-180' : ''}`} />
          </button>

          {menuOpen && (
            <div role="menu"
                 className="absolute left-0 top-full mt-3 w-64 p-1.5 rounded-2xl bg-surface border border-line shadow-2xl z-40">
              <MenuItem icon="donate" label={t('app.donate')} onClick={pick(onOpenDonate)} iconClass="text-accent" />
              {showMap && (
                <MenuItem icon="map" label={t('views.map.title')} onClick={pick(onOpenMap)}
                          iconClass="text-brand-700 dark:text-brand-400" />
              )}
              {account && showProfile && (
                <MenuItem icon="wallet" label={t('auth.profile')} onClick={pick(onOpenProfile)}
                          iconClass="text-brand-700 dark:text-brand-400" />
              )}
              {account?.isAdmin && (
                <MenuItem icon="admin" label={t('auth.admin')} onClick={pick(onOpenAdmin)}
                          iconClass="text-brand-700 dark:text-brand-400" />
              )}
              {account && <MenuItem icon="key" label={t('auth.changePassword')} onClick={pick(onChangePassword)} />}
              <MenuItem icon={account ? 'logout' : 'user'}
                        label={account ? `${t('auth.logout')} (${account.username})` : t('auth.loginTitle')}
                        onClick={pick(account ? () => setConfirmLogout(true) : onOpenAuth)}
                        iconClass={account ? 'text-brand-700 dark:text-brand-400' : undefined} />
              {/* Đổi ngôn ngữ VI ⇄ EN: không đóng menu để thấy nhãn đổi ngay (i18n tự nhớ lựa chọn) */}
              <button role="menuitem" onClick={() => i18n.changeLanguage(lang === 'vi' ? 'en' : 'vi')}
                      title={t('app.switchLang')}
                      className="w-full flex items-center gap-3 px-3 py-2.5 rounded-xl text-sm font-semibold text-ink hover:bg-muted transition">
                <Icon name="globe" className="w-5 h-5 shrink-0 text-ink-faint" />
                <span className="flex-1 text-left">{t('app.language')}</span>
                <span className="flex gap-0.5 p-0.5 rounded-lg bg-muted text-xs font-bold">
                  {(['vi', 'en'] as const).map(l => (
                    <span key={l} className={`px-2 py-0.5 rounded-md
                                              ${l === lang ? 'bg-surface shadow-sm text-brand-700 dark:text-brand-400' : 'text-ink-faint'}`}>
                      {l.toUpperCase()}
                    </span>
                  ))}
                </span>
              </button>
              <MenuItem icon="palette" label={t('app.openThemeTitle')} onClick={pick(onOpenTheme)} />
            </div>
          )}
        </div>

        <nav className="hidden md:flex ml-auto items-center gap-1 p-1 rounded-xl bg-muted/80 border border-line/60"
             aria-label={t('app.features')}>
          {TABS.map(v => {
            const active = v.id === view
            return (
              <button key={v.id} onClick={() => onChange(v.id)} aria-current={active ? 'page' : undefined}
                      className={`flex items-center gap-2 px-3 lg:px-4 py-2 rounded-lg text-sm font-semibold whitespace-nowrap transition active:scale-95
                                  ${active
                                    ? 'bg-surface dark:bg-brand-500/10 text-brand-700 shadow-sm dark:text-brand-400'
                                    : 'text-ink-faint hover:text-ink'}`}>
                <Icon name={v.icon} className={`w-[18px] h-[18px] ${active ? 'wiggle' : ''}`} />
                {/* md chật (logo + nhiều tab) → tên ngắn; lg mới đủ chỗ cho tên đầy đủ */}
                <span className="lg:hidden">{t(`views.${v.id}.short`)}</span>
                <span className="hidden lg:inline">{t(`views.${v.id}.title`)}</span>
              </button>
            )
          })}
        </nav>
        {/* key: đổi tài khoản thì nối lại hub + tải lại danh sách của người mới */}
        {account && (
          <div className="ml-auto md:ml-0 shrink-0">
            <NotificationBell key={account.username} onOpen={onOpenNotification} />
          </div>
        )}
        {/* Dò vé — tính năng chính: nút primary riêng ở góc phải (điện thoại là nút giữa BottomNav) */}
        <button onClick={() => onChange('check')} aria-current={view === 'check' ? 'page' : undefined}
                className={`hidden md:flex shrink-0 items-center gap-2 px-4 py-2.5 rounded-xl text-sm font-bold whitespace-nowrap
                            bg-gradient-to-r from-primary to-primary-end text-on-primary shadow-md shadow-primary/25
                            hover:shadow-lg hover:shadow-primary/30 active:scale-95 transition
                            ${view === 'check' ? 'ring-2 ring-offset-2 ring-offset-canvas ring-primary/50' : ''}`}>
          <span className="relative">
            <Icon name="ticket" className={`w-[18px] h-[18px] ${view === 'check' ? 'wiggle' : ''}`} />
            {busy && view !== 'check' && (
              <span aria-hidden className="absolute -top-1 -right-1 w-2 h-2 rounded-full bg-on-primary ring-2 ring-primary motion-safe:animate-pulse" />
            )}
          </span>
          {t('views.check.short')}
          {busy && view !== 'check' && <span className="sr-only">{t('app.busy')}</span>}
        </button>
      </div>
      {confirmLogout && account && (
        <ConfirmDialog title={t('auth.logoutConfirmTitle')}
                       message={t('auth.logoutConfirmMessage', { username: account.username })}
                       confirmLabel={t('auth.logout')} onClose={closeConfirm}
                       onConfirm={() => { closeConfirm(); onLogout() }} />
      )}
    </header>
  )
}

function MenuItem({ icon, label, onClick, iconClass = 'text-ink-faint' }:
  { icon: IconName; label: string; onClick: () => void; iconClass?: string }) {
  return (
    <button role="menuitem" onClick={onClick}
            className="w-full flex items-center gap-3 px-3 py-2.5 rounded-xl text-sm font-semibold text-ink hover:bg-muted transition">
      <Icon name={icon} className={`w-5 h-5 shrink-0 ${iconClass}`} />
      <span className="flex-1 text-left truncate">{label}</span>
    </button>
  )
}
