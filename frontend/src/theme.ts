import { useEffect, useState } from 'react'

// Tuỳ chọn giao diện của từng người xem, lưu ở localStorage của trình duyệt đó.
// Script đầu index.html đọc CÙNG key + mặc định này để đặt theme trước khi React chạy
// (khỏi nháy nền sáng/màu đỏ rồi mới đổi) — sửa ở đây thì sửa cả bên đó.
// v2: đổi mặc định sang Hoàng kim + nền tối — key mới để lựa chọn cũ (đỏ / theo máy) không che mất.
const STORAGE_KEY = 'dvs-theme-v2'

// ink = màu dấu ✓ trên ô màu trong bảng chọn (mặc định trắng); vàng sáng cần chữ tối.
export const ACCENTS = [
  { id: 'gold', name: 'Hoàng kim', from: '#FBBF24', to: '#D97706', ink: '#0F172A' },
  { id: 'red', name: 'Đỏ may mắn', from: '#DC2626', to: '#EA580C' },
  { id: 'orange', name: 'Cam hoàng hôn', from: '#EA580C', to: '#E11D48' },
  { id: 'pink', name: 'Hồng kẹo ngọt', from: '#DB2777', to: '#9333EA' },
  { id: 'violet', name: 'Tím mộng mơ', from: '#7C3AED', to: '#C026D3' },
  { id: 'blue', name: 'Xanh đại dương', from: '#2563EB', to: '#0891B2' },
  { id: 'mint', name: 'Xanh bạc hà', from: '#059669', to: '#65A30D' },
] as const

export const MODES = [
  { id: 'light', name: 'Sáng' },
  { id: 'dark', name: 'Tối' },
  { id: 'system', name: 'Theo máy' },
] as const

export const BACKGROUNDS = [
  { id: 'aurora', name: 'Cực quang' },
  { id: 'dots', name: 'Chấm bi' },
  { id: 'grid', name: 'Kẻ ô' },
  { id: 'plain', name: 'Trơn' },
] as const

export type Accent = (typeof ACCENTS)[number]['id']
export type Mode = (typeof MODES)[number]['id']
export type Background = (typeof BACKGROUNDS)[number]['id']
export type Theme = { accent: Accent; mode: Mode; bg: Background }

// Thiết kế Hoàng kim dựng trên nền navy tối nên mặc định là Tối, không theo máy.
const DEFAULT_THEME: Theme = { accent: 'gold', mode: 'dark', bg: 'aurora' }

// Màu thanh trạng thái / thanh địa chỉ trên điện thoại = màu nền trang (--canvas trong index.css).
const CANVAS_HEX = { light: '#F8FAFC', dark: '#0F172A' }

const darkQuery = () => window.matchMedia('(prefers-color-scheme: dark)')

const oneOf = <T extends string>(list: readonly { id: T }[], value: unknown, fallback: T): T =>
  list.some(x => x.id === value) ? (value as T) : fallback

function loadTheme(): Theme {
  try {
    const saved = JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '{}')
    return {
      accent: oneOf(ACCENTS, saved.accent, DEFAULT_THEME.accent),
      mode: oneOf(MODES, saved.mode, DEFAULT_THEME.mode),
      bg: oneOf(BACKGROUNDS, saved.bg, DEFAULT_THEME.bg),
    }
  } catch {
    // Chế độ ẩn danh / chặn dữ liệu trang: dùng mặc định, vẫn đổi được trong phiên.
    return DEFAULT_THEME
  }
}

function applyTheme(t: Theme) {
  const mode = t.mode === 'system' ? (darkQuery().matches ? 'dark' : 'light') : t.mode
  const root = document.documentElement
  root.dataset.mode = mode
  root.dataset.accent = t.accent
  root.dataset.bg = t.bg
  document.querySelector('meta[name="theme-color"]')?.setAttribute('content', CANVAS_HEX[mode])
}

/** Theme hiện tại + hàm đổi một phần (vd chỉ đổi màu). Tự áp dụng và lưu mỗi lần đổi. */
export function useTheme() {
  const [theme, setTheme] = useState(loadTheme)

  useEffect(() => {
    applyTheme(theme)
    try { localStorage.setItem(STORAGE_KEY, JSON.stringify(theme)) } catch { /* xem loadTheme */ }
    if (theme.mode !== 'system') return
    // "Theo máy": điện thoại tự chuyển tối lúc tối trời thì app đổi theo ngay.
    const q = darkQuery()
    const onChange = () => applyTheme(theme)
    q.addEventListener('change', onChange)
    return () => q.removeEventListener('change', onChange)
  }, [theme])

  const update = (patch: Partial<Theme>) => setTheme(t => ({ ...t, ...patch }))
  return [theme, update] as const
}
