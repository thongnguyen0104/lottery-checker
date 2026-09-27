import { useState } from 'react'
import { LUCKY_GAMES, pickNumbers, type LuckyGame } from '../utils/lucky'

const HISTORY_MAX = 5
const pad = (n: number) => String(n).padStart(2, '0')

// Mega đỏ, Power vàng cam — theo màu nhận diện của từng loại vé Vietlott.
const BALL = {
  mega: 'bg-brand-500 text-white',
  power: 'bg-amber-400 text-amber-950',
} satisfies Record<LuckyGame, string>

export default function LuckyNumbers() {
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
      <div className="grid grid-cols-2 gap-1 bg-gray-100 p-1 rounded-xl" role="tablist">
        {(Object.keys(LUCKY_GAMES) as LuckyGame[]).map(g => (
          <button key={g} role="tab" aria-selected={g === game} onClick={() => switchGame(g)}
                  className={`py-2 rounded-lg text-sm font-semibold ${
                    g === game ? 'bg-white shadow text-gray-900' : 'text-gray-500'}`}>
            {LUCKY_GAMES[g].name}
          </button>
        ))}
      </div>

      <div className="bg-white rounded-2xl shadow p-5 text-center">
        <div className="text-sm text-gray-500 mb-4">
          6 số ngẫu nhiên từ 01 đến {LUCKY_GAMES[game].max}
        </div>
        <div className="grid grid-cols-6 gap-2" aria-live="polite">
          {current
            ? current.map((n, i) => (
                <div key={`${round}-${i}`}
                     className={`step-pop aspect-square rounded-full flex items-center justify-center
                                 text-lg font-bold tabular-nums shadow-inner ${BALL[game]}`}
                     style={{ animationDelay: `${i * 90}ms`, animationFillMode: 'both' }}>
                  {pad(n)}
                </div>
              ))
            : Array.from({ length: 6 }, (_, i) => (
                <div key={i}
                     className="aspect-square rounded-full flex items-center justify-center
                                border-2 border-dashed border-gray-300 text-gray-300 text-lg font-bold">
                  ?
                </div>
              ))}
        </div>
        <button onClick={roll}
                className="mt-5 w-full bg-blue-600 text-white py-3 rounded-lg font-medium">
          🎲 {current ? 'Chọn bộ khác' : 'Chọn số'}
        </button>
      </div>

      {history.length > 0 && (
        <div className="bg-white rounded-2xl shadow p-4">
          <div className="text-sm font-semibold text-gray-600 mb-2">Các bộ vừa chọn</div>
          <ul className="space-y-2">
            {history.map((h, i) => (
              <li key={i} className="flex items-center gap-2">
                <span className="w-9 shrink-0 text-xs text-gray-500">{LUCKY_GAMES[h.game].short}</span>
                <span className="flex flex-wrap gap-1">
                  {h.numbers.map(n => (
                    <span key={n}
                          className={`w-7 h-7 rounded-full flex items-center justify-center
                                      text-xs font-bold tabular-nums ${BALL[h.game]}`}>
                      {pad(n)}
                    </span>
                  ))}
                </span>
              </li>
            ))}
          </ul>
        </div>
      )}

      <p className="text-xs text-gray-500 text-center px-2">
        Xác suất trúng Jackpot {LUCKY_GAMES[game].name}: 1/{LUCKY_GAMES[game].jackpotOdds.toLocaleString('vi-VN')}.
        Bộ số nào cũng có cơ hội như nhau — chọn cho vui thôi nhé!
      </p>
    </div>
  )
}
