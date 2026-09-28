import { useState } from 'react'
import { LUCKY_GAMES, pickNumbers, type LuckyGame } from '../utils/lucky'
import Icon from './Icon'
import DreamChat from './DreamChat'
import type { ResultsFocus } from './AvailableData'

const HISTORY_MAX = 5
const pad = (n: number) => String(n).padStart(2, '0')

// Mega đỏ, Power vàng cam — theo màu nhận diện của từng loại vé Vietlott, nên KHÔNG đổi theo
// màu theme (brand) như phần còn lại của app.
const BALL = {
  mega: 'bg-red-500 text-white',
  power: 'bg-amber-400 text-amber-950',
} satisfies Record<LuckyGame, string>

type Mode = 'vietlott' | 'dream'

/** Tab Số may mắn: chọn ngẫu nhiên Vietlott, hoặc luận số từ giấc mơ. Cả hai luôn mount để giữ state khi đổi mục. */
export default function LuckyNumbers({ onShowResults }: { onShowResults: (focus: ResultsFocus) => void }) {
  const [mode, setMode] = useState<Mode>('vietlott')
  return (
    <div className="space-y-4">
      <div className="grid grid-cols-2 gap-1 bg-muted p-1 rounded-xl border border-line/60" role="tablist">
        {([['vietlott', 'Vietlott'], ['dream', 'Luận số giấc mơ']] as const).map(([m, label]) => (
          <button key={m} role="tab" aria-selected={m === mode} onClick={() => setMode(m)}
                  className={`py-2.5 rounded-lg text-sm font-semibold transition ${m === mode
                    ? 'bg-surface dark:bg-brand-500/10 shadow-sm text-brand-700 dark:text-brand-400'
                    : 'text-ink-faint hover:text-ink-soft'}`}>
            {label}
          </button>
        ))}
      </div>
      <div hidden={mode !== 'vietlott'}><VietlottPicker /></div>
      <div hidden={mode !== 'dream'}><DreamChat onShowResults={onShowResults} /></div>
    </div>
  )
}

function VietlottPicker() {
  const [game, setGame] = useState<LuckyGame>('mega')
  const [current, setCurrent] = useState<number[] | null>(null)
  const [history, setHistory] = useState<{ game: LuckyGame; numbers: number[] }[]>([])
  // Tăng mỗi lượt chọn: đổi key của bi → React mount lại → animation nảy chạy lại.
  const [round, setRound] = useState(0)

  // Bộ đang hiện (luôn thuộc `game` hiện tại — đổi loại vé là cất đi) chuyển xuống lịch sử.
  const archive = () => {
    if (current) setHistory(h => [{ game, numbers: current }, ...h].slice(0, HISTORY_MAX))
  }

  const roll = () => {
    archive()
    setCurrent(pickNumbers(LUCKY_GAMES[game].max))
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
        <h1 className="text-xl md:text-3xl font-extrabold tracking-tight">6 số may mắn</h1>
        <p className="text-sm md:text-base text-ink-soft mt-0.5">Bí ý tưởng? Để máy chọn giúp một bộ số Vietlott.</p>
      </div>

      <div className="space-y-4 lg:space-y-0 lg:grid lg:grid-cols-[1.4fr_1fr] lg:gap-6 lg:items-start">
        <div className="space-y-4">
          <div className="grid grid-cols-2 gap-1 bg-muted p-1 rounded-xl border border-line/60" role="tablist">
            {(Object.keys(LUCKY_GAMES) as LuckyGame[]).map(g => (
              <button key={g} role="tab" aria-selected={g === game} onClick={() => switchGame(g)}
                      className={`py-2.5 rounded-lg text-sm font-semibold transition ${g === game
                        ? 'bg-surface dark:bg-brand-500/10 shadow-sm text-brand-700 dark:text-brand-400'
                        : 'text-ink-faint hover:text-ink-soft'}`}>
                {LUCKY_GAMES[g].name}
              </button>
            ))}
          </div>

          <div className="card p-5 md:p-7 text-center">
            <div className="text-sm text-ink-faint mb-4 md:mb-6">
              6 số ngẫu nhiên từ 01 đến {LUCKY_GAMES[game].max}
            </div>
            <div className="grid grid-cols-6 gap-2 sm:gap-3 max-w-md mx-auto" aria-live="polite">
              {current
                ? current.map((n, i) => (
                    <div key={`${round}-${i}`}
                         className={`ball step-pop aspect-square rounded-full flex items-center justify-center
                                     text-lg sm:text-2xl font-extrabold tabular-nums ${BALL[game]}`}
                         style={{ animationDelay: `${i * 90}ms`, animationFillMode: 'both' }}>
                      {pad(n)}
                    </div>
                  ))
                : Array.from({ length: 6 }, (_, i) => (
                    <div key={i}
                         className="aspect-square rounded-full flex items-center justify-center
                                    border-2 border-dashed border-line text-ink-faint/60 text-lg sm:text-2xl font-bold">
                      ?
                    </div>
                  ))}
            </div>
            <button onClick={roll} className="btn btn-primary mt-6 w-full max-w-md py-3.5 text-lg">
              <Icon name="dice" className="w-6 h-6" /> {current ? 'Chọn bộ khác' : 'Chọn số'}
            </button>
          </div>
        </div>

        <div className="space-y-4">
          {/* Màn rộng luôn có cột lịch sử (kể cả trống) cho bố cục khỏi lệch; điện thoại chỉ hiện khi đã có bộ. */}
          <div className={`card p-4 ${history.length ? '' : 'hidden lg:block'}`}>
            <div className="flex items-center gap-2 text-sm font-bold text-ink-soft mb-3">
              <Icon name="history" className="w-4 h-4 text-ink-faint" /> Các bộ vừa chọn
            </div>
            {history.length === 0 && (
              <div className="text-sm text-ink-faint py-4 text-center">Chưa có bộ nào — bấm Chọn số để bắt đầu.</div>
            )}
            <ul className="space-y-2.5">
              {history.map((h, i) => (
                <li key={i} className="flex items-center gap-2">
                  <span className="w-10 shrink-0 text-xs font-semibold text-ink-faint">{LUCKY_GAMES[h.game].short}</span>
                  <span className="flex flex-wrap gap-1.5">
                    {h.numbers.map(n => (
                      <span key={n}
                            className={`ball w-8 h-8 rounded-full flex items-center justify-center
                                        text-xs font-bold tabular-nums ${BALL[h.game]}`}>
                        {pad(n)}
                      </span>
                    ))}
                  </span>
                </li>
              ))}
            </ul>
          </div>

          <p className="text-xs text-ink-faint text-center lg:text-left px-2">
            Xác suất trúng Jackpot {LUCKY_GAMES[game].name}: 1/{LUCKY_GAMES[game].jackpotOdds.toLocaleString('vi-VN')}.
            Bộ số nào cũng có cơ hội như nhau — chọn cho vui thôi nhé!
          </p>
        </div>
      </div>
    </div>
  )
}
