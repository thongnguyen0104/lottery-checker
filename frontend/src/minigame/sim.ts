// Mô phỏng minigame đua xe — PHẢI giống hệt backend Services/MiniGameSim.cs (máy chủ chạy lại ván để tự tính
// điểm). Chỉ số nguyên, PRNG mulberry32 dùng Math.imul / >>> 0, cùng thứ tự gọi PRNG. Đổi gì ở đây thì đổi cả
// bên kia và chạy `npm run check:minigame` (so với minigame-golden.json).

export const LANES = 3
export const TICKS_PER_SECOND = 30
export const MAX_TICKS = 90 * TICKS_PER_SECOND
export const FIRST_ROW_AT = 18_000
export const ROW_GAP = 12_000

/** Quãng đường đi trong tick này — tăng dần từ 300 tới 750. */
export const speed = (tick: number) => 300 + Math.floor(tick / 6)

export const rowY = (i: number) => FIRST_ROW_AT + i * ROW_GAP

/** blocked: bitmask làn bị chặn (luôn chừa ≥ 1 làn); coinLane −1 = không có xu. */
export type Row = { blocked: number; coinLane: number }

export class MiniGameSim {
  tick = 0
  lane = 1
  distance = 0
  score = 0
  coins = 0
  crashed = false
  private rng: number
  private rows: Row[] = []
  private nextRow = 0

  constructor(seed: number) {
    this.rng = seed >>> 0
  }

  get over() {
    return this.crashed || this.tick >= MAX_TICKS
  }

  private next() {
    this.rng = (this.rng + 0x6d2b79f5) >>> 0
    let t = this.rng
    t = Math.imul(t ^ (t >>> 15), t | 1) >>> 0
    t = (t ^ (t + Math.imul(t ^ (t >>> 7), t | 61))) >>> 0
    return (t ^ (t >>> 14)) >>> 0
  }

  private nextInt(n: number) {
    return this.next() % n
  }

  /** Hàng thứ i — sinh lần lượt theo thứ tự để PRNG khớp máy chủ. */
  row(i: number): Row {
    while (this.rows.length <= i) {
      const idx = this.rows.length
      const blocked = this.nextInt(100) < Math.min(60, 20 + idx)
        ? 7 & ~(1 << this.nextInt(LANES))   // chặn 2 làn
        : 1 << this.nextInt(LANES)          // chặn 1 làn
      let coinLane = -1
      if (this.nextInt(2) === 0) {
        const free = [0, 1, 2].filter(l => (blocked & (1 << l)) === 0)
        coinLane = free[this.nextInt(free.length)]
      }
      this.rows.push({ blocked, coinLane })
    }
    return this.rows[i]
  }

  /** Hàng kế tiếp chưa đi qua — để vẽ: các hàng từ đây trở đi còn trên đường. */
  get upcoming() {
    return this.nextRow
  }

  /** Chạy 1 tick; dir −1/0/+1 = đổi làn trước khi chạy (ra ngoài đường thì bỏ qua). */
  step(dir: number) {
    if (this.over) return
    this.lane = Math.min(LANES - 1, Math.max(0, this.lane + dir))
    this.distance += speed(this.tick)
    this.tick++
    while (rowY(this.nextRow) <= this.distance) {
      const r = this.row(this.nextRow++)
      if (r.blocked & (1 << this.lane)) { this.crashed = true; return }
      this.score++
      if (r.coinLane === this.lane) { this.score++; this.coins++ }
    }
  }
}

export type Input = [tick: number, dir: number]

export function replay(seed: number, inputs: Input[]) {
  const sim = new MiniGameSim(seed)
  let i = 0
  while (!sim.over) {
    let dir = 0
    if (i < inputs.length && inputs[i][0] === sim.tick) dir = inputs[i++][1]
    sim.step(dir)
  }
  return { score: sim.score, coins: sim.coins, ticks: sim.tick, crashed: sim.crashed }
}
