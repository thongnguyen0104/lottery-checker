import { useEffect, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import {
  getVietlottResults, getVietlottStats,
  type VietlottDraw, type VietlottGame, type VietlottNumberStat, type VietlottResults, type VietlottStats,
} from '../api/client'
import { currentLocale } from '../i18n'
import { formatDate, weekday } from '../utils/date'
import Icon, { IconBadge } from './Icon'

const GAMES: { id: VietlottGame; name: string }[] = [
  { id: '645', name: 'Mega 6/45' },
  { id: '655', name: 'Power 6/55' },
]

// Mega đỏ, Power vàng cam — cùng màu bi với màn Số may mắn (màu nhận diện vé Vietlott, không theo theme).
const BALL: Record<VietlottGame, string> = {
  '645': 'bg-red-500 text-white',
  '655': 'bg-amber-400 text-amber-950',
}
const BONUS_BALL = 'bg-surface text-amber-700 dark:text-amber-400 ring-2 ring-amber-400'

/** Khoảng thống kê (VietlottController.Windows): 0 = toàn bộ lịch sử. */
const WINDOWS = [0, 500, 200, 100, 50]
const PAGE_SIZE = 20

const pad = (n: number) => String(n).padStart(2, '0')
const pct = (p: number) =>
  new Intl.NumberFormat(currentLocale(), { style: 'percent', maximumFractionDigits: 1 }).format(p)
const int = (n: number) => new Intl.NumberFormat(currentLocale()).format(n)

type Tab = 'results' | 'stats'

/** Màn Vietlott: kết quả Mega 6/45 / Power 6/55 từ kỳ đầu + thống kê, gợi ý số kỳ tới. */
export default function Vietlott() {
  const { t } = useTranslation('vietlott')
  const [game, setGame] = useState<VietlottGame>('645')
  const [tab, setTab] = useState<Tab>('results')

  return (
    <div className="space-y-4">
      <div>
        <h1 className="text-xl md:text-3xl font-extrabold tracking-tight">{t('title')}</h1>
        <p className="text-sm md:text-base text-ink-soft mt-0.5">{t('hint')}</p>
      </div>

      <div className="grid grid-cols-2 gap-1 bg-muted p-1 rounded-xl border border-line/60" role="tablist">
        {GAMES.map(g => (
          <button key={g.id} role="tab" aria-selected={g.id === game} onClick={() => setGame(g.id)}
                  className={`py-2.5 rounded-lg text-sm font-semibold transition active:scale-95 ${g.id === game
                    ? 'tab-pop bg-surface dark:bg-brand-500/10 shadow-sm text-brand-700 dark:text-brand-400'
                    : 'text-ink-faint hover:text-ink-soft'}`}>
            {g.name}
          </button>
        ))}
      </div>

      <div className="flex gap-2">
        {(['results', 'stats'] as const).map(x => (
          <button key={x} onClick={() => setTab(x)} aria-pressed={x === tab}
                  className={`flex items-center gap-1.5 px-3.5 py-2 rounded-full text-sm font-semibold transition active:scale-95
                              ${x === tab ? 'bg-brand-500 text-white' : 'bg-muted text-ink-soft hover:text-ink'}`}>
            <Icon name={x === 'results' ? 'list' : 'predict'} className="w-4 h-4" />
            {t(`tabs.${x}`)}
          </button>
        ))}
      </div>

      {/* key: đổi loại vé thì mount lại — khỏi lẫn dữ liệu 2 loại khi đang tải */}
      {tab === 'results' ? <ResultsList key={game} game={game} /> : <StatsView key={game} game={game} />}
    </div>
  )
}

function ErrorCard({ message, onRetry }: { message: string; onRetry: () => void }) {
  const { t } = useTranslation('vietlott')
  return (
    <div className="card p-6 text-center space-y-3">
      <IconBadge name="error" tone="bad" />
      <div className="text-sm text-bad">{message}</div>
      <button onClick={onRetry} className="btn btn-soft">
        <Icon name="retry" className="w-4 h-4" /> {t('retry')}
      </button>
    </div>
  )
}

function Balls({ game, numbers, bonus, size = 'md', highlight }: {
  game: VietlottGame; numbers: number[]; bonus?: number | null; size?: 'sm' | 'md'; highlight?: Set<number>
}) {
  const cls = size === 'sm' ? 'w-7 h-7 text-xs' : 'w-9 h-9 sm:w-10 sm:h-10 text-sm sm:text-base'
  return (
    <span className="flex flex-wrap items-center gap-1.5">
      {numbers.map(n => (
        <span key={n} className={`ball rounded-full flex items-center justify-center font-extrabold tabular-nums ${cls}
                                   ${highlight && !highlight.has(n) ? 'bg-muted text-ink-faint' : BALL[game]}`}>
          {pad(n)}
        </span>
      ))}
      {bonus != null && (
        <>
          <span className="text-ink-faint px-0.5">|</span>
          <span className={`rounded-full flex items-center justify-center font-extrabold tabular-nums ${cls} ${BONUS_BALL}`}>
            {pad(bonus)}
          </span>
        </>
      )}
    </span>
  )
}

function NextDraw({ date }: { date: string }) {
  const { t } = useTranslation('vietlott')
  return (
    <p className="flex items-center gap-2 text-sm rounded-xl border border-line/60 bg-muted/60 px-3 py-2">
      <Icon name="clock" className="w-4 h-4 text-ink-faint shrink-0" />
      {t('nextDraw', { day: weekday(date), date: formatDate(date) })}
    </p>
  )
}

// ── Tab Kết quả ──

function ResultsList({ game }: { game: VietlottGame }) {
  const { t } = useTranslation('vietlott')
  const [items, setItems] = useState<VietlottDraw[]>([])
  const [meta, setMeta] = useState<VietlottResults | null>(null)
  const [page, setPage] = useState(0)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [reload, setReload] = useState(0)

  useEffect(() => {
    let alive = true
    getVietlottResults(game, page, PAGE_SIZE)
      .then(r => {
        if (!alive) return
        setMeta(r)
        setItems(prev => page === 0 ? r.items : [...prev, ...r.items])
      })
      .catch(e => alive && setError(e?.message ?? ''))
      .finally(() => alive && setLoading(false))
    return () => { alive = false }
  }, [game, page, reload])

  const more = () => { setLoading(true); setPage(p => p + 1) }
  const retry = () => { setError(null); setLoading(true); setReload(n => n + 1) }

  if (error && items.length === 0) return <ErrorCard message={error} onRetry={retry} />

  return (
    <div className="space-y-3">
      {meta && <NextDraw date={meta.nextDraw} />}
      {meta && meta.total === 0 && (
        <div className="card p-6 text-center text-sm text-ink-soft">{t('empty')}</div>
      )}
      {items.length > 0 && (
        <ul className="card divide-y divide-line/60">
          {items.map((d, i) => (
            <li key={d.drawId} className={`flex flex-col sm:flex-row sm:items-center gap-2 sm:gap-4 px-4 py-3
                                           ${i === 0 ? 'bg-brand-500/5' : ''}`}>
              <div className="sm:w-44 shrink-0">
                <div className="text-sm font-bold">
                  {t('drawNo', { id: String(d.drawId).padStart(5, '0') })}
                  {i === 0 && <span className="ml-2 text-[11px] font-semibold text-brand-700 dark:text-brand-400">{t('latest')}</span>}
                </div>
                <div className="text-xs text-ink-faint">{weekday(d.date)}, {formatDate(d.date)}</div>
              </div>
              <Balls game={game} numbers={d.numbers} bonus={d.bonus} />
            </li>
          ))}
        </ul>
      )}
      {loading && <div className="text-center text-sm text-ink-faint py-3">{t('loading')}</div>}
      {error && items.length > 0 && <p className="text-center text-sm text-bad">{error}</p>}
      {!loading && meta && items.length < meta.total && (
        <button onClick={more} className="btn btn-soft w-full">
          {t('more', { shown: int(items.length), total: int(meta.total) })}
        </button>
      )}
      {game === '655' && items.length > 0 && <p className="text-xs text-ink-faint">{t('bonusHint')}</p>}
    </div>
  )
}

// ── Tab Thống kê & dự đoán ──

function StatsView({ game }: { game: VietlottGame }) {
  const { t } = useTranslation('vietlott')
  const [last, setLast] = useState(0)
  const [data, setData] = useState<VietlottStats | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [reload, setReload] = useState(0)

  useEffect(() => {
    let alive = true
    getVietlottStats(game, last)
      .then(d => alive && setData(d))
      .catch(e => alive && setError(e?.message ?? ''))
      .finally(() => alive && setLoading(false))
    return () => { alive = false }
  }, [game, last, reload])

  const pick = (w: number) => { setLoading(true); setError(null); setLast(w) }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-1.5">
        <span className="text-xs font-semibold text-ink-faint mr-1">{t('window')}</span>
        {WINDOWS.map(w => (
          <button key={w} onClick={() => pick(w)} aria-pressed={w === last}
                  className={`px-3 py-1.5 rounded-lg text-xs font-bold transition
                              ${w === last ? 'bg-brand-500 text-white' : 'bg-muted text-ink-faint hover:text-ink-soft'}`}>
            {w === 0 ? t('windowAll') : t('windowLast', { count: w })}
          </button>
        ))}
      </div>

      {error && <ErrorCard message={error} onRetry={() => { setError(null); setLoading(true); setReload(n => n + 1) }} />}
      {!error && loading && !data && <div className="card p-6 text-center text-sm text-ink-faint">{t('loading')}</div>}
      {data && data.draws === 0 && !error && <div className="card p-6 text-center text-sm text-ink-soft">{t('empty')}</div>}

      {data && data.draws > 0 && !error && (
        <div className={`space-y-4 transition-opacity ${loading ? 'opacity-50' : ''}`} aria-busy={loading}>
          <p className="flex gap-2 text-xs text-ink-faint rounded-xl border border-line/60 bg-muted/60 px-3 py-2">
            <Icon name="tip" className="w-4 h-4 shrink-0 mt-px" />
            {t('disclaimer', { baseline: pct(data.baseline), jackpot: int(data.jackpotOneIn) })}
          </p>

          <NextDraw date={data.nextDraw} />
          <p className="text-xs text-ink-faint">
            {t('basedOn', { count: data.draws, from: data.from && formatDate(data.from), to: data.to && formatDate(data.to) })}
          </p>

          <div className="grid gap-4 lg:grid-cols-2">
            <Card icon="sparkles" title={t('suggest.title')} hint={t('suggest.hint')}>
              <div className="space-y-3">
                {data.suggestions.map(s => (
                  <div key={s.kind}>
                    <div className="text-xs font-semibold text-ink-soft mb-1">{t(`suggest.${s.kind}`)}</div>
                    <Balls game={game} numbers={s.numbers} />
                  </div>
                ))}
              </div>
            </Card>

            {data.backtest && <BacktestCard game={game} bt={data.backtest} />}

            <Card icon="hot" title={t('hot')} hint={t('hotHint', { recent: 30 })}>
              <StatRow game={game} stats={data.hot} total={data.draws} />
            </Card>
            <Card icon="cold" title={t('cold')} hint={t('coldHint')}>
              <StatRow game={game} stats={data.cold} total={data.draws} showGap />
            </Card>
          </div>

          <Card icon="predict" title={t('heat.title')} hint={t('heat.hint')}>
            <HeatGrid stats={data.all} draws={data.draws} baseline={data.baseline} />
          </Card>

          {data.bonusAll.length > 0 && (
            <Card icon="star" title={t('heat.bonusTitle')} hint={t('heat.bonusHint')}>
              <HeatGrid stats={data.bonusAll} draws={data.draws} baseline={1 / data.maxNumber} />
            </Card>
          )}

          <div className="grid gap-4 lg:grid-cols-3">
            <Card icon="link" title={t('pairs.title')} hint={t('pairs.hint')}>
              <ul className="space-y-1.5">
                {data.pairs.map(p => (
                  <li key={`${p.a}-${p.b}`} className="flex items-center justify-between gap-2">
                    <Balls game={game} numbers={[p.a, p.b]} size="sm" />
                    <span className="text-xs text-ink-faint tabular-nums">{t('times', { count: p.hits })}</span>
                  </li>
                ))}
              </ul>
            </Card>
            <Card icon="list" title={t('odd.title')} hint={t('odd.hint')}>
              <Bars values={data.oddCounts} total={data.draws} label={k => t('odd.label', { odd: k, even: 6 - k })} />
            </Card>
            <Card icon="trophy" title={t('odds.title')} hint={t('odds.hint')}>
              <ul className="space-y-1.5 text-sm">
                {data.odds.slice().reverse().map(o => (
                  <li key={o.match} className="flex justify-between gap-2">
                    <span className="text-ink-soft">{t('odds.match', { count: o.match })}</span>
                    <span className="font-semibold tabular-nums">1 / {int(o.oneIn)}</span>
                  </li>
                ))}
              </ul>
              {data.sumBand.length === 3 && (
                <p className="text-xs text-ink-faint mt-3">
                  {t('sum', { low: data.sumBand[0], mid: data.sumBand[1], high: data.sumBand[2] })}
                </p>
              )}
            </Card>
          </div>
        </div>
      )}
    </div>
  )
}

