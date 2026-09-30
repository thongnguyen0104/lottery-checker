import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { getCheckStats, type CheckSummary } from '../api/client'
import { currentLocale } from '../i18n'
import Icon, { type IconName } from './Icon'

/** Thẻ "Cộng đồng đã dò": tổng vé đã dò, vé trúng, tổng tiền thưởng của cả hệ thống.
 *  Lỗi / chưa có vé nào thì ẩn luôn — chỉ là thông tin phụ, không chen vào luồng chụp vé. */
export default function SystemStats() {
  const { t } = useTranslation('check')
  const [stats, setStats] = useState<CheckSummary | null>(null)
  useEffect(() => { getCheckStats().then(setStats).catch(() => {}) }, [])
  if (!stats?.tickets) return null

  const num = (n: number) => n.toLocaleString(currentLocale())
  const short = (n: number) => n.toLocaleString(currentLocale(), { maximumFractionDigits: 1 })
  // Tiền thưởng gọn cho ô nhỏ: 2,3 tỷ / 150 triệu; dưới 1 triệu ghi đủ.
  const money = (n: number) =>
    n >= 1e9 ? t('stats.billion', { n: short(n / 1e9) })
    : n >= 1e6 ? t('stats.million', { n: short(n / 1e6) })
    : t('money', { amount: num(n) })

  const tiles: [IconName, string, string][] = [
    ['ticket', t('stats.tickets'), num(stats.tickets)],
    ['trophy', t('stats.winners'), num(stats.winners)],
    ['award', t('stats.prize'), money(stats.totalPrize)],
  ]
  return (
    <div className="card p-4">
      <div className="flex items-center gap-2 font-semibold mb-3">
        <Icon name="sparkles" className="w-[18px] h-[18px] text-brand-700 dark:text-brand-400" />
        {t('stats.title')}
      </div>
      <dl className="grid grid-cols-3 gap-2">
        {tiles.map(([icon, label, value]) => (
          <div key={icon} className="rounded-xl bg-muted/60 border border-line/60 px-2 py-2.5 text-center">
            <dt className="flex items-center justify-center gap-1 text-[11px] text-ink-faint">
              <Icon name={icon} className="w-3.5 h-3.5 shrink-0" /> <span className="truncate">{label}</span>
            </dt>
            <dd className="mt-0.5 font-extrabold tabular-nums text-brand-700 dark:text-brand-400 truncate">{value}</dd>
          </div>
        ))}
      </dl>
    </div>
  )
}
