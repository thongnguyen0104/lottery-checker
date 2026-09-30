import type { IconName } from './components/Icon'

export type View = 'check' | 'lucky' | 'predict' | 'results' | 'blog' | 'profile' | 'admin'

/** Các tính năng — dùng chung cho tab trên AppHeader (màn rộng) và BottomNav (điện thoại). */
// Chữ hiển thị nằm ở common.json → views.<id>.title/short/hint, dịch lúc render để đổi ngôn ngữ là đổi ngay.
export const VIEWS: { id: View; path: string; icon: IconName }[] = [
  { id: 'check', path: '/', icon: 'ticket' },
  { id: 'lucky', path: '/so-may-man', icon: 'dice' },
  { id: 'predict', path: '/du-doan', icon: 'predict' },
  { id: 'results', path: '/ket-qua', icon: 'calendar' },
  { id: 'blog', path: '/blog', icon: 'blog' },
]

/** Màn có đường dẫn riêng nhưng không nằm trên thanh tab — mở từ menu logo. */
const EXTRA_VIEWS: { id: View; path: string }[] = [
  { id: 'profile', path: '/tai-khoan' },
  { id: 'admin', path: '/quan-tri' },
]

const ROUTES = [...VIEWS, ...EXTRA_VIEWS]

/** Đường dẫn riêng của mỗi màn — để chia sẻ link / F5 vẫn đúng màn. */
export const viewPath = (v: View) => ROUTES.find(x => x.id === v)!.path

/** Màn ứng với đường dẫn; đường dẫn lạ → Dò vé. Bỏ "/" cuối để "/ket-qua/" cũng khớp. */
export const viewFromPath = (path: string): View =>
  blogPostIdFromPath(path) ? 'blog'
    : ROUTES.find(x => x.path === (path.replace(/\/+$/, '') || '/'))?.id ?? 'check'

/** Link chia sẻ riêng của 1 bài Blog: /blog/{guid}. */
export const blogPostPath = (publicId: string) => `/blog/${publicId}`

const BLOG_POST = /^\/blog\/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\/?$/i

/** Guid bài trong link chia sẻ; null nếu không phải link 1 bài. */
export const blogPostIdFromPath = (path: string) => BLOG_POST.exec(path)?.[1].toLowerCase() ?? null
