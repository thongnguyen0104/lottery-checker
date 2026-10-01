import { useEffect, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import Icon from '../Icon'
import Sheet, { primaryBtn, secondaryBtn } from './Sheet'
import ShopImagePicker from './ShopImagePicker'
import {
  createShop, geoReverse, getShopOptions, nearbyShops, SHOP_TYPES, updateShop,
  type ShopDetail, type ShopMarker, type ShopType,
} from '../../api/client'
import { fmtMinutes, parseMinutes, TYPE_ICON } from './shopUtils'

type Props = {
  /** Sửa điểm có sẵn; bỏ trống = thêm mới. */
  initial?: ShopDetail
  position: { lat: number; lng: number }
  onClose: () => void
  onSaved: (shop: ShopDetail) => void
  /** Người dùng nhận ra điểm định thêm đã có → mở điểm đó. */
  onPickExisting: (id: string) => void
  /** Quay lại bản đồ để đặt lại ghim (giữ nội dung đã nhập). */
  onMovePin: (draft: Draft) => void
  draft?: Draft
}

export type Draft = {
  name: string; type: ShopType; address: string; phone: string; phoneConsent: boolean
  hoursKnown: boolean; opens: string; closes: string; note: string
  image: { id: number | null; url: string } | null
}

const fromShop = (s?: ShopDetail): Draft => ({
  name: s?.name ?? '',
  type: s?.type ?? 'Agency',
  address: s?.address ?? '',
  phone: s?.phone ?? '',
  phoneConsent: !!s?.phone,
  hoursKnown: s?.opensAtMin != null,
  opens: fmtMinutes(s?.opensAtMin ?? 360),
  closes: fmtMinutes(s?.closesAtMin ?? 1260),
  note: s?.note ?? '',
  image: s?.imageUrl ? { id: null, url: s.imageUrl } : null,
})

/** Thêm / sửa điểm bán. Vị trí lấy từ ghim giữa bản đồ (ShopMap), địa chỉ tự điền theo vị trí. */
export default function ShopForm({ initial, position, onClose, onSaved, onPickExisting, onMovePin, draft }: Props) {
  const { t } = useTranslation('map')
  const [d, setD] = useState<Draft>(() => draft ?? fromShop(initial))
  const set = <K extends keyof Draft>(k: K, v: Draft[K]) => setD(x => ({ ...x, [k]: v }))
  const moved = !initial || initial.lat !== position.lat || initial.lng !== position.lng
  // Mỗi lần đặt lại ghim là form mount mới (ShopMap) → vị trí cố định trong 1 lần mount.
  const [addressLoading, setAddressLoading] = useState(moved)
  const [nearby, setNearby] = useState<ShopMarker[]>([])
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Vị trí mới → tự điền địa chỉ + kiểm tra trùng trong bán kính duplicateRadiusM.
  useEffect(() => {
    if (!moved) return
    const ac = new AbortController()
    geoReverse(position.lat, position.lng, ac.signal)
      // Chỉ điền khi ô còn trống hoặc vừa đặt lại ghim (draft) — không đè chữ người dùng đang gõ.
      .then(p => { if (p) setD(x => (!x.address || draft ? { ...x, address: p.label } : x)) })
      .catch(() => {})
      .finally(() => { if (!ac.signal.aborted) setAddressLoading(false) })
    getShopOptions()
      .then(o => nearbyShops(position.lat, position.lng, o.duplicateRadiusM, ac.signal))
      .then(list => setNearby(list.filter(x => x.id !== initial?.id)))
      .catch(() => {})
    return () => ac.abort()
  }, [position.lat, position.lng, moved, initial, draft])

  const isStreet = d.type === 'Street'

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setSaving(true)
    setError(null)
    const opens = d.hoursKnown ? parseMinutes(d.opens) : null
    const closes = d.hoursKnown ? parseMinutes(d.closes) : null
    const body = {
      name: d.name.trim(),
      type: d.type,
      lat: position.lat,
      lng: position.lng,
      address: d.address.trim(),
      phone: isStreet || !d.phone.trim() ? null : d.phone.trim(),
      phoneConsent: !isStreet && d.phoneConsent,
      opensAtMin: opens,
      closesAtMin: closes,
      note: d.note.trim() || null,
      imageId: d.image?.id ?? null,
      removeImage: !!initial?.imageUrl && !d.image,
    }
    try {
      onSaved(initial ? await updateShop(initial.id, body) : await createShop(body))
    } catch (err) {
      setError((err as Error).message)
      setSaving(false)
    }
  }

  return (
    <Sheet title={t(initial ? 'form.editTitle' : 'form.createTitle')} onClose={onClose} wide>
      <form onSubmit={submit} className="space-y-4">
        {nearby.length > 0 && (
          <div className="p-3 rounded-xl bg-warn/10 ring-1 ring-warn/25 text-sm space-y-2">
            <p className="font-semibold flex items-center gap-1.5">
              <Icon name="warn" className="w-4 h-4 text-warn" /> {t('form.duplicateTitle')}
            </p>
            <p className="text-ink-soft">{t('form.duplicateHint')}</p>
            <ul className="space-y-1">
              {nearby.map(s => (
                <li key={s.id} className="flex items-center gap-2">
                  <Icon name={TYPE_ICON[s.type]} className="w-4 h-4 shrink-0 text-ink-faint" />
                  <span className="flex-1 min-w-0 truncate">
                    {s.name} <span className="text-ink-faint">· {t('list.meters', { n: s.distanceM ?? 0 })}</span>
                  </span>
                  <button type="button" onClick={() => onPickExisting(s.id)}
                          className="shrink-0 text-xs font-semibold text-brand-700 dark:text-brand-400 hover:underline">
                    {t('form.thisOne')}
                  </button>
                </li>
              ))}
            </ul>
          </div>
        )}

        <label className="block">
          <span className="block text-sm font-semibold mb-1">{t('form.name')}</span>
          <input className="field" value={d.name} onChange={e => set('name', e.target.value)} maxLength={80} required minLength={2}
                 placeholder={t('form.namePlaceholder')} autoFocus={!initial} />
        </label>

        <fieldset>
          <legend className="block text-sm font-semibold mb-1">{t('form.type')}</legend>
          <div className="grid grid-cols-2 gap-2">
            {SHOP_TYPES.map(type => (
              <button type="button" key={type} onClick={() => set('type', type)} aria-pressed={d.type === type}
                      className={`flex items-center gap-2 px-3 py-2 rounded-xl text-sm font-semibold text-left border transition
                                  ${d.type === type
                                    ? 'border-brand-500 bg-brand-500/10 text-brand-700 dark:text-brand-400'
                                    : 'border-line text-ink-soft hover:bg-muted'}`}>
                <Icon name={TYPE_ICON[type]} className="w-4 h-4 shrink-0" /> {t(`types.${type}`)}
              </button>
            ))}
          </div>
        </fieldset>

        <div>
          <label className="block">
            <span className="block text-sm font-semibold mb-1">{t('form.address')}</span>
            <input className="field" value={d.address} onChange={e => set('address', e.target.value)} maxLength={200}
                   placeholder={addressLoading ? t('form.addressLoading') : t('form.addressPlaceholder')} />
          </label>
          <div className="mt-1 flex items-center gap-2 text-xs text-ink-faint">
            <Icon name="pin" className="w-3.5 h-3.5" />
            <span className="flex-1">{t('form.position', { lat: position.lat.toFixed(5), lng: position.lng.toFixed(5) })}</span>
            <button type="button" onClick={() => onMovePin(d)}
                    className="font-semibold text-brand-700 dark:text-brand-400 hover:underline">
              {t('form.movePin')}
            </button>
          </div>
        </div>

        {isStreet ? (
          <p className="text-xs text-ink-faint flex gap-1.5">
            <Icon name="tip" className="w-4 h-4 shrink-0" /> {t('form.phoneStreetHint')}
          </p>
        ) : (
          <div className="space-y-1.5">
            <label className="block">
              <span className="block text-sm font-semibold mb-1">{t('form.phone')}</span>
              <input className="field" type="tel" inputMode="tel" value={d.phone} maxLength={20}
                     onChange={e => set('phone', e.target.value)} placeholder={t('form.phonePlaceholder')} />
            </label>
            {d.phone.trim() && (
              <label className="flex items-start gap-2 text-sm text-ink-soft">
                <input type="checkbox" className="mt-0.5" checked={d.phoneConsent} required
                       onChange={e => set('phoneConsent', e.target.checked)} />
                {t('form.phoneConsent')}
              </label>
            )}
          </div>
        )}

        <div className="space-y-1.5">
          <label className="flex items-center gap-2 text-sm font-semibold">
            <input type="checkbox" checked={d.hoursKnown} onChange={e => set('hoursKnown', e.target.checked)} />
            {t('form.hoursKnown')}
          </label>
          {d.hoursKnown && (
            <div className="grid grid-cols-2 gap-2">
              <label className="block">
                <span className="block text-xs text-ink-faint mb-1">{t('form.opens')}</span>
                <input className="field" type="time" value={d.opens} onChange={e => set('opens', e.target.value)} required />
              </label>
              <label className="block">
                <span className="block text-xs text-ink-faint mb-1">{t('form.closes')}</span>
                <input className="field" type="time" value={d.closes} onChange={e => set('closes', e.target.value)} required />
              </label>
            </div>
          )}
        </div>

        <label className="block">
          <span className="block text-sm font-semibold mb-1">{t('form.note')}</span>
          <textarea className="field min-h-[72px]" value={d.note} onChange={e => set('note', e.target.value)} maxLength={500}
                    placeholder={t('form.notePlaceholder')} />
        </label>

        <ShopImagePicker label={t('form.image')} url={d.image?.url ?? null}
                         onChange={img => set('image', img)} />

        {error && <p className="text-sm text-bad">{error}</p>}

        <div className="grid grid-cols-2 gap-2 pt-1">
          <button type="button" onClick={onClose} className={`${secondaryBtn} py-2.5`}>{t('cancel')}</button>
          <button type="submit" disabled={saving} className={primaryBtn}>
            {saving ? t('form.saving') : t('form.save')}
          </button>
        </div>
      </form>
    </Sheet>
  )
}
