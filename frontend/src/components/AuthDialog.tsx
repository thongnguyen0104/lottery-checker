import { useEffect, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import Icon from './Icon'
import { checkUsername, login, register, type Account } from '../api/client'
import { PASSWORD_RULES, USERNAME_MAX, normalizeUsername, passwordValid, usernameError } from '../utils/accountRules'

type Mode = 'login' | 'register'
/** Trạng thái tra trùng username ở form đăng ký. */
type NameCheck = { state: 'idle' | 'checking' | 'ok' | 'taken'; message?: string }

type Props = {
  onClose: () => void
  onDone: (account: Account) => void
  /** Lý do mở form (vd hết lượt dò thử) — hiện ở đầu form. */
  reason?: string
}

/** Đăng nhập / đăng ký bằng username + mật khẩu. Cùng kiểu bảng trượt với DonateDialog. */
export default function AuthDialog({ onClose, onDone, reason }: Props) {
  const { t } = useTranslation()
  const [mode, setMode] = useState<Mode>('login')
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [showPw, setShowPw] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const isRegister = mode === 'register'
  const nameErr = username ? usernameError(username) : null
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  // Đăng ký: gõ xong 400ms thì hỏi máy chủ tên đã có người dùng chưa.
  // Kết quả gắn với tên đã hỏi — gõ tên khác thì kết quả cũ tự thành "đang kiểm tra".
  const [checked, setChecked] = useState<{ name: string } & NameCheck>({ name: '', state: 'idle' })
  const wantCheck = isRegister && !!username && !nameErr
  const normalized = normalizeUsername(username)
  const nameCheck: NameCheck = !wantCheck ? { state: 'idle' }
    : checked.name === normalized ? checked : { state: 'checking' }
  useEffect(() => {
    if (!wantCheck) return
    const ctrl = new AbortController()
    const timer = setTimeout(() => {
      checkUsername(normalized, ctrl.signal)
        .then(r => setChecked(r.available ? { name: normalized, state: 'ok' }
          : { name: normalized, state: 'taken', message: r.error ?? undefined }))
        .catch(() => setChecked({ name: normalized, state: 'idle' }))  // lỗi mạng: để máy chủ báo lúc bấm Đăng ký
    }, 400)
    return () => { clearTimeout(timer); ctrl.abort() }
  }, [normalized, wantCheck])

  const canSubmit = isRegister
    ? !nameErr && !!username && nameCheck.state !== 'taken' && passwordValid(password) && password === confirm
    : !!username && !!password

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (!canSubmit || busy) return
    setBusy(true)
    setError(null)
    try {
      const u = normalizeUsername(username)
      onDone(await (isRegister ? register(u, password) : login(u, password)))
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  const switchMode = (m: Mode) => { setMode(m); setError(null); setConfirm('') }
  const field = (bad: boolean) =>
    `mt-1 w-full px-3 py-2.5 rounded-xl bg-surface border outline-none transition
     focus:ring-2 focus:ring-brand-500/30 ${bad ? 'border-bad' : 'border-line focus:border-brand-500'}`

  return (
    <div className="fixed inset-0 z-50">
      <div className="absolute inset-0 bg-black/40" onClick={onClose} aria-hidden />

      <div role="dialog" aria-modal="true" aria-label={isRegister ? t('auth.register') : t('auth.login')}
           className="sheet-up absolute inset-x-0 bottom-0 max-h-[90vh] overflow-y-auto
                      bg-surface text-ink rounded-t-3xl border border-line shadow-2xl
                      px-5 pt-3 pb-[calc(1.25rem+env(safe-area-inset-bottom))]
                      sm:inset-x-auto sm:bottom-auto sm:right-4 sm:top-[calc(4.5rem+env(safe-area-inset-top))]
                      sm:w-[400px] sm:rounded-2xl sm:pt-5">
        <div className="mx-auto mb-3 h-1.5 w-10 rounded-full bg-line sm:hidden" aria-hidden />

        <div className="flex items-center justify-between mb-3">
          <h2 className="flex items-center gap-2 text-lg font-bold">
            <Icon name="user" className="w-5 h-5 text-brand-700 dark:text-brand-400" />
            {isRegister ? t('auth.createAccount') : t('auth.login')}
          </h2>
          <button onClick={onClose} aria-label={t('auth.close')}
                  className="btn-close w-9 h-9 rounded-full flex items-center justify-center bg-muted text-ink-soft">
            <Icon name="close" className="w-4 h-4" />
          </button>
        </div>

        {reason && <p className="mb-3 text-sm text-ink-soft">{reason}</p>}

        <div className="grid grid-cols-2 gap-1 p-1 mb-4 rounded-xl bg-muted/80 border border-line/60">
          {(['login', 'register'] as const).map(m => (
            <button key={m} type="button" onClick={() => switchMode(m)}
                    className={`py-2 rounded-lg text-sm font-semibold transition ${mode === m
                      ? 'bg-surface text-brand-700 shadow-sm dark:text-brand-400' : 'text-ink-faint hover:text-ink'}`}>
              {m === 'login' ? t('auth.login') : t('auth.register')}
            </button>
          ))}
        </div>

        <form onSubmit={submit} className="space-y-3" noValidate>
          <label className="block">
            <span className="text-sm font-medium text-ink-soft">{t('auth.username')}</span>
            <input value={username} onChange={e => setUsername(e.target.value.replace(/\s/g, ''))}
                   maxLength={USERNAME_MAX} autoComplete="username" autoCapitalize="none" spellCheck={false}
                   placeholder={t('auth.usernamePlaceholder')} className={field(isRegister && (!!nameErr || nameCheck.state === 'taken'))} />
            {isRegister && username && (
              <span className={`mt-1 block text-xs ${nameErr || nameCheck.state === 'taken' ? 'text-bad'
                : nameCheck.state === 'ok' ? 'text-ok' : 'text-ink-faint'}`}>
                {nameErr ?? (nameCheck.state === 'checking' ? t('auth.checking')
                  : nameCheck.state === 'taken' ? nameCheck.message ?? t('auth.usernameTaken')
                  : nameCheck.state === 'ok' ? t('auth.usernameOk') : '')}
              </span>
            )}
          </label>

          <label className="block">
            <span className="text-sm font-medium text-ink-soft">{t('auth.password')}</span>
            <div className="relative">
              <input type={showPw ? 'text' : 'password'} value={password} onChange={e => setPassword(e.target.value)}
                     autoComplete={isRegister ? 'new-password' : 'current-password'} maxLength={64}
                     className={`${field(false)} pr-16`} />
              <button type="button" onClick={() => setShowPw(v => !v)}
                      className="absolute right-2 top-1/2 -translate-y-[calc(50%-2px)] px-2 py-1 text-xs font-semibold
                                 text-ink-faint hover:text-ink">
                {showPw ? t('auth.hide') : t('auth.show')}
              </button>
            </div>
          </label>

          {isRegister && (
            <>
              <ul className="grid gap-1 text-xs">
                {PASSWORD_RULES.map(r => {
                  const ok = r.test(password)
                  return (
                    <li key={r.key} className={`flex items-center gap-1.5 ${ok ? 'text-ok' : 'text-ink-faint'}`}>
                      <Icon name={ok ? 'ok' : 'fail'} className="w-3.5 h-3.5" /> {t(`auth.rules.${r.key}`)}
                    </li>
                  )
                })}
              </ul>
              <label className="block">
                <span className="text-sm font-medium text-ink-soft">{t('auth.confirmPassword')}</span>
                <input type={showPw ? 'text' : 'password'} value={confirm} onChange={e => setConfirm(e.target.value)}
                       autoComplete="new-password" maxLength={64} className={field(!!confirm && confirm !== password)} />
                {confirm && confirm !== password &&
                  <span className="mt-1 block text-xs text-bad">{t('auth.passwordMismatch')}</span>}
              </label>
            </>
          )}

          {error && (
            <p role="alert" className="flex items-center gap-1.5 text-sm text-bad">
              <Icon name="error" className="w-4 h-4 shrink-0" /> {error}
            </p>
          )}

          <button type="submit" disabled={!canSubmit || busy}
                  className="w-full py-3 rounded-xl font-semibold bg-gradient-to-r from-primary to-primary-end
                             text-on-primary shadow-md shadow-primary/25 active:scale-[0.98] transition
                             disabled:opacity-50 disabled:active:scale-100">
            {busy ? t('auth.busy') : isRegister ? t('auth.register') : t('auth.login')}
          </button>
        </form>
      </div>
    </div>
  )
}
