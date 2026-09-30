import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { LUCKY_GAMES, SMS_MAX_SETS, SMS_NUMBER, pickNumbers, smsHref, toSms, type LuckyGame } from '../utils/lucky'
import Icon from './Icon'
import DreamChat from './DreamChat'
import type { ResultsFocus } from './AvailableData'
import { currentLocale } from '../i18n'

const HISTORY_MAX = 5
const pad = (n: number) => String(n).padStart(2, '0')

// Mega đỏ, Power vàng cam — theo màu nhận diện của từng loại vé Vietlott, nên KHÔNG đổi theo
// màu theme (brand) như phần còn lại của app.
const BALL = {
  mega: 'bg-red-500 text-white',
  power: 'bg-amber-400 text-amber-950',
} satisfies Record<LuckyGame, string>

type Mode = 'vietlott' | 'dream'

/** Nút copy nội dung SMS; đổi thành dấu tích ~1.5s sau khi copy xong. */
function CopyButton({ text, label }: { text: string; label?: string }) {
  const { t } = useTranslation('lucky')
  const [copied, setCopied] = useState(false)
  const copy = async () => {
    try {
      await navigator.clipboard.writeText(text)
      setCopied(true)
      setTimeout(() => setCopied(false), 1500)
    } catch { /* trình duyệt chặn clipboard — bỏ qua */ }
  }
  return (
    <button onClick={copy} title={t('copy.title', { text })} aria-label={t('copy.aria', { text })}
            className={`shrink-0 inline-flex items-center gap-1 rounded-lg px-2 py-1.5 text-xs font-semibold transition
                        ${copied ? 'text-ok' : 'text-ink-faint hover:text-ink-soft hover:bg-muted'}`}>
      <Icon name={copied ? 'check' : 'copy'} className="w-4 h-4" />
      {label && (copied ? t('copy.done') : label)}
    </button>
  )
}

/** Tab Số may mắn: chọn ngẫu nhiên Vietlott, hoặc luận số từ giấc mơ. Cả hai luôn mount để giữ state khi đổi mục. */
export default function LuckyNumbers({ onShowResults }: { onShowResults: (focus: ResultsFocus) => void }) {
  const { t } = useTranslation('lucky')
  const [mode, setMode] = useState<Mode>('vietlott')
  return (
    <div className="space-y-4">
      <div className="grid grid-cols-2 gap-1 bg-muted p-1 rounded-xl border border-line/60" role="tablist">
        {(['vietlott', 'dream'] as const).map(m => (
          <button key={m} role="tab" aria-selected={m === mode} onClick={() => setMode(m)}
                  className={`py-2.5 rounded-lg text-sm font-semibold transition active:scale-95 ${m === mode
                    ? 'tab-pop bg-surface dark:bg-brand-500/10 shadow-sm text-brand-700 dark:text-brand-400'
                    : 'text-ink-faint hover:text-ink-soft'}`}>
            {t(`tabs.${m}`)}
          </button>
        ))}
      </div>
      <div hidden={mode !== 'vietlott'}><VietlottPicker /></div>
      <div hidden={mode !== 'dream'}><DreamChat onShowResults={onShowResults} /></div>
    </div>
  )
}

/** Ô SMS: nội dung + nút SMS (mở app nhắn tin tới 9969, điền sẵn nội dung) + nút Copy. */
function SmsBox({ text }: { text: string }) {
  const { t } = useTranslation('lucky')
  return (
    <div className="mt-5 max-w-md mx-auto flex items-center gap-2 rounded-xl border border-line/60 bg-muted px-3 py-2">
      <a href={smsHref(text)} title={t('sms.sendTo', { number: SMS_NUMBER })} aria-label={t('sms.sendTo', { number: SMS_NUMBER })}
         className="shrink-0 rounded-md bg-brand-500/15 p-1.5 text-brand-700 dark:text-brand-400 hover:bg-brand-500/25">
        <Icon name="sms" className="w-4 h-4" />
      </a>
      <code className="flex-1 text-left text-sm font-mono tabular-nums break-words">{text}</code>
      <CopyButton text={text} label={t('copy.label')} />
    </div>
  )
}

