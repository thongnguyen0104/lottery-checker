import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import Icon from '../Icon'
import { getSiteOptions, siteImageUrl, uploadSiteImage } from '../../api/client'
import { secondaryBtn } from '../map/Sheet'

/**
 * 1 ảnh của site (logo / bìa / sản phẩm / bìa bài): chọn là nén + upload ngay, trả url để lưu thẳng vào
 * cấu hình. Máy chủ chưa cấu hình bucket thì không hiện gì.
 */
export default function SiteImagePicker({ label, url, onChange, round }: {
  label: string
  url: string | null
  onChange: (url: string | null) => void
  /** Logo: khung tròn nhỏ thay vì ảnh ngang. */
  round?: boolean
}) {
  const { t } = useTranslation('site')
  const [enabled, setEnabled] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const input = useRef<HTMLInputElement>(null)

  useEffect(() => { getSiteOptions().then(o => setEnabled(o.imagesEnabled)) }, [])
  if (!enabled) return null

  const pick = async (file: File | undefined) => {
    if (!file) return
    setBusy(true)
    setError(null)
    try {
      onChange((await uploadSiteImage(file)).url)
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
      <div className="flex items-center gap-3 flex-wrap">
        {url && (
          <img src={siteImageUrl(url)} alt=""
               className={round ? 'w-16 h-16 rounded-full object-cover border border-line'
                                : 'w-full max-h-40 object-cover rounded-xl border border-line'} />
        )}
        <button type="button" onClick={() => input.current?.click()} disabled={busy} className={secondaryBtn}>
          <Icon name="addImage" className="w-4 h-4 inline -mt-0.5" /> {busy ? t('editor.uploading') : t('editor.addImage')}
        </button>
        {url && (
          <button type="button" onClick={() => onChange(null)} className={secondaryBtn}>
            <Icon name="trash" className="w-4 h-4 inline -mt-0.5" /> {t('editor.removeImage')}
          </button>
        )}
      </div>
      <input ref={input} type="file" accept="image/*" hidden onChange={e => pick(e.target.files?.[0])} />
      {error && <p className="mt-1 text-sm text-bad">{error}</p>}
    </div>
  )
}
