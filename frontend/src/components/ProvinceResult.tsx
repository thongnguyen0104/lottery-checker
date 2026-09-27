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

/** Số chữ số cuối trùng nhau giữa vé và số giải (0 = không trùng, vd 912990 & 8730 → 1). */
function commonSuffix(ticket: string | undefined, n: string) {
  if (!ticket) return 0
  let k = 0
  while (k < n.length && k < ticket.length && n[n.length - 1 - k] === ticket[ticket.length - 1 - k]) k++
  return k
}

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
        <div className="alert flex items-center gap-2 bg-ok/10 border-ok/30">
          <Icon name="ticket" className="w-4 h-4 shrink-0 text-ok" />
          <span>
            Đang so với vé <b className="tracking-wider">{ticketNumber}</b> — chữ số cuối trùng vé được tô xanh,
            số trúng giải có khung.
          </span>
        </div>
      )}

      {sameDay.length > 1 && (
        <div className="flex flex-wrap gap-2">
          {sameDay.map(code => (
            <button key={code} onClick={() => onSelectProvince(code)}
                    aria-pressed={code === province}
                    className={`text-sm font-medium px-3 py-1.5 rounded-full border transition ${code === province
                      ? 'border-transparent bg-gradient-to-r from-primary to-primary-end text-on-primary shadow-md shadow-primary/20'
                      : 'border-line bg-muted/60 text-ink-soft hover:border-brand-500/60 hover:text-brand-700 dark:hover:text-brand-400'}`}>
              {provinceName(code)}
            </button>
          ))}
        </div>
      )}

      <div className="card overflow-hidden">
        <div className="flex items-center justify-center gap-2 py-2.5 text-sm font-semibold border-b border-line
                        bg-muted/60 text-brand-700 dark:text-brand-400">
          <Icon name="calendar" className="w-4 h-4" /> {formatDay(drawDate)}
        </div>

        {!data && !error && (
          <div className="p-6 flex items-center justify-center gap-2 text-ink-faint">
            <span className="w-5 h-5 rounded-full border-2 border-brand-500/20 border-t-brand-500 motion-safe:animate-spin" />
            Đang tải...
          </div>
        )}
        {error && (
          <div className="p-6 flex items-center justify-center gap-2 text-bad">
            <Icon name="error" className="w-5 h-5" /> {error}
          </div>
        )}

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
                        {numbers.map((n, i) => {
                          // Trùng hết cả số = trúng giải (cùng cách so của LotteryMatcher: ĐB trùng đủ 6
                          // số, G.1..G.8 trùng N số cuối) → khung xanh cả số. Trùng một phần đuôi → chỉ tô
                          // các chữ số đó, để thấy vé "suýt trúng" giải nào (với ĐB là cả giải phụ).
                          const k = commonSuffix(ticketNumber, n)
                          const won = k === n.length
                          return (
                            <span key={i}
                                  className={`tabular-nums tracking-wider font-bold ${
                                    special ? 'text-2xl md:text-3xl' : tier === '8' ? 'text-xl' : 'text-lg'
                                  } ${
                                    // ĐB và G.8 màu brand như bảng XSMN quen thuộc.
                                    won
                                      ? 'bg-ok/15 text-ok ring-1 ring-ok/60 rounded-md px-1.5'
                                      : special || tier === '8' ? 'text-brand-700 dark:text-brand-400' : 'text-ink'
                                  }`}>
                              {won || k === 0 ? n : (
                                <>
                                  {n.slice(0, -k)}
                                  <mark className="bg-ok/20 text-ok rounded px-0.5">{n.slice(-k)}</mark>
                                </>
                              )}
                            </span>
                          )
                        })}
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
