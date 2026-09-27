// Hạn lĩnh thưởng vé XSKT: 30 ngày kể từ ngày mở thưởng — khớp DrawSchedule.ClaimDays ở backend.
export const CLAIM_DAYS = 30

// Ngày hôm nay theo giờ VN dạng 'YYYY-MM-DD' — không dùng giờ máy, vì điện thoại/trình duyệt
// có thể đặt múi giờ khác; en-CA cho sẵn định dạng ISO.
const todayVn = () =>
  new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Ho_Chi_Minh' }).format(new Date())

/** Ngày cuối còn lĩnh thưởng được ('YYYY-MM-DD'), tính cả ngày đó. */
export function claimDeadline(drawDate: string): string {
  // Cộng ngày trên UTC để không dính giờ mùa hè / lệch múi giờ.
  const d = new Date(`${drawDate.slice(0, 10)}T00:00:00Z`)
  d.setUTCDate(d.getUTCDate() + CLAIM_DAYS)
  return d.toISOString().slice(0, 10)
}

/** Vé đã quá hạn lĩnh thưởng chưa. So chuỗi ISO là đủ (cùng định dạng YYYY-MM-DD). */
export const isExpired = (drawDate: string | null | undefined): boolean =>
  !!drawDate && /^\d{4}-\d{2}-\d{2}/.test(drawDate) && todayVn() > claimDeadline(drawDate)
