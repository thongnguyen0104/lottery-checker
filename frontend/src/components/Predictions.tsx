import { useEffect, useRef, useState, type ReactNode } from 'react'
import type { TFunction } from 'i18next'
import { useTranslation } from 'react-i18next'
import { getPrediction, type NumberStat, type Prediction, type ProvincePrediction } from '../api/client'
import { animalOf } from '../data/animals'
import { provinceName } from '../data/provinces'
import { currentLocale } from '../i18n'
import { addDays, formatDate, formatDayMonth, todayIso, weekday } from '../utils/date'
import Icon, { IconBadge } from './Icon'

/** Số kỳ "gần đây" backend dùng để chấm điểm (PredictionService.RecentDraws). */
const RECENT_DRAWS = 10

const pct = (p: number) =>
  new Intl.NumberFormat(currentLocale(), { style: 'percent', maximumFractionDigits: 1 }).format(p)

/** Màn Dự đoán: chọn ngày → mỗi đài xổ ngày đó một thẻ gợi ý (số nóng, lô gan, gợi ý ĐB, bảng 00–99). */
export default function Predictions() {
  const { t } = useTranslation('predict')
  const [date, setDate] = useState<string | null>(null)   // null = để máy chủ chọn kỳ kế tiếp
  // Mốc của dải chọn ngày = kỳ kế tiếp máy chủ trả lần đầu; giữ nguyên khi user bấm sang ngày khác.
  const [base, setBase] = useState<string | null>(null)
  const [data, setData] = useState<Prediction | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [reload, setReload] = useState(0)

  // Đổi ngày / thử lại: bật trạng thái tải ngay lúc bấm, effect bên dưới lo gọi máy chủ.
  const load = (d: string | null) => {
    setLoading(true)
    setError(null)
    setDate(d)
    setReload(n => n + 1)
  }

  useEffect(() => {
    let alive = true
    getPrediction(date ?? undefined)
      .then(d => {
        if (!alive) return
        setData(d)
        setBase(b => b ?? d.date)
      })
      .catch(e => alive && setError(e?.message ?? ''))
      .finally(() => alive && setLoading(false))
    return () => { alive = false }
  }, [date, reload])

  const today = todayIso()
  const days = base ? Array.from({ length: 10 }, (_, i) => addDays(base, i - 3)) : []
  const selected = date ?? data?.date

  return (
    <div className="space-y-4">
      <div>
        <h1 className="text-xl md:text-3xl font-extrabold tracking-tight">{t('title')}</h1>
        <p className="text-sm md:text-base text-ink-soft mt-0.5">{t('hint')}</p>
      </div>

      {days.length > 0 && <DateStrip days={days} selected={selected} today={today} onSelect={load} />}

      {data && (
        <p className="flex gap-2 text-xs text-ink-faint rounded-xl border border-line/60 bg-muted/60 px-3 py-2">
          <Icon name="tip" className="w-4 h-4 shrink-0 mt-px" />
          {t('disclaimer', { baseline: pct(data.baseline) })}
        </p>
      )}

      {error && (
        <div className="card p-6 text-center space-y-3">
          <IconBadge name="error" tone="bad" />
          <div className="text-sm text-bad">{error}</div>
          <button onClick={() => load(date)} className="btn btn-soft">
            <Icon name="retry" className="w-4 h-4" /> {t('retry')}
          </button>
        </div>
      )}

      {!error && loading && !data && (
        <div className="grid gap-4 md:grid-cols-2">
          {Array.from({ length: 3 }, (_, i) => (
            <div key={i} className="card p-4 motion-safe:animate-pulse space-y-3">
              <div className="h-5 w-32 rounded-full bg-muted" />
              <div className="flex gap-2">{Array.from({ length: 6 }, (_, j) => <div key={j} className="h-11 w-11 rounded-full bg-muted" />)}</div>
              <div className="h-4 w-48 rounded-full bg-muted" />
            </div>
          ))}
        </div>
      )}

      {!error && data && (
        <div className={`grid gap-4 md:grid-cols-2 transition-opacity ${loading ? 'opacity-50' : ''}`} aria-busy={loading}>
          {data.provinces.length === 0 && (
            <div className="card p-6 text-center text-sm text-ink-soft md:col-span-2">{t('noDraw')}</div>
          )}
          {data.provinces.map(p => <ProvinceCard key={`${data.date}/${p.province}`} p={p} />)}
        </div>
      )}
    </div>
  )
}

