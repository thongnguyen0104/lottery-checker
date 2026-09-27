import Webcam from 'react-webcam'
import { useRef, useCallback, useState } from 'react'
import Icon from './Icon'

/**
 * Chụp đúng phần user đang THẤY trong khung (video object-cover bị cắt bớt trên/dưới), ở độ
 * phân giải gốc của camera. Không dùng webcam.getScreenshot(): nó chụp cả khung hình — chữ ngoài
 * khung (đồ trên bàn...) vẫn lọt vào OCR — và còn thu ảnh về bằng bề ngang hiển thị (~350px).
 */
function captureVisible(video: HTMLVideoElement): Promise<Blob | null> {
  const vw = video.videoWidth, vh = video.videoHeight
  const cw = video.clientWidth, ch = video.clientHeight
  if (!vw || !vh || !cw || !ch) return Promise.resolve(null)

  // object-cover: phóng video cho phủ kín khung rồi cắt đều hai phía thừa.
  const scale = Math.max(cw / vw, ch / vh)
  const sw = cw / scale, sh = ch / scale
  const canvas = document.createElement('canvas')
  canvas.width = Math.round(sw)
  canvas.height = Math.round(sh)
  const ctx = canvas.getContext('2d')
  if (!ctx) return Promise.resolve(null)
  ctx.drawImage(video, (vw - sw) / 2, (vh - sh) / 2, sw, sh, 0, 0, canvas.width, canvas.height)
  return new Promise(r => canvas.toBlob(r, 'image/jpeg', 0.92))
}

// 4 góc của khung căn vé — mỗi góc: vị trí + 2 cạnh viền + bo góc.
const CORNERS = [
  'top-0 left-0 border-t-4 border-l-4 rounded-tl-2xl',
  'top-0 right-0 border-t-4 border-r-4 rounded-tr-2xl',
  'bottom-0 left-0 border-b-4 border-l-4 rounded-bl-2xl',
  'bottom-0 right-0 border-b-4 border-r-4 rounded-br-2xl',
]

export default function CameraCapture({ onCapture }: { onCapture: (blob: Blob) => void }) {
  const webcamRef = useRef<Webcam>(null)
  // Laptop không có camera / user chặn quyền thì khung đen trơn, tưởng app treo → báo rõ.
  const [cam, setCam] = useState<'starting' | 'ready' | 'error'>('starting')

  const capture = useCallback(async () => {
    const video = webcamRef.current?.video
    if (!video) return
    const blob = await captureVisible(video)
    if (blob) onCapture(blob)
  }, [onCapture])

  return (
    <div>
      {/* Khung ngang 4:3 (vé cũng nằm ngang) thay vì cao theo camera dọc của điện thoại —
          đỡ chiếm gần hết màn hình, nút Chụp và ô Chọn ảnh hiện ngay không cần cuộn. */}
      <div className="relative aspect-[4/3] overflow-hidden rounded-xl bg-slate-900">
        <Webcam
          ref={webcamRef}
          videoConstraints={{ facingMode: 'environment' }}
          onUserMedia={() => setCam('ready')}
          onUserMediaError={() => setCam('error')}
          className="w-full h-full object-cover"
        />
        {/* Khung hướng dẫn căn vé */}
        <div className="absolute inset-x-[9%] top-1/2 -translate-y-1/2 h-[48%] pointer-events-none">
          {CORNERS.map(c => (
            <span key={c} className={`absolute w-8 h-8 border-brand-400 drop-shadow-[0_0_6px_rgba(0,0,0,.5)] ${c}`} />
          ))}
          <span className="absolute -top-9 left-1/2 -translate-x-1/2 whitespace-nowrap rounded-full
                           bg-black/45 backdrop-blur px-3 py-1 text-xs font-medium text-white">
            Đặt vé vào giữa khung
          </span>
        </div>
        {cam !== 'ready' && (
          <div className="absolute inset-0 flex flex-col items-center justify-center gap-2 p-6 text-center
                          text-sm text-white/80">
            {cam === 'starting' ? (
              <>
                <span className="w-8 h-8 rounded-full border-2 border-white/25 border-t-white motion-safe:animate-spin" />
                Đang mở camera...
              </>
            ) : (
              <>
                <Icon name="camera" className="w-8 h-8 text-white/60" />
                Không mở được camera. Hãy cho phép quyền camera, hoặc chọn ảnh vé có sẵn.
              </>
            )}
          </div>
        )}
      </div>
      <button onClick={capture} disabled={cam !== 'ready'}
              className="btn btn-primary mt-3 w-full py-3.5 text-lg">
        <Icon name="camera" className="w-6 h-6" /> Chụp vé
      </button>
    </div>
  )
}
