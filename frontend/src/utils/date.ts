const WEEKDAYS = ['Chủ Nhật', 'Thứ Hai', 'Thứ Ba', 'Thứ Tư', 'Thứ Năm', 'Thứ Sáu', 'Thứ Bảy']
const pad = (n: number) => String(n).padStart(2, '0')

// Dựng Date từ từng thành phần (giờ địa phương), KHÔNG new Date('YYYY-MM-DD') — chuỗi đó bị
// hiểu là UTC nên máy ở múi giờ âm sẽ lùi sang hôm trước.
export const weekday = (iso: string) => {
  const [y, m, d] = iso.split('-').map(Number)
  return WEEKDAYS[new Date(y, m - 1, d).getDay()]
}

/** 'YYYY-MM-DD' → 'DD/MM/YYYY' */
export const formatDate = (iso: string) => iso.split('-').reverse().join('/')

/** 'YYYY-MM-DD' → 'Thứ Bảy, 26/09/2026' */
export const formatDay = (iso: string) => `${weekday(iso)}, ${formatDate(iso)}`

/** Hôm nay theo giờ máy, dạng 'YYYY-MM-DD' (toISOString là giờ UTC — trước 7h sáng ở VN còn là hôm qua). */
export const todayIso = () => {
  const d = new Date()
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}
