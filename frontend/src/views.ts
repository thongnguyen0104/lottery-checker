import type { IconName } from './components/Icon'

export type View = 'check' | 'lucky' | 'results'

/** Các tính năng — dùng chung cho tab trên AppHeader (màn rộng) và BottomNav (điện thoại). */
export const VIEWS: { id: View; icon: IconName; title: string; short: string; hint: string }[] = [
  { id: 'check', icon: 'ticket', title: 'Dò Vé Số', short: 'Dò vé', hint: 'Chụp vé, tự đọc số và dò giải' },
  { id: 'lucky', icon: 'dice', title: '6 Số May Mắn', short: 'Số may mắn', hint: 'Chọn số Vietlott, luận số giấc mơ' },
  { id: 'results', icon: 'calendar', title: 'Kết Quả Xổ Số', short: 'Kết quả', hint: 'Xem bảng kết quả chi tiết từng đài' },
]
