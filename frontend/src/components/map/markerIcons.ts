import L from 'leaflet'
import type { ShopType } from '../../api/client'

// Icon Leaflet của bản đồ — tách khỏi shopUtils để chỗ không dùng bản đồ (tab Quản trị) khỏi kéo theo Leaflet.

/** Màu marker theo loại (class Tailwind — có trong chuỗi nên Tailwind quét thấy). */
const TYPE_BG: Record<ShopType, string> = {
  Agency: 'bg-brand-600',
  Street: 'bg-info',
  Vietlott: 'bg-ok',
  Redemption: 'bg-warn',
}

// SVG lucide rút gọn (path) cho marker — divIcon là HTML thuần, không render được component React.
const TYPE_SVG: Record<ShopType, string> = {
  Agency: '<path d="m2 7 4.41-4.41A2 2 0 0 1 7.83 2h8.34a2 2 0 0 1 1.42.59L22 7"/><path d="M4 12v8a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-8"/><path d="M15 22v-4a2 2 0 0 0-2-2h-2a2 2 0 0 0-2 2v4"/><path d="M2 7h20"/>',
  Street: '<path d="M4 16v-2.38C4 11.5 2.97 10.5 3 8c.03-2.72 1.49-6 4.5-6C9.37 2 10 3.8 10 5.5c0 3.11-2 5.66-2 8.68V16a2 2 0 1 1-4 0Z"/><path d="M20 20v-2.38c0-2.12 1.03-3.12 1-5.62-.03-2.72-1.49-6-4.5-6C14.63 6 14 7.8 14 9.5c0 3.11 2 5.66 2 8.68V20a2 2 0 1 0 4 0Z"/>',
  Vietlott: '<path d="M2 9a3 3 0 0 1 0 6v2a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-2a3 3 0 0 1 0-6V7a2 2 0 0 0-2-2H4a2 2 0 0 0-2 2Z"/><path d="M13 5v2"/><path d="M13 17v2"/><path d="M13 11v2"/>',
  Redemption: '<circle cx="12" cy="8" r="6"/><path d="M15.477 12.89 17 22l-5-3-5 3 1.523-9.11"/>',
}

const iconCache = new Map<string, L.DivIcon>()

/** Ghim tròn có icon theo loại; điểm có báo vé trúng gắn ngôi sao; đang chọn thì to hơn. */
export function markerIcon(type: ShopType, hasWin: boolean, selected: boolean) {
  const key = `${type}|${hasWin}|${selected}`
  let icon = iconCache.get(key)
  if (!icon) {
    const size = selected ? 44 : 34
    icon = L.divIcon({
      className: '',
      iconSize: [size, size],
      iconAnchor: [size / 2, size],
      popupAnchor: [0, -size],
      html: `<div class="relative flex items-center justify-center rounded-full rounded-br-none rotate-45 ${TYPE_BG[type]}
                         text-white shadow-lg ring-2 ring-white ${selected ? 'scale-110' : ''}"
                  style="width:${size}px;height:${size}px">
               <svg class="-rotate-45" width="${size * 0.5}" height="${size * 0.5}" viewBox="0 0 24 24" fill="none"
                    stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">${TYPE_SVG[type]}</svg>
               ${hasWin ? '<span class="absolute -top-1 -left-1 -rotate-45 w-4 h-4 rounded-full bg-yellow-400 text-[10px] leading-4 text-center ring-2 ring-white">★</span>' : ''}
             </div>`,
    })
    iconCache.set(key, icon)
  }
  return icon
}

/** Vị trí của người dùng: chấm xanh. */
export const ME_ICON = L.divIcon({
  className: '',
  iconSize: [18, 18],
  iconAnchor: [9, 9],
  html: '<div class="w-[18px] h-[18px] rounded-full bg-info ring-4 ring-white shadow-md"></div>',
})
