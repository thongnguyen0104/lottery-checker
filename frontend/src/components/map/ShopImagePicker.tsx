import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import Icon from '../Icon'
import { getShopOptions, shopImageUrl, uploadShopImage } from '../../api/client'
import { secondaryBtn } from './Sheet'

/**
 * 1 ảnh (điểm bán / vé trúng): chọn là nén + upload ngay, trả id để gửi kèm lúc lưu. Máy chủ chưa
 * cấu hình bucket thì không hiện gì.
 */
export default function ShopImagePicker({ label, url, onChange }: {
  label: string
  /** Ảnh đang hiện (đường dẫn tương đối /api/shops/images/... hoặc blob preview). */
  url: string | null
  /** id null = bỏ ảnh. */
  onChange: (image: { id: number; url: string } | null) => void
}) {
  const { t } = useTranslation('map')
  const [enabled, setEnabled] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const input = useRef<HTMLInputElement>(null)

  useEffect(() => { getShopOptions().then(o => setEnabled(o.imagesEnabled)) }, [])
  if (!enabled) return null

  const pick = async (file: File | undefined) => {
    if (!file) return
    setBusy(true)
    setError(null)
    try {
      onChange(await uploadShopImage(file))
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
      if (input.current) input.current.value = ''
    }
  }

  return (
    <div>
      <span className="block text-sm font-semibold mb-1">{label}</span>
      {url ? (
        <div className="relative">
          <img src={shopImageUrl(url)} alt="" className="w-full max-h-48 object-cover rounded-xl border border-line" />
          <button type="button" onClick={() => onChange(null)}
                  className={`${secondaryBtn} absolute top-2 right-2 bg-surface/90`}>
            <Icon name="trash" className="w-4 h-4" /> {t('form.removeImage')}
          </button>
        </div>
      ) : (
        <button type="button" onClick={() => input.current?.click()} disabled={busy} className={secondaryBtn}>
          <Icon name="addImage" className="w-4 h-4" /> {busy ? t('form.uploading') : t('form.addImage')}
        </button>
      )}
      <input ref={input} type="file" accept="image/*" hidden onChange={e => pick(e.target.files?.[0])} />
      {error && <p className="mt-1 text-sm text-bad">{error}</p>}
    </div>
  )
}
