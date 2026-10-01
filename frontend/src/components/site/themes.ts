import type { CSSProperties } from 'react'
import type { SiteTheme } from '../../api/client'

/**
 * Giao diện của website con — độc lập với theme sáng/tối của app chính (chủ site chọn, khách nào cũng thấy
 * giống nhau). Màu chủ đạo do chủ site chọn đi qua biến CSS --sp (xem siteStyle), class dùng bg-[var(--sp)].
 */
export type ThemeClasses = {
  page: string
  card: string
  heading: string
  muted: string
  button: string
  ghostButton: string
  input: string
  hero: string
}

export const THEMES: Record<SiteTheme, ThemeClasses> = {
  classic: {
    page: 'bg-[#fbf7f0] text-stone-800',
    card: 'bg-white rounded-2xl shadow-sm ring-1 ring-stone-200',
    heading: 'font-bold tracking-tight text-stone-900',
    muted: 'text-stone-500',
    button: 'rounded-xl bg-[var(--sp)] text-white font-semibold shadow hover:brightness-110',
    ghostButton: 'rounded-xl ring-1 ring-[var(--sp)] text-[var(--sp)] font-semibold hover:bg-[var(--sp)]/10',
    input: 'rounded-xl border border-stone-300 bg-white focus:outline-none focus:ring-2 focus:ring-[var(--sp)]/40',
    hero: 'bg-gradient-to-br from-[var(--sp)] to-stone-900 text-white',
  },
  modern: {
    page: 'bg-slate-50 text-slate-800',
    card: 'bg-white rounded-lg border border-slate-200',
    heading: 'font-extrabold tracking-tight text-slate-900',
    muted: 'text-slate-500',
    button: 'rounded-lg bg-[var(--sp)] text-white font-semibold hover:brightness-110',
    ghostButton: 'rounded-lg border border-slate-300 text-slate-700 font-semibold hover:bg-slate-100',
    input: 'rounded-lg border border-slate-300 bg-white focus:outline-none focus:ring-2 focus:ring-[var(--sp)]/40',
    hero: 'bg-slate-900 text-white',
  },
  lucky: {
    page: 'bg-gradient-to-b from-[#7f1010] via-[#9b1c1c] to-[#7f1010] text-amber-50',
    card: 'bg-[#fff8e7] text-stone-800 rounded-3xl shadow-lg ring-2 ring-amber-300',
    heading: 'font-extrabold text-amber-300 drop-shadow',
    muted: 'text-amber-100/80',
    button: 'rounded-full bg-gradient-to-r from-amber-300 to-yellow-500 text-red-900 font-bold shadow-md hover:brightness-105',
    ghostButton: 'rounded-full ring-2 ring-amber-300 text-amber-200 font-bold hover:bg-amber-300/10',
    input: 'rounded-xl border border-amber-300 bg-white text-stone-800 focus:outline-none focus:ring-2 focus:ring-amber-400',
    hero: 'bg-[radial-gradient(circle_at_top,_var(--sp),_#5b0a0a)] text-amber-50',
  },
  minimal: {
    page: 'bg-white text-neutral-800',
    card: 'bg-white border border-neutral-200',
    heading: 'font-semibold text-neutral-900',
    muted: 'text-neutral-500',
    button: 'bg-neutral-900 text-white font-medium hover:bg-[var(--sp)]',
    ghostButton: 'border border-neutral-300 text-neutral-800 font-medium hover:border-neutral-900',
    input: 'border border-neutral-300 bg-white focus:outline-none focus:border-neutral-900',
    hero: 'bg-white text-neutral-900 border-b border-neutral-200',
  },
}

/** Khung "lucky" nền đỏ: chữ trong thẻ sáng vẫn phải tối — tiêu đề trong thẻ dùng màu này thay cho heading. */
export const cardHeading = (theme: SiteTheme) => theme === 'lucky' ? 'font-extrabold text-red-800' : THEMES[theme].heading
export const cardMuted = (theme: SiteTheme) => theme === 'lucky' ? 'text-stone-500' : THEMES[theme].muted

export const siteStyle = (primaryColor: string) => ({ '--sp': primaryColor }) as CSSProperties

/** Màu gợi ý trong trình chọn màu. */
export const PRESET_COLORS = ['#d32f2f', '#c2185b', '#f57c00', '#f9a825', '#2e7d32', '#00897b', '#1565c0', '#5e35b1', '#37474f']

export const formatVnd = (n: number, locale: string) => n.toLocaleString(locale) + ' ₫'