function Card({ icon, title, hint, children }: {
  icon: 'hot' | 'cold' | 'trophy' | 'sparkles' | 'predict' | 'star' | 'link' | 'list' | 'ok'
  title: string; hint: string; children: ReactNode
}) {
  const color = icon === 'hot' ? 'text-bad' : icon === 'cold' ? 'text-info' : icon === 'ok' ? 'text-ok' : 'text-warn'
  return (
    <section className="card p-4">
      <h3 className="flex items-center gap-1.5 text-sm font-bold">
        <Icon name={icon} className={`w-4 h-4 ${color}`} /> {title}
      </h3>
      <p className="text-xs text-ink-faint mb-3">{hint}</p>
      {children}
    </section>
  )
}

function StatRow({ game, stats, total, showGap }: {
  game: VietlottGame; stats: VietlottNumberStat[]; total: number; showGap?: boolean
}) {
  const { t } = useTranslation('vietlott')
  return (
    <div className="grid grid-cols-6 gap-2">
      {stats.map(s => (
        <span key={s.number} className="flex flex-col items-center gap-1"
              title={t('hitsOf', { hits: s.hits, draws: total })}>
          <span className={`ball w-10 h-10 rounded-full flex items-center justify-center font-extrabold tabular-nums ${BALL[game]}`}>
            {pad(s.number)}
          </span>
          <span className="text-[11px] text-ink-faint tabular-nums whitespace-nowrap">
            {showGap ? t('gap', { count: s.gap }) : pct(s.probability)}
          </span>
        </span>
      ))}
    </div>
  )
}

