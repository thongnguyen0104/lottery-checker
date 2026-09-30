import { currentLang, currentLocale } from '../i18n'

const WEEKDAYS = ['Chủ Nhật', 'Thứ Hai', 'Thứ Ba', 'Thứ Tư', 'Thứ Năm', 'Thứ Sáu', 'Thứ Bảy']
const pad = (n: number) => String(n).padStart(2, '0')

// Dựng Date từ từng thành phần (giờ địa phương), KHÔNG new Date('YYYY-MM-DD') — chuỗi đó bị
// hiểu là UTC nên máy ở múi giờ âm sẽ lùi sang hôm trước.
const toDate = (iso: string) => {
  const [y, m, d] = iso.split('-').map(Number)
  return new Date(y, m - 1, d)
}

// Tiếng Việt giữ bảng tên thứ riêng (viết hoa kiểu "Thứ Bảy" quen dùng); tiếng Anh dùng Intl.
export const weekday = (iso: string) => currentLang() === 'en'
  ? toDate(iso).toLocaleDateString(currentLocale(), { weekday: 'long' })
  : WEEKDAYS[toDate(iso).getDay()]

/** 'YYYY-MM-DD' → 'DD/MM/YYYY' (tiếng Anh: 'Sep 26, 2026' — tránh nhầm ngày/tháng kiểu Mỹ) */
export const formatDate = (iso: string) => currentLang() === 'en'
  ? toDate(iso).toLocaleDateString(currentLocale(), { month: 'short', day: 'numeric', year: 'numeric' })
  : iso.split('-').reverse().join('/')

/** 'YYYY-MM-DD' → 'Thứ Bảy, 26/09/2026' (tiếng Anh: 'Saturday, Sep 26, 2026') */
export const formatDay = (iso: string) => `${weekday(iso)}, ${formatDate(iso)}`

/** Hôm nay theo giờ máy, dạng 'YYYY-MM-DD' (toISOString là giờ UTC — trước 7h sáng ở VN còn là hôm qua). */
export const todayIso = () => {
  const d = new Date()
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

/** Cộng n ngày vào 'YYYY-MM-DD' (theo lịch, không lệch múi giờ). */
export const addDays = (iso: string, n: number) => {
  const d = toDate(iso)
  d.setDate(d.getDate() + n)
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

/** 'YYYY-MM-DD' → '26/09' (tiếng Anh: 'Sep 26') — nhãn ngắn cho dải chọn ngày. */
export const formatDayMonth = (iso: string) => currentLang() === 'en'
  ? toDate(iso).toLocaleDateString(currentLocale(), { month: 'short', day: 'numeric' })
  : iso.split('-').slice(1).reverse().join('/')
