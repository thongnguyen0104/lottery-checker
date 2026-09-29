import { useCallback, useState } from 'react'
import Icon from './Icon'
import ConfirmDialog from './ConfirmDialog'
import { VIEWS, type View } from '../views'
import type { Account } from '../api/client'

type Props = {
  view: View
  /** Dò vé đang quét/dò — đánh dấu tab đó khi user ở tab khác. */
  busy?: boolean
  onChange: (v: View) => void
  onOpenTheme: () => void
  onOpenDonate: () => void
  /** null = khách → nút mở form đăng nhập; có tài khoản → nút đăng xuất. */
  account: Account | null
  onOpenAuth: () => void
  onLogout: () => void
}

/** Thanh trên cùng: logo + tên tính năng đang mở; màn rộng có thêm tab chuyển tính năng
 *  (điện thoại dùng BottomNav cho vừa tầm ngón cái). Nút bảng màu mở ThemePicker. */
export default function AppHeader({ view, busy, onChange, onOpenTheme, onOpenDonate, account, onOpenAuth, onLogout }: Props) {
  const current = VIEWS.find(v => v.id === view)!
  const [confirmLogout, setConfirmLogout] = useState(false)
  const closeConfirm = useCallback(() => setConfirmLogout(false), [])

  return (
    <header className="sticky top-0 z-30 pt-[env(safe-area-inset-top)]
                       bg-canvas/75 backdrop-blur-xl border-b border-line/60">
      {/* Cùng bề rộng với <main> (App.tsx) để logo thẳng mép với nội dung bên dưới */}
      <div className="max-w-md md:max-w-3xl lg:max-w-5xl mx-auto h-16 px-4 flex items-center gap-3">
        <button onClick={() => onChange('check')} className="flex items-center gap-2.5 min-w-0 text-left">
          {/* Logo vẽ bằng icon + màu primary để đổi theo bảng màu (favicon.svg cố định đỏ–cam) */}
          <span className="shrink-0 w-10 h-10 rounded-xl flex items-center justify-center
                           bg-gradient-to-br from-primary to-primary-end text-on-primary shadow-md shadow-primary/25">
            <Icon name="ticket" className="w-[22px] h-[22px]" />
          </span>
          <span className="min-w-0">
            <span className="block text-lg font-extrabold leading-tight bg-gradient-to-r from-brand-700 to-accent
                             bg-clip-text text-transparent dark:from-brand-300 dark:to-brand-500">
              Dò Vé Số
            </span>
            {/* Điện thoại không có tab ở trên → ghi tính năng đang mở ngay dưới tên app */}
            <span className="block text-xs text-ink-faint truncate md:hidden">{current.hint}</span>
            <span className="hidden md:block text-xs text-ink-faint">Chụp là biết trúng</span>
          </span>
        </button>

        <nav className="hidden md:flex mx-auto items-center gap-1 p-1 rounded-xl bg-muted/80 border border-line/60"
             aria-label="Tính năng">
          {VIEWS.map(v => {
            const active = v.id === view
            return (
              <button key={v.id} onClick={() => onChange(v.id)} aria-current={active ? 'page' : undefined}
                      className={`flex items-center gap-2 px-4 py-2 rounded-lg text-sm font-semibold transition active:scale-95
                                  ${active
                                    ? 'bg-surface dark:bg-brand-500/10 text-brand-700 shadow-sm dark:text-brand-400'
                                    : 'text-ink-faint hover:text-ink'}`}>
                <span className="relative">
                  <Icon name={v.icon} className={`w-[18px] h-[18px] ${active ? 'wiggle' : ''}`} />
                  {busy && !active && v.id === 'check' && (
                    <span aria-hidden className="absolute -top-1 -right-1 w-2 h-2 rounded-full bg-brand-500 motion-safe:animate-pulse" />
                  )}
                </span>
                {v.title}
                {busy && !active && v.id === 'check' && <span className="sr-only">(đang xử lý)</span>}
              </button>
            )
          })}
        </nav>

        <button onClick={onOpenDonate} title="Ủng hộ" aria-label="Ủng hộ Dò Vé Số"
                className="ml-auto md:ml-0 shrink-0 w-11 h-11 rounded-xl flex items-center justify-center
                           bg-surface border border-line text-accent hover:text-brand-700 dark:hover:text-brand-400
                           shadow-sm transition active:scale-95">
          <Icon name="donate" className="w-[22px] h-[22px]" />
        </button>

        <button onClick={account ? () => setConfirmLogout(true) : onOpenAuth}
                title={account ? `${account.username} — bấm để đăng xuất` : 'Đăng nhập / Đăng ký'}
                aria-label={account ? 'Đăng xuất' : 'Đăng nhập'}
                className={`shrink-0 w-11 h-11 rounded-xl flex items-center justify-center bg-surface border border-line
                           shadow-sm transition active:scale-95 hover:text-brand-700 dark:hover:text-brand-400
                           ${account ? 'text-brand-700 dark:text-brand-400' : 'text-ink-faint'}`}>
          <Icon name={account ? 'logout' : 'user'} className="w-[22px] h-[22px]" />
        </button>

        <button onClick={onOpenTheme} title="Đổi màu & hình nền" aria-label="Đổi màu và hình nền"
                className="shrink-0 w-11 h-11 rounded-xl flex items-center justify-center
                           bg-surface border border-line text-ink-faint hover:text-brand-700 dark:hover:text-brand-400
                           shadow-sm transition active:scale-95">
          <Icon name="palette" className="w-[22px] h-[22px]" />
        </button>
      </div>
      {confirmLogout && account && (
        <ConfirmDialog title="Đăng xuất?" message={`Bạn đang đăng nhập với tài khoản ${account.username}.`}
                       confirmLabel="Đăng xuất" onClose={closeConfirm}
                       onConfirm={() => { closeConfirm(); onLogout() }} />
      )}
    </header>
  )
}
