import { useCallback, useEffect, useRef, useState, type PointerEvent } from 'react'
import { useTranslation } from 'react-i18next'
import {
  finishMiniGame, getMiniGame, startMiniGame, type MiniGameResult, type MiniGameStart, type MiniGameStatus,
} from '../api/client'
import { currentLocale } from '../i18n'
import { LANES, MAX_TICKS, MiniGameSim, rowY, speed, TICKS_PER_SECOND, type Input } from '../minigame/sim'
import Icon, { IconBadge } from './Icon'

const num = (n: number) => n.toLocaleString(currentLocale())
const STEP_MS = 1000 / TICKS_PER_SECOND
/** Quãng đường (đơn vị mô phỏng) ứng với chiều cao canvas. */
const VIEW_UNITS = 36_000
/** Xe nằm ở 80% chiều cao, phía trên là đường sắp tới. */
const CAR_AT = 0.8

type Phase = 'idle' | 'ready' | 'starting' | 'playing' | 'submitting' | 'done'

/**
 * Minigame đua xe né chướng ngại để kiếm xu ảo mua vé cào. Client chỉ ghi lại thao tác (đổi làn ở tick nào)
 * rồi gửi lên — máy chủ chạy lại ván từ seed để chấm điểm, nên điểm hiện trên màn chỉ để xem.
 */
export default function MiniGame({ onBalance }: { onBalance: (balance: number) => void }) {
  const { t } = useTranslation('profile')
  const [status, setStatus] = useState<MiniGameStatus | null>(null)
  const [phase, setPhase] = useState<Phase>('idle')
  const [session, setSession] = useState<MiniGameStart | null>(null)
  const [result, setResult] = useState<(MiniGameResult & { localScore: number }) | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    getMiniGame().then(s => { setStatus(s); onBalance(s.balance) }).catch(e => setError(e?.message ?? ''))
  }, [onBalance])

  const start = async () => {
    setPhase('starting')
    setError(null)
    setResult(null)
    try {
      const s = await startMiniGame()
      setSession(s)
      setStatus(st => st && { ...st, rewardedLeft: s.rewardedLeft })
      setPhase('playing')
    } catch (e) {
      setError((e as Error).message)
      setPhase('ready')
    }
  }

  const onOver = useCallback(async (inputs: Input[], localScore: number) => {
    if (!session) return
    setPhase('submitting')
    try {
      const r = await finishMiniGame(session.sessionId, inputs)
      setResult({ ...r, localScore })
      setStatus(st => st && { ...st, rewardedLeft: r.rewardedLeft, balance: r.balance })
      onBalance(r.balance)
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setPhase('done')
    }
  }, [session, onBalance])

  return (
    <div className="card p-4 md:p-5 space-y-4">
      <div>
        <h2 className="font-extrabold text-lg flex items-center gap-2">
          <Icon name="trophy" className="w-5 h-5 text-brand-700 dark:text-brand-400" /> {t('game.title')}
        </h2>
        <p className="text-sm text-ink-soft mt-1">
          {status ? t('game.rule', { perPoint: num(status.rewardPerPoint), max: num(status.maxRewardPerPlay) }) : ' '}
        </p>
        {status && (
          <p className="text-xs font-semibold mt-2 text-brand-700 dark:text-brand-400">
            {status.rewardedLeft > 0
              ? t('game.left', { count: status.rewardedLeft, total: status.rewardedPerDay })
              : t('game.noneLeft')}
          </p>
        )}
      </div>

      {phase === 'ready' || phase === 'starting' || phase === 'playing' || phase === 'submitting' ? (
        <>
          <Track key={phase === 'ready' || phase === 'starting' ? 'ready' : session!.sessionId}
                 seed={phase === 'ready' || phase === 'starting' ? null : session!.seed}
                 starting={phase === 'starting'} onReady={start} onOver={onOver} />
          {error && <p className="text-sm text-bad text-center">{error}</p>}
          {phase === 'submitting' && <p className="text-sm text-ink-soft text-center">{t('game.submitting')}</p>}
        </>
      ) : (
        <div className="rounded-2xl bg-muted border border-line p-5 text-center space-y-3">
          {result ? (
            <>
              <IconBadge name={result.crashed ? 'error' : 'trophy'} tone={result.reward > 0 ? 'ok' : 'info'} />
              <div className="text-lg font-extrabold">
                {result.crashed ? t('game.crashed') : t('game.finished')} · {t('game.score', { score: result.score })}
              </div>
              <div className={`text-2xl font-extrabold ${result.reward > 0 ? 'text-ok' : 'text-ink-soft'}`}>
                {result.reward > 0 ? t('game.reward', { amount: num(result.reward) }) : t('game.noReward')}
              </div>
              {result.localScore !== result.score && (
                <p className="text-xs text-warn">{t('game.mismatch')}</p>
              )}
            </>
          ) : (
            <p className="text-sm text-ink-soft">{t('game.howTo')}</p>
          )}
          {error && <p className="text-sm text-bad">{error}</p>}
          <button onClick={() => { setError(null); setResult(null); setPhase('ready') }} disabled={!status} className="btn btn-primary w-full">
            {result ? t('game.again') : t('game.play')}
          </button>
        </div>
      )}

      <p className="text-xs text-ink-faint text-center">{t('balanceNote')}</p>
    </div>
  )
}

