// Giống hệt backend Services/AccountRules.cs — báo lỗi ngay khi gõ, máy chủ vẫn kiểm tra lại.
import i18n from '../i18n'
export const USERNAME_MIN = 10
export const USERNAME_MAX = 20

export const normalizeUsername = (u: string) => u.trim().toLowerCase()

export function usernameError(raw: string): string | null {
  const u = normalizeUsername(raw)
  if (u.length < USERNAME_MIN || u.length > USERNAME_MAX)
    return i18n.t('auth.usernameLength', { min: USERNAME_MIN, max: USERNAME_MAX })
  if (!/^[a-z0-9._]+$/.test(u)) return i18n.t('auth.usernameChars')
  return null
}

/** Từng điều kiện mật khẩu — hiện thành checklist trong form đăng ký. key → common:auth.rules.<key>. */
export const PASSWORD_RULES: { key: 'length' | 'upper' | 'lower' | 'digit' | 'special'; test: (p: string) => boolean }[] = [
  { key: 'length', test: p => p.length >= 8 && p.length <= 64 && !/\s/.test(p) },
  { key: 'upper', test: p => /\p{Lu}/u.test(p) },
  { key: 'lower', test: p => /\p{Ll}/u.test(p) },
  { key: 'digit', test: p => /\d/.test(p) },
  { key: 'special', test: p => /[^\p{L}\p{N}]/u.test(p.replace(/\s/g, '')) },
]

export const passwordValid = (p: string) => PASSWORD_RULES.every(r => r.test(p))