function DateStrip({ days, selected, today, onSelect }: {
  days: string[]; selected?: string; today: string; onSelect: (d: string) => void
}) {
  const { t } = useTranslation('predict')
  const ref = useRef<HTMLDivElement>(null)
  // Cuộn ngày đang chọn vào giữa dải (điện thoại chỉ thấy ~5 ngày).
  useEffect(() => {
    ref.current?.querySelector('[aria-pressed="true"]')?.scrollIntoView({ inline: 'center', block: 'nearest' })
  }, [selected])
  return (
    <div ref={ref} className="-mx-4 px-4 flex gap-2 overflow-x-auto pb-1 [scrollbar-width:none]">
      {days.map(d => {
        const active = d === selected
        const label = d === today ? t('today') : d === addDays(today, 1) ? t('tomorrow') : weekday(d)
        return (
          <button key={d} onClick={() => onSelect(d)} aria-pressed={active}
                  className={`shrink-0 flex flex-col items-center min-w-[4.5rem] px-3 py-2 rounded-xl border text-xs transition active:scale-95
                              ${active
                                ? 'border-transparent bg-gradient-to-r from-primary to-primary-end text-on-primary shadow-md shadow-primary/20'
                                : 'border-line bg-surface text-ink-soft hover:border-brand-500/60'}`}>
            <span className="font-semibold whitespace-nowrap">{label}</span>
            <span className={active ? 'opacity-90' : 'text-ink-faint'}>{formatDayMonth(d)}</span>
          </button>
        )
      })}
    </div>
  )
}

function ProvinceCard({ p }: { p: ProvincePrediction }) {
  const { t } = useTranslation('predict')
  const [table, setTable] = useState(false)
  const actual = p.actual ? new Set(p.actual) : null
  const hitCount = actual ? p.top.filter(s => actual.has(s.number)).length : 0

  return (
    <div className="card p-4 space-y-4">
      <div className="flex items-start gap-2">
        <span className="mt-1 w-1.5 h-5 rounded-full bg-gradient-to-b from-primary to-primary-end" aria-hidden />
        <div className="min-w-0">
          <h2 className="font-bold text-lg leading-tight">{provinceName(p.province)}</h2>
          {p.draws > 0 && (
            <p className="text-xs text-ink-faint">
              {t('basedOn', { count: p.draws, from: formatDate(p.from!), to: formatDate(p.to!) })}
            </p>
          )}
        </div>
      </div>

      {p.draws === 0 ? <p className="text-sm text-ink-soft">{t('noData')}</p> : (
        <>
          <Section icon="hot" title={t('hot')} hint={t('hotHint', { recent: RECENT_DRAWS })}>
            <div className="flex flex-wrap gap-2">
              {p.top.map(s => <Ball key={s.number} stat={s} total={p.draws} hit={actual?.has(s.number)} />)}
            </div>
            {actual && (
              <p className={`mt-2 text-xs font-semibold ${hitCount ? 'text-ok' : 'text-ink-faint'}`}>
                {hitCount ? t('actual', { count: hitCount, total: p.top.length }) : t('actualNone')}
              </p>
            )}
          </Section>

          <Section icon="cold" title={t('overdue')} hint={t('overdueHint')}>
            <div className="flex flex-wrap gap-2">
              {p.overdue.map(s => {
                const animal = animalOf(s.number)
                return (
                  <span key={s.number} title={withAnimal(t, s.number, t('hitsOf', { hits: s.hits, draws: p.draws }))}
                        className={`inline-flex items-baseline gap-1.5 rounded-full border px-3 py-1.5 text-sm
                                    ${actual?.has(s.number) ? 'border-ok bg-ok/10' : 'border-line bg-muted/60'}`}>
                    {animal && <span aria-hidden className="self-center text-base leading-none">{animal.emoji}</span>}
                    <b className="font-mono tabular-nums">{s.number}</b>
                    <span className="text-xs text-ink-faint">{t('gap', { count: s.gap })}</span>
                  </span>
                )
              })}
            </div>
          </Section>

          {p.special && (
            <Section icon="trophy" title={t('special')} hint={t('specialHint')}>
              <div className="flex gap-1.5">
                {p.special.split('').map((c, i) => (
                  <span key={i} className="w-9 h-11 rounded-lg flex items-center justify-center font-mono text-xl font-extrabold
                                           bg-muted border border-line text-brand-700 dark:text-brand-400">{c}</span>
                ))}
              </div>
            </Section>
          )}

          <div>
            <button onClick={() => setTable(v => !v)} aria-expanded={table}
                    className="inline-flex items-center gap-1 text-sm font-semibold text-brand-700 dark:text-brand-400">
              {table ? t('hideTable') : t('showTable')}
              <Icon name="down" className={`w-4 h-4 transition-transform ${table ? 'rotate-180' : ''}`} />
            </button>
            {table && <HeatGrid stats={p.all} draws={p.draws} actual={actual} />}
          </div>
        </>
      )}
    </div>
  )
}