/** Đếm ngược sau khi máy chủ đã mở lượt, trước khi xe chạy. */
const COUNTDOWN = 3

/**
 * Đường đua (canvas): vòng lặp tick cố định chạy MiniGameSim, chỉ nội suy khi vẽ.
 * seed null = màn chờ (đường trống + nút Bắt đầu, chưa mở lượt ở máy chủ nên chờ bao lâu cũng không hết hạn);
 * có seed thì đếm ngược rồi mới chạy.
 */
function Track({ seed, starting, onReady, onOver }: {
  seed: number | null
  starting: boolean
  onReady: () => void
  onOver: (inputs: Input[], localScore: number) => void
}) {
  const { t } = useTranslation('profile')
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const queue = useRef<number[]>([])
  const onOverRef = useRef(onOver)
  const tRef = useRef(t)
  useEffect(() => { onOverRef.current = onOver; tRef.current = t }, [onOver, t])
  const [count, setCount] = useState(COUNTDOWN)
  const go = useRef(false)
  useEffect(() => {
    if (seed === null) return
    if (count === 0) { go.current = true; return }
    const id = setTimeout(() => setCount(c => c - 1), 1000)
    return () => clearTimeout(id)
  }, [seed, count])

  useEffect(() => {
    const canvas = canvasRef.current!
    const ctx = canvas.getContext('2d')!
    const sim = new MiniGameSim(seed ?? 0)
    const inputs: Input[] = []
    let acc = 0
    let last = performance.now()
    let shownLane = sim.lane
    let raf = 0
    let ended = false

    const resize = () => {
      const w = canvas.clientWidth
      const dpr = window.devicePixelRatio || 1
      canvas.width = Math.round(w * dpr)
      canvas.height = Math.round(w * 1.4 * dpr)
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
    }
    resize()
    window.addEventListener('resize', resize)

    const draw = (alpha: number, dt: number) => {
      const W = canvas.clientWidth
      const H = W * 1.4
      const scale = H / VIEW_UNITS
      const carY = H * CAR_AT
      const laneW = W / LANES
      const dist = sim.distance + (sim.over ? 0 : speed(sim.tick) * alpha)
      shownLane += (sim.lane - shownLane) * Math.min(1, dt / 70)
      const toY = (y: number) => carY - (y - dist) * scale

      ctx.fillStyle = '#374151'
      ctx.fillRect(0, 0, W, H)
      ctx.fillStyle = '#16a34a'
      ctx.fillRect(0, 0, 6, H)
      ctx.fillRect(W - 6, 0, 6, H)
      // Vạch chia làn trôi theo quãng đường.
      ctx.fillStyle = '#e5e7eb'
      const period = 48
      const off = (dist * scale) % period
      for (let l = 1; l < LANES; l++)
        for (let y = -period + off; y < H; y += period) ctx.fillRect(l * laneW - 2, y, 4, 24)

      for (let i = Math.max(0, sim.upcoming - 1); seed !== null && rowY(i) - dist < VIEW_UNITS; i++) {
        const row = sim.row(i)
        const y = toY(rowY(i))
        for (let l = 0; l < LANES; l++) {
          const x = l * laneW
          if (row.blocked & (1 << l)) {
            ctx.fillStyle = '#f97316'
            ctx.fillRect(x + 8, y - 14, laneW - 16, 28)
            ctx.fillStyle = '#fff7ed'
            for (let s = x + 14; s < x + laneW - 16; s += 18) ctx.fillRect(s, y - 14, 8, 28)
          } else if (row.coinLane === l && i >= sim.upcoming) {
            ctx.fillStyle = '#facc15'
            ctx.beginPath()
            ctx.arc(x + laneW / 2, y, 11, 0, Math.PI * 2)
            ctx.fill()
            ctx.fillStyle = '#a16207'
            ctx.fillRect(x + laneW / 2 - 2, y - 6, 4, 12)
          }
        }
      }

      // Xe.
      const cx = (shownLane + 0.5) * laneW
      const cw = Math.min(40, laneW * 0.45)
      ctx.fillStyle = sim.crashed ? '#ef4444' : '#2563eb'
      ctx.beginPath()
      ctx.roundRect(cx - cw / 2, carY - 30, cw, 60, 10)
      ctx.fill()
      ctx.fillStyle = '#bfdbfe'
      ctx.fillRect(cx - cw / 2 + 6, carY - 20, cw - 12, 12)

      // HUD.
      const left = Math.max(0, Math.ceil((MAX_TICKS - sim.tick) / TICKS_PER_SECOND))
      ctx.fillStyle = 'rgba(0,0,0,.55)'
      ctx.fillRect(0, 0, W, 34)
      ctx.fillStyle = '#fff'
      ctx.font = 'bold 15px system-ui, sans-serif'
      ctx.textBaseline = 'middle'
      ctx.textAlign = 'left'
      ctx.fillText(tRef.current('game.hudScore', { score: sim.score }), 12, 17)
      ctx.textAlign = 'right'
      ctx.fillText(tRef.current('game.hudTime', { seconds: left }), W - 12, 17)
    }

    const frame = (now: number) => {
      // Tab ẩn thì rAF dừng; quay lại không "bù" cả đoạn — coi như tạm dừng (máy chủ chỉ chặn chơi NHANH hơn thật).
      const dt = Math.min(250, now - last)
      last = now
      if (go.current) acc += dt
      while (acc >= STEP_MS && !sim.over) {
        const dir = queue.current.shift() ?? 0
        if (dir) inputs.push([sim.tick, dir])
        sim.step(dir)
        acc -= STEP_MS
      }
      draw(acc / STEP_MS, dt)
      if (sim.over && !ended) {
        ended = true
        // Để người chơi kịp thấy cú đâm rồi mới chuyển màn kết quả.
        setTimeout(() => onOverRef.current(inputs, sim.score), sim.crashed ? 700 : 0)
      }
      if (!ended) raf = requestAnimationFrame(frame)
    }
    raf = requestAnimationFrame(frame)

    const onKey = (e: KeyboardEvent) => {
      const dir = e.key === 'ArrowLeft' || e.key === 'a' ? -1 : e.key === 'ArrowRight' || e.key === 'd' ? 1 : 0
      if (!dir || !go.current) return
      e.preventDefault()
      if (queue.current.length < 3) queue.current.push(dir)
    }
    window.addEventListener('keydown', onKey)
    return () => {
      cancelAnimationFrame(raf)
      window.removeEventListener('resize', resize)
      window.removeEventListener('keydown', onKey)
    }
  }, [seed])

  const tap = (e: PointerEvent<HTMLCanvasElement>) => {
    if (!go.current) return
    const r = e.currentTarget.getBoundingClientRect()
    if (queue.current.length < 3) queue.current.push(e.clientX - r.left < r.width / 2 ? -1 : 1)
  }

  return (
    <div className="space-y-2">
      <div className="relative w-full max-w-sm mx-auto">
        <canvas ref={canvasRef} onPointerDown={tap} aria-label={t('game.canvasLabel')}
                className="w-full block rounded-2xl touch-none select-none cursor-pointer"
                style={{ aspectRatio: '1 / 1.4' }} />
        {seed === null ? (
          <div className="absolute inset-0 rounded-2xl bg-black/45 flex flex-col items-center justify-center gap-3 p-6 text-center">
            <p className="text-sm text-white/90">{t('game.ready')}</p>
            <button onClick={onReady} disabled={starting} className="btn btn-primary px-8 text-lg">
              {starting ? t('loading') : t('game.go')}
            </button>
          </div>
        ) : count > 0 && (
          <div className="absolute inset-0 flex items-center justify-center pointer-events-none" aria-live="assertive">
            <span key={count} className="text-7xl font-extrabold text-white drop-shadow-lg step-pop">{count}</span>
          </div>
        )}
      </div>
      <p className="text-xs text-ink-faint text-center">{t('game.controls')}</p>
    </div>
  )
}
