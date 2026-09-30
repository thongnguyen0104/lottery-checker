import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { getAvailableDraws } from '../api/client'
import { provinceName } from '../data/provinces'
import ProvinceResult from './ProvinceResult'
import Icon, { IconBadge } from './Icon'
import { formatDate, todayIso, weekday } from '../utils/date'

/** Mở thẳng bảng của 1 đài từ màn kết quả dò vé — tô các số trùng với vé đó. */
/** from = màn đã mở bảng này (mặc định Dò vé), để nút quay lại đưa về đúng chỗ. */
export type ResultsFocus = { drawDate: string; province: string; ticketNumber: string; from?: 'check' | 'lucky' | 'profile' }

type Props = {
  /** Có = mở từ vé vừa dò: vào thẳng bảng đài đó, nút quay lại đưa về vé (onBack). */
  focus?: ResultsFocus | null
  onBack: () => void
}

export default function AvailableData({ focus, onBack }: Props) {
  const { t } = useTranslation('results')
  const [data, setData] = useState<{ drawDate: string; provinces: string[] }[] | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [selected, setSelected] = useState<{ drawDate: string; province: string } | null>(
    focus ? { drawDate: focus.drawDate, province: focus.province } : null)
  // Danh sách 30 ngày khá dài: xem xong 1 đài quay về đúng chỗ đang cuộn tới.
  const listScrollY = useRef(0)

  useEffect(() => {
    getAvailableDraws()
      .then(setData)
      .catch(e => setError(e?.message ?? t('unknownError')))
      .finally(() => setLoading(false))
  }, [])

  // Mở bảng 1 đài = 1 mục lịch sử: nút Quay lại và Back của trình duyệt cùng đưa về danh sách.
  const openDetail = (drawDate: string, province: string) => {
    listScrollY.current = window.scrollY
    setSelected({ drawDate, province })
    window.scrollTo(0, 0)
    history.pushState({ view: 'results', focus: null, detail: { drawDate, province } }, '')
  }

  const closeDetail = () => history.back()

  useEffect(() => {
    if (focus) return   // mở theo vé: Back là rời màn Kết quả, App lo
    const onPop = (e: PopStateEvent) => {
      const st = e.state as { view?: string; detail?: { drawDate: string; province: string } } | null
      if (st?.view !== 'results') return
      setSelected(st.detail ?? null)
      if (st.detail) window.scrollTo(0, 0)
      else requestAnimationFrame(() => window.scrollTo(0, listScrollY.current))
    }
    window.addEventListener('popstate', onPop)
    return () => window.removeEventListener('popstate', onPop)
  }, [focus])

  if (selected) {
    // Chỉ tô số trên đúng bảng của vé — chuyển sang đài khác cùng ngày thì vé không liên quan.
    const ofTicket = focus && focus.drawDate === selected.drawDate && focus.province === selected.province
    return (
      <ProvinceResult
        drawDate={selected.drawDate}
        province={selected.province}
        sameDay={data?.find(d => d.drawDate === selected.drawDate)?.provinces ?? []}
        ticketNumber={ofTicket ? focus.ticketNumber : undefined}
        onSelectProvince={province => setSelected({ ...selected, province })}
        backLabel={focus ? t(`backLabel.${focus.from === 'lucky' || focus.from === 'profile' ? focus.from : 'ticket'}`) : t('backLabel.list')}
        onBack={focus ? onBack : closeDetail}
      />
    )
  }

  const today = todayIso()

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between gap-3">
        <div>
          <h1 className="text-xl md:text-3xl font-extrabold tracking-tight">{t('title')}</h1>
          {!loading && !error && !!data?.length && (
            <p className="text-sm md:text-base text-ink-soft mt-0.5">{t('hint')}</p>
          )}
        </div>
        <button onClick={onBack} className="btn btn-soft shrink-0 px-3 py-2 text-sm">
          <Icon name="back" className="w-4 h-4" /> {t('back')}
        </button>
      </div>

      {error && (
        <div className="card p-6 text-center">
          <IconBadge name="error" tone="bad" />
          <div className="text-sm text-bad mt-3">{error}</div>
        </div>
      )}

      {!loading && !error && data?.length === 0 && (
        <div className="card p-6 text-center text-sm text-ink-soft">
          <IconBadge name="empty" tone="info" />
          <p className="mt-3">
            {t('empty')}
          </p>
        </div>
      )}

      <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
        {/* Khung xương trong lúc tải: giữ đúng bố cục, đỡ giật trang khi dữ liệu về */}
        {loading && Array.from({ length: 6 }, (_, i) => (
          <div key={i} className="card p-4 motion-safe:animate-pulse">
            <div className="h-4 w-32 rounded-full bg-muted mb-4" />
            <div className="flex flex-wrap gap-2">
              {['w-20', 'w-24', 'w-16'].map(w => <div key={w} className={`h-8 ${w} rounded-full bg-muted`} />)}
            </div>
          </div>
        ))}

        {!loading && !error && data?.map(d => (
          <div key={d.drawDate} className="card p-4">
            <div className="flex items-center gap-2 mb-3">
              <span className="w-1.5 h-5 rounded-full bg-gradient-to-b from-primary to-primary-end" aria-hidden />
              <span className="font-bold">{weekday(d.drawDate)}</span>
              <span className="text-ink-faint">{formatDate(d.drawDate)}</span>
              {d.drawDate === today && (
                <span className="ml-auto text-[11px] font-bold uppercase tracking-wide text-on-primary rounded-full px-2 py-0.5
                                 bg-gradient-to-r from-primary to-primary-end">
                  {t('today')}
                </span>
              )}
            </div>
            {/* Chip trung tính, chỉ ánh màu brand khi rê/chạm: cả trang toàn chip vàng thì loè */}
            <div className="flex flex-wrap gap-2">
              {d.provinces.map(code => (
                <button key={code} onClick={() => openDetail(d.drawDate, code)}
                        className="inline-flex items-center gap-0.5 text-sm font-medium pl-3 pr-2 py-1.5 rounded-full
                                   border border-line bg-muted/60 text-ink-soft transition
                                   hover:border-brand-500/60 hover:text-brand-700 dark:hover:text-brand-400
                                   active:bg-brand-500/10">
                  {provinceName(code)} <Icon name="next" className="w-3.5 h-3.5 opacity-60" />
                </button>
              ))}
            </div>
          </div>
        ))}
      </div>
    </div>
  )
}
