import { useEffect, useState } from 'react'
import { getProvinceResult, type ProvinceResult as Result } from '../api/client'
import { provinceName } from '../data/provinces'

const WEEKDAYS = ['Chủ Nhật', 'Thứ Hai', 'Thứ Ba', 'Thứ Tư', 'Thứ Năm', 'Thứ Sáu', 'Thứ Bảy']

// Dựng Date từ từng thành phần (giờ địa phương), KHÔNG new Date('YYYY-MM-DD') — chuỗi đó bị
// hiểu là UTC nên máy ở múi giờ âm sẽ lùi sang hôm trước.
const formatDay = (iso: string) => {
  const [y, m, d] = iso.split('-').map(Number)
  return `${WEEKDAYS[new Date(y, m - 1, d).getDay()]}, ${iso.split('-').reverse().join('/')}`
}

// Kết quả đã xổ không đổi nữa → giữ trong phiên, bấm qua lại giữa các đài khỏi tải lại.
const cache = new Map<string, Result>()

type Props = {
  drawDate: string
  province: string
  /** Các đài cùng ngày có dữ liệu — hiện thành chip để chuyển nhanh. */
  sameDay: string[]
  /** Vé đang so (mở từ màn dò vé) — tô các số trùng. */
  ticketNumber?: string
  onSelectProvince: (code: string) => void
  backLabel: string
  onBack: () => void
}

// Cùng cách so của LotteryMatcher: ĐB phải trùng cả 6 số, G.1..G.8 trùng N số cuối.
// Giải phụ ĐB / khuyến khích (suy ra từ ĐB) không tô — màn kết quả dò vé đã liệt kê.
const matches = (ticket: string | undefined, tier: string, n: string) =>
  !!ticket && (tier === 'DB' ? ticket === n : ticket.endsWith(n))

export default function ProvinceResult({
  drawDate, province, sameDay, ticketNumber, onSelectProvince, backLabel, onBack,
}: Props) {
  const key = `${drawDate}/${province}`
  const [fetched, setFetched] = useState<{ key: string; data?: Result; error?: string } | null>(null)

  useEffect(() => {
    if (cache.has(key)) return
    let cancelled = false
    getProvinceResult(drawDate, province)
      .then(data => {
        cache.set(key, data)
        if (!cancelled) setFetched({ key, data })
      })
      .catch(e => { if (!cancelled) setFetched({ key, error: e?.message ?? 'Lỗi không xác định' }) })
    return () => { cancelled = true }
  }, [key, drawDate, province])

  const data = cache.get(key) ?? (fetched?.key === key ? fetched.data : undefined)
  const error = fetched?.key === key ? fetched.error : undefined

  // Hiện như bảng XSMN quen thuộc: G.8 trên cùng, ĐB dưới cùng.
  const rows = data ? [...data.prizes].reverse() : []

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h2 className="text-lg font-bold">Xổ số {provinceName(province)}</h2>
        <button onClick={onBack} className="text-sm text-blue-600">{backLabel}</button>
      </div>

      {ticketNumber && (
        <div className="bg-yellow-50 border border-yellow-300 rounded-xl p-3 text-sm text-yellow-900">
          Đang so với vé <b className="tracking-wider">{ticketNumber}</b> — số trùng được tô vàng.
        </div>
      )}

      {sameDay.length > 1 && (
        <div className="flex flex-wrap gap-2">
          {sameDay.map(code => (
            <button key={code} onClick={() => onSelectProvince(code)}
                    aria-pressed={code === province}
                    className={`text-sm px-2.5 py-1 rounded-full ${code === province
                      ? 'bg-brand-500 text-white' : 'bg-brand-50 text-brand-700'}`}>
              {provinceName(code)}
            </button>
          ))}
        </div>
      )}

      <div className="bg-white rounded-2xl shadow overflow-hidden">
        <div className="bg-brand-50 text-brand-700 text-center font-semibold py-2 text-sm">
          {formatDay(drawDate)}
        </div>

        {!data && !error && <div className="p-6 text-center text-gray-500">Đang tải...</div>}
        {error && <div className="p-6 text-center text-red-600">❌ {error}</div>}

        {data && (
          <table className="w-full">
            <tbody>
              {rows.map(({ tier, numbers }) => {
                const special = tier === 'DB'
                return (
                  <tr key={tier} className="border-t border-gray-100 odd:bg-gray-50/60">
                    <th scope="row"
                        className="w-14 py-2.5 text-sm font-medium text-gray-500 border-r border-gray-100">
                      {special ? 'ĐB' : `G.${tier}`}
                    </th>
                    <td className="py-2.5 px-3">
                      <div className="flex flex-wrap justify-center gap-x-5 gap-y-1">
                        {numbers.map((n, i) => (
                          <span key={i}
                                className={`tabular-nums tracking-wider font-bold ${special
                                  ? 'text-2xl text-brand-600'
                                  : tier === '8' ? 'text-xl text-brand-600' : 'text-lg text-gray-800'
                                } ${matches(ticketNumber, tier, n)
                                  ? 'bg-yellow-200 ring-2 ring-yellow-400 rounded-md px-1.5' : ''}`}>
                            {n}
                          </span>
                        ))}
                      </div>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        )}
      </div>
    </div>
  )
}
