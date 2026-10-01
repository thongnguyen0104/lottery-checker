import type { IconName } from './components/Icon'

export type View = 'check' | 'lucky' | 'predict' | 'results' | 'blog' | 'map' | 'profile' | 'admin' | 'mySite' | 'site'

/** Các tính năng — dùng chung cho tab trên AppHeader (màn rộng) và BottomNav (điện thoại). */
// Chữ hiển thị nằm ở common.json → views.<id>.title/short/hint, dịch lúc render để đổi ngôn ngữ là đổi ngay.
export const VIEWS: { id: View; path: string; icon: IconName }[] = [
  { id: 'check', path: '/', icon: 'ticket' },
  { id: 'lucky', path: '/so-may-man', icon: 'dice' },
  { id: 'predict', path: '/du-doan', icon: 'predict' },
  { id: 'results', path: '/ket-qua', icon: 'calendar' },
  { id: 'blog', path: '/blog', icon: 'blog' },
]

/** Thứ tự tab BottomNav (điện thoại): Dò vé — tính năng chính — nằm giữa, vẽ thành nút tròn nổi lên.
 *  Màn rộng thì Dò vé là nút primary riêng ở góc phải AppHeader, không nằm trong dãy tab. */
export const BOTTOM_NAV: View[] = ['lucky', 'predict', 'check', 'results', 'blog']

/** Màn có đường dẫn riêng nhưng không nằm trên thanh tab — mở từ menu logo (bản đồ: cả footer). */
export const EXTRA_VIEWS: { id: View; path: string; icon: IconName }[] = [
  { id: 'map', path: '/ban-do', icon: 'map' },
  { id: 'profile', path: '/tai-khoan', icon: 'user' },
  { id: 'admin', path: '/quan-tri', icon: 'admin' },
  { id: 'mySite', path: '/website-cua-toi', icon: 'site' },
]

const ROUTES = [...VIEWS, ...EXTRA_VIEWS]

/** Đường dẫn riêng của mỗi màn — để chia sẻ link / F5 vẫn đúng màn. */
// "site" không có đường dẫn cố định (theo slug) — gọi viewPath("site") thì về trang chủ.
export const viewPath = (v: View) => ROUTES.find(x => x.id === v)?.path ?? '/'

/** Màn ứng với đường dẫn; đường dẫn lạ → Dò vé. Bỏ "/" cuối để "/ket-qua/" cũng khớp. */
export const viewFromPath = (path: string): View =>
  siteRouteFromPath(path) ? 'site'
    : blogPostIdFromPath(path) ? 'blog'
    : shopIdFromPath(path) ? 'map'
    : ROUTES.find(x => x.path === (path.replace(/\/+$/, '') || '/'))?.id ?? 'check'

const GUID = '([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})'

/** Link chia sẻ riêng của 1 bài Blog: /blog/{guid}. */
export const blogPostPath = (publicId: string) => `/blog/${publicId}`

const BLOG_POST = new RegExp(`^/blog/${GUID}/?$`, 'i')

/** Guid bài trong link chia sẻ; null nếu không phải link 1 bài. */
export const blogPostIdFromPath = (path: string) => BLOG_POST.exec(path)?.[1].toLowerCase() ?? null

/** Link chia sẻ riêng của 1 điểm bán: /ban-do/{guid}. */
export const shopPath = (publicId: string) => `/ban-do/${publicId}`

const SHOP = new RegExp(`^/ban-do/${GUID}/?$`, 'i')

/** Guid điểm bán trong link chia sẻ; null nếu không phải link 1 điểm. */
export const shopIdFromPath = (path: string) => SHOP.exec(path)?.[1].toLowerCase() ?? null

/** Website con: /s/{slug}, /s/{slug}/bai-viet, /s/{slug}/bai-viet/{guid}. Không có ở ROUTES vì đường dẫn đổi theo slug. */
export type SiteRoute = { slug: string; posts: boolean; postId: string | null }

const SITE = new RegExp(`^/s/([a-z0-9-]{3,40})(/bai-viet(?:/${GUID})?)?/?$`, 'i')

export const siteRouteFromPath = (path: string): SiteRoute | null => {
  const m = SITE.exec(path)
  return m ? { slug: m[1].toLowerCase(), posts: !!m[2], postId: m[3]?.toLowerCase() ?? null } : null
}

export const sitePath = (slug: string, postId?: string | null, posts = false) =>
  `/s/${slug}` + (postId ? `/bai-viet/${postId}` : posts ? '/bai-viet' : '')
