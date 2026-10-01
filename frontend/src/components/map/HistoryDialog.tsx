import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import Sheet from './Sheet'
import { getShopHistory, type ShopRevision, type ShopSnapshot } from '../../api/client'
import { formatDateTime } from '../../utils/date'
import { fmtMinutes } from './shopUtils'

type Field = 'name' | 'type' | 'position' | 'address' | 'phone' | 'hours' | 'note' | 'image'
const FIELDS: Field[] = ['name', 'type', 'position', 'address', 'phone', 'hours', 'note', 'image']

/** Lịch sử tạo / sửa 1 điểm (chỉ admin): mỗi lần lưu chỉ hiện các trường đổi so với bản trước. */
export default function HistoryDialog({ shopId, onClose }: { shopId: string; onClose: () => void }) {
  const { t } = useTranslation('map')
  const [items, setItems] = useState<ShopRevision[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => { getShopHistory(shopId).then(setItems).catch(e => setError((e as Error).message)) }, [shopId])

  const show = (f: Field, s: ShopSnapshot): string => {
    switch (f) {
      case 'name': return s.name
      case 'type': return t(`types.${s.type}`)
      case 'position': return `${s.lat.toFixed(5)}, ${s.lng.toFixed(5)}`
      case 'address': return s.address
      case 'phone': return s.phone ?? ''
      case 'hours': return s.opensAtMin == null || s.closesAtMin == null ? '' : `${fmtMinutes(s.opensAtMin)} – ${fmtMinutes(s.closesAtMin)}`
      case 'note': return s.note ?? ''
      case 'image': return s.imageUrl ?? ''
    }
  }

  return (
    <Sheet title={t('history.title')} onClose={onClose} wide>
      {error && <p className="text-sm text-bad">{error}</p>}
      {items?.length === 0 && <p className="text-sm text-ink-faint">{t('history.empty')}</p>}
      <ol className="space-y-3">
        {items?.map((r, i) => {
          const prev = items[i + 1]?.snapshot   // danh sách mới nhất trước → bản trước nằm sau
          const changed = prev ? FIELDS.filter(f => show(f, r.snapshot) !== show(f, prev)) : FIELDS.filter(f => show(f, r.snapshot))
          return (
            <li key={r.id} className="p-3 rounded-xl bg-muted/60 text-sm">
              <p className="font-semibold">
                {t(`history.${r.action}`, { name: r.username })}
                <span className="ml-2 text-xs font-normal text-ink-faint">{formatDateTime(r.createdAt)}</span>
              </p>
              {changed.length === 0 ? <p className="text-xs text-ink-faint mt-1">{t('history.noChange')}</p> : (
                <dl className="mt-1.5 space-y-1">
                  {changed.map(f => (
                    <div key={f} className="grid grid-cols-[5.5rem_minmax(0,1fr)] gap-2">
                      <dt className="text-ink-faint">{t(`history.fields.${f}`)}</dt>
                      <dd className="break-words">
                        {f === 'image' ? t('history.imageChanged') : (
                          <>
                            {prev && <><s className="text-ink-faint">{show(f, prev) || t('history.none')}</s> → </>}
                            {show(f, r.snapshot) || t('history.none')}
                          </>
                        )}
                      </dd>
                    </div>
                  ))}
                </dl>
              )}
            </li>
          )
        })}
      </ol>
    </Sheet>
  )
}
