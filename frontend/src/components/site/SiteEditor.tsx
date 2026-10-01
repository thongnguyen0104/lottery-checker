import { useEffect, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { DndContext, KeyboardSensor, PointerSensor, closestCenter, useSensor, useSensors, type DragEndEvent } from '@dnd-kit/core'
import { SortableContext, arrayMove, sortableKeyboardCoordinates, useSortable, verticalListSortingStrategy } from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import Icon from '../Icon'
import { primaryBtn, secondaryBtn } from '../map/Sheet'
import SiteImagePicker from './SiteImagePicker'
import RichTextEditor from './RichTextEditor'
import SiteRenderer from './SiteRenderer'
import SiteProductsTab from './SiteProductsTab'
import SitePostsTab from './SitePostsTab'
import SiteReservationsTab from './SiteReservationsTab'
import { PRESET_COLORS } from './themes'
import {
  checkSiteSlug, createMySite, getMySite, getPublicSite, publishSite, saveSiteDraft,
  type MySite, type PublicSite, type SiteBlock, type SiteConfig, type SiteTheme,
} from '../../api/client'
import { sitePath } from '../../views'

const THEME_IDS: SiteTheme[] = ['classic', 'modern', 'lucky', 'minimal']
type Tab = 'settings' | 'blocks' | 'products' | 'posts' | 'reservations'
const TABS: Tab[] = ['settings', 'blocks', 'products', 'posts', 'reservations']

/** Bản nháp đang sửa trên máy — so với bản đã lưu để biết có thay đổi chưa lưu. */
type Draft = { slug: string; config: SiteConfig; shopId: string | null }
const draftOf = (s: MySite): Draft => ({ slug: s.slug, config: s.draft, shopId: s.shop?.id ?? null })

/** Màn "Website của tôi": tạo site lần đầu, rồi sửa nháp / xem trước / đăng + quản lý sản phẩm, bài, giữ vé. */
export default function SiteEditor() {
  const [site, setSite] = useState<MySite | null | undefined>(undefined)
  const [pending, setPending] = useState(0)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    getMySite().then(r => { setSite(r.site); setPending(r.pendingReservations) }).catch(e => setError((e as Error).message))
  }, [])

  if (error) return <div className="card p-6 text-center text-bad">{error}</div>
  if (site === undefined) return <div className="card h-64 animate-pulse" aria-busy />
  if (site === null) return <CreateSite onCreated={setSite} />
  return <Editor site={site} setSite={setSite} pending={pending} setPending={setPending} />
}

function useSlugCheck(slug: string, current: string | null) {
  // Kết quả gắn với slug đã hỏi — gõ tiếp thì kết quả cũ tự không khớp, khỏi phải xoá trong effect.
  const [state, setState] = useState<{ slug: string; ok: boolean; msg: string } | null>(null)
  useEffect(() => {
    if (!slug || slug === current) return
    const ctl = new AbortController()
    const id = setTimeout(() => {
      checkSiteSlug(slug, ctl.signal).then(r => setState({ slug, ok: r.available, msg: r.error ?? '' })).catch(() => {})
    }, 350)
    return () => { clearTimeout(id); ctl.abort() }
  }, [slug, current])
  return state?.slug === slug && slug !== current ? state : null
}

/** Gõ tiếng Việt có dấu → slug không dấu. */
const slugify = (s: string) => s.normalize('NFD').replace(/\p{Diacritic}/gu, '').replace(/đ/gi, 'd')
  .toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+/, '').slice(0, 40)

function SlugInput({ value, onChange, current }: { value: string; onChange: (v: string) => void; current: string | null }) {
  const { t } = useTranslation('site')
  const check = useSlugCheck(value, current)
  return (
    <label className="block">
      <span className="block text-sm font-semibold mb-1">{t('editor.slug')}</span>
      <div className="flex items-center rounded-xl border border-line bg-surface overflow-hidden focus-within:ring-2 focus-within:ring-brand-500/40">
        <span className="pl-3 text-ink-faint text-sm select-none">{location.host}/s/</span>
        <input value={value} onChange={e => onChange(slugify(e.target.value))} className="flex-1 min-w-0 bg-transparent px-1 py-2 focus:outline-none"
               maxLength={40} required />
      </div>
      {check && (
        <span className={`block mt-1 text-xs ${check.ok ? 'text-ok' : 'text-bad'}`}>
          {check.ok ? t('editor.slugAvailable') : check.msg || t('editor.slugTaken')}
        </span>
      )}
    </label>
  )
}