/** Lưới 1..max: độ đậm theo tần suất (giữa số về ít nhất và nhiều nhất — các số chênh nhau ít nên kéo giãn cho dễ thấy). */
function HeatGrid({ stats, draws, baseline }: { stats: VietlottNumberStat[]; draws: number; baseline: number }) {
  const { t } = useTranslation('vietlott')
  const [picked, setPicked] = useState<VietlottNumberStat | null>(null)
  const ps = stats.map(s => s.probability)
  const lo = Math.min(...ps), hi = Math.max(...ps)
  return (
    <div>
      <div className="grid grid-cols-9 sm:grid-cols-[repeat(15,minmax(0,1fr))] gap-1">
        {stats.map(s => {
          const a = hi > lo ? (s.probability - lo) / (hi - lo) : 0.5
          return (
            <button key={s.number} onClick={() => setPicked(s)}
                    style={{ backgroundColor: `rgb(var(--brand-500) / ${(0.08 + a * 0.82).toFixed(2)})` }}
                    className={`aspect-square rounded-md text-[11px] sm:text-xs font-mono font-semibold tabular-nums
                                ${a > 0.55 ? 'text-white' : 'text-ink'}
                                ${picked?.number === s.number ? 'outline outline-2 outline-ink' : ''}`}>
              {pad(s.number)}
            </button>
          )
        })}
      </div>
      <p className="mt-2 text-sm min-h-5">
        {picked ? (
          <>
            <b className="font-mono">{pad(picked.number)}</b>
            {' · '}{t('hitsOf', { hits: picked.hits, draws })}
            {' · '}{pct(picked.probability)} ({t('vsBaseline', { baseline: pct(baseline) })})
            {' · '}{t('recent', { value: pct(picked.recentProbability) })}
            {' · '}{t('gapLabel')}: {t('gap', { count: picked.gap })}
          </>
        ) : <span className="text-xs text-ink-faint">{t('heat.tap')}</span>}
      </p>
    </div>
  )
}

