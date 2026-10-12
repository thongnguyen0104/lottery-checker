// So mô phỏng minigame (src/minigame/sim.ts) với bộ golden dùng chung với test C# (MiniGameTests) —
// 2 bên lệch nhau là máy chủ chấm điểm khác client thấy.
//   npm run check:minigame            → kiểm tra
//   npm run check:minigame -- --write → sinh lại file (chỉ khi CỐ Ý đổi luật chơi, đổi cả MiniGameSim.cs)
import { readFileSync, writeFileSync } from 'node:fs'
import { MiniGameSim, replay, rowY, type Input } from '../src/minigame/sim.ts'

const FILE = new URL('../../backend/LotteryChecker.Tests/TestData/minigame-golden.json', import.meta.url)

type Case = { seed: number; inputs: Input[]; score: number; coins: number; ticks: number; crashed: boolean }

/** Tự lái: né hàng sắp tới, ưu tiên làn có xu — tạo được ván dài, ăn nhiều xu. skill < 1 thì thỉnh thoảng bỏ lỡ. */
function autopilot(seed: number, skill: number): Input[] {
  const sim = new MiniGameSim(seed)
  const inputs: Input[] = []
  let r = seed
  const rand = () => ((r = (Math.imul(r, 1103515245) + 12345) >>> 0) / 2 ** 32)
  while (!sim.over) {
    const row = sim.row(sim.upcoming)
    const near = rowY(sim.upcoming) - sim.distance < 2_500
    let dir = 0
    if (near && rand() < skill) {
      const target = row.coinLane >= 0 ? row.coinLane : [0, 1, 2].find(l => !(row.blocked & (1 << l)))!
      dir = Math.sign(target - sim.lane)
    }
    if (dir) inputs.push([sim.tick, dir])
    sim.step(dir)
  }
  return inputs
}

const seeds = [1, 42, 123456789, 0xdeadbeef, 0xffffffff, 7, 2026, 31337]
const skills = [1, 0.98, 0.9, 1, 0.5, 0, 0.95, 1]
const cases: Case[] = seeds.map((seed, i) => {
  const inputs = autopilot(seed, skills[i])
  return { seed, inputs, ...replay(seed, inputs) }
})

if (process.argv.includes('--write')) {
  writeFileSync(FILE, JSON.stringify(cases) + '\n')
  console.log(cases.map(c => `seed ${c.seed}: score ${c.score}, ticks ${c.ticks}, crashed ${c.crashed}`).join('\n'))
} else {
  const golden: Case[] = JSON.parse(readFileSync(FILE, 'utf8'))
  let bad = 0
  for (const g of golden) {
    const got = replay(g.seed, g.inputs)
    if (got.score !== g.score || got.coins !== g.coins || got.ticks !== g.ticks || got.crashed !== g.crashed) {
      bad++
      console.error(`seed ${g.seed}: expected ${JSON.stringify(g)}, got ${JSON.stringify(got)}`)
    }
  }
  if (bad) process.exit(1)
  console.log(`minigame golden OK (${golden.length} cases)`)
}
