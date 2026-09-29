// Giống hệt backend Services/AccountRules.cs — báo lỗi ngay khi gõ, máy chủ vẫn kiểm tra lại.
export const USERNAME_MIN = 10
export const USERNAME_MAX = 20

export const normalizeUsername = (u: string) => u.trim().toLowerCase()

export function usernameError(raw: string): string | null {
  const u = normalizeUsername(raw)
  if (u.length < USERNAME_MIN || u.length > USERNAME_MAX)
    return `Tên đăng nhập phải từ ${USERNAME_MIN} đến ${USERNAME_MAX} ký tự.`
  if (!/^[a-z0-9._]+$/.test(u)) return 'Tên đăng nhập chỉ gồm chữ không dấu, số, dấu chấm hoặc gạch dưới.'
  return null
}

/** Từng điều kiện mật khẩu — hiện thành checklist trong form đăng ký. */
export const PASSWORD_RULES: { label: string; test: (p: string) => boolean }[] = [
  { label: '8–64 ký tự, không khoảng trắng', test: p => p.length >= 8 && p.length <= 64 && !/\s/.test(p) },
  { label: 'Có chữ hoa (A–Z)', test: p => /\p{Lu}/u.test(p) },
  { label: 'Có chữ thường (a–z)', test: p => /\p{Ll}/u.test(p) },
  { label: 'Có chữ số (0–9)', test: p => /\d/.test(p) },
  { label: 'Có ký tự đặc biệt (vd !@#$%)', test: p => /[^\p{L}\p{N}]/u.test(p.replace(/\s/g, '')) },
]

export const passwordValid = (p: string) => PASSWORD_RULES.every(r => r.test(p))
