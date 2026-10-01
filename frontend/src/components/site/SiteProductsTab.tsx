import { useEffect, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import Icon from '../Icon'
import Sheet, { primaryBtn, secondaryBtn } from '../map/Sheet'
import ConfirmDialog from '../ConfirmDialog'
import SiteImagePicker from './SiteImagePicker'
import { formatVnd } from './themes'
import { currentLocale } from '../../i18n'
import {
  deleteSiteProduct, getMySiteProducts, saveSiteProduct, siteImageUrl, SITE_PRODUCT_KINDS,
  type SiteProduct, type SiteProductInput,
} from '../../api/client'

const EMPTY: SiteProductInput = { name: '', description: null, price: null, kind: 'Traditional', imageUrl: null, stock: null, isVisible: true, sortOrder: 0 }

/** Sản phẩm trưng bày trên site — lưu thẳng (không qua nháp), ẩn/hiện từng cái. */
export default function SiteProductsTab() {
  const { t } = useTranslation('site')
  const [items, setItems] = useState<SiteProduct[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [editing, setEditing] = useState<{ id: number | null; input: SiteProductInput } | null>(null)
  const [deleting, setDeleting] = useState<SiteProduct | null>(null)

  useEffect(() => { getMySiteProducts().then(setItems).catch(e => setError((e as Error).message)) }, [])

  const remove = async (p: SiteProduct) => {
    try {
      await deleteSiteProduct(p.id)
      setItems(list => list?.filter(x => x.id !== p.id) ?? null)
    } catch (e) {
      setError((e as Error).message)
    }
  }

  return (
    <div className="space-y-3">
      <button onClick={() => setEditing({ id: null, input: { ...EMPTY, sortOrder: items?.length ?? 0 } })} className={primaryBtn}>
        <Icon name="plus" className="w-4 h-4 inline -mt-0.5" /> {t('editor.productAdd')}
      </button>
      {error && <p className="text-sm text-bad">{error}</p>}
      {!items ? <div className="card h-32 animate-pulse" /> : items.length === 0 ? (
        <p className="card p-6 text-center text-ink-soft">{t('editor.productsEmpty')}</p>
      ) : (
        <ul className="grid gap-3 sm:grid-cols-2">
          {items.map(p => (
            <li key={p.id} className={`card p-3 flex gap-3 ${p.isVisible ? '' : 'opacity-60'}`}>
              {p.imageUrl ? <img src={siteImageUrl(p.imageUrl)} alt="" className="w-20 h-20 rounded-xl object-cover shrink-0" />
                : <span className="w-20 h-20 rounded-xl bg-muted flex items-center justify-center shrink-0"><Icon name="product" className="w-8 h-8 text-ink-faint" /></span>}
              <div className="flex-1 min-w-0">
                <p className="font-semibold truncate">{p.name}</p>
                <p className="text-sm text-ink-soft">
                  {t(`kinds.${p.kind}`)} · {p.price != null ? formatVnd(p.price, currentLocale()) : t('public.contactPrice')}
                  {p.stock != null && ` · ${t('public.stockLeft', { count: p.stock })}`}
                </p>
                {!p.isVisible && <p className="text-xs text-warn">{t('editor.productHidden')}</p>}
                <div className="mt-2 flex gap-2">
                  <button onClick={() => setEditing({ id: p.id, input: p })} className={secondaryBtn}><Icon name="edit" className="w-4 h-4" /></button>
                  <button onClick={() => setDeleting(p)} className={secondaryBtn}><Icon name="trash" className="w-4 h-4" /></button>
                </div>
              </div>
            </li>
          ))}
        </ul>
      )}
      {editing && (
        <ProductForm initial={editing.input} id={editing.id} onClose={() => setEditing(null)}
                     onSaved={p => {
                       setItems(list => {
                         const next = list?.some(x => x.id === p.id) ? list.map(x => x.id === p.id ? p : x) : [...(list ?? []), p]
                         return next.sort((a, b) => a.sortOrder - b.sortOrder || a.id - b.id)
                       })
                       setEditing(null)
                     }} />
      )}
      {deleting && (
        <ConfirmDialog title={deleting.name} message={t('editor.confirmDelete')} confirmLabel={t('editor.delete')}
                       onConfirm={() => { remove(deleting); setDeleting(null) }} onClose={() => setDeleting(null)} />
      )}
    </div>
  )
}

function ProductForm({ initial, id, onClose, onSaved }: {
  initial: SiteProductInput; id: number | null; onClose: () => void; onSaved: (p: SiteProduct) => void
}) {
  const { t } = useTranslation('site')
  const [d, setD] = useState(initial)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const set = <K extends keyof SiteProductInput>(k: K, v: SiteProductInput[K]) => setD(x => ({ ...x, [k]: v }))
  const num = (s: string) => s.trim() === '' ? null : Math.max(0, Math.floor(Number(s.replace(/\D/g, '')) || 0))

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      onSaved(await saveSiteProduct(id, { ...d, name: d.name.trim(), description: d.description?.trim() || null }))
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Sheet title={t(id == null ? 'editor.productAdd' : 'editor.productEdit')} onClose={onClose} wide>
      <form onSubmit={submit} className="space-y-3">
        <label className="block">
          <span className="block text-sm font-semibold mb-1">{t('editor.productName')}</span>
          <input className="field" value={d.name} onChange={e => set('name', e.target.value)} required minLength={2} maxLength={80} />
        </label>
        <label className="block">
          <span className="block text-sm font-semibold mb-1">{t('editor.productKind')}</span>
          <select className="field" value={d.kind} onChange={e => set('kind', e.target.value as SiteProductInput['kind'])}>
            {SITE_PRODUCT_KINDS.map(k => <option key={k} value={k}>{t(`kinds.${k}`)}</option>)}
          </select>
        </label>
        <div className="grid grid-cols-2 gap-3">
          <label className="block">
            <span className="block text-sm font-semibold mb-1">{t('editor.productPrice')}</span>
            <input className="field" inputMode="numeric" value={d.price ?? ''} onChange={e => set('price', num(e.target.value))} />
          </label>
          <label className="block">
            <span className="block text-sm font-semibold mb-1">{t('editor.productStock')}</span>
            <input className="field" inputMode="numeric" value={d.stock ?? ''} onChange={e => set('stock', num(e.target.value))} />
          </label>
        </div>
        <label className="block">
          <span className="block text-sm font-semibold mb-1">{t('editor.productDesc')}</span>
          <textarea className="field" rows={3} maxLength={500} value={d.description ?? ''} onChange={e => set('description', e.target.value)} />
        </label>
        <SiteImagePicker label={t('editor.productImage')} url={d.imageUrl} onChange={u => set('imageUrl', u)} />
        <label className="flex items-center gap-2 text-sm">
          <input type="checkbox" checked={d.isVisible} onChange={e => set('isVisible', e.target.checked)} /> {t('editor.productVisible')}
        </label>
        {error && <p className="text-sm text-bad">{error}</p>}
        <div className="flex gap-2 justify-end pb-2">
          <button type="button" onClick={onClose} className={secondaryBtn}>{t('editor.cancel')}</button>
          <button type="submit" disabled={busy} className={primaryBtn}>{t('editor.saveItem')}</button>
        </div>
      </form>
    </Sheet>
  )
}
