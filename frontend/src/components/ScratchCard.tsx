import { useLayoutEffect, useRef, useState, type ReactNode } from 'react'
import { isScratchSoundOn, scratchStart, scratchStop, scratchTick, setScratchSoundOn } from '../utils/scratchSound'

type Props = {
  children: ReactNode
  /** Gọi 1 lần khi lớp phủ đã mở hết (tự mở hoặc bấm "Cào tất cả"). */
  onReveal: () => void
}

const BRUSH = 16          // bán kính nét cào (px CSS) — nhỏ để phải cào kỹ như vé thật
const REVEAL_AT = 0.8     // cào được 80% diện tích mới tự mở phần còn lại
const CHECK_EVERY = 200   // ms — getImageData tốn CPU nên chỉ đo theo nhịp throttle + lúc buông tay

/**
 * Lớp tráng bạc (canvas) phủ lên kết quả: vuốt/rê chuột để cào, dùng
 * globalCompositeOperation='destination-out' xóa trong suốt để lộ nội dung HTML bên dưới.
 */
export default function ScratchCard({ children, onReveal }: Props) {
  const wrapRef = useRef<HTMLDivElement>(null)
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const [revealed, setRevealed] = useState(false)
  const [gone, setGone] = useState(false)   // đã fade xong → gỡ canvas khỏi DOM
  const [painted, setPainted] = useState(false)   // chưa vẽ xong lớp bạc thì ẩn hẳn kết quả, tránh chớp lộ
  const scratching = useRef(false)
  const last = useRef<{ x: number; y: number } | null>(null)
  const lastCheck = useRef(0)
  const [soundOn, setSoundOn] = useState(isScratchSoundOn)
  const toggleSound = () => { setScratchSoundOn(!soundOn); setSoundOn(!soundOn); if (soundOn) scratchStop() }

  const reveal = () => {
    if (revealed) return
    setRevealed(true)
    scratchStop()
    onReveal()
    setTimeout(() => setGone(true), 500)
  }

  // Vẽ lớp bạc theo kích thước thật của nội dung, scale theo devicePixelRatio cho nét trên màn Retina.
  useLayoutEffect(() => {
    const wrap = wrapRef.current, canvas = canvasRef.current
    if (!wrap || !canvas) return
    let drawnW = 0, drawnH = 0
    const paint = () => {
      const { width: w, height: h } = wrap.getBoundingClientRect()
      // Chỉ vẽ lại khi đổi kích thước đáng kể (vẽ lại = phủ kín lại phần đã cào)
      if (Math.abs(w - drawnW) < 1 && Math.abs(h - drawnH) < 1) return
      drawnW = w; drawnH = h
      const dpr = window.devicePixelRatio || 1
      canvas.width = Math.round(w * dpr)
      canvas.height = Math.round(h * dpr)
      const ctx = canvas.getContext('2d')!
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
      ctx.globalCompositeOperation = 'source-over'

      const g = ctx.createLinearGradient(0, 0, w, h)
      g.addColorStop(0, '#c9ccd1'); g.addColorStop(0.45, '#eef0f2')
      g.addColorStop(0.55, '#b7bbc2'); g.addColorStop(1, '#d7dade')
      ctx.fillStyle = g
      ctx.fillRect(0, 0, w, h)
      // Hạt nhám cho giống lớp tráng bạc thật
      for (let i = 0; i < (w * h) / 18; i++) {
        ctx.fillStyle = Math.random() < 0.5 ? 'rgba(255,255,255,.35)' : 'rgba(0,0,0,.08)'
        ctx.fillRect(Math.random() * w, Math.random() * h, 1.5, 1.5)
      }
      ctx.textAlign = 'center'
      ctx.textBaseline = 'middle'

      // Watermark lặp chéo như vé cào thật: chữ "XỔ SỐ" xen biểu tượng may mắn
      ctx.save()
      ctx.translate(w / 2, h / 2)
      ctx.rotate(-Math.PI / 9)
      ctx.font = '800 13px "Be Vietnam Pro", sans-serif'
      const marks = ['XỔ SỐ', '★', 'MAY MẮN', '♣', 'LỘC', '✦']
      const stepX = 92, stepY = 30, span = Math.hypot(w, h)
      for (let y = -span / 2, row = 0; y < span / 2; y += stepY, row++)
        for (let x = -span / 2 + (row % 2) * (stepX / 2), k = row; x < span / 2; x += stepX, k++) {
          ctx.fillStyle = k % 2 ? 'rgba(90,96,106,.16)' : 'rgba(255,255,255,.45)'
          ctx.fillText(marks[k % marks.length], x, y)
        }
      ctx.restore()


      // Nhãn giữa nằm trên "tem" sáng để đọc được giữa watermark
      const pw = Math.min(w - 32, 300), ph = 58
      ctx.fillStyle = 'rgba(245,246,248,.88)'
      ctx.beginPath()
      ctx.roundRect(w / 2 - pw / 2, h / 2 - ph / 2, pw, ph, 12)
      ctx.fill()
      ctx.fillStyle = '#4b5058'
      ctx.font = '700 18px "Be Vietnam Pro", sans-serif'
      ctx.fillText('🪙 Cào để xem kết quả', w / 2, h / 2 - 10)
      ctx.font = '500 13px "Be Vietnam Pro", sans-serif'
      ctx.fillText('Vuốt ngón tay hoặc rê chuột lên đây', w / 2, h / 2 + 14)
    }
    paint()
    setPainted(true)
    const ro = new ResizeObserver(paint)
    ro.observe(wrap)
    return () => ro.disconnect()
  }, [])

  const point = (e: React.PointerEvent) => {
    const r = canvasRef.current!.getBoundingClientRect()
    return { x: e.clientX - r.left, y: e.clientY - r.top }
  }

  const scratchTo = (p: { x: number; y: number }) => {
    const ctx = canvasRef.current!.getContext('2d')!
    ctx.globalCompositeOperation = 'destination-out'
    ctx.lineCap = 'round'
    ctx.lineJoin = 'round'
    ctx.lineWidth = BRUSH * 2
    ctx.beginPath()
    // Nối với điểm trước để vuốt nhanh không bị đứt nét
    const from = last.current ?? p
    scratchTick(Math.hypot(p.x - from.x, p.y - from.y))
    ctx.moveTo(from.x, from.y)
    ctx.lineTo(p.x + 0.01, p.y)
    ctx.stroke()
    last.current = p
  }

  const checkCleared = () => {
    const canvas = canvasRef.current
    if (!canvas || revealed || !canvas.width) return
    const data = canvas.getContext('2d')!.getImageData(0, 0, canvas.width, canvas.height).data
    let cleared = 0, total = 0
    // Lấy mẫu mỗi 8 pixel là đủ chính xác mà nhanh hơn nhiều
    for (let i = 3; i < data.length; i += 32) { total++; if (data[i] === 0) cleared++ }
    if (cleared / total >= REVEAL_AT) reveal()
  }

  const onDown = (e: React.PointerEvent) => {
    if (revealed) return
    scratching.current = true
    last.current = null
    canvasRef.current!.setPointerCapture(e.pointerId)
    scratchStart()
    scratchTo(point(e))
  }
  const onMove = (e: React.PointerEvent) => {
    if (!scratching.current) return
    scratchTo(point(e))
    if (navigator.vibrate && e.pointerType === 'touch') navigator.vibrate(5)
    const now = performance.now()
    if (now - lastCheck.current > CHECK_EVERY) { lastCheck.current = now; checkCleared() }
  }
  const onUp = () => {
    if (!scratching.current) return
    scratching.current = false
    scratchStop()
    last.current = null
    checkCleared()
  }

  return (
    <div className="space-y-2">
      <div ref={wrapRef} className="relative rounded-2xl">
        {/* Chưa cào thì ẩn nội dung với trình đọc màn hình, tránh lộ kết quả */}
        <div aria-hidden={!revealed} className={painted ? '' : 'invisible'}>{children}</div>
        {!gone && (
          <canvas ref={canvasRef}
                  role="img" aria-label="Lớp cào che kết quả"
                  onPointerDown={onDown} onPointerMove={onMove} onPointerUp={onUp} onPointerCancel={onUp}
                  className={`absolute inset-0 w-full h-full rounded-2xl cursor-grab touch-none select-none
                              transition-opacity duration-500 ${revealed ? 'opacity-0 pointer-events-none' : ''}`} />
        )}
      </div>
      {!revealed && (
        <div className="flex items-center justify-center gap-4 text-sm font-medium text-ink-faint">
          <button onClick={reveal} className="transition hover:text-brand-700 dark:hover:text-brand-400">
            ⚡ Cào tất cả / Xem ngay
          </button>
          <button onClick={toggleSound} aria-pressed={soundOn}
                  className="transition hover:text-brand-700 dark:hover:text-brand-400">
            {soundOn ? '🔊 Tắt âm thanh' : '🔇 Bật âm thanh'}
          </button>
        </div>
      )}
    </div>
  )
}
