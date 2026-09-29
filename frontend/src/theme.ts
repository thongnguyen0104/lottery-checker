import { useEffect, useState } from 'react'

// Tuỳ chọn giao diện của từng người xem, lưu ở localStorage của trình duyệt đó.
// Script đầu index.html đọc CÙNG key + mặc định này để đặt theme trước khi React chạy
// (khỏi nháy nền sáng/màu đỏ rồi mới đổi) — sửa ở đây thì sửa cả bên đó.
// v2: đổi mặc định sang Hoàng kim + nền tối — key mới để lựa chọn cũ (đỏ / theo máy) không che mất.
const STORAGE_KEY = 'dvs-theme-v2'

// Tên hiển thị ở common.json → theme.accents/modes/backgrounds.<id>.
// ink = màu dấu ✓ trên ô màu trong bảng chọn (mặc định trắng); vàng sáng cần chữ tối.
export const ACCENTS = [
  { id: 'gold', from: '#FBBF24', to: '#D97706', ink: '#0F172A' },
  { id: 'red', from: '#DC2626', to: '#EA580C' },
  { id: 'orange', from: '#EA580C', to: '#E11D48' },
  { id: 'pink', from: '#DB2777', to: '#9333EA' },
  { id: 'violet', from: '#7C3AED', to: '#C026D3' },
  { id: 'blue', from: '#2563EB', to: '#0891B2' },
  { id: 'mint', from: '#059669', to: '#65A30D' },
] as const

export const MODES = [
  { id: 'light' },
  { id: 'dark' },
  { id: 'system' },
] as const

export const BACKGROUNDS = [
  { id: 'aurora' },
  { id: 'dots' },
  { id: 'grid' },
  { id: 'plain' },
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

/**
 * Favicon theo màu đang chọn: cùng hình với public/favicon.svg (nền gradient, tấm vé kem, vạch quét
 * xanh) nhưng nền + dãy số lấy màu của bảng màu. File tĩnh vẫn là bản cho lúc trang chưa chạy JS.
 */
function faviconHref(accent: Accent) {
  const a = ACCENTS.find(x => x.id === accent) ?? ACCENTS[0]
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32">`
    + `<defs><linearGradient id="bg" x1="0" y1="0" x2="1" y2="1">`
    + `<stop offset="0" stop-color="${a.from}"/><stop offset="1" stop-color="${a.to}"/></linearGradient></defs>`
    + `<rect width="32" height="32" rx="7" fill="url(#bg)"/>`
    + `<path fill="#FFFBEB" d="M6 8.5h20a1.5 1.5 0 0 1 1.5 1.5v3.5a2.5 2.5 0 0 0 0 5V22a1.5 1.5 0 0 1-1.5 1.5H6A1.5 1.5 0 0 1 4.5 22v-3.5a2.5 2.5 0 0 0 0-5V10A1.5 1.5 0 0 1 6 8.5z"/>`
    + `<g fill="${a.to}"><rect x="8" y="10.75" width="4.5" height="3.5" rx="1"/>`
    + `<rect x="13.75" y="10.75" width="4.5" height="3.5" rx="1"/><rect x="19.5" y="10.75" width="4.5" height="3.5" rx="1"/></g>`
    + `<rect x="8" y="19" width="9" height="1.75" rx=".875" fill="#D6C7A1"/>`
    + `<rect x="2" y="15.75" width="28" height="1.75" rx=".875" fill="#10B981"/></svg>`
  return `data:image/svg+xml,${encodeURIComponent(svg)}`
}

function applyTheme(t: Theme) {
  const mode = t.mode === 'system' ? (darkQuery().matches ? 'dark' : 'light') : t.mode
  const root = document.documentElement
  root.dataset.mode = mode
  root.dataset.accent = t.accent
  root.dataset.bg = t.bg
  document.querySelector('meta[name="theme-color"]')?.setAttribute('content', CANVAS_HEX[mode])
  // Đổi cả 2 thẻ icon (.ico + .svg): còn thẻ .ico thì có trình duyệt vẫn chọn nó, tab giữ màu cũ.
  const href = faviconHref(t.accent)
  document.querySelectorAll<HTMLLinkElement>('link[rel="icon"]').forEach(l => {
    l.type = 'image/svg+xml'
    l.removeAttribute('sizes')
    l.href = href
  })
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