function CreateSite({ onCreated }: { onCreated: (s: MySite) => void }) {
  const { t } = useTranslation('site')
  const [slug, setSlug] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const create = async () => {
    setBusy(true)
    setError(null)
    try {
      onCreated(await createMySite(slug.replace(/-+$/, '')))
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }
  return (
    <div className="card p-6 max-w-lg mx-auto space-y-4">
      <div className="text-center space-y-2">
        <Icon name="site" className="w-10 h-10 mx-auto text-brand-700 dark:text-brand-400" />
        <h1 className="text-xl font-bold">{t('editor.createTitle')}</h1>
        <p className="text-sm text-ink-soft">{t('editor.createBody')}</p>
      </div>
      <SlugInput value={slug} onChange={setSlug} current={null} />
      {error && <p className="text-sm text-bad">{error}</p>}
      <button onClick={create} disabled={busy || slug.length < 3} className={`${primaryBtn} w-full`}>{t('editor.create')}</button>
    </div>
  )
}

function Editor({ site, setSite, pending, setPending }: {
  site: MySite; setSite: (s: MySite) => void; pending: number; setPending: (n: number) => void
}) {
  const { t } = useTranslation('site')
  const [tab, setTab] = useState<Tab>('settings')
  const [draft, setDraft] = useState<Draft>(() => draftOf(site))
  const [busy, setBusy] = useState<'save' | 'publish' | null>(null)
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null)
  const [preview, setPreview] = useState<PublicSite | null>(null)
  const dirty = JSON.stringify(draft) !== JSON.stringify(draftOf(site))

  const setConfig = <K extends keyof SiteConfig>(k: K, v: SiteConfig[K]) => setDraft(d => ({ ...d, config: { ...d.config, [k]: v } }))

  // Đang có thay đổi chưa lưu mà đóng tab / tải lại → trình duyệt hỏi lại.
  useEffect(() => {
    if (!dirty) return
    const onUnload = (e: BeforeUnloadEvent) => { e.preventDefault() }
    window.addEventListener('beforeunload', onUnload)
    return () => window.removeEventListener('beforeunload', onUnload)
  }, [dirty])

  const save = async (): Promise<MySite | null> => {
    setBusy('save')
    setMsg(null)
    try {
      const s = await saveSiteDraft({ ...draft, slug: draft.slug.replace(/-+$/, '') })
      setSite(s)
      setDraft(draftOf(s))
      setMsg({ ok: true, text: t('editor.saved') })
      return s
    } catch (e) {
      setMsg({ ok: false, text: (e as Error).message })
      return null
    } finally {
      setBusy(null)
    }
  }

  const publish = async () => {
    if (dirty && !await save()) return
    setBusy('publish')
    try {
      setSite(await publishSite())
      setMsg({ ok: true, text: t('editor.published') })
    } catch (e) {
      setMsg({ ok: false, text: (e as Error).message })
    } finally {
      setBusy(null)
    }
  }

  // Xem trước: sản phẩm / bài lấy từ máy chủ (bản nháp), cấu hình lấy từ bản đang sửa trên máy (chưa cần lưu).
  const openPreview = async () => {
    try {
      const s = await getPublicSite(site.slug, true)
      const shop = site.myShops.find(x => x.id === draft.shopId) ?? null
      setPreview({ ...s, config: draft.config, shop })
    } catch (e) {
      setMsg({ ok: false, text: (e as Error).message })
    }
  }

  const url = `${location.origin}${sitePath(site.slug)}`
  const status = site.status === 'Hidden' ? { tone: 'text-bad', text: t('editor.hidden') }
    : dirty ? { tone: 'text-warn', text: t('editor.unsaved') }
    : !site.published ? { tone: 'text-warn', text: t('editor.notPublished') }
    : site.hasUnpublishedChanges ? { tone: 'text-warn', text: t('editor.unpublished') }
    : { tone: 'text-ok', text: t('editor.upToDate') }

  return (
    <div className="space-y-4">
      <div className="card p-4 flex flex-wrap items-center gap-3">
        <div className="flex-1 min-w-0">
          <h1 className="text-xl font-bold flex items-center gap-2"><Icon name="site" className="w-6 h-6" /> {t('editor.title')}</h1>
          <p className={`text-sm font-medium ${status.tone}`}>{status.text}</p>
          {site.published && (
            <div className="flex items-center gap-2 text-sm mt-1">
              <a href={sitePath(site.slug)} target="_blank" rel="noopener" className="text-brand-700 dark:text-brand-400 underline truncate">{url}</a>
              <CopyButton text={url} />
            </div>
          )}
        </div>
        <div className="flex flex-wrap gap-2">
          <button onClick={openPreview} className={secondaryBtn}><Icon name="show" className="w-4 h-4" /> {t('editor.preview')}</button>
          <button onClick={save} disabled={!dirty || busy != null} className={secondaryBtn}>
            {busy === 'save' ? t('editor.saving') : t('editor.save')}
          </button>
          <button onClick={publish} disabled={busy != null || (!dirty && !!site.published && !site.hasUnpublishedChanges)}
                  className={`${primaryBtn} inline-flex items-center gap-1.5 !py-2`}>
            <Icon name="publish" className="w-4 h-4" /> {busy === 'publish' ? t('editor.publishing') : t('editor.publish')}
          </button>
        </div>
        {msg && <p className={`basis-full text-sm ${msg.ok ? 'text-ok' : 'text-bad'}`}>{msg.text}</p>}
      </div>

      <div role="tablist" className="flex gap-1 overflow-x-auto pb-1">
        {TABS.map(x => (
          <button key={x} role="tab" aria-selected={tab === x} onClick={() => setTab(x)}
                  className={`shrink-0 px-4 py-2 rounded-xl text-sm font-semibold transition relative
                              ${tab === x ? 'bg-brand-500/15 text-brand-700 dark:text-brand-400' : 'text-ink-soft hover:bg-muted'}`}>
            {t(`editor.tabs.${x}`)}
            {x === 'reservations' && pending > 0 && (
              <span className="ml-1.5 inline-flex min-w-5 h-5 px-1 rounded-full bg-bad text-white text-xs items-center justify-center">{pending}</span>
            )}
          </button>
        ))}
      </div>

      {tab === 'settings' && <SettingsTab site={site} draft={draft} setDraft={setDraft} setConfig={setConfig} />}
      {tab === 'blocks' && <BlocksTab blocks={draft.config.blocks} onChange={b => setConfig('blocks', b)} />}
      {tab === 'products' && <SiteProductsTab />}
      {tab === 'posts' && <SitePostsTab published={!!site.published} />}
      {tab === 'reservations' && <SiteReservationsTab onPendingChange={setPending} />}

      {preview && (
        <div className="fixed inset-0 z-50 overflow-auto bg-white">
          <div className="sticky top-0 z-30 flex items-center gap-3 px-4 py-2 bg-amber-100 text-amber-900 text-sm">
            <span className="flex-1">{t('previewBanner')}</span>
            <button onClick={() => setPreview(null)} className="rounded-lg bg-amber-900 text-white px-3 py-1 font-semibold">{t('editor.closePreview')}</button>
          </div>
          <PreviewFrame site={preview} />
        </div>
      )}
    </div>
  )
}

