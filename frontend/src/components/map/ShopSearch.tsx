import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import Icon from '../Icon'
import { geoSearch, searchShops, type GeoPlace, type ShopMarker } from '../../api/client'
import { TYPE_ICON } from './shopUtils'

const DEBOUNCE_MS = 450

/** Ô tìm: gợi ý từ 2 nguồn — điểm bán trong DB (theo tên/địa chỉ) và địa chỉ (Nominatim qua máy chủ). */
export default function ShopSearch({ near, onPickShop, onPickPlace }: {
  near: { lat: number; lng: number } | null
  onPickShop: (s: ShopMarker) => void
  onPickPlace: (p: GeoPlace) => void
}) {
  const { t } = useTranslation('map')
  const [q, setQ] = useState('')
  const [shops, setShops] = useState<ShopMarker[]>([])
  const [places, setPlaces] = useState<GeoPlace[]>([])
  const [loading, setLoading] = useState(false)
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)
  // Vị trí đổi liên tục khi kéo bản đồ — đọc qua ref để không tìm lại mỗi lần kéo.
  const nearRef = useRef(near)
  useEffect(() => { nearRef.current = near }, [near])

  useEffect(() => {
    const text = q.trim()
    // Quá ngắn thì thôi tìm — danh sách cũ bị ẩn (hasText) chứ không cần xoá.
    if (text.length < 2) return
    const ac = new AbortController()
    const timer = setTimeout(() => {
      setLoading(true)
      Promise.all([
        searchShops(text, nearRef.current ?? undefined, ac.signal).catch(() => []),
        text.length >= 3 ? geoSearch(text, ac.signal).catch(() => []) : Promise.resolve([]),
      ]).then(([s, p]) => { setShops(s); setPlaces(p) })
        .finally(() => { if (!ac.signal.aborted) setLoading(false) })
    }, DEBOUNCE_MS)
    return () => { clearTimeout(timer); ac.abort() }
  }, [q])

  useEffect(() => {
    if (!open) return
    const onDown = (e: PointerEvent) => { if (!ref.current?.contains(e.target as Node)) setOpen(false) }
    document.addEventListener('pointerdown', onDown)
    return () => document.removeEventListener('pointerdown', onDown)
  }, [open])

  const done = () => { setOpen(false); (document.activeElement as HTMLElement | null)?.blur() }
  const hasText = q.trim().length >= 2

  return (
    <div ref={ref} className="relative">
      <Icon name="find" className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-ink-faint pointer-events-none" />
      <input type="search" value={q} onChange={e => { setQ(e.target.value); setOpen(true) }} onFocus={() => setOpen(true)}
             onKeyDown={e => { if (e.key === 'Escape') done() }}
             placeholder={t('searchPlaceholder')} aria-label={t('searchPlaceholder')} className="field pl-9" />

      {open && hasText && (
        <div className="absolute left-0 right-0 top-full mt-2 z-[1100] max-h-[60vh] overflow-y-auto rounded-2xl bg-surface border border-line shadow-2xl p-1.5">
          {shops.length > 0 && <Group label={t('searchShops')} />}
          {shops.map(s => (
            <button key={s.id} onClick={() => { done(); onPickShop(s) }}
                    className="w-full flex items-center gap-2.5 px-3 py-2 rounded-xl text-left hover:bg-muted">
              <Icon name={TYPE_ICON[s.type]} className="w-4 h-4 shrink-0 text-brand-700 dark:text-brand-400" />
              <span className="min-w-0 flex-1">
                <span className="block text-sm font-semibold truncate">{s.name}</span>
                {s.address && <span className="block text-xs text-ink-faint truncate">{s.address}</span>}
              </span>
            </button>
          ))}
          {places.length > 0 && <Group label={t('searchPlaces')} />}
          {places.map((p, i) => (
            <button key={i} onClick={() => { done(); onPickPlace(p) }}
                    className="w-full flex items-center gap-2.5 px-3 py-2 rounded-xl text-left hover:bg-muted">
              <Icon name="pin" className="w-4 h-4 shrink-0 text-ink-faint" />
              <span className="text-sm line-clamp-2">{p.label}</span>
            </button>
          ))}
          {!loading && shops.length === 0 && places.length === 0 && (
            <p className="px-3 py-4 text-center text-sm text-ink-faint">{t('noResults')}</p>
          )}
          {loading && shops.length === 0 && places.length === 0 && <p className="px-3 py-4 text-center text-sm text-ink-faint">…</p>}
        </div>
      )}
    </div>
  )
}

const Group = ({ label }: { label: string }) => (
  <p className="px-3 pt-2 pb-1 text-xs font-bold uppercase tracking-wide text-ink-faint">{label}</p>
)
