import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import Icon from './Icon'
import { currentLang } from '../i18n'
import { VIEWS, viewPath, type View } from '../views'

type DrawId = 'south' | 'central' | 'north' | 'vietlott'
type DrawState = 'done' | 'live' | 'next' | 'later' | 'off'

// Giờ quay theo giờ VN (phút tính từ 0h). Mỗi kỳ quay + đọc kết quả mất chừng 30 phút.
const DRAWS: { id: DrawId; start: number }[] = [
  { id: 'south', start: 16 * 60 + 15 },
  { id: 'central', start: 17 * 60 + 15 },
  { id: 'vietlott', start: 18 * 60 },
  { id: 'north', start: 18 * 60 + 15 },
]
const LIVE_MIN = 30
// Vietlott quay 18h: Power 6/55 thứ 3-5-7, Mega 6/45 thứ 4-6-CN; thứ 2 không có 2 loại này.
const VIETLOTT_GAME: Record<number, 'power' | 'mega' | undefined> = { 2: 'power', 4: 'power', 6: 'power', 3: 'mega', 5: 'mega', 0: 'mega' }
const WEEKDAY_INDEX: Record<string, number> = { Sun: 0, Mon: 1, Tue: 2, Wed: 3, Thu: 4, Fri: 5, Sat: 6 }

/** Giờ hiện tại theo giờ VN (máy ở múi giờ khác vẫn đúng lịch quay). */
function vnNow() {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone: 'Asia/Ho_Chi_Minh', weekday: 'short', hour: 'numeric', minute: 'numeric', hourCycle: 'h23',
  }).formatToParts(new Date())
  const get = (type: string) => parts.find(p => p.type === type)?.value ?? ''
  return { minutes: Number(get('hour')) * 60 + Number(get('minute')), weekday: WEEKDAY_INDEX[get('weekday')] ?? 0 }
}

/** Trạng thái từng kỳ quay hôm nay; kỳ gần nhất chưa quay đánh dấu "sắp quay". Cập nhật mỗi phút. */
function useDrawSchedule() {
  const [now, setNow] = useState(vnNow)
  useEffect(() => {
    const id = setInterval(() => setNow(vnNow()), 60_000)
    return () => clearInterval(id)
  }, [])
  const game = VIETLOTT_GAME[now.weekday]
  const drawsToday = (d: typeof DRAWS[number]) => d.id !== 'vietlott' || !!game
  const next = DRAWS.find(d => drawsToday(d) && now.minutes < d.start)
  return DRAWS.map(d => {
    const state: DrawState = !drawsToday(d) ? 'off'
      : now.minutes >= d.start + LIVE_MIN ? 'done'
      : now.minutes >= d.start ? 'live'
      : d === next ? 'next' : 'later'
    return { ...d, state, game: d.id === 'vietlott' ? game : undefined }
  })
}

const fmtTime = (m: number) => `${String(Math.floor(m / 60)).padStart(2, '0')}:${String(m % 60).padStart(2, '0')}`

/** Chân trang: giới thiệu + ủng hộ, lịch quay số hôm nay, link tính năng (điện thoại đã có BottomNav
 *  nên ẩn), lưu ý chơi có trách nhiệm, bản quyền + đổi ngôn ngữ + lên đầu trang. */
