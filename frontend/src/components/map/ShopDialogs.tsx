import { useEffect, useMemo, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import Sheet, { primaryBtn, secondaryBtn } from './Sheet'
import ShopImagePicker from './ShopImagePicker'
import {
  addShopWin, getShopOptions, reportShop, SHOP_REPORT_REASONS,
  type ShopReportReason, type ShopWin,
} from '../../api/client'
import { ALL_PROVINCES, provinceName } from '../../data/provinces'
import { todayIso } from '../../utils/date'
import { mayDrawOn, VIETLOTT } from './shopUtils'

/** Báo "điểm này từng bán vé trúng": ngày quay → đài quay ngày đó (+ Vietlott) → hạng giải. */
export function WinDialog({ shopId, onClose, onDone }: { shopId: string; onClose: () => void; onDone: (w: ShopWin) => void }) {
  const { t } = useTranslation('map')
  const [date, setDate] = useState(todayIso)
  const [province, setProvince] = useState('')
  const [tier, setTier] = useState('')
  const [image, setImage] = useState<{ id: number; url: string } | null>(null)
  const [tiers, setTiers] = useState<{ xskt: string[]; vietlott: string[] }>({ xskt: [], vietlott: [] })
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => { getShopOptions().then(o => setTiers({ xskt: o.xsktTiers, vietlott: o.vietlottTiers })) }, [])

  const stations = useMemo(() => [...ALL_PROVINCES.filter(p => mayDrawOn(date, p.code)).map(p => p.code), VIETLOTT], [date])
  const tierList = province === VIETLOTT ? tiers.vietlott : tiers.xskt
  // Đổi ngày mà đài đang chọn không quay ngày đó → bỏ chọn.
  const validProvince = stations.includes(province) ? province : ''
  const validTier = tierList.includes(tier) ? tier : ''

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setSaving(true)
    setError(null)
    try {
      onDone(await addShopWin(shopId, { drawDate: date, provinceCode: validProvince, prizeTier: validTier, imageId: image?.id }))
    } catch (err) {
      setError((err as Error).message)
      setSaving(false)
    }
  }

  return (
    <Sheet title={t('wins.dialogTitle')} onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        <p className="text-xs text-ink-faint">{t('wins.unverified')}</p>
        <label className="block">
          <span className="block text-sm font-semibold mb-1">{t('wins.drawDate')}</span>
          <input className="field" type="date" value={date} max={todayIso()} required onChange={e => setDate(e.target.value)} />
        </label>
        <label className="block">
          <span className="block text-sm font-semibold mb-1">{t('wins.province')}</span>
          <select className="field" value={validProvince} required onChange={e => setProvince(e.target.value)}>
            <option value="" disabled>—</option>
            {stations.map(c => <option key={c} value={c}>{c === VIETLOTT ? 'Vietlott' : provinceName(c)}</option>)}
          </select>
        </label>
        <label className="block">
          <span className="block text-sm font-semibold mb-1">{t('wins.tier')}</span>
          <select className="field" value={validTier} required disabled={!validProvince} onChange={e => setTier(e.target.value)}>
            <option value="" disabled>—</option>
            {tierList.map(x => <option key={x} value={x}>{t(`wins.tiers.${x}`, { defaultValue: x })}</option>)}
          </select>
        </label>
        <ShopImagePicker label={t('wins.image')} url={image?.url ?? null} onChange={setImage} />
        {error && <p className="text-sm text-bad">{error}</p>}
        <div className="grid grid-cols-2 gap-2">
          <button type="button" onClick={onClose} className={`${secondaryBtn} py-2.5`}>{t('cancel')}</button>
          <button type="submit" disabled={saving || !validProvince || !validTier} className={primaryBtn}>{t('wins.submit')}</button>
        </div>
      </form>
    </Sheet>
  )
}

/** Báo cáo sai / spam — đủ ngưỡng thì máy chủ tự ẩn điểm. */
export function ReportDialog({ shopId, onClose, onDone }: {
  shopId: string; onClose: () => void; onDone: (hidden: boolean) => void
}) {
  const { t } = useTranslation('map')
  const [reason, setReason] = useState<ShopReportReason>('NotExist')
  const [note, setNote] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setSaving(true)
    setError(null)
    try {
      onDone((await reportShop(shopId, reason, note.trim() || null)).hidden)
    } catch (err) {
      setError((err as Error).message)
      setSaving(false)
    }
  }

  return (
    <Sheet title={t('report.title')} onClose={onClose}>
      <form onSubmit={submit} className="space-y-3">
        <p className="text-sm text-ink-soft">{t('report.hint')}</p>
        <div className="space-y-1">
          {SHOP_REPORT_REASONS.map(r => (
            <label key={r} className="flex items-center gap-2 px-3 py-2 rounded-xl hover:bg-muted text-sm cursor-pointer">
              <input type="radio" name="reason" checked={reason === r} onChange={() => setReason(r)} />
              {t(`report.reasons.${r}`)}
            </label>
          ))}
        </div>
        <label className="block">
          <span className="block text-sm font-semibold mb-1">{t('report.note')}</span>
          <textarea className="field min-h-[64px]" value={note} maxLength={300} onChange={e => setNote(e.target.value)} />
        </label>
        {error && <p className="text-sm text-bad">{error}</p>}
        <div className="grid grid-cols-2 gap-2">
          <button type="button" onClick={onClose} className={`${secondaryBtn} py-2.5`}>{t('cancel')}</button>
          <button type="submit" disabled={saving} className={primaryBtn}>{t('report.submit')}</button>
        </div>
      </form>
    </Sheet>
  )
}
