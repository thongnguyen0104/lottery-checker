import { useEffect, useRef, useState } from 'react'
import { getAvailableDraws } from '../api/client'
import { provinceName } from '../data/provinces'
import ProvinceResult from './ProvinceResult'
import Icon from './Icon'
import { formatDate, todayIso, weekday } from '../utils/date'

/** Mở thẳng bảng của 1 đài từ màn kết quả dò vé — tô các số trùng với vé đó. */
export type ResultsFocus = { drawDate: string; province: string; ticketNumber: string }

type Props = {
  /** Có = mở từ vé vừa dò: vào thẳng bảng đài đó, nút quay lại đưa về vé (onBack). */
  focus?: ResultsFocus | null
  onBack: () => void
}

export default function AvailableData({ focus, onBack }: Props) {
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
      .catch(e => setError(e?.message ?? 'Lỗi không xác định'))
      .finally(() => setLoading(false))
  }, [])

  const openDetail = (drawDate: string, province: string) => {
    listScrollY.current = window.scrollY
    setSelected({ drawDate, province })
    window.scrollTo(0, 0)
  }

  const closeDetail = () => {
    setSelected(null)
    requestAnimationFrame(() => window.scrollTo(0, listScrollY.current))
  }

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
        backLabel={focus ? 'Vé của bạn' : 'Danh sách'}
        onBack={focus ? onBack : closeDetail}
      />
    )
  }

  const today = todayIso()

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between gap-3">
        <div>
          <h1 className="text-xl md:text-3xl font-extrabold tracking-tight">Kết quả xổ số 📅</h1>
          {!loading && !error && !!data?.length && (
            <p className="text-sm md:text-base text-ink-soft mt-0.5">Chạm vào tên đài để xem bảng kết quả chi tiết.</p>
          )}
        </div>
        <button onClick={onBack} className="btn btn-soft shrink-0 px-3 py-2 text-sm">
          <Icon name="back" className="w-4 h-4" /> Quay lại
        </button>
      </div>

      {error && (
        <div className="card p-6 text-center">
          <div className="text-4xl mb-2">😵‍💫</div>
          <div className="text-sm text-bad">{error}</div>
        </div>
      )}

      {!loading && !error && data?.length === 0 && (
        <div className="card p-6 text-center text-sm text-ink-soft">
          <div className="text-4xl mb-2">📭</div>
          Chưa có dữ liệu. Hãy chạy cào kết quả (POST <code>/api/admin/fetch</code>) hoặc đợi worker tự cào lúc 19h.
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
              <span className="w-1.5 h-5 rounded-full bg-gradient-to-b from-brand-500 to-accent" aria-hidden />
              <span className="font-bold">{weekday(d.drawDate)}</span>
              <span className="text-ink-faint">{formatDate(d.drawDate)}</span>
              {d.drawDate === today && (
                <span className="ml-auto text-[11px] font-bold uppercase tracking-wide text-white rounded-full px-2 py-0.5
                                 bg-gradient-to-r from-brand-600 to-accent">
                  Hôm nay
                </span>
              )}
            </div>
            <div className="flex flex-wrap gap-2">
              {d.provinces.map(code => (
                <button key={code} onClick={() => openDetail(d.drawDate, code)}
                        className="text-sm font-medium px-3 py-1.5 rounded-full transition
                                   bg-brand-500/10 text-brand-700 dark:text-brand-300
                                   hover:bg-brand-500 hover:text-white active:bg-brand-600 active:text-white
                                   dark:hover:text-white">
                  {provinceName(code)} ›
                </button>
              ))}
            </div>
          </div>
        ))}
      </div>
    </div>
  )
}