export default function AppFooter({ onNavigate, onOpenDonate }: { onNavigate: (v: View) => void; onOpenDonate: () => void }) {
  const { t, i18n } = useTranslation()
  const lang = currentLang()
  const draws = useDrawSchedule()
  // Mặc định thu gọn còn 1 dòng (kỳ quay gần nhất) cho đỡ chiếm chỗ; bấm mới mở phần đầy đủ.
  const [open, setOpen] = useState(false)
  const footerRef = useRef<HTMLElement>(null)
  // Footer nằm cuối trang → phần vừa mở nằm dưới mép màn hình: trượt xong thì cuộn xuống cho thấy.
  useEffect(() => {
    if (!open) return
    const id = setTimeout(() => footerRef.current?.scrollIntoView({ behavior: 'smooth', block: 'end' }), 320)
    return () => clearTimeout(id)
  }, [open])
  const live = draws.find(d => d.state === 'live')
  const next = draws.find(d => d.state === 'next')

  return (
    // Điện thoại: chừa chỗ cho BottomNav (fixed) và vạch home của iPhone.
    <footer ref={footerRef}
            className="mt-4 border-t border-line/60 bg-surface/60 backdrop-blur-xl
                       pb-[calc(5rem+env(safe-area-inset-bottom))] md:pb-0">
      {/* Cùng bề rộng với <main> (App.tsx) */}
      <div className="max-w-md md:max-w-3xl lg:max-w-5xl mx-auto px-4">
        <button onClick={() => setOpen(o => !o)} aria-expanded={open} aria-controls="footer-details"
                className="w-full flex items-center gap-3 py-3 text-left text-sm group">
          <span className="flex-1 min-w-0 flex items-center gap-2 truncate text-ink-soft">
            <span className="hidden sm:inline font-bold text-ink">© {new Date().getFullYear()} {t('app.name')}</span>
            <span aria-hidden className="hidden sm:inline text-ink-faint">·</span>
            {live ? (
              <span className="inline-flex items-center gap-1.5 min-w-0 truncate">
                <LiveDot /> <b className="text-bad">{t('footer.state.live')}</b> {t(`footer.draws.${live.id}`)}
              </span>
            ) : next ? (
              <span className="inline-flex items-center gap-1.5 min-w-0 truncate">
                <Icon name="clock" className="w-4 h-4 shrink-0 text-brand-700 dark:text-brand-400" />
                {t('footer.state.next')} · {t(`footer.draws.${next.id}`)} <b className="tabular-nums">{fmtTime(next.start)}</b>
              </span>
            ) : (
              <span className="inline-flex items-center gap-1.5 min-w-0 truncate">
                <Icon name="ok" className="w-4 h-4 shrink-0 text-ok" /> {t('footer.allDone')}
              </span>
            )}
          </span>
          <span className="shrink-0 inline-flex items-center gap-1 text-xs font-semibold text-ink-faint group-hover:text-ink transition">
            {t(open ? 'footer.less' : 'footer.more')}
            <Icon name="down" className={`w-4 h-4 transition-transform duration-300 ${open ? 'rotate-180' : ''}`} />
          </span>
        </button>

        {/* Trượt mở bằng grid-template-rows 0fr → 1fr (cao theo nội dung, khỏi đo chiều cao).
            inert khi thu gọn: phần ẩn không nhận Tab/đọc màn hình. */}
        <div id="footer-details"
             className={`grid transition-[grid-template-rows] duration-300 ease-out motion-reduce:transition-none
                         ${open ? 'grid-rows-[1fr]' : 'grid-rows-[0fr]'}`}>
          <div className="overflow-hidden" inert={!open}>
            <div className="pt-3 pb-6 space-y-6">
              <div className="grid gap-8 md:grid-cols-2 lg:grid-cols-[1.3fr_1fr_0.8fr]">
                {/* Thương hiệu + ủng hộ */}
                <section className="space-y-3">
                  <div className="flex items-center gap-2.5">
                    <span className="shrink-0 w-10 h-10 rounded-xl flex items-center justify-center
                                     bg-gradient-to-br from-primary to-primary-end text-on-primary shadow-md shadow-primary/25">
                      <Icon name="ticket" className="w-[22px] h-[22px]" />
                    </span>
                    <span>
                      <span className="block text-lg font-extrabold leading-tight bg-gradient-to-r from-brand-700 to-accent
                                       bg-clip-text text-transparent dark:from-brand-300 dark:to-brand-500">
                        {t('app.name')}
                      </span>
                      <span className="block text-xs text-ink-faint">{t('app.tagline')}</span>
                    </span>
                  </div>
                  <p className="text-sm text-ink-soft max-w-sm">{t('footer.about')}</p>
                  <button onClick={onOpenDonate}
                          className="inline-flex items-center gap-2 px-4 py-2 rounded-xl text-sm font-semibold
                                     bg-brand-500/10 text-brand-700 dark:text-brand-400 ring-1 ring-brand-500/25
                                     hover:bg-brand-500/15 active:scale-95 transition">
                    <Icon name="donate" className="w-[18px] h-[18px]" /> {t('app.donateLabel')}
                  </button>
                </section>

                {/* Lịch quay số hôm nay */}
                <section>
                  <h2 className="flex items-center gap-2 text-sm font-bold mb-3">
                    <Icon name="clock" className="w-4 h-4 text-brand-700 dark:text-brand-400" /> {t('footer.scheduleTitle')}
                  </h2>
                  <ul className="space-y-1">
                    {draws.map(d => (
                      <li key={d.id}
                          className={`flex items-center gap-3 px-3 py-2 rounded-xl text-sm transition
                                      ${d.state === 'live' || d.state === 'next' ? 'bg-brand-500/10' : ''}
                                      ${d.state === 'done' || d.state === 'off' ? 'text-ink-faint' : ''}`}>
                        <span className="w-12 font-bold tabular-nums">{fmtTime(d.start)}</span>
                        <span className="flex-1 min-w-0 truncate font-medium">
                          {t(`footer.draws.${d.id}`)}
                          {d.game && <span className="text-ink-faint font-normal"> · {t(`footer.games.${d.game}`)}</span>}
                        </span>
                        <DrawBadge state={d.state} />
                      </li>
                    ))}
                  </ul>
                  <p className="mt-2 px-3 text-xs text-ink-faint">{t('footer.scheduleNote')}</p>
                </section>

                {/* Link tính năng — điện thoại đã có BottomNav */}
                <nav aria-label={t('app.features')} className="hidden md:block">
                  <h2 className="text-sm font-bold mb-3">{t('app.features')}</h2>
                  <ul className="space-y-1">
                    {VIEWS.map(v => (
                      <li key={v.id}>
                        <a href={viewPath(v.id)}
                           onClick={e => {
                             // Ctrl/Cmd/giữa chuột: để trình duyệt mở tab mới như link thường
                             if (e.metaKey || e.ctrlKey || e.shiftKey || e.button !== 0) return
                             e.preventDefault()
                             onNavigate(v.id)
                           }}
                           className="inline-flex items-center gap-2 py-1 text-sm text-ink-soft hover:text-brand-700
                                      dark:hover:text-brand-400 transition">
                          <Icon name={v.icon} className="w-4 h-4" /> {t(`views.${v.id}.title`)}
                        </a>
                      </li>
                    ))}
                  </ul>
                </nav>
              </div>

              {/* Lưu ý chơi có trách nhiệm */}
              <div className="flex gap-3 p-4 rounded-2xl bg-warn/10 ring-1 ring-warn/25 text-sm">
                <Icon name="warn" className="w-5 h-5 shrink-0 text-warn mt-0.5" />
                <div className="space-y-1 text-ink-soft">
                  <p className="font-semibold text-ink">{t('footer.disclaimerTitle')}</p>
                  <p>{t('footer.disclaimer')}</p>
                </div>
              </div>

              <div className="flex flex-wrap items-center justify-between gap-3 pt-4 border-t border-line/60 text-xs text-ink-faint">
                {/* Màn rộng đã ghi © trên thanh thu gọn */}
                <p><span className="sm:hidden">© {new Date().getFullYear()} {t('app.name')} · </span>{t('footer.madeIn')}</p>
                <div className="flex items-center gap-2">
                  <div className="flex gap-0.5 p-0.5 rounded-lg bg-muted font-bold" role="group" aria-label={t('app.language')}>
                    {(['vi', 'en'] as const).map(l => (
                      <button key={l} onClick={() => i18n.changeLanguage(l)} aria-pressed={l === lang}
                              className={`px-2 py-0.5 rounded-md transition
                                          ${l === lang ? 'bg-surface shadow-sm text-brand-700 dark:text-brand-400' : 'hover:text-ink'}`}>
                        {l.toUpperCase()}
                      </button>
                    ))}
                  </div>
                  <button onClick={() => window.scrollTo({ top: 0, behavior: 'smooth' })}
                          className="inline-flex items-center gap-1 px-2.5 py-1 rounded-lg bg-muted font-semibold hover:text-ink transition">
                    <Icon name="down" className="w-3.5 h-3.5 rotate-180" /> {t('footer.backToTop')}
                  </button>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </footer>
  )
}

function LiveDot() {
  return (
    <span aria-hidden className="relative flex w-2 h-2 shrink-0">
      <span className="absolute inset-0 rounded-full bg-bad opacity-75 motion-safe:animate-ping" />
      <span className="relative w-2 h-2 rounded-full bg-bad" />
    </span>
  )
}

function DrawBadge({ state }: { state: DrawState }) {
  const { t } = useTranslation()
  if (state === 'later') return null
  if (state === 'live') {
    return (
      <span className="inline-flex items-center gap-1.5 text-xs font-bold text-bad">
        <LiveDot /> {t('footer.state.live')}
      </span>
    )
  }
  const tone = state === 'next' ? 'text-brand-700 dark:text-brand-400' : 'text-ink-faint'
  return <span className={`text-xs font-semibold ${tone}`}>{t(`footer.state.${state}`)}</span>
}
