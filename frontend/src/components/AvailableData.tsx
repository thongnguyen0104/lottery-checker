import { useEffect, useRef, useState } from 'react'
import { getAvailableDraws } from '../api/client'
import { provinceName } from '../data/provinces'
import ProvinceResult from './ProvinceResult'

const formatDate = (iso: string) => {
  const [y, m, d] = iso.split('-')
  return `${d}/${m}/${y}`
}

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
        backLabel={focus ? '← Vé của bạn' : '← Danh sách'}
        onBack={focus ? onBack : closeDetail}
      />
    )
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h2 className="text-lg font-bold">📅 Kết quả xổ số</h2>
        <button onClick={onBack} className="text-sm text-blue-600">← Quay lại</button>
      </div>

      {loading && <div className="p-6 text-center text-gray-500">Đang tải...</div>}
      {error && <div className="p-4 text-center text-red-600">❌ {error}</div>}

      {!loading && !error && data?.length === 0 && (
        <div className="bg-gray-50 border border-gray-200 rounded-2xl p-6 text-center text-sm text-gray-600">
          Chưa có dữ liệu. Hãy chạy cào kết quả (POST <code>/api/admin/fetch</code>) hoặc đợi worker tự cào lúc 19h.
        </div>
      )}

      {!loading && !error && !!data?.length && (
        <p className="text-sm text-gray-500">Chạm vào tên đài để xem bảng kết quả chi tiết.</p>
      )}

      {!loading && !error && data?.map(d => (
        <div key={d.drawDate} className="bg-white rounded-2xl shadow p-4">
          <div className="font-semibold text-brand-600 mb-2">{formatDate(d.drawDate)}</div>
          <div className="flex flex-wrap gap-2">
            {d.provinces.map(code => (
              <button key={code} onClick={() => openDetail(d.drawDate, code)}
                      className="bg-brand-50 text-brand-700 text-sm px-2.5 py-1 rounded-full
                                 hover:bg-brand-500 hover:text-white active:bg-brand-600 active:text-white">
                {provinceName(code)} ›
              </button>
            ))}
          </div>
        </div>
      ))}
    </div>
  )
}