/** Xem trước có điều hướng nội bộ (trang chủ / bài) nhưng không đổi URL của trình duyệt. */
function PreviewFrame({ site }: { site: PublicSite }) {
  const [route, setRoute] = useState({ slug: site.slug, posts: false, postId: null as string | null })
  return <SiteRenderer site={site} route={route} preview
                       onNavigate={to => setRoute({ slug: site.slug, posts: !!to.posts || !!to.postId, postId: to.postId ?? null })} />
}

function CopyButton({ text }: { text: string }) {
  const { t } = useTranslation('site')
  const [done, setDone] = useState(false)
  return (
    <button onClick={() => navigator.clipboard?.writeText(text).then(() => { setDone(true); setTimeout(() => setDone(false), 1500) })}
            className="text-ink-soft hover:text-ink" title={t('editor.copyLink')} aria-label={t('editor.copyLink')}>
      <Icon name={done ? 'check' : 'copy'} className="w-4 h-4" />
    </button>
  )
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <label className="block">
      <span className="block text-sm font-semibold mb-1">{label}</span>
      {children}
    </label>
  )
}

function SettingsTab({ site, draft, setDraft, setConfig }: {
  site: MySite; draft: Draft; setDraft: (f: (d: Draft) => Draft) => void
  setConfig: <K extends keyof SiteConfig>(k: K, v: SiteConfig[K]) => void
}) {
  const { t } = useTranslation('site')
  const c = draft.config
  const text = (k: 'name' | 'tagline' | 'phone' | 'zalo' | 'facebook' | 'address' | 'openingHours', max: number, extra: object = {}) => (
    <input className="field" value={c[k] ?? ''} maxLength={max} {...extra}
           onChange={e => setConfig(k, (e.target.value || (k === 'name' ? '' : null)) as never)} />
  )
  // Nội dung giới thiệu lúc mở tab: TipTap chỉ đọc content lúc khởi tạo, sau đó tự giữ.
  const [aboutInit] = useState(() => c.aboutHtml ?? '')

  return (
    <div className="grid gap-4 lg:grid-cols-2">
      <div className="card p-5 space-y-4">
        <SlugInput value={draft.slug} onChange={slug => setDraft(d => ({ ...d, slug }))} current={site.slug} />
        <Field label={t('editor.name')}>{text('name', 60, { required: true })}</Field>
        <Field label={t('editor.tagline')}>{text('tagline', 120)}</Field>
        <div>
          <span className="block text-sm font-semibold mb-1">{t('editor.theme')}</span>
          <div className="grid grid-cols-2 gap-2">
            {THEME_IDS.map(id => (
              <button key={id} type="button" onClick={() => setConfig('theme', id)}
                      className={`rounded-xl p-3 text-left text-sm font-semibold border-2 transition
                                  ${c.theme === id ? 'border-brand-500' : 'border-line hover:border-ink-faint'}`}>
                <span className={`block h-8 rounded-lg mb-2 ${THEME_SWATCH[id]}`} />
                {t(`themes.${id}`)}
              </button>
            ))}
          </div>
        </div>
        <div>
          <span className="block text-sm font-semibold mb-1">{t('editor.color')}</span>
          <div className="flex flex-wrap items-center gap-2">
            {PRESET_COLORS.map(col => (
              <button key={col} type="button" onClick={() => setConfig('primaryColor', col)} aria-label={col}
                      className={`w-8 h-8 rounded-full ring-offset-2 ring-offset-surface ${c.primaryColor === col ? 'ring-2 ring-ink' : ''}`}
                      style={{ background: col }} />
            ))}
            <input type="color" value={c.primaryColor} onChange={e => setConfig('primaryColor', e.target.value)}
                   className="w-10 h-8 rounded cursor-pointer bg-transparent" aria-label={t('editor.color')} />
          </div>
        </div>
        <SiteImagePicker label={t('editor.logo')} url={c.logoUrl} onChange={u => setConfig('logoUrl', u)} round />
        <SiteImagePicker label={t('editor.cover')} url={c.coverUrl} onChange={u => setConfig('coverUrl', u)} />
      </div>

      <div className="card p-5 space-y-4">
        <div className="grid sm:grid-cols-2 gap-4">
          <Field label={t('editor.phone')}>{text('phone', 20, { type: 'tel', inputMode: 'tel' })}</Field>
          <Field label={t('editor.zalo')}>{text('zalo', 20, { type: 'tel', inputMode: 'tel' })}</Field>
        </div>
        <Field label={t('editor.facebook')}>{text('facebook', 200, { type: 'url', placeholder: 'https://facebook.com/…' })}</Field>
        <Field label={t('editor.address')}>{text('address', 200)}</Field>
        <Field label={t('editor.hours')}>{text('openingHours', 80, { placeholder: t('editor.hoursPlaceholder') })}</Field>
        <Field label={t('editor.shop')}>
          <select className="field" value={draft.shopId ?? ''} onChange={e => setDraft(d => ({ ...d, shopId: e.target.value || null }))}>
            <option value="">{t('editor.noShop')}</option>
            {site.myShops.map(s => <option key={s.id} value={s.id}>{s.name} — {s.address}</option>)}
          </select>
          {site.myShops.length === 0 && <span className="block mt-1 text-xs text-ink-faint">{t('editor.noShopsHint')}</span>}
        </Field>
        <div>
          <span className="block text-sm font-semibold mb-1">{t('editor.about')}</span>
          <RichTextEditor value={aboutInit} onChange={html => setConfig('aboutHtml', html || null)} />
        </div>
      </div>
    </div>
  )
}

