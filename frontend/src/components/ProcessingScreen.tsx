import { useState } from 'react'
import Icon from './Icon'

type Props = {
  title: string
  /** Ảnh vé vừa chụp/chọn (object URL) — null/lỗi giải mã (vd HEIC trên Chrome) thì vẽ khung vé giả. */
  imageUrl: string | null
  steps: string[]
  /** Chỉ số bước đang chạy; bằng steps.length = xong hết. */
  current: number
  /** Ghi chú nhỏ cạnh bước đang chạy (vd % upload). */
  detail?: string
}

// Màn chờ kiểu "máy đang quét vé": ảnh vé tối đi, vạch sáng quét lên xuống, bên dưới là
// checklist từng bước (xong ✓ / đang chạy / chờ) để user thấy máy đang làm tới đâu.
export default function ProcessingScreen({ title, imageUrl, steps, current, detail }: Props) {
  const [imgFailed, setImgFailed] = useState(false)
  // Ảnh gốc 12MP giải mã mất một nhịp → hiện dần khi xong, thay vì "bụp" ra giữa chừng.
  const [imgLoaded, setImgLoaded] = useState(false)
  const showImage = imageUrl && !imgFailed

  return (
    <div className="bg-slate-950 rounded-3xl p-3 pt-5 shadow-2xl shadow-black/40 ring-1 ring-white/5">
      <h2 className="text-slate-50 text-center font-semibold mb-4">{title}</h2>

      <div className="bg-slate-800/60 rounded-2xl min-h-[300px] flex items-center justify-center overflow-hidden">
        {/* Khung bọc sát ảnh → vạch quét chạy đúng trong mép ảnh chứ không tràn ra nền.
            min-w-0 + max-w-full: flex item mặc định nở theo bề rộng gốc của ảnh (4000px) */}
        <div className="relative overflow-hidden min-w-0 max-w-full">
          {showImage ? (
            <img src={imageUrl} alt="Vé đang được quét" decoding="async"
                 onLoad={() => setImgLoaded(true)} onError={() => setImgFailed(true)}
                 className={`block max-w-full max-h-[55vh] brightness-[.65] transition-opacity duration-300
                             ${imgLoaded ? 'opacity-100' : 'opacity-0'}`} />
          ) : (
            <div className="w-72 aspect-[2/1] m-8 rounded-lg border-2 border-dashed border-slate-600
                            flex items-center justify-center text-slate-500">
              <Icon name="ticket" className="w-12 h-12" />
            </div>
          )}
          {/* Chỉ gắn vạch khi khung đã đủ cỡ (ảnh tải xong, hoặc khung vé giả cỡ cố định). Safari
              đổi translateY(-100%) ra px theo chiều cao khung lúc animation BẮT ĐẦU và không tính lại
              khi khung đổi cỡ: bắt đầu lúc ảnh chưa tải (khung cao 0) thì -100% = 0px, vạch nằm im ở
              đáy. Ảnh tải nhanh hay chậm hơn nhịp đó thì tuỳ lượt, nên lỗi lúc có lúc không. */}
          {(!showImage || imgLoaded) && (
            <div className="scan-sweep absolute inset-0 pointer-events-none">
              <div className="scan-line absolute inset-x-0 bottom-0 h-0.5 bg-brand-300" />
            </div>
          )}
        </div>
      </div>

      <ol className="bg-surface rounded-2xl mt-3 px-5 py-4 space-y-3.5" role="status" aria-live="polite">
        {steps.map((label, i) => {
          const state = i < current ? 'done' : i === current ? 'active' : 'pending'
          return (
            <li key={label} className="flex items-center gap-3">
              <StepIcon state={state} />
              <span className={state === 'pending' ? 'text-ink-faint' : 'text-ink font-medium'}>
                {label}
              </span>
              {state === 'active' && detail && (
                <span className="ml-auto text-sm text-brand-700 dark:text-brand-400 tabular-nums">{detail}</span>
              )}
            </li>
          )
        })}
      </ol>
    </div>
  )
}

function StepIcon({ state }: { state: 'done' | 'active' | 'pending' }) {
  if (state === 'done') {
    return (
      <span className="step-pop shrink-0 w-6 h-6 rounded-full bg-gradient-to-br from-primary to-primary-end
                       text-on-primary flex items-center justify-center">
        <Icon name="check" className="w-3.5 h-3.5" strokeWidth={3} />
      </span>
    )
  }
  if (state === 'active') {
    return (
      <span className="shrink-0 w-6 h-6 rounded-full border-2 border-brand-500/20 border-t-brand-500
                       motion-safe:animate-spin" />
    )
  }
  return <span className="shrink-0 w-6 h-6 rounded-full border-2 border-line" />
}
