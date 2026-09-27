import Webcam from 'react-webcam'
import { useRef, useCallback } from 'react'

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

export default function CameraCapture({ onCapture }: { onCapture: (blob: Blob) => void }) {
  const webcamRef = useRef<Webcam>(null)

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
      <div className="relative aspect-[4/3] overflow-hidden rounded-lg bg-gray-900">
        <Webcam
          ref={webcamRef}
          videoConstraints={{ facingMode: 'environment' }}
          className="w-full h-full object-cover"
        />
        {/* Khung hướng dẫn căn vé */}
        <div className="absolute inset-x-8 top-1/2 -translate-y-1/2 h-32
                        border-4 border-yellow-400 rounded pointer-events-none" />
      </div>
      <button onClick={capture}
              className="mt-4 w-full bg-blue-600 text-white py-3 rounded-lg">
        📷 Chụp vé
      </button>
    </div>
  )
}
