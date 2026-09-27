import type { CSSProperties } from 'react'

const COLORS = ['rgb(var(--brand-500))', 'rgb(var(--accent))', '#FACC15', '#34D399', '#60A5FA', '#F472B6']

// Số giả ngẫu nhiên cố định theo chỉ số mảnh: pháo lần nào cũng đẹp như nhau, và tính một lần
// lúc nạp module chứ không gọi Math.random trong lúc render.
const rnd = (i: number, k: number) => {
  const x = Math.sin(i * 12.9898 + k * 78.233) * 43758.5453
  return x - Math.floor(x)
}

const PIECES = Array.from({ length: 40 }, (_, i) => ({
  left: `${rnd(i, 1) * 100}%`,
  width: 6 + rnd(i, 2) * 5,
  height: 9 + rnd(i, 3) * 8,
  background: COLORS[i % COLORS.length],
  borderRadius: i % 4 === 0 ? '999px' : '2px',
  '--drift': `${(rnd(i, 4) - 0.5) * 30}vw`,
  '--spin': `${(rnd(i, 5) * 2 - 1) * 720}deg`,
  '--dur': `${2.2 + rnd(i, 6) * 1.6}s`,
  '--delay': `${rnd(i, 7) * 0.7}s`,
}) as CSSProperties)

/** Pháo giấy rơi một lượt khi vé trúng (mỗi mảnh rơi hết màn hình rồi nằm ngoài khung nhìn).
 *  Người bật "giảm chuyển động" thì không hiện — xem .confetti-piece trong index.css. */
export default function Confetti() {
  return (
    <div aria-hidden className="pointer-events-none fixed inset-0 z-40 overflow-hidden">
      {PIECES.map((style, i) => <span key={i} className="confetti-piece" style={style} />)}
    </div>
  )
}
