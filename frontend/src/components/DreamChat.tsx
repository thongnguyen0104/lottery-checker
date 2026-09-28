import { useEffect, useRef, useState, type FormEvent } from 'react'
import { interpretDream, searchTail, type DreamResult, type TailHit } from '../api/client'
import { provinceName } from '../data/provinces'
import { formatDate } from '../utils/date'
import type { ResultsFocus } from './AvailableData'
import Icon from './Icon'

const MAX_LEN = 500
const SUGGESTIONS = ['Tôi mơ thấy con rắn trắng bò vào nhà', 'Mơ thấy mèo đen trước cửa', 'Mơ thấy Thần Tài']

type Turn = { id: number; question: string; answer?: DreamResult; error?: string }
/** Kết quả dò 1 số: đang tải (undefined), lỗi, hoặc danh sách giải trùng đuôi. */
type Search = { tail: string; hits?: TailHit[]; error?: string }

export default function DreamChat({ onShowResults }: { onShowResults: (focus: ResultsFocus) => void }) {
  const [text, setText] = useState('')
  const [turns, setTurns] = useState<Turn[]>([])
  const [busy, setBusy] = useState(false)
  const [search, setSearch] = useState<Search | null>(null)
  const endRef = useRef<HTMLDivElement>(null)
  const nextId = useRef(0)

  useEffect(() => { endRef.current?.scrollIntoView({ behavior: 'smooth', block: 'nearest' }) }, [turns, search])

  const ask = async (question: string) => {
    question = question.trim()
    if (!question || busy) return
    const id = nextId.current++
    setTurns(t => [...t, { id, question }])
    setText('')
    setBusy(true)
    try {
      const answer = await interpretDream(question)
      setTurns(t => t.map(x => x.id === id ? { ...x, answer } : x))
    } catch (e) {
      setTurns(t => t.map(x => x.id === id ? { ...x, error: (e as Error).message } : x))
    } finally {
      setBusy(false)
    }
  }

  const check = async (tail: string) => {
    setSearch({ tail })
    try {
      setSearch({ tail, hits: await searchTail(tail) })
    } catch (e) {
      setSearch({ tail, error: (e as Error).message })
    }
  }

  const submit = (e: FormEvent) => { e.preventDefault(); ask(text) }

  return (
    <div className="space-y-4">
      <div>
        <h1 className="text-xl md:text-3xl font-extrabold tracking-tight">Luận số giấc mơ</h1>
        <p className="text-sm md:text-base text-ink-soft mt-0.5">
          Kể lại giấc mơ, máy tra sổ mơ dân gian ra số tham khảo.
        </p>
      </div>

      <div className="card p-4 md:p-5 space-y-4">
        {turns.length === 0 && (
          <div className="space-y-2">
            <div className="text-sm text-ink-faint">Gợi ý:</div>
            <div className="flex flex-wrap gap-2">
              {SUGGESTIONS.map(s => (
                <button key={s} onClick={() => ask(s)} disabled={busy}
                        className="btn btn-soft text-sm py-1.5 px-3">{s}</button>
              ))}
            </div>
          </div>
        )}

        {turns.map(t => (
          <div key={t.id} className="space-y-2">
            <div className="flex justify-end">
              <div className="max-w-[85%] rounded-2xl rounded-br-md bg-brand-500/15 px-3.5 py-2 text-sm">{t.question}</div>
            </div>
            <div className="flex gap-2 items-start">
              <span className="shrink-0 mt-0.5 text-brand-700 dark:text-brand-400"><Icon name="ai" /></span>
              <div className="flex-1 min-w-0 rounded-2xl rounded-tl-md bg-muted px-3.5 py-3 text-sm space-y-2">
                {!t.answer && !t.error && <span className="text-ink-faint">Đang tra sổ mơ…</span>}
                {t.error && <span className="text-bad">{t.error}</span>}
                {t.answer && <Answer answer={t.answer} onCheck={check} />}
              </div>
            </div>
          </div>
        ))}

        {search && (
          <div className="rounded-xl border border-line p-3 text-sm space-y-2">
            <div className="font-bold">Đuôi {search.tail} trong kết quả gần đây</div>
            {!search.hits && !search.error && <div className="text-ink-faint">Đang dò…</div>}
            {search.error && <div className="text-bad">{search.error}</div>}
            {search.hits?.length === 0 && <div className="text-ink-faint">Chưa đài nào về đuôi {search.tail} trong dữ liệu hiện có.</div>}
            <ul className="divide-y divide-line/70">
              {search.hits?.map((h, i) => (
                <li key={i}>
                  <button onClick={() => onShowResults({ drawDate: h.drawDate, province: h.province, ticketNumber: search.tail, from: 'lucky' })}
                          className="w-full flex items-center justify-between gap-2 py-2 text-left hover:text-brand-700 dark:hover:text-brand-400">
                    <span>{formatDate(h.drawDate)} · {provinceName(h.province)}</span>
                    <span className="tabular-nums font-semibold">{h.tier === 'DB' ? 'ĐB' : `G.${h.tier}`} · {h.number}</span>
                  </button>
                </li>
              ))}
            </ul>
          </div>
        )}
        <div ref={endRef} />

        <form onSubmit={submit} className="flex gap-2">
          <input value={text} onChange={e => setText(e.target.value)} maxLength={MAX_LEN}
                 placeholder="Tôi mơ thấy…" aria-label="Kể lại giấc mơ"
                 className="flex-1 min-w-0 rounded-xl border border-line bg-surface px-3.5 py-2.5 text-base
                            focus:outline-none focus:ring-2 focus:ring-brand-500/40" />
          <button type="submit" disabled={busy || !text.trim()} className="btn btn-primary px-4" aria-label="Gửi">
            <Icon name="next" />
          </button>
        </form>
      </div>

      <p className="text-xs text-ink-faint text-center px-2">
        Chỉ để tham khảo cho vui, không có cơ sở khoa học. Đừng nhập thông tin cá nhân — nội dung được gửi tới Google Gemini.
      </p>
    </div>
  )
}

function Answer({ answer, onCheck }: { answer: DreamResult; onCheck: (tail: string) => void }) {
  if (!answer.mainNumber) return <p>{answer.explanation}</p>
  return (
    <>
      <div className="font-semibold">{answer.summary}</div>
      <div className="flex flex-wrap items-center gap-2">
        <span className="ball w-12 h-12 rounded-full flex items-center justify-center text-xl font-extrabold tabular-nums bg-brand-500 text-white">
          {answer.mainNumber}
        </span>
        {answer.secondaryNumbers.map(n => (
          <span key={n} className="w-9 h-9 rounded-full flex items-center justify-center text-sm font-bold tabular-nums border border-line bg-surface">
            {n}
          </span>
        ))}
      </div>
      <p>{answer.explanation}</p>
      <div className="text-xs text-ink-faint">Theo sổ mơ: {answer.entries.map(e => `${e.label} (${e.numbers.join(', ')})`).join(' · ')}</div>
      <div className="flex flex-wrap gap-2 pt-1">
        {[answer.mainNumber, ...answer.secondaryNumbers.slice(0, 2)].map(n => (
          <button key={n} onClick={() => onCheck(n)} className="btn btn-secondary text-sm py-1.5 px-3">
            <Icon name="search" className="w-4 h-4" /> Dò số {n}
          </button>
        ))}
      </div>
      <p className="text-xs text-ink-faint">{answer.disclaimer}</p>
    </>
  )
}
