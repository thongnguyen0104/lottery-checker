import { useEffect, useState, type FormEvent } from 'react'
import { createPortal } from 'react-dom'
import { useTranslation } from 'react-i18next'
import Icon from './Icon'
import { changePassword, type Account } from '../api/client'
import { PASSWORD_RULES, passwordValid } from '../utils/accountRules'

type Props = {
  onDone: (account: Account) => void
  onClose: () => void
  /** Bắt buộc đổi (mật khẩu mặc định / admin vừa đặt lại): không đóng được, chỉ đổi hoặc đăng xuất. */
  forced?: boolean
  onLogout: () => void
}

/** Đổi mật khẩu của chính mình — cùng kiểu bảng trượt với AuthDialog. */
export default function ChangePasswordDialog({ onDone, onClose, forced, onLogout }: Props) {
  const { t } = useTranslation()
  const [current, setCurrent] = useState('')
  const [next, setNext] = useState('')
  const [confirm, setConfirm] = useState('')
  const [showPw, setShowPw] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    if (forced) return
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose, forced])

  const canSubmit = !!current && passwordValid(next) && next === confirm && next !== current

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (!canSubmit || busy) return
    setBusy(true)
    setError(null)
    try {
      onDone(await changePassword(current, next))
    } catch (err) {
      setError((err as Error).message)
      setBusy(false)
    }
  }

  const field = (bad: boolean) =>
    `mt-1 w-full px-3 py-2.5 rounded-xl bg-surface border outline-none transition
     focus:ring-2 focus:ring-brand-500/30 ${bad ? 'border-bad' : 'border-line focus:border-brand-500'}`
  const type = showPw ? 'text' : 'password'

  return createPortal(
    <div className="fixed inset-0 z-50">
      <div className="absolute inset-0 bg-black/40" onClick={forced ? undefined : onClose} aria-hidden />

      <div role="dialog" aria-modal="true" aria-label={t('auth.changePassword')}
           className="sheet-up absolute inset-x-0 bottom-0 max-h-[90vh] overflow-y-auto
                      bg-surface text-ink rounded-t-3xl border border-line shadow-2xl
                      px-5 pt-3 pb-[calc(1.25rem+env(safe-area-inset-bottom))]
                      sm:inset-x-auto sm:bottom-auto sm:right-4 sm:top-[calc(4.5rem+env(safe-area-inset-top))]
                      sm:w-[400px] sm:rounded-2xl sm:pt-5">
        <div className="mx-auto mb-3 h-1.5 w-10 rounded-full bg-line sm:hidden" aria-hidden />

        <div className="flex items-center justify-between mb-3">
          <h2 className="flex items-center gap-2 text-lg font-bold">
            <Icon name="key" className="w-5 h-5 text-brand-700 dark:text-brand-400" />
            {t('auth.changePassword')}
          </h2>
          {!forced && (
            <button onClick={onClose} aria-label={t('auth.close')}
                    className="btn-close w-9 h-9 rounded-full flex items-center justify-center bg-muted text-ink-soft">
              <Icon name="close" className="w-4 h-4" />
            </button>
          )}
        </div>

        {forced && (
          <p className="mb-3 flex items-start gap-2 rounded-xl bg-warn/10 text-warn ring-1 ring-warn/25 px-3 py-2.5 text-sm">
            <Icon name="warn" className="w-4 h-4 shrink-0 mt-0.5" /> {t('auth.mustChangePassword')}
          </p>
        )}

        <form onSubmit={submit} className="space-y-3" noValidate>
          <label className="block">
            <span className="text-sm font-medium text-ink-soft">{t('auth.currentPassword')}</span>
            <div className="relative">
              <input type={type} value={current} onChange={e => setCurrent(e.target.value)} autoFocus
                     autoComplete="current-password" maxLength={64} className={`${field(false)} pr-16`} />
              <button type="button" onClick={() => setShowPw(v => !v)}
                      className="absolute right-2 top-1/2 -translate-y-[calc(50%-2px)] px-2 py-1 text-xs font-semibold
                                 text-ink-faint hover:text-ink">
                {showPw ? t('auth.hide') : t('auth.show')}
              </button>
            </div>
          </label>
          <label className="block">
            <span className="text-sm font-medium text-ink-soft">{t('auth.newPassword')}</span>
            <input type={type} value={next} onChange={e => setNext(e.target.value)}
                   autoComplete="new-password" maxLength={64} className={field(!!next && next === current)} />
            {!!next && next === current && <span className="mt-1 block text-xs text-bad">{t('auth.samePassword')}</span>}
          </label>
          <ul className="grid gap-1 text-xs">
            {PASSWORD_RULES.map(r => {
              const ok = r.test(next)
              return (
                <li key={r.key} className={`flex items-center gap-1.5 ${ok ? 'text-ok' : 'text-ink-faint'}`}>
                  <Icon name={ok ? 'ok' : 'fail'} className="w-3.5 h-3.5" /> {t(`auth.rules.${r.key}`)}
                </li>
              )
            })}
          </ul>
          <label className="block">
            <span className="text-sm font-medium text-ink-soft">{t('auth.confirmPassword')}</span>
            <input type={type} value={confirm} onChange={e => setConfirm(e.target.value)}
                   autoComplete="new-password" maxLength={64} className={field(!!confirm && confirm !== next)} />
            {confirm && confirm !== next && <span className="mt-1 block text-xs text-bad">{t('auth.passwordMismatch')}</span>}
          </label>

          {error && (
            <p role="alert" className="flex items-center gap-1.5 text-sm text-bad">
              <Icon name="error" className="w-4 h-4 shrink-0" /> {error}
            </p>
          )}

          <button type="submit" disabled={!canSubmit || busy}
                  className="w-full py-3 rounded-xl font-semibold bg-gradient-to-r from-primary to-primary-end
                             text-on-primary shadow-md shadow-primary/25 active:scale-[0.98] transition
                             disabled:opacity-50 disabled:active:scale-100">
            {busy ? t('auth.busy') : t('auth.changePassword')}
          </button>
          {forced && (
            <button type="button" onClick={onLogout}
                    className="w-full py-2 text-sm font-semibold text-ink-faint hover:text-ink transition">
              {t('auth.logout')}
            </button>
          )}
        </form>
      </div>
    </div>,
    document.body,
  )
}
