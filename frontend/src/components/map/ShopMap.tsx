import 'leaflet/dist/leaflet.css'
import 'react-leaflet-cluster/dist/assets/MarkerCluster.css'
import 'react-leaflet-cluster/dist/assets/MarkerCluster.Default.css'
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { MapContainer, Marker, TileLayer, useMapEvents } from 'react-leaflet'
import MarkerClusterGroup from 'react-leaflet-cluster'
import type { Map as LeafletMap } from 'leaflet'
import Icon from '../Icon'
import ShopDetail from './ShopDetail'
import ShopForm, { type Draft } from './ShopForm'
import ShopSearch from './ShopSearch'
import { primaryBtn, secondaryBtn } from './Sheet'
import { listShops, SHOP_TYPES, type Account, type ShopDetail as Detail, type ShopMarker, type ShopType } from '../../api/client'
import {
  DEFAULT_CENTER, DEFAULT_ZOOM, isOpenNow, MIN_LOAD_ZOOM, TILE_ATTRIBUTION, TILE_ATTRIBUTION_DARK, TILE_LABELS_DARK, TILE_URL, TILE_URL_DARK, TYPE_ICON,
} from './shopUtils'
import { ME_ICON, markerIcon } from './markerIcons'

/** Điểm cần mở (link chia sẻ / thông báo). `at` đổi = mở lại kể cả cùng điểm. */
export type MapFocus = { id: string; at: number }

type LatLng = { lat: number; lng: number }
type View = { bbox: [number, number, number, number]; zoom: number; center: LatLng }

type Mode =
  | { kind: 'browse' }
  | { kind: 'pick'; edit?: Detail; draft?: Draft }
  | { kind: 'form'; pos: LatLng; edit?: Detail; draft?: Draft }

const LIST_TAKE = 30
const LOAD_DEBOUNCE_MS = 300

type Props = {
  account: Account | null
  onRequireLogin: () => void
  focus: MapFocus | null
  /** Điểm đang mở đổi → App sửa URL (/ban-do/{id}) để F5 / chia sẻ đúng điểm. */
  onSelect: (id: string | null) => void
}

const distanceM = (a: LatLng, b: LatLng) => {
  const r = (d: number) => d * Math.PI / 180
  const x = Math.sin(r(b.lat - a.lat) / 2) ** 2 + Math.cos(r(a.lat)) * Math.cos(r(b.lat)) * Math.sin(r(b.lng - a.lng) / 2) ** 2
  return 2 * 6_371_000 * Math.asin(Math.min(1, Math.sqrt(x)))
}

/** Báo khung nhìn mỗi lần kéo / phóng xong (và 1 lần lúc bản đồ sẵn sàng). */
function ViewWatcher({ onChange }: { onChange: (m: LeafletMap) => void }) {
  const map = useMapEvents({ moveend: () => onChange(map) })
  useEffect(() => { onChange(map) }, [map, onChange])
  return null
}

/**
 * Bản đồ điểm bán vé số (OpenStreetMap + Leaflet): tải điểm theo khung nhìn, gom cụm, lọc, tìm kiếm,
 * danh sách gần đây, chi tiết từng điểm, thêm / sửa điểm bằng ghim giữa bản đồ.
 */
