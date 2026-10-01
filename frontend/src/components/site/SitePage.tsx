import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import Icon from '../Icon'
import SiteRenderer from './SiteRenderer'
import { getPublicSite, type PublicSite } from '../../api/client'
import { siteRouteFromPath, sitePath, type SiteRoute } from '../../views'

/**
 * Trang công khai /s/{slug} — hiện toàn màn hình, không có khung app chính. Chuyển giữa trang chủ / bài viết
 * bằng pushState (state view 'site' để App giữ nguyên màn này khi bấm Back).
 */
export default function SitePage({ initial }: { initial: SiteRoute }) {
  const { t } = useTranslation('site')
  const [route, setRoute] = useState<SiteRoute>(initial)
  const [site, setSite] = useState<PublicSite | null>(null)
  const [error, setError] = useState(false)

  useEffect(() => {
    let alive = true
    getPublicSite(route.slug).then(s => { if (alive) setSite(s) }).catch(() => { if (alive) setError(true) })
    return () => { alive = false }
  }, [route.slug])

  useEffect(() => {
    const onPop = () => { const r = siteRouteFromPath(location.pathname); if (r) setRoute(r) }
    window.addEventListener('popstate', onPop)
    return () => window.removeEventListener('popstate', onPop)
  }, [])

  // Tiêu đề tab + mô tả theo site (chia sẻ link thì máy chủ vẫn trả index.html chung — đủ cho người xem).
  useEffect(() => {
    if (!site) return
    const prev = document.title
    document.title = site.config.tagline ? `${site.config.name} — ${site.config.tagline}` : site.config.name
    const meta = document.querySelector('meta[name="description"]')
    const prevDesc = meta?.getAttribute('content') ?? null
    if (meta && site.config.tagline) meta.setAttribute('content', site.config.tagline)
    return () => {
      document.title = prev
      if (meta && prevDesc != null) meta.setAttribute('content', prevDesc)
    }
  }, [site])

  const navigate = useCallback((to: { posts?: boolean; postId?: string | null }) => {
    const next: SiteRoute = { slug: route.slug, posts: !!to.posts || !!to.postId, postId: to.postId ?? null }
    history.pushState({ view: 'site' }, '', sitePath(next.slug, next.postId, next.posts))
    setRoute(next)
    window.scrollTo(0, 0)
  }, [route.slug])

  if (error) return (
    <div className="min-h-screen flex items-center justify-center p-6">
      <div className="card p-6 text-center space-y-3 max-w-md">
        <Icon name="site" className="w-10 h-10 mx-auto text-brand-700 dark:text-brand-400" />
        <p className="font-semibold">{t('notFound')}</p>
        <a href="/" className="inline-block underline text-sm">{t('backToApp')}</a>
      </div>
    </div>
  )
  if (!site) return <div className="min-h-screen animate-pulse bg-muted" aria-busy />
  return <SiteRenderer site={site} route={route} onNavigate={navigate} />
}