function Section({ icon, title, hint, children }: {
  icon: 'hot' | 'cold' | 'trophy'; title: string; hint: string; children: ReactNode
}) {
  return (
    <section>
      <h3 className="flex items-center gap-1.5 text-sm font-bold">
        <Icon name={icon} className={`w-4 h-4 ${icon === 'hot' ? 'text-bad' : icon === 'cold' ? 'text-info' : 'text-warn'}`} />
        {title}
      </h3>
      <p className="text-xs text-ink-faint mb-2">{hint}</p>
      {children}
    </section>
  )
}

/** Tooltip kèm con vật của số (nếu có): "07 · Con heo — Về 5 lần / 50 kỳ". */
function withAnimal(t: TFunction<'predict'>, number: string, text: string) {
  const animal = animalOf(number)
  return animal ? `${number} · ${t(`animals.${animal.key}`)} — ${text}` : text
}

/** Viên số gợi ý + con vật + xác suất bên dưới; hit = ngày đã có kết quả và số này về thật. */
function Ball({ stat, total, hit }: { stat: NumberStat; total: number; hit?: boolean }) {
  const { t } = useTranslation('predict')
  const animal = animalOf(stat.number)
  return (
    <span className="flex flex-col items-center gap-1" title={withAnimal(t, stat.number, t('hitsOf', { hits: stat.hits, draws: total }))}>
      <span className={`relative w-11 h-11 rounded-full flex items-center justify-center font-mono text-lg font-extrabold shadow-md
                        bg-gradient-to-br from-primary to-primary-end text-on-primary
                        ${hit ? 'ring-4 ring-ok/60' : ''}`}>
        {stat.number}
        {animal && (
          <span aria-hidden className="absolute -bottom-1 -left-1.5 w-6 h-6 rounded-full bg-surface border border-line shadow-sm
                                       flex items-center justify-center text-sm leading-none">{animal.emoji}</span>
        )}
        {hit && (
          <span className="absolute -top-1 -right-1 w-4 h-4 rounded-full bg-ok text-white flex items-center justify-center">
            <Icon name="check" className="w-3 h-3" strokeWidth={3} />
          </span>
        )}
      </span>
      <span className="text-[11px] text-ink-faint tabular-nums">{pct(stat.probability)}</span>
      {animal && <span className="text-[10px] leading-none text-ink-soft whitespace-nowrap">{t(`animals.${animal.key}`)}</span>}
    </span>
  )
}

/** Bảng 10×10 số 00–99: độ đậm theo xác suất về trong 1 năm. */
function HeatGrid({ stats, draws, actual }: { stats: NumberStat[]; draws: number; actual: Set<string> | null }) {
  const { t } = useTranslation('predict')
  const [picked, setPicked] = useState<NumberStat | null>(null)
  const max = Math.max(...stats.map(s => s.probability), 0.0001)
  const pickedAnimal = picked && animalOf(picked.number)
  return (
    <div className="mt-3">
      <p className="text-xs text-ink-faint mb-2">{t('tableHint')}</p>
      <div className="grid grid-cols-10 gap-1">
        {stats.map(s => {
          const a = s.probability / max
          return (
            <button key={s.number} onClick={() => setPicked(s)}
                    style={{ backgroundColor: `rgb(var(--brand-500) / ${(0.08 + a * 0.82).toFixed(2)})` }}
                    className={`aspect-square rounded-md text-[11px] font-mono font-semibold tabular-nums
                                ${a > 0.55 ? 'text-white' : 'text-ink'}
                                ${actual?.has(s.number) ? 'ring-2 ring-ok' : ''}
                                ${picked?.number === s.number ? 'outline outline-2 outline-ink' : ''}`}>
              {s.number}
            </button>
          )
        })}
      </div>
      {picked && (
        <p className="mt-2 text-sm">
          <b className="font-mono">{picked.number}</b>
          {pickedAnimal && <> {pickedAnimal.emoji} {t(`animals.${pickedAnimal.key}`)}</>}
          {' · '}{t('probability', { value: pct(picked.probability) })}
          {' · '}{t('hitsOf', { hits: picked.hits, draws })}
          {' · '}{t('overdue')}: {t('gap', { count: picked.gap })}
        </p>
      )}
    </div>
  )
}