export default function ShopMap({ account, onRequireLogin, focus, onSelect }: Props) {
  const { t } = useTranslation('map')
  const dark = useDarkMode()
  const [map, setMap] = useState<LeafletMap | null>(null)
  const [view, setView] = useState<View | null>(null)
  const [markers, setMarkers] = useState<ShopMarker[]>([])
  const [loadError, setLoadError] = useState(false)
  const [version, setVersion] = useState(0)
  const [types, setTypes] = useState<ShopType[]>([])
  const [hasWin, setHasWin] = useState(false)
  const [openNow, setOpenNow] = useState(false)
  const [filtersOpen, setFiltersOpen] = useState(false)
  const [selected, setSelected] = useState<string | null>(focus?.id ?? null)
  const [detailVersion, setDetailVersion] = useState(0)
  const [me, setMe] = useState<LatLng | null>(null)
  const [locateError, setLocateError] = useState(false)
  const [mode, setMode] = useState<Mode>({ kind: 'browse' })
  // Điểm cần bay tới khi chi tiết tải xong (mở từ link / thông báo / ô tìm) — bấm marker thì không bay.
  const [flyTo, setFlyTo] = useState<string | null>(focus?.id ?? null)
  const sideRef = useRef<HTMLDivElement>(null)

  const select = useCallback((id: string | null, scroll = false) => {
    setSelected(id)
    onSelect(id)
    // Điện thoại: chi tiết nằm dưới bản đồ → cuộn tới cho thấy.
    if (id && scroll && window.innerWidth < 1024)
      setTimeout(() => sideRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' }), 50)
  }, [onSelect])

  // Focus mới từ App (bấm thông báo khi đang ở bản đồ) — chỉnh state ngay lúc render thay vì trong effect.
  const [prevFocus, setPrevFocus] = useState(focus)
  if (focus !== prevFocus) {
    setPrevFocus(focus)
    if (focus) { setSelected(focus.id); setFlyTo(focus.id) }
  }

  const onViewChange = useCallback((m: LeafletMap) => {
    const b = m.getBounds()
    const c = m.getCenter()
    setView({ bbox: [b.getSouth(), b.getWest(), b.getNorth(), b.getEast()], zoom: m.getZoom(), center: { lat: c.lat, lng: c.lng } })
  }, [])

  // Tải điểm trong khung nhìn — chờ người dùng ngừng kéo một chút; khung quá rộng thì thôi.
  useEffect(() => {
    if (!view || view.zoom < MIN_LOAD_ZOOM) return
    const ac = new AbortController()
    const timer = setTimeout(() => {
      listShops(view.bbox, types, hasWin, ac.signal)
        .then(list => { setMarkers(list); setLoadError(false) })
        .catch(() => { if (!ac.signal.aborted) setLoadError(true) })
    }, LOAD_DEBOUNCE_MS)
    return () => { clearTimeout(timer); ac.abort() }
  }, [view, types, hasWin, version])

  const requestPosition = useCallback((onError: () => void) => {
    if (!navigator.geolocation) { onError(); return }
    navigator.geolocation.getCurrentPosition(
      p => {
        const pos = { lat: p.coords.latitude, lng: p.coords.longitude }
        setMe(pos)
        setLocateError(false)
        map?.flyTo([pos.lat, pos.lng], Math.max(map.getZoom(), 16))
      },
      onError,
      { enableHighAccuracy: true, timeout: 10_000, maximumAge: 60_000 },
    )
  }, [map])
  const locate = () => requestPosition(() => setLocateError(true))

  // Mở bản đồ: xin vị trí 1 lần (trừ khi mở thẳng 1 điểm) — từ chối thì im lặng ở lại TP.HCM.
  const askedLocation = useRef(!!focus)
  useEffect(() => {
    if (!map || askedLocation.current) return
    askedLocation.current = true
    requestPosition(() => {})
  }, [map, requestPosition])

  const shown = useMemo(() => openNow ? markers.filter(m => isOpenNow(m) === true) : markers, [markers, openNow])

  const listed = useMemo(() => {
    const origin = me ?? view?.center
    if (!origin) return []
    return shown.map(m => ({ m, d: distanceM(origin, m) })).sort((a, b) => a.d - b.d).slice(0, LIST_TAKE)
  }, [shown, me, view?.center])

  const startAdd = () => {
    if (!account) { onRequireLogin(); return }
    select(null)
    setMode({ kind: 'pick' })
  }

  const confirmPick = () => {
    if (!map || mode.kind !== 'pick') return
    const c = map.getCenter()
    setMode({ kind: 'form', pos: { lat: c.lat, lng: c.lng }, edit: mode.edit, draft: mode.draft })
  }

  const onSaved = (d: Detail) => {
    setMode({ kind: 'browse' })
    select(d.id, true)
    setDetailVersion(v => v + 1)
    setVersion(v => v + 1)
  }

  const typeFilterOn = types.length > 0 || hasWin || openNow
  const toggleType = (ty: ShopType) => setTypes(list => list.includes(ty) ? list.filter(x => x !== ty) : [...list, ty])
  const fmtDistance = (d: number) => d < 1000 ? t('list.meters', { n: Math.round(d / 10) * 10 }) : t('list.km', { n: (d / 1000).toFixed(1) })

  return (
    <div className="space-y-3">
      <div>
        <h1 className="text-xl md:text-3xl font-extrabold tracking-tight flex items-center gap-2">
          <Icon name="map" className="w-6 h-6 md:w-8 md:h-8 text-brand-700 dark:text-brand-400" /> {t('title')}
        </h1>
        <p className="text-sm md:text-base text-ink-soft mt-0.5">{t('hint')}</p>
      </div>

      <div className="flex gap-2">
        <div className="flex-1 min-w-0">
          <ShopSearch near={view?.center ?? null}
                      onPickShop={s => { setFlyTo(s.id); select(s.id, true) }}
                      onPickPlace={p => map?.flyTo([p.lat, p.lng], 17)} />
        </div>
        <button onClick={() => setFiltersOpen(o => !o)} aria-expanded={filtersOpen} aria-label={t('filters.title')}
                className={`${secondaryBtn} shrink-0 px-3 ${typeFilterOn ? 'text-brand-700 dark:text-brand-400 ring-1 ring-brand-500/40' : ''}`}>
          <Icon name="filter" className="w-4 h-4" /> <span className="hidden sm:inline">{t('filters.title')}</span>
        </button>
        <button onClick={startAdd} disabled={mode.kind !== 'browse'}
                className={`${primaryBtn} shrink-0 inline-flex items-center gap-1.5 px-3 text-sm`}>
          <Icon name="plus" className="w-4 h-4" /> <span className="hidden sm:inline">{t('add')}</span>
        </button>
      </div>

      {filtersOpen && (
        <div className="flex flex-wrap gap-2">
          {SHOP_TYPES.map(ty => (
            <Chip key={ty} on={types.includes(ty)} onClick={() => toggleType(ty)}>
              <Icon name={TYPE_ICON[ty]} className="w-4 h-4" /> {t(`types.${ty}`)}
            </Chip>
          ))}
          <Chip on={hasWin} onClick={() => setHasWin(x => !x)}><Icon name="trophy" className="w-4 h-4" /> {t('filters.hasWin')}</Chip>
          <Chip on={openNow} onClick={() => setOpenNow(x => !x)}><Icon name="clock" className="w-4 h-4" /> {t('filters.openNow')}</Chip>
          {typeFilterOn && (
            <button onClick={() => { setTypes([]); setHasWin(false); setOpenNow(false) }}
                    className="px-2 text-sm font-semibold text-ink-faint hover:text-ink">{t('filters.clear')}</button>
          )}
        </div>
      )}

      <div className="lg:grid lg:grid-cols-[minmax(0,1.6fr)_minmax(0,1fr)] lg:gap-4 lg:items-start space-y-3 lg:space-y-0">
        {/* isolate: Leaflet dùng z-index 400–1000, giữ nó dưới header (z-30) và hộp thoại (z-50). */}
        <div className="relative isolate z-0 h-[58vh] lg:h-[70vh] rounded-2xl overflow-hidden border border-line shadow-soft">
          <MapContainer ref={setMap} center={DEFAULT_CENTER} zoom={DEFAULT_ZOOM} minZoom={5} className="h-full w-full"
                        zoomControl={false} attributionControl>
            {/* key: đổi sáng/tối là dựng lại lớp tile (TileLayer không nhận url mới sau khi tạo). */}
            <TileLayer key={dark ? 'dark' : 'light'} url={dark ? TILE_URL_DARK : TILE_URL}
                       attribution={dark ? TILE_ATTRIBUTION_DARK : TILE_ATTRIBUTION} maxZoom={19} maxNativeZoom={dark ? 16 : 18} />
            {dark && TILE_LABELS_DARK && <TileLayer url={TILE_LABELS_DARK} maxZoom={19} maxNativeZoom={16} />}
            <ViewWatcher onChange={onViewChange} />
            {mode.kind === 'browse' && (
              <MarkerClusterGroup chunkedLoading showCoverageOnHover={false} maxClusterRadius={45}>
                {shown.map(m => (
                  <Marker key={m.id} position={[m.lat, m.lng]} icon={markerIcon(m.type, m.winCount > 0, m.id === selected)}
                          title={m.name} eventHandlers={{ click: () => select(m.id, true) }} />
                ))}
              </MarkerClusterGroup>
            )}
            {me && <Marker position={[me.lat, me.lng]} icon={ME_ICON} interactive={false} />}
          </MapContainer>

          <button onClick={locate} aria-label={t('locate')} title={t('locate')}
                  className="absolute top-3 right-3 z-[1000] w-10 h-10 rounded-xl bg-surface border border-line shadow-md
                             flex items-center justify-center text-brand-700 dark:text-brand-400 active:scale-95">
            <Icon name="locate" />
          </button>
          <div className="absolute top-3 left-3 z-[1000] flex flex-col rounded-xl bg-surface border border-line shadow-md overflow-hidden">
            <button onClick={() => map?.zoomIn()} aria-label="+" className="w-10 h-10 text-lg font-bold hover:bg-muted">+</button>
            <button onClick={() => map?.zoomOut()} aria-label="−" className="w-10 h-10 text-lg font-bold hover:bg-muted border-t border-line">−</button>
          </div>

          {mode.kind === 'browse' && view && view.zoom < MIN_LOAD_ZOOM && (
            <Toast>{t('list.zoomIn')}</Toast>
          )}
          {mode.kind === 'browse' && loadError && <Toast>{t('loadError')}</Toast>}
          {locateError && mode.kind === 'browse' && <Toast onClose={() => setLocateError(false)}>{t('locateDenied')}</Toast>}

          {mode.kind === 'pick' && (
            <>
              {/* Ghim cố định giữa bản đồ — người dùng kéo bản đồ để đặt ghim. */}
              <div className="pointer-events-none absolute left-1/2 top-1/2 z-[1000] -translate-x-1/2 -translate-y-full">
                <div className="w-9 h-9 rounded-full rounded-br-none rotate-45 bg-bad ring-4 ring-white shadow-xl" />
              </div>
              <div className="absolute inset-x-3 bottom-3 z-[1000] p-3 rounded-2xl bg-surface/95 border border-line shadow-xl space-y-2">
                <p className="text-sm font-semibold text-center">{t('pickHint')}</p>
                <div className="grid grid-cols-2 gap-2">
                  <button onClick={() => setMode(mode.edit ? { kind: 'form', pos: { lat: mode.edit.lat, lng: mode.edit.lng }, edit: mode.edit, draft: mode.draft } : { kind: 'browse' })}
                          className={`${secondaryBtn} py-2.5`}>{t('cancel')}</button>
                  <button onClick={confirmPick} className={primaryBtn}>{t('pickConfirm')}</button>
                </div>
              </div>
            </>
          )}
        </div>

        <div ref={sideRef} className="scroll-mt-20">
          {selected ? (
            <ShopDetail key={`${selected}:${detailVersion}`} id={selected} account={account} onRequireLogin={onRequireLogin}
                        onClose={() => select(null)}
                        onLoaded={d => {
                          if (flyTo !== d.id || !map) return
                          setFlyTo(null)
                          map.flyTo([d.lat, d.lng], Math.max(map.getZoom(), 16))
                        }}
                        onEdit={d => setMode({ kind: 'form', pos: { lat: d.lat, lng: d.lng }, edit: d })}
                        onDeleted={() => { select(null); setVersion(v => v + 1) }}
                        onChanged={() => setVersion(v => v + 1)} />
          ) : (
            <section className="card p-3">
              <h2 className="px-1 pb-2 font-bold flex items-center gap-2">
                <Icon name="list" className="w-4 h-4 text-brand-700 dark:text-brand-400" />
                {t(me ? 'list.nearby' : 'list.title')}
                {shown.length > 0 && <span className="text-xs font-semibold text-ink-faint">({shown.length})</span>}
              </h2>
              {listed.length === 0 ? (
                <p className="px-1 pb-2 text-sm text-ink-faint">
                  {view && view.zoom < MIN_LOAD_ZOOM ? t('list.zoomIn') : t('list.empty')}
                </p>
              ) : (
                <ul className="max-h-[50vh] lg:max-h-[62vh] overflow-y-auto -mx-1">
                  {listed.map(({ m, d }) => {
                    const open = isOpenNow(m)
                    return (
                      <li key={m.id}>
                        <button onClick={() => { select(m.id, true); map?.panTo([m.lat, m.lng]) }}
                                className="w-full flex items-center gap-3 px-2 py-2 rounded-xl text-left hover:bg-muted">
                          <span className="shrink-0 w-9 h-9 rounded-xl flex items-center justify-center bg-brand-500/10 text-brand-700 dark:text-brand-400">
                            <Icon name={TYPE_ICON[m.type]} className="w-[18px] h-[18px]" />
                          </span>
                          <span className="min-w-0 flex-1">
                            <span className="block text-sm font-semibold truncate">
                              {m.name}{m.winCount > 0 && <span className="ml-1 text-warn" title={t('filters.hasWin')}>★</span>}
                            </span>
                            <span className="block text-xs text-ink-faint truncate">
                              {[
                                fmtDistance(d),
                                m.rating != null ? `${m.rating.toFixed(1)}★` : null,
                                open == null ? null : t(open ? 'detail.openNow' : 'detail.closedNow'),
                                m.address || null,
                              ].filter(Boolean).join(' · ')}
                            </span>
                          </span>
                        </button>
                      </li>
                    )
                  })}
                </ul>
              )}
            </section>
          )}
        </div>
      </div>

      <p className="text-xs text-ink-faint">{t('attribution')}</p>

      {mode.kind === 'form' && (
        <ShopForm initial={mode.edit} position={mode.pos} draft={mode.draft}
                  onClose={() => setMode({ kind: 'browse' })}
                  onSaved={onSaved}
                  onPickExisting={id => { setMode({ kind: 'browse' }); setFlyTo(id); select(id, true) }}
                  onMovePin={draft => {
                    map?.setView([mode.pos.lat, mode.pos.lng], Math.max(map.getZoom(), 17))
                    setMode({ kind: 'pick', edit: mode.edit, draft })
                  }} />
      )}
    </div>
  )
}

function Chip({ on, onClick, children }: { on: boolean; onClick: () => void; children: ReactNode }) {
  return (
    <button onClick={onClick} aria-pressed={on}
            className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-full text-sm font-semibold border transition active:scale-95
                        ${on ? 'border-brand-500 bg-brand-500/10 text-brand-700 dark:text-brand-400' : 'border-line text-ink-soft hover:bg-muted'}`}>
      {children}
    </button>
  )
}

function Toast({ children, onClose }: { children: ReactNode; onClose?: () => void }) {
  return (
    <div className="absolute left-1/2 -translate-x-1/2 bottom-3 z-[1000] max-w-[90%] px-3 py-2 rounded-xl bg-surface/95 border border-line
                    shadow-lg text-sm text-ink-soft flex items-center gap-2">
      {children}
      {onClose && <button onClick={onClose} aria-label="×" className="text-ink-faint hover:text-ink"><Icon name="close" className="w-4 h-4" /></button>}
    </div>
  )
}

/** Theo data-mode="dark" trên <html> (theme.ts đặt) — đổi giao diện là đổi tile ngay. */
function useDarkMode() {
  const read = () => document.documentElement.dataset.mode === 'dark'
  const [dark, setDark] = useState(read)
  useEffect(() => {
    const obs = new MutationObserver(() => setDark(read()))
    obs.observe(document.documentElement, { attributes: true, attributeFilter: ['data-mode'] })
    return () => obs.disconnect()
  }, [])
  return dark
}