const THEME_SWATCH: Record<SiteTheme, string> = {
  classic: 'bg-[#fbf7f0] ring-1 ring-stone-300',
  modern: 'bg-slate-900',
  lucky: 'bg-gradient-to-r from-[#9b1c1c] to-amber-400',
  minimal: 'bg-white ring-1 ring-neutral-300',
}

function BlocksTab({ blocks, onChange }: { blocks: SiteBlock[]; onChange: (b: SiteBlock[]) => void }) {
  const { t } = useTranslation('site')
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 5 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  )
  const onDragEnd = ({ active, over }: DragEndEvent) => {
    if (!over || active.id === over.id) return
    const from = blocks.findIndex(b => b.type === active.id)
    const to = blocks.findIndex(b => b.type === over.id)
    onChange(arrayMove(blocks, from, to))
  }
  const move = (i: number, d: -1 | 1) => { const j = i + d; if (j >= 0 && j < blocks.length) onChange(arrayMove(blocks, i, j)) }
  const toggle = (i: number) => onChange(blocks.map((b, k) => k === i ? { ...b, enabled: !b.enabled } : b))

  return (
    <div className="card p-5 max-w-xl space-y-3">
      <p className="text-sm text-ink-soft">{t('editor.blocksHint')}</p>
      <DndContext sensors={sensors} collisionDetection={closestCenter} onDragEnd={onDragEnd}>
        <SortableContext items={blocks.map(b => b.type)} strategy={verticalListSortingStrategy}>
          <ul className="space-y-2">
            {blocks.map((b, i) => (
              <BlockRow key={b.type} block={b} first={i === 0} last={i === blocks.length - 1}
                        onToggle={() => toggle(i)} onUp={() => move(i, -1)} onDown={() => move(i, 1)} />
            ))}
          </ul>
        </SortableContext>
      </DndContext>
    </div>
  )
}