function VietlottPicker() {
  const { t } = useTranslation('lucky')
  const [game, setGame] = useState<LuckyGame>('mega')
  const [count, setCount] = useState(1)
  const [current, setCurrent] = useState<number[][] | null>(null)
  const [history, setHistory] = useState<{ game: LuckyGame; sets: number[][] }[]>([])
  // Tăng mỗi lượt chọn: đổi key của bi → React mount lại → animation nảy chạy lại.
  const [round, setRound] = useState(0)

  // Lượt đang hiện (luôn thuộc `game` hiện tại — đổi loại vé là cất đi) chuyển xuống lịch sử.
  const archive = () => {
    if (current) setHistory(h => [{ game, sets: current }, ...h].slice(0, HISTORY_MAX))
  }

  const roll = () => {
    archive()
    setCurrent(Array.from({ length: count }, () => pickNumbers(LUCKY_GAMES[game].max)))
    setRound(r => r + 1)
  }

  const switchGame = (g: LuckyGame) => {
    if (g === game) return
    archive()
    setCurrent(null)
    setGame(g)
  }

  return (
    <div className="space-y-4">
      <div>
        <h1 className="text-xl md:text-3xl font-extrabold tracking-tight">{t('picker.title')}</h1>
        <p className="text-sm md:text-base text-ink-soft mt-0.5">{t('picker.subtitle')}</p>
      </div>

      <div className="space-y-4 lg:space-y-0 lg:grid lg:grid-cols-[1.4fr_1fr] lg:gap-6 lg:items-start">
        <div className="space-y-4">
          <div className="grid grid-cols-2 gap-1 bg-muted p-1 rounded-xl border border-line/60" role="tablist">
            {(Object.keys(LUCKY_GAMES) as LuckyGame[]).map(g => (
              <button key={g} role="tab" aria-selected={g === game} onClick={() => switchGame(g)}
                      className={`py-2.5 rounded-lg text-sm font-semibold transition active:scale-95 ${g === game
                        ? 'tab-pop bg-surface dark:bg-brand-500/10 shadow-sm text-brand-700 dark:text-brand-400'
                        : 'text-ink-faint hover:text-ink-soft'}`}>
                {LUCKY_GAMES[g].name}
              </button>
            ))}
          </div>

          <div className="card p-5 md:p-7 text-center">
            <div className="text-sm text-ink-faint mb-3">
              {t('picker.range', { max: LUCKY_GAMES[game].max })}
            </div>
            <div className="flex items-center justify-center gap-1.5 mb-4 md:mb-6">
              <span className="text-xs font-semibold text-ink-faint mr-1">{t('picker.sets')}</span>
              {Array.from({ length: SMS_MAX_SETS }, (_, i) => i + 1).map(c => (
                <button key={c} onClick={() => setCount(c)} aria-pressed={c === count}
                        className={`w-8 h-8 rounded-lg text-sm font-bold transition ${c === count
                          ? 'bg-brand-500 text-white'
                          : 'bg-muted text-ink-faint hover:text-ink-soft'}`}>
                  {c}
                </button>
              ))}
            </div>
            <div className="space-y-2 sm:space-y-3 max-w-md mx-auto" aria-live="polite">
              {current
                ? current.map((set, r) => (
                    <div key={`${round}-${r}`} className="grid grid-cols-6 gap-2 sm:gap-3">
                      {set.map((n, i) => (
                        <div key={i}
                             className={`ball step-pop aspect-square rounded-full flex items-center justify-center
                                         ${current.length > 1 ? 'text-base sm:text-xl' : 'text-lg sm:text-2xl'}
                                         font-extrabold tabular-nums ${BALL[game]}`}
                             style={{ animationDelay: `${(r * 6 + i) * 60}ms`, animationFillMode: 'both' }}>
                          {pad(n)}
                        </div>
                      ))}
                    </div>
                  ))
                : (
                    <div className="grid grid-cols-6 gap-2 sm:gap-3">
                      {Array.from({ length: 6 }, (_, i) => (
                        <div key={i}
                             className="aspect-square rounded-full flex items-center justify-center
                                        border-2 border-dashed border-line text-ink-faint/60 text-lg sm:text-2xl font-bold">
                          ?
                        </div>
                      ))}
                    </div>
                  )}
            </div>
            {current && <SmsBox text={toSms(game, current)} />}
            <button onClick={roll} className="btn btn-primary mt-6 w-full max-w-md py-3.5 text-lg">
              <Icon name="dice" className="w-6 h-6" /> {current ? t('picker.pickAgain') : t('picker.pick')}
            </button>
          </div>
        </div>

        <div className="space-y-4">
          {/* Màn rộng luôn có cột lịch sử (kể cả trống) cho bố cục khỏi lệch; điện thoại chỉ hiện khi đã có bộ. */}
          <div className={`card p-4 ${history.length ? '' : 'hidden lg:block'}`}>
            <div className="flex items-center gap-2 text-sm font-bold text-ink-soft mb-3">
              <Icon name="history" className="w-4 h-4 text-ink-faint" /> {t('picker.history')}
            </div>
            {history.length === 0 && (
              <div className="text-sm text-ink-faint py-4 text-center">{t('picker.historyEmpty')}</div>
            )}
            <ul className="space-y-3">
              {history.map((h, i) => {
                const sms = toSms(h.game, h.sets)
                return (
                  <li key={i} className="flex items-center gap-2">
                    <span className="w-10 shrink-0 text-xs font-semibold text-ink-faint">{LUCKY_GAMES[h.game].short}</span>
                    {/* 6 số luôn nằm 1 hàng: bi co theo bề ngang (tối đa 32px) thay vì xuống dòng. */}
                    <span className="flex-1 min-w-0 space-y-1.5">
                      {h.sets.map((set, r) => (
                        <span key={r} className="grid grid-cols-6 gap-1 max-w-[14.5rem]">
                          {set.map(n => (
                            <span key={n}
                                  className={`ball w-full max-w-8 aspect-square rounded-full flex items-center justify-center
                                              text-[11px] sm:text-xs font-bold tabular-nums ${BALL[h.game]}`}>
                              {pad(n)}
                            </span>
                          ))}
                        </span>
                      ))}
                    </span>
                    <span className="shrink-0 flex items-center">
                      <a href={smsHref(sms)} title={t('sms.sendTo', { number: SMS_NUMBER })} aria-label={t('sms.sendTo', { number: SMS_NUMBER })}
                         className="rounded-lg p-1.5 text-ink-faint hover:text-ink-soft hover:bg-muted">
                        <Icon name="sms" className="w-4 h-4" />
                      </a>
                      <CopyButton text={sms} />
                    </span>
                  </li>
                )
              })}
            </ul>
          </div>

          <p className="text-xs text-ink-faint text-center lg:text-left px-2">
            {t('picker.odds', { game: LUCKY_GAMES[game].name, odds: LUCKY_GAMES[game].jackpotOdds.toLocaleString(currentLocale()) })}{' '}
            {t('picker.oddsNote')}
          </p>
        </div>
      </div>
    </div>
  )
}

