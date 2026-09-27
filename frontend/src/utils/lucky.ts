// Vietlott: người chơi chọn 6 số khác nhau — Mega từ 01..45, Power từ 01..55.
export const LUCKY_GAMES = {
  mega: { name: 'Mega 6/45', short: '6/45', max: 45, jackpotOdds: 8_145_060 },   // C(45, 6)
  power: { name: 'Power 6/55', short: '6/55', max: 55, jackpotOdds: 28_989_675 }, // C(55, 6)
} as const

export type LuckyGame = keyof typeof LUCKY_GAMES

// Số nguyên đều trong [0, n). crypto thay Math.random; bỏ phần dư cuối dải 2^32 để `% n`
// không nghiêng về số nhỏ.
function randomInt(n: number): number {
  const limit = Math.floor(0x1_0000_0000 / n) * n
  const buf = new Uint32Array(1)
  do crypto.getRandomValues(buf); while (buf[0] >= limit)
  return buf[0] % n
}

/** `count` số khác nhau trong 1..max, xếp tăng dần như vé Vietlott in ra. */
export function pickNumbers(max: number, count = 6): number[] {
  const pool = Array.from({ length: max }, (_, i) => i + 1)
  // Fisher–Yates dừng sớm: chỉ cần xáo `count` vị trí đầu.
  for (let i = 0; i < count; i++) {
    const j = i + randomInt(max - i)
    ;[pool[i], pool[j]] = [pool[j], pool[i]]
  }
  return pool.slice(0, count).sort((a, b) => a - b)
}