function Bars({ values, total, label }: { values: number[]; total: number; label: (k: number) => string }) {
  const max = Math.max(...values, 1)
  return (
    <ul className="space-y-1.5">
      {values.map((v, k) => (
        <li key={k} className="flex items-center gap-2 text-xs">
          <span className="w-20 shrink-0 whitespace-nowrap text-ink-soft">{label(k)}</span>
          <span className="flex-1 h-3 rounded-full bg-muted overflow-hidden">
            <span className="block h-full rounded-full bg-brand-500" style={{ width: `${(v / max) * 100}%` }} />
          </span>
          <span className="w-10 text-right tabular-nums text-ink-faint">{pct(total ? v / total : 0)}</span>
        </li>
      ))}
    </ul>
  )
}

function BacktestCard({ game, bt }: { game: VietlottGame; bt: NonNullable<VietlottStats['backtest']> }) {
  const { t } = useTranslation('vietlott')
  const [open, setOpen] = useState(false)
  return (
    <Card icon="ok" title={t('backtest.title')} hint={t('backtest.hint', { count: bt.rows.length })}>
      <p className="text-sm">
        {t('backtest.summary', {
          avg: bt.averageMatches.toLocaleString(currentLocale(), { maximumFractionDigits: 2 }),
          random: bt.randomExpectation.toLocaleString(currentLocale(), { maximumFractionDigits: 2 }),
        })}
      </p>
      <div className="flex flex-wrap gap-1.5 mt-2">
        {bt.matchCounts.map((c, k) => c > 0 && (
          <span key={k} className="text-xs rounded-lg bg-muted px-2 py-1 tabular-nums">
            {t('backtest.bucket', { match: k, count: c })}
          </span>
        ))}
      </div>
      <button onClick={() => setOpen(o => !o)} className="mt-3 text-xs font-semibold text-brand-700 dark:text-brand-400">
        {open ? t('backtest.hide') : t('backtest.show')}
      </button>
      {open && (
        <ul className="mt-2 space-y-2 max-h-80 overflow-y-auto pr-1">
          {bt.rows.map(r => (
            <li key={r.drawId} className="text-xs">
              <div className="text-ink-faint mb-1">
                {t('drawNo', { id: String(r.drawId).padStart(5, '0') })} · {formatDate(r.date)} · {t('backtest.matches', { count: r.matches })}
              </div>
              <Balls game={game} numbers={r.suggested} size="sm" highlight={new Set(r.actual)} />
            </li>
          ))}
        </ul>
      )}
    </Card>
  )
}
