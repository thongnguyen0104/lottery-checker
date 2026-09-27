import { useEffect, useState } from 'react'
import { getProvinceResult, type ProvinceResult as Result } from '../api/client'
import { provinceName } from '../data/provinces'
import { formatDay } from '../utils/date'
import Icon from './Icon'

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
    <div className="space-y-4 max-w-2xl mx-auto">
      <div className="flex items-center justify-between gap-3">
        <h1 className="text-xl md:text-3xl font-extrabold tracking-tight">Xổ số {provinceName(province)}</h1>
        <button onClick={onBack} className="btn btn-soft shrink-0 px-3 py-2 text-sm">
          <Icon name="back" className="w-4 h-4" /> {backLabel}
        </button>
      </div>

      {ticketNumber && (
        <div className="alert bg-yellow-400/15 border-yellow-400/50">
          Đang so với vé <b className="tracking-wider">{ticketNumber}</b> — số trùng được tô vàng.
        </div>
      )}

      {sameDay.length > 1 && (
        <div className="flex flex-wrap gap-2">
          {sameDay.map(code => (
            <button key={code} onClick={() => onSelectProvince(code)}
                    aria-pressed={code === province}
                    className={`text-sm font-medium px-3 py-1.5 rounded-full transition ${code === province
                      ? 'bg-gradient-to-r from-brand-600 to-accent text-white shadow-md shadow-brand-500/25'
                      : 'bg-brand-500/10 text-brand-700 dark:text-brand-300 hover:bg-brand-500/20'}`}>
              {provinceName(code)}
            </button>
          ))}
        </div>
      )}

      <div className="card overflow-hidden">
        <div className="bg-gradient-to-r from-brand-600 to-accent text-white text-center font-semibold py-2.5 text-sm">
          {formatDay(drawDate)}
        </div>

        {!data && !error && (
          <div className="p-6 flex items-center justify-center gap-2 text-ink-faint">
            <span className="w-5 h-5 rounded-full border-2 border-brand-500/20 border-t-brand-500 motion-safe:animate-spin" />
            Đang tải...
          </div>
        )}
        {error && <div className="p-6 text-center text-bad">😵‍💫 {error}</div>}

        {data && (
          <table className="w-full">
            <tbody>
              {rows.map(({ tier, numbers }) => {
                const special = tier === 'DB'
                return (
                  <tr key={tier} className="border-t border-line/70 first:border-t-0 odd:bg-muted/50">
                    <th scope="row"
                        className="w-14 py-2.5 text-sm font-semibold text-ink-faint border-r border-line/70">
                      {special ? 'ĐB' : `G.${tier}`}
                    </th>
                    <td className="py-2.5 px-3">
                      <div className="flex flex-wrap justify-center gap-x-5 gap-y-1">
                        {numbers.map((n, i) => (
                          <span key={i}
                                className={`tabular-nums tracking-wider font-bold ${special
                                  ? 'text-2xl md:text-3xl text-brand-600 dark:text-brand-400'
                                  : tier === '8' ? 'text-xl text-brand-600 dark:text-brand-400' : 'text-lg text-ink'
                                } ${matches(ticketNumber, tier, n)
                                  ? 'bg-yellow-300 text-yellow-950 dark:text-yellow-950 ring-2 ring-yellow-400 rounded-md px-1.5' : ''}`}>
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
