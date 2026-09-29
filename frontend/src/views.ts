import type { IconName } from './components/Icon'

export type View = 'check' | 'lucky' | 'results'

/** Các tính năng — dùng chung cho tab trên AppHeader (màn rộng) và BottomNav (điện thoại). */
export const VIEWS: { id: View; path: string; icon: IconName; title: string; short: string; hint: string }[] = [
  { id: 'check', path: '/', icon: 'ticket', title: 'Dò Vé Số', short: 'Dò vé', hint: 'Chụp vé, tự đọc số và dò giải' },
  { id: 'lucky', path: '/so-may-man', icon: 'dice', title: '6 Số May Mắn', short: 'Số may mắn', hint: 'Chọn số Vietlott, luận số giấc mơ' },
  { id: 'results', path: '/ket-qua', icon: 'calendar', title: 'Kết Quả Xổ Số', short: 'Kết quả', hint: 'Xem bảng kết quả chi tiết từng đài' },
]

/** Đường dẫn riêng của mỗi màn — để chia sẻ link / F5 vẫn đúng màn. */
export const viewPath = (v: View) => VIEWS.find(x => x.id === v)!.path

/** Màn ứng với đường dẫn; đường dẫn lạ → Dò vé. Bỏ "/" cuối để "/ket-qua/" cũng khớp. */
export const viewFromPath = (path: string): View =>
  VIEWS.find(x => x.path === (path.replace(/\/+$/, '') || '/'))?.id ?? 'check'
