import type { IconName } from './components/Icon'

export type View = 'check' | 'lucky' | 'predict' | 'results'

/** Các tính năng — dùng chung cho tab trên AppHeader (màn rộng) và BottomNav (điện thoại). */
// Chữ hiển thị nằm ở common.json → views.<id>.title/short/hint, dịch lúc render để đổi ngôn ngữ là đổi ngay.
export const VIEWS: { id: View; path: string; icon: IconName }[] = [
  { id: 'check', path: '/', icon: 'ticket' },
  { id: 'lucky', path: '/so-may-man', icon: 'dice' },
  { id: 'predict', path: '/du-doan', icon: 'predict' },
  { id: 'results', path: '/ket-qua', icon: 'calendar' },
]

/** Đường dẫn riêng của mỗi màn — để chia sẻ link / F5 vẫn đúng màn. */
export const viewPath = (v: View) => VIEWS.find(x => x.id === v)!.path

/** Màn ứng với đường dẫn; đường dẫn lạ → Dò vé. Bỏ "/" cuối để "/ket-qua/" cũng khớp. */
export const viewFromPath = (path: string): View =>
  VIEWS.find(x => x.path === (path.replace(/\/+$/, '') || '/'))?.id ?? 'check'
