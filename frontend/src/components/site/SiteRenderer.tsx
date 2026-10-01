import { lazy, Suspense, useEffect, useState, type FormEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import Icon from '../Icon'
import {
  getAvailableDraws, getProvinceResult, getPublicSitePost, getPublicSitePosts, reportSite, reserveOnSite, siteImageUrl,
  SITE_REPORT_REASONS, type PublicSite, type SiteBlockType, type SitePost, type SitePostSummary, type SiteProduct,
  type SiteReportReason, type SiteTheme,
} from '../../api/client'
import { currentLocale } from '../../i18n'
import { provinceName } from '../../data/provinces'
import { formatDate } from '../../utils/date'
import { cardHeading, cardMuted, formatVnd, siteStyle, THEMES, type ThemeClasses } from './themes'
import type { SiteRoute } from '../../views'

const SiteMapBlock = lazy(() => import('./SiteMapBlock'))

type Nav = (to: { posts?: boolean; postId?: string | null }) => void

/**
 * Trang của 1 website con: trang chủ (các khối theo thứ tự chủ site chọn), danh sách bài, 1 bài.
 * Dùng chung cho trang công khai /s/{slug} và khung Xem trước trong trình sửa (preview: form giữ vé tắt,
 * bài viết / link nội bộ không điều hướng).
 */
export default function SiteRenderer({ site, route, preview, onNavigate }: {
  site: PublicSite
  route: SiteRoute
  preview?: boolean
  onNavigate: Nav
}) {
  const { t } = useTranslation('site')
  const c = site.config
  const th = THEMES[c.theme] ?? THEMES.classic
  const [reserveProduct, setReserveProduct] = useState<number | null>(null)

  const reserve = (p: SiteProduct | null) => {
    setReserveProduct(p?.id ?? null)
    document.getElementById('site-reserve')?.scrollIntoView({ behavior: 'smooth', block: 'start' })
  }
  const enabled = (b: SiteBlockType) => c.blocks.some(x => x.type === b && x.enabled)

  let body: ReactNode
  if (route.postId) body = <PostView site={site} th={th} id={route.postId} onNavigate={onNavigate} />
  else if (route.posts) body = <PostList slug={site.slug} theme={c.theme} th={th} initial={null} onNavigate={onNavigate} />
  else body = c.blocks.filter(b => b.enabled).map(b => {
    switch (b.type) {
      case 'hero': return <Hero key={b.type} site={site} th={th} canReserve={enabled('reserve')} onReserve={() => reserve(null)} />
      case 'about': return c.aboutHtml && (
        <Section key={b.type} title={t('public.about')} th={th}>
          <div className={`${th.card} p-5 md:p-7`}>
            {/* HTML đã qua SiteHtmlSanitizer ở máy chủ lúc lưu. */}
            <div className="site-prose" dangerouslySetInnerHTML={{ __html: c.aboutHtml }} />
          </div>
        </Section>
      )
      case 'products': return site.products.length > 0 && (
        <Section key={b.type} title={t('public.products')} th={th}>
          <Products products={site.products} theme={c.theme} th={th} canReserve={enabled('reserve')} onReserve={reserve} />
        </Section>
      )
      case 'reserve': return (
        <Section key={b.type} id="site-reserve" title={t('reserve.title')} th={th}>
          <ReserveForm slug={site.slug} products={site.products} theme={c.theme} th={th} preview={preview}
                       productId={reserveProduct} onProductChange={setReserveProduct} />
        </Section>
      )
      case 'posts': return (
        <Section key={b.type} title={t('public.posts')} th={th}>
          <PostList slug={site.slug} theme={c.theme} th={th} initial={site.posts} onNavigate={onNavigate} />
        </Section>
      )
      case 'results': return (
        <Section key={b.type} title={t('public.results')} th={th}>
          <Results theme={c.theme} th={th} />
        </Section>
      )
      case 'map': return site.shop && (
        <Section key={b.type} title={t('public.map')} th={th}>
          <div className={`${th.card} overflow-hidden`}>
            <Suspense fallback={<div className="h-64 animate-pulse bg-black/5" />}>
              <SiteMapBlock lat={site.shop.lat} lng={site.shop.lng} color={c.primaryColor} />
            </Suspense>
            <div className="p-4 flex items-center gap-3 flex-wrap">
              <p className={`flex-1 text-sm ${cardMuted(c.theme)}`}>{site.shop.address}</p>
              <a href={`https://www.google.com/maps/dir/?api=1&destination=${site.shop.lat},${site.shop.lng}`}
                 target="_blank" rel="noopener noreferrer" className={`${th.button} px-4 py-2 text-sm inline-flex items-center gap-1.5`}>
                <Icon name="directions" className="w-4 h-4" /> {t('public.directions')}
              </a>
            </div>
          </div>
        </Section>
      )
      case 'contact': return <Contact key={b.type} site={site} th={th} />
    }
  })

  return (
    <div className={`min-h-screen flex flex-col ${th.page}`} style={siteStyle(c.primaryColor)}>
      <header className="sticky top-0 z-20 backdrop-blur bg-black/5 border-b border-black/5">
        <div className="max-w-5xl mx-auto px-4 h-14 flex items-center gap-3">
          <button onClick={() => onNavigate({})} className="flex items-center gap-2 min-w-0">
            {c.logoUrl && <img src={siteImageUrl(c.logoUrl)} alt="" className="w-9 h-9 rounded-full object-cover" />}
            <span className={`truncate text-lg ${th.heading}`}>{c.name}</span>
          </button>
          <span className="flex-1" />
          {enabled('posts') && (
            <button onClick={() => onNavigate({ posts: true })} className={`text-sm font-semibold ${th.muted} hover:opacity-80`}>
              {t('public.posts')}
            </button>
          )}
          {c.phone && (
            <a href={`tel:${c.phone}`} className={`${th.button} px-3 py-1.5 text-sm inline-flex items-center gap-1.5`}>
              <Icon name="phone" className="w-4 h-4" /> <span className="hidden sm:inline">{c.phone}</span>
            </a>
          )}
        </div>
      </header>
      <main className="flex-1 w-full max-w-5xl mx-auto px-4 pb-12 space-y-10">{body}</main>
      <SiteFooter site={site} th={th} preview={preview} />
    </div>
  )
}

function Section({ id, title, th, children }: { id?: string; title: string; th: ThemeClasses; children: ReactNode }) {
  return (
    <section id={id} className="scroll-mt-20">
      <h2 className={`text-2xl mb-4 ${th.heading}`}>{title}</h2>
      {children}
    </section>
  )
}

function Hero({ site, th, canReserve, onReserve }: { site: PublicSite; th: ThemeClasses; canReserve: boolean; onReserve: () => void }) {
  const { t } = useTranslation('site')
  const c = site.config
  return (
    <section className={`-mx-4 md:mx-0 md:mt-6 md:rounded-3xl overflow-hidden relative ${th.hero}`}>
      {c.coverUrl && (
        <>
          <img src={siteImageUrl(c.coverUrl)} alt="" className="absolute inset-0 w-full h-full object-cover" />
          <div className="absolute inset-0 bg-gradient-to-t from-black/75 via-black/35 to-black/10" />
        </>
      )}
      <div className={`relative px-6 py-14 md:py-20 text-center ${c.coverUrl ? 'text-white' : ''}`}>
        {c.logoUrl && <img src={siteImageUrl(c.logoUrl)} alt="" className="w-24 h-24 rounded-full object-cover mx-auto mb-4 ring-4 ring-white/70 shadow-lg" />}
        <h1 className="text-3xl md:text-5xl font-extrabold tracking-tight">{c.name}</h1>
        {c.tagline && <p className="mt-3 text-lg opacity-90 max-w-xl mx-auto">{c.tagline}</p>}
        <div className="mt-6 flex justify-center gap-3 flex-wrap">
          {canReserve && (
            <button onClick={onReserve} className={`${th.button} px-5 py-2.5 inline-flex items-center gap-2`}>
              <Icon name="ticket" className="w-5 h-5" /> {t('public.reserveCta')}
            </button>
          )}
          {c.phone && (
            <a href={`tel:${c.phone}`} className="rounded-xl px-5 py-2.5 font-semibold bg-white/90 text-stone-900 inline-flex items-center gap-2 hover:bg-white">
              <Icon name="phone" className="w-5 h-5" /> {t('public.call')}
            </a>
          )}
          {c.zalo && (
            <a href={`https://zalo.me/${c.zalo}`} target="_blank" rel="noopener noreferrer"
               className="rounded-xl px-5 py-2.5 font-semibold bg-[#0068ff] text-white inline-flex items-center gap-2 hover:brightness-110">
              {t('public.zalo')}
            </a>
          )}
        </div>
      </div>
    </section>
  )
}

function Products({ products, theme, th, canReserve, onReserve }: {
  products: SiteProduct[]; theme: SiteTheme; th: ThemeClasses; canReserve: boolean; onReserve: (p: SiteProduct) => void
}) {
  const { t } = useTranslation('site')
  return (
    <div className="grid gap-4 grid-cols-1 sm:grid-cols-2 lg:grid-cols-3">
      {products.map(p => {
        const soldOut = p.stock === 0
        return (
          <article key={p.id} className={`${th.card} overflow-hidden flex flex-col`}>
            {p.imageUrl && <img src={siteImageUrl(p.imageUrl)} alt="" className="w-full aspect-[4/3] object-cover" loading="lazy" />}
            <div className="p-4 flex-1 flex flex-col gap-1.5">
              <span className={`text-xs font-semibold uppercase tracking-wide ${cardMuted(theme)}`}>{t(`kinds.${p.kind}`)}</span>
              <h3 className={`text-lg ${cardHeading(theme)}`}>{p.name}</h3>
              {p.description && <p className={`text-sm whitespace-pre-line ${cardMuted(theme)}`}>{p.description}</p>}
              <div className="mt-auto pt-3 flex items-center gap-2">
                <span className="text-lg font-bold text-[var(--sp)] flex-1">
                  {p.price != null ? formatVnd(p.price, currentLocale()) : t('public.contactPrice')}
                </span>
                {soldOut ? <span className="text-sm font-semibold text-red-600">{t('public.soldOut')}</span>
                  : p.stock != null && <span className={`text-xs ${cardMuted(theme)}`}>{t('public.stockLeft', { count: p.stock })}</span>}
              </div>
              {canReserve && !soldOut && (
                <button onClick={() => onReserve(p)} className={`${th.button} mt-2 px-4 py-2 text-sm`}>{t('public.reserveThis')}</button>
              )}
            </div>
          </article>
        )
      })}
    </div>
  )
}

function ReserveForm({ slug, products, theme, th, preview, productId, onProductChange }: {
  slug: string; products: SiteProduct[]; theme: SiteTheme; th: ThemeClasses; preview?: boolean
  productId: number | null; onProductChange: (id: number | null) => void
}) {
  const { t } = useTranslation('site')
  const [name, setName] = useState('')
  const [phone, setPhone] = useState('')
  const [qty, setQty] = useState(1)
  const [note, setNote] = useState('')
  const [busy, setBusy] = useState(false)
  const [done, setDone] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const available = products.filter(p => p.stock !== 0)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (preview) return
    setBusy(true)
    setError(null)
    try {
      await reserveOnSite(slug, { productId, customerName: name.trim(), phone: phone.trim(), quantity: qty, note: note.trim() || null })
      setDone(true)
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  const label = `block text-sm font-semibold mb-1 ${theme === 'lucky' ? 'text-stone-700' : ''}`
  const input = `${th.input} w-full px-3 py-2`
  return (
    <form onSubmit={submit} className={`${th.card} p-5 md:p-7 space-y-4 max-w-2xl`}>
      <p className="flex gap-2 text-sm rounded-xl bg-amber-50 text-amber-900 ring-1 ring-amber-200 p-3">
        <Icon name="warn" className="w-5 h-5 shrink-0" /> {t('reserve.notice')}
      </p>
      {done ? (
        <p className="flex items-center gap-2 font-semibold text-green-700"><Icon name="ok" className="w-5 h-5" /> {t('reserve.done')}</p>
      ) : (
        <>
          {available.length > 0 && (
            <label className="block">
              <span className={label}>{t('reserve.product')}</span>
              <select value={productId ?? ''} onChange={e => onProductChange(e.target.value ? Number(e.target.value) : null)} className={input}>
                <option value="">{t('reserve.anyProduct')}</option>
                {available.map(p => (
                  <option key={p.id} value={p.id}>
                    {p.name}{p.price != null ? ` — ${formatVnd(p.price, currentLocale())}` : ''}
                  </option>
                ))}
              </select>
            </label>
          )}
          <div className="grid sm:grid-cols-2 gap-4">
            <label className="block">
              <span className={label}>{t('reserve.name')}</span>
              <input value={name} onChange={e => setName(e.target.value)} required minLength={2} maxLength={40} className={input} autoComplete="name" />
            </label>
            <label className="block">
              <span className={label}>{t('reserve.phone')}</span>
              <input value={phone} onChange={e => setPhone(e.target.value)} required type="tel" inputMode="tel" maxLength={20}
                     className={input} autoComplete="tel" />
            </label>
          </div>
          <label className="block w-32">
            <span className={label}>{t('reserve.quantity')}</span>
            <input type="number" min={1} max={100} value={qty} onChange={e => setQty(Math.max(1, Number(e.target.value) || 1))} className={input} />
          </label>
          <label className="block">
            <span className={label}>{t('reserve.note')}</span>
            <textarea value={note} onChange={e => setNote(e.target.value)} maxLength={300} rows={3} className={input} />
          </label>
          {error && <p className="text-sm text-red-600">{error}</p>}
          {preview && <p className={`text-sm ${cardMuted(theme)}`}>{t('reserve.previewDisabled')}</p>}
          <button type="submit" disabled={busy || preview} className={`${th.button} px-5 py-2.5 disabled:opacity-50`}>
            {busy ? t('reserve.sending') : t('reserve.submit')}
          </button>
        </>
      )}
    </form>
  )
}

function PostList({ slug, theme, th, initial, onNavigate }: {
  slug: string; theme: SiteTheme; th: ThemeClasses; initial: SitePostSummary[] | null; onNavigate: Nav
}) {
  const { t } = useTranslation('site')
  // initial = vài bài mới nhất có sẵn trong trang chủ; null = trang "Tất cả bài" tự tải.
  const [posts, setPosts] = useState<SitePostSummary[] | null>(initial)
  const [error, setError] = useState<string | null>(null)
  useEffect(() => {
    if (initial) return
    getPublicSitePosts(slug).then(setPosts).catch(e => setError((e as Error).message))
  }, [slug, initial])

  if (error) return <p className="text-red-600 pt-8">{error}</p>
  if (!posts) return <div className="h-40 animate-pulse rounded-2xl bg-black/5 mt-8" />
  return (
    <div className={initial ? '' : 'pt-8'}>
      {!initial && <h1 className={`text-3xl mb-6 ${th.heading}`}>{t('public.posts')}</h1>}
      {posts.length === 0 ? <p className={th.muted}>{t('public.noPosts')}</p> : (
        <div className="grid gap-4 sm:grid-cols-2">
          {posts.map(p => (
            <button key={p.id} onClick={() => onNavigate({ postId: p.id })} className={`${th.card} overflow-hidden text-left flex flex-col hover:-translate-y-0.5 transition`}>
              {p.coverUrl && <img src={siteImageUrl(p.coverUrl)} alt="" className="w-full aspect-video object-cover" loading="lazy" />}
              <span className="p-4 block">
                <span className={`block text-lg ${cardHeading(theme)}`}>{p.title}</span>
                {p.publishedAt && <span className={`block text-xs mt-1 ${cardMuted(theme)}`}>{formatDate(p.publishedAt)}</span>}
                <span className={`block text-sm mt-2 line-clamp-3 ${cardMuted(theme)}`}>{p.excerpt}</span>
              </span>
            </button>
          ))}
        </div>
      )}
      {initial && initial.length > 0 && (
        <button onClick={() => onNavigate({ posts: true })} className={`${th.ghostButton} mt-4 px-4 py-2 text-sm`}>{t('public.allPosts')}</button>
      )}
    </div>
  )
}

function PostView({ site, th, id, onNavigate }: { site: PublicSite; th: ThemeClasses; id: string; onNavigate: Nav }) {
  const { t } = useTranslation('site')
  const [post, setPost] = useState<SitePost | null>(null)
  const [error, setError] = useState<string | null>(null)
  useEffect(() => {
    getPublicSitePost(site.slug, id).then(setPost).catch(e => setError((e as Error).message))
  }, [site.slug, id])

  return (
    <article className="pt-6 max-w-3xl mx-auto">
      <button onClick={() => onNavigate({ posts: true })} className={`text-sm font-semibold inline-flex items-center gap-1 ${th.muted}`}>
        <Icon name="back" className="w-4 h-4" /> {t('public.back')}
      </button>
      {error ? <p className="mt-6 text-red-600">{error}</p> : !post ? <div className="mt-6 h-64 animate-pulse rounded-2xl bg-black/5" /> : (
        <div className={`${th.card} mt-4 overflow-hidden`}>
          {post.coverUrl && <img src={siteImageUrl(post.coverUrl)} alt="" className="w-full max-h-96 object-cover" />}
          <div className="p-5 md:p-8">
            <h1 className={`text-3xl ${cardHeading(site.config.theme)}`}>{post.title}</h1>
            {post.publishedAt && <p className={`mt-1 text-sm ${cardMuted(site.config.theme)}`}>{formatDate(post.publishedAt)}</p>}
            <div className="site-prose mt-6" dangerouslySetInnerHTML={{ __html: post.contentHtml }} />
          </div>
        </div>
      )}
    </article>
  )
}

/** Giải đặc biệt các đài của ngày xổ gần nhất — kéo khách quay lại site mỗi ngày. */
function Results({ theme, th }: { theme: SiteTheme; th: ThemeClasses }) {
  const { t } = useTranslation('site')
  const [rows, setRows] = useState<{ date: string; items: { province: string; special: string }[] } | null>(null)
  const [failed, setFailed] = useState(false)
  useEffect(() => {
    (async () => {
      const draws = await getAvailableDraws()
      const latest = draws.reduce<typeof draws[number] | null>((a, d) => !a || d.drawDate > a.drawDate ? d : a, null)
      if (!latest) { setRows({ date: '', items: [] }); return }
      const results = await Promise.all(latest.provinces.map(p => getProvinceResult(latest.drawDate, p).catch(() => null)))
      setRows({
        date: latest.drawDate,
        items: results.filter(r => r != null).map(r => ({
          province: r.province, special: r.prizes.find(x => x.tier === 'DB')?.numbers.join(', ') ?? '—',
        })),
      })
    })().catch(() => setFailed(true))
  }, [])

  if (failed) return null
  return (
    <div className={`${th.card} p-5`}>
      {!rows ? <div className="h-24 animate-pulse rounded-xl bg-black/5" /> : rows.items.length === 0 ? (
        <p className={cardMuted(theme)}>{t('public.resultsEmpty')}</p>
      ) : (
        <>
          <p className={`text-sm mb-3 ${cardMuted(theme)}`}>{formatDate(rows.date)} · {t('public.resultsSpecial')}</p>
          <ul className="grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
            {rows.items.map(r => (
              <li key={r.province} className="flex items-center justify-between gap-3 rounded-xl bg-black/[0.03] px-3 py-2">
                <span className="font-semibold">{provinceName(r.province)}</span>
                <span className="font-mono text-lg font-bold text-[var(--sp)]">{r.special}</span>
              </li>
            ))}
          </ul>
          <a href="/ket-qua" className={`${th.ghostButton} inline-block mt-4 px-4 py-2 text-sm`}>{t('public.resultsMore')}</a>
        </>
      )}
    </div>
  )
}

const hhmm = (min: number) => `${String(Math.floor(min / 60)).padStart(2, '0')}:${String(min % 60).padStart(2, '0')}`

function Contact({ site, th }: { site: PublicSite; th: ThemeClasses }) {
  const { t } = useTranslation('site')
  const c = site.config
  const address = c.address ?? site.shop?.address
  const hours = c.openingHours
    ?? (site.shop?.opensAtMin != null && site.shop.closesAtMin != null ? `${hhmm(site.shop.opensAtMin)} – ${hhmm(site.shop.closesAtMin)}` : null)
  const rows: [string, ReactNode][] = []
  if (address) rows.push([t('public.address'), address])
  if (hours) rows.push([t('public.hours'), hours])
  if (c.phone) rows.push([t('public.phone'), <a href={`tel:${c.phone}`} className="underline">{c.phone}</a>])
  if (c.zalo) rows.push([t('public.zalo'), <a href={`https://zalo.me/${c.zalo}`} target="_blank" rel="noopener noreferrer" className="underline">{c.zalo}</a>])
  if (c.facebook) rows.push([t('public.facebook'), <a href={c.facebook} target="_blank" rel="noopener noreferrer nofollow" className="underline break-all">{c.facebook}</a>])
  if (rows.length === 0) return null
  return (
    <Section title={t('public.contact')} th={th}>
      <dl className={`${th.card} p-5 md:p-7 grid sm:grid-cols-[10rem_1fr] gap-x-6 gap-y-3`}>
        {rows.map(([k, v]) => (
          <div key={k} className="contents">
            <dt className={`text-sm font-semibold ${cardMuted(c.theme)}`}>{k}</dt>
            <dd>{v}</dd>
          </div>
        ))}
      </dl>
    </Section>
  )
}

function SiteFooter({ site, th, preview }: { site: PublicSite; th: ThemeClasses; preview?: boolean }) {
  const { t } = useTranslation('site')
  const [open, setOpen] = useState(false)
  const [reason, setReason] = useState<SiteReportReason>('Scam')
  const [note, setNote] = useState('')
  const [msg, setMsg] = useState<string | null>(null)

  const send = async () => {
    try {
      await reportSite(site.slug, reason, note.trim() || null)
      setMsg(t('public.reportDone'))
      setOpen(false)
    } catch (e) {
      setMsg((e as Error).message)
    }
  }

  return (
    <footer className={`border-t border-black/10 py-6 text-sm ${th.muted}`}>
      <div className="max-w-5xl mx-auto px-4 flex flex-wrap items-center gap-x-4 gap-y-2">
        <span className="flex-1">© {site.config.name}</span>
        <a href="/" className="hover:underline">{t('madeWith')}</a>
        {!preview && <button onClick={() => setOpen(o => !o)} className="hover:underline">{t('public.report')}</button>}
      </div>
      {open && (
        <div className="max-w-5xl mx-auto px-4 mt-3">
          <div className="rounded-xl bg-white text-stone-800 p-4 space-y-3 max-w-md ring-1 ring-black/10">
            <label className="block">
              <span className="block text-sm font-semibold mb-1">{t('public.reportReason')}</span>
              <select value={reason} onChange={e => setReason(e.target.value as SiteReportReason)} className="w-full rounded-lg border border-stone-300 px-2 py-1.5">
                {SITE_REPORT_REASONS.map(r => <option key={r} value={r}>{t(`public.reportReasons.${r}`)}</option>)}
              </select>
            </label>
            <label className="block">
              <span className="block text-sm font-semibold mb-1">{t('public.reportNote')}</span>
              <textarea value={note} onChange={e => setNote(e.target.value)} maxLength={300} rows={2} className="w-full rounded-lg border border-stone-300 px-2 py-1.5" />
            </label>
            <button onClick={send} className="rounded-lg bg-stone-900 text-white px-4 py-1.5 font-semibold">{t('public.send')}</button>
          </div>
        </div>
      )}
      {msg && <p className="max-w-5xl mx-auto px-4 mt-2">{msg}</p>}
    </footer>
  )
}