function BlockRow({ block, first, last, onToggle, onUp, onDown }: {
  block: SiteBlock; first: boolean; last: boolean; onToggle: () => void; onUp: () => void; onDown: () => void
}) {
  const { t } = useTranslation('site')
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({ id: block.type })
  return (
    <li ref={setNodeRef} style={{ transform: CSS.Transform.toString(transform), transition }}
        className={`flex items-center gap-2 rounded-xl border border-line bg-surface px-2 py-2 ${isDragging ? 'shadow-lg z-10 relative' : ''}
                    ${block.enabled ? '' : 'opacity-60'}`}>
      <button {...attributes} {...listeners} className="p-1 text-ink-faint cursor-grab touch-none" aria-label={t(`blocks.${block.type}`)}>
        <Icon name="grip" className="w-5 h-5" />
      </button>
      <span className="flex-1 font-medium">{t(`blocks.${block.type}`)}</span>
      <button onClick={onUp} disabled={first} className="p-1 text-ink-soft disabled:opacity-30" aria-label={t('editor.moveUp')}>
        <Icon name="down" className="w-4 h-4 rotate-180" />
      </button>
      <button onClick={onDown} disabled={last} className="p-1 text-ink-soft disabled:opacity-30" aria-label={t('editor.moveDown')}>
        <Icon name="down" className="w-4 h-4" />
      </button>
      <label className="relative inline-flex items-center cursor-pointer">
        <input type="checkbox" checked={block.enabled} onChange={onToggle} className="sr-only peer" />
        <span className="w-10 h-6 rounded-full bg-line peer-checked:bg-brand-500 transition
                         after:absolute after:top-1 after:left-1 after:w-4 after:h-4 after:rounded-full after:bg-white after:transition
                         peer-checked:after:translate-x-4" />
      </label>
    </li>
  )
}
