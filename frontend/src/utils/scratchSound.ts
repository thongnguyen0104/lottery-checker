// Âm thanh sột soạt khi cào: tổng hợp bằng Web Audio (nhiễu trắng qua bộ lọc band-pass), không cần file.
const KEY = 'scratch-sound'

export const isScratchSoundOn = () => {
  try { return localStorage.getItem(KEY) !== 'off' } catch { return true }
}
export const setScratchSoundOn = (on: boolean) => {
  try { localStorage.setItem(KEY, on ? 'on' : 'off') } catch { /* private mode: bỏ qua */ }
}

let ctx: AudioContext | null = null
let gain: GainNode | null = null
let filter: BiquadFilterNode | null = null

/** Phải gọi trong sự kiện người dùng (pointerdown) — trình duyệt chặn audio tự phát. */
function ensure() {
  if (ctx) { if (ctx.state === 'suspended') void ctx.resume(); return }
  const AC = window.AudioContext ?? (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext
  if (!AC) return
  ctx = new AC()
  const buf = ctx.createBuffer(1, ctx.sampleRate * 2, ctx.sampleRate)
  const data = buf.getChannelData(0)
  for (let i = 0; i < data.length; i++) data[i] = Math.random() * 2 - 1
  const src = ctx.createBufferSource()
  src.buffer = buf
  src.loop = true
  filter = ctx.createBiquadFilter()
  filter.type = 'bandpass'
  filter.frequency.value = 3000
  filter.Q.value = 0.8
  gain = ctx.createGain()
  gain.gain.value = 0
  src.connect(filter).connect(gain).connect(ctx.destination)
  src.start()
}

export function scratchStart() { if (isScratchSoundOn()) ensure() }

/** Gọi mỗi lần di chuyển khi cào; speed = px đã đi — vuốt nhanh thì to và "sắc" hơn. */
export function scratchTick(speed: number) {
  if (!ctx || !gain || !filter || !isScratchSoundOn()) return
  const t = ctx.currentTime
  const vol = Math.min(0.35, 0.06 + speed / 120)
  gain.gain.cancelScheduledValues(t)
  gain.gain.setTargetAtTime(vol, t, 0.01)
  gain.gain.setTargetAtTime(0, t + 0.06, 0.04)   // tự tắt dần nếu ngừng vuốt
  filter.frequency.setTargetAtTime(2200 + Math.min(speed, 40) * 60 + Math.random() * 600, t, 0.02)
}

export function scratchStop() {
  if (!ctx || !gain) return
  gain.gain.cancelScheduledValues(ctx.currentTime)
  gain.gain.setTargetAtTime(0, ctx.currentTime, 0.03)
}
