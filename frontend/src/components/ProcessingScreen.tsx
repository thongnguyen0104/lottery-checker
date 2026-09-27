import { useState } from 'react'

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
    <div className="bg-gray-950 rounded-[28px] p-3 pt-5 shadow-xl">
      <h2 className="text-white text-center font-semibold mb-4">{title}</h2>

      <div className="bg-gray-800/60 rounded-2xl min-h-[300px] flex items-center justify-center overflow-hidden">
        {/* Khung bọc sát ảnh → vạch quét chạy đúng trong mép ảnh chứ không tràn ra nền.
            min-w-0 + max-w-full: flex item mặc định nở theo bề rộng gốc của ảnh (4000px) */}
        <div className="relative overflow-hidden min-w-0 max-w-full">
          {showImage ? (
            <img src={imageUrl} alt="Vé đang được quét" decoding="async"
                 onLoad={() => setImgLoaded(true)} onError={() => setImgFailed(true)}
                 className={`block max-w-full max-h-[55vh] brightness-[.65] transition-opacity duration-300
                             ${imgLoaded ? 'opacity-100' : 'opacity-0'}`} />
          ) : (
            <div className="w-72 aspect-[2/1] m-8 rounded-lg border-2 border-dashed border-gray-600
                            flex items-center justify-center text-4xl opacity-60">🎫</div>
          )}
          <div className="scan-sweep absolute inset-0 pointer-events-none">
            <div className="absolute inset-x-0 bottom-0 h-0.5 bg-teal-300
                            shadow-[0_0_14px_3px_rgba(94,234,212,0.75)]" />
          </div>
        </div>
      </div>

      <ol className="bg-white rounded-2xl mt-3 px-5 py-4 space-y-3.5" role="status" aria-live="polite">
        {steps.map((label, i) => {
          const state = i < current ? 'done' : i === current ? 'active' : 'pending'
          return (
            <li key={label} className="flex items-center gap-3">
              <StepIcon state={state} />
              <span className={state === 'pending' ? 'text-gray-400' : 'text-gray-900 font-medium'}>
                {label}
              </span>
              {state === 'active' && detail && (
                <span className="ml-auto text-sm text-teal-700 tabular-nums">{detail}</span>
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
      <span className="step-pop shrink-0 w-6 h-6 rounded-full bg-teal-700 flex items-center justify-center">
        <svg viewBox="0 0 16 16" className="w-3.5 h-3.5 text-white" fill="none"
             stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
          <path d="M3.5 8.5l3 3 6-7" />
        </svg>
      </span>
    )
  }
  if (state === 'active') {
    return (
      <span className="shrink-0 w-6 h-6 rounded-full border-2 border-teal-100 border-t-teal-600
                       motion-safe:animate-spin" />
    )
  }
  return <span className="shrink-0 w-6 h-6 rounded-full border-2 border-gray-200" />
}
