import type { IconName } from '../Icon'
import type { ShopMarker, ShopType } from '../../api/client'

/** Mặc định khi không lấy được vị trí: trung tâm TP.HCM. */
export const DEFAULT_CENTER: [number, number] = [10.7769, 106.7009]
export const DEFAULT_ZOOM = 14
/** Nhỏ hơn mức này thì khung nhìn quá rộng — không tải điểm (nhắc phóng to). */
export const MIN_LOAD_ZOOM = 11

/** Tile mặc định: Esri (không cần API key). KHÔNG dùng tile.openstreetmap.org — DNS của VNPT (và vài nhà mạng VN)
 *  báo tên miền "không tồn tại" → bản đồ xám trơn; CARTO giờ bắt API key. Đổi được qua VITE_MAP_TILE_URL. */
const ESRI = 'https://server.arcgisonline.com/ArcGIS/rest/services'
export const TILE_URL = import.meta.env.VITE_MAP_TILE_URL || `${ESRI}/World_Street_Map/MapServer/tile/{z}/{y}/{x}`
export const TILE_ATTRIBUTION = 'Tiles &copy; Esri &mdash; Esri, HERE, Garmin, OpenStreetMap contributors'
/** Giao diện tối: nền Dark Gray Canvas + lớp nhãn đè lên (nền không có chữ). */
export const TILE_URL_DARK = import.meta.env.VITE_MAP_TILE_URL_DARK || `${ESRI}/Canvas/World_Dark_Gray_Base/MapServer/tile/{z}/{y}/{x}`
export const TILE_LABELS_DARK = import.meta.env.VITE_MAP_TILE_URL_DARK ? null : `${ESRI}/Canvas/World_Dark_Gray_Reference/MapServer/tile/{z}/{y}/{x}`
export const TILE_ATTRIBUTION_DARK = TILE_ATTRIBUTION

export const TYPE_ICON: Record<ShopType, IconName> = {
  Agency: 'store',
  Street: 'street',
  Vietlott: 'ticket',
  Redemption: 'award',
}

/** Phút trong ngày theo giờ VN. */
export function vnMinutesNow() {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone: 'Asia/Ho_Chi_Minh', hour: 'numeric', minute: 'numeric', hourCycle: 'h23',
  }).formatToParts(new Date())
  const get = (type: string) => Number(parts.find(p => p.type === type)?.value ?? 0)
  return get('hour') * 60 + get('minute')
}

/** null = không rõ giờ. Đóng < mở = mở qua nửa đêm; mở = đóng = mở cả ngày. */
export function isOpenNow(s: Pick<ShopMarker, 'opensAtMin' | 'closesAtMin'>, now = vnMinutesNow()): boolean | null {
  const { opensAtMin: o, closesAtMin: c } = s
  if (o == null || c == null) return null
  if (o === c) return true
  return o < c ? now >= o && now < c : now >= o || now < c
}

export const fmtMinutes = (m: number) => `${String(Math.floor(m / 60)).padStart(2, '0')}:${String(m % 60).padStart(2, '0')}`

/** "08:30" → 510. */
export const parseMinutes = (hhmm: string) => {
  const [h, m] = hhmm.split(':').map(Number)
  return Number.isFinite(h) && Number.isFinite(m) ? h * 60 + m : null
}

export const directionsUrl = (lat: number, lng: number) =>
  `https://www.google.com/maps/dir/?api=1&destination=${lat},${lng}`

// Lịch xổ Miền Nam theo thứ — khớp DrawSchedule.MnSchedule ở backend (0 = Chủ nhật).
const MN_SCHEDULE: Record<number, string[]> = {
  1: ['TPHCM', 'DongThap', 'CaMau'],
  2: ['BenTre', 'VungTau', 'BacLieu'],
  3: ['DongNai', 'CanTho', 'SocTrang'],
  4: ['TayNinh', 'AnGiang', 'BinhThuan'],
  5: ['VinhLong', 'BinhDuong', 'TraVinh'],
  6: ['TPHCM', 'LongAn', 'BinhPhuoc', 'HauGiang'],
  0: ['TienGiang', 'KienGiang', 'DaLat'],
}
const MN_SCHEDULED = new Set(Object.values(MN_SCHEDULE).flat())

/**
 * Đài có thể quay ngày `iso` — khớp DrawSchedule.MayDrawOn: MN theo lịch, MB mỗi ngày; MT (chưa có lịch)
 * và đài MN ngoài lịch thì luôn hiện.
 */
export function mayDrawOn(iso: string, code: string) {
  if (!MN_SCHEDULED.has(code)) return true
  const [y, m, d] = iso.split('-').map(Number)
  return MN_SCHEDULE[new Date(y, m - 1, d).getDay()].includes(code)
}

export const VIETLOTT = 'Vietlott'
