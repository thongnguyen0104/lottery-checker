import Webcam from 'react-webcam'
import { useRef, useCallback, useEffect, useState } from 'react'
import Icon from './Icon'

/**
 * checking: đang hỏi trình duyệt quyền camera · off: chờ user bấm Bật camera · starting/ready: đã
 * gọi camera, đang mở / đang chạy · denied: user chặn hoặc đóng hộp hỏi quyền · error: lỗi khác
 * (máy không có camera, app khác đang chiếm...).
 */
type Cam = 'checking' | 'off' | 'starting' | 'ready' | 'denied' | 'error'

// Đã mở được camera trong phiên này → quay lại khung chụp (Dò vé khác, đổi tab) thì mở luôn, khỏi bấm lại.
let startedThisSession = false

/** Quyền camera hiện tại; null = trình duyệt không cho hỏi (Firefox cũ...) → coi như chưa biết. */
async function cameraPermission(): Promise<PermissionStatus | null> {
  try {
    return await navigator.permissions.query({ name: 'camera' })
  } catch {
    return null
  }
}

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

// Lời trong khung khi camera chưa chạy. "off" báo trước hộp xin quyền để user không bất ngờ mà bấm Chặn;
// lỗi thì chỉ đường gỡ, vì trình duyệt đã chặn thì app không tự hỏi lại được.
const CAM_TEXT = {
  off: {
    title: 'Dò vé bằng camera',
    body: 'Bấm Bật camera, trình duyệt hỏi thì chọn Cho phép. Camera chỉ dùng để chụp vé.',
  },
  denied: {
    title: 'Chưa được dùng camera',
    body: 'Bấm Thử lại rồi chọn Cho phép. Không thấy hỏi thì bấm biểu tượng cạnh địa chỉ web, '
        + 'bật Camera — hoặc chọn ảnh vé có sẵn.',
  },
  error: {
    title: 'Không mở được camera',
    body: 'Máy không có camera, hoặc ứng dụng khác đang dùng. Bạn có thể chọn ảnh vé có sẵn.',
  },
}

export default function CameraCapture({ onCapture }: { onCapture: (blob: Blob) => void }) {
  const webcamRef = useRef<Webcam>(null)
  const autoTimer = useRef<number | null>(null)
  const stableHits = useRef(0)
  const prevScore = useRef<number | null>(null)
  const [cam, setCam] = useState<Cam>('checking')
  const [autoCapture, setAutoCapture] = useState(true)
  const [ticketInFrame, setTicketInFrame] = useState(false)
  // Webcam chỉ được mount lúc này — mount là trình duyệt bật camera (và hỏi quyền nếu chưa có).
  const live = cam === 'starting' || cam === 'ready'

  useEffect(() => {
    let status: PermissionStatus | null = null
    let gone = false
    // Đổi quyền ngay trên trình duyệt (sửa trong cài đặt trang...) → theo luôn, khỏi tải lại trang.
    const onChange = () => setCam(c => {
      if (status!.state === 'denied') return 'denied'
      if (c === 'starting' || c === 'ready') return c
      return status!.state === 'granted' ? 'starting' : 'off'
    })
    void cameraPermission().then(s => {
      if (gone) return
      status = s
      s?.addEventListener('change', onChange)
      if (s?.state === 'denied') return setCam('denied')
      // Chỉ tự mở khi đã có quyền: chưa có mà mở luôn thì hộp xin quyền bật lên trước khi user kịp
      // biết app định làm gì — dễ bấm Chặn, mà đã chặn thì phải vào cài đặt trình duyệt mới gỡ được.
      // Máy tính (chuột) lần đầu cũng chờ bấm: webcam chĩa vào mặt, ở đó người ta hay chọn ảnh hơn.
      const granted = s ? s.state === 'granted' : startedThisSession
      const auto = startedThisSession || window.matchMedia('(pointer: coarse)').matches
      setCam(granted && auto ? 'starting' : 'off')
    })
    return () => { gone = true; status?.removeEventListener('change', onChange) }
  }, [])

  const capture = useCallback(async () => {
    const video = webcamRef.current?.video
    if (!video) return
    const blob = await captureVisible(video)
    if (blob) onCapture(blob)
  }, [onCapture])

  useEffect(() => {
    if (!live || !autoCapture) {
      if (autoTimer.current) window.clearInterval(autoTimer.current)
      autoTimer.current = null
      stableHits.current = 0
      prevScore.current = null
      setTicketInFrame(false)
      return
    }
    const canvas = document.createElement('canvas')
    const ctx = canvas.getContext('2d', { willReadFrequently: true })
    if (!ctx) return
    const scan = () => {
      const video = webcamRef.current?.video
      if (!video || !video.videoWidth || !video.videoHeight) return
      // Dò đúng vùng trong khung căn vé ở giữa màn hình.
      const rw = Math.round(video.videoWidth * 0.82)
      const rh = Math.round(video.videoHeight * 0.46)
      const rx = Math.round((video.videoWidth - rw) / 2)
      const ry = Math.round((video.videoHeight - rh) / 2)
      canvas.width = 96
      canvas.height = 54
      ctx.drawImage(video, rx, ry, rw, rh, 0, 0, canvas.width, canvas.height)
      const d = ctx.getImageData(0, 0, canvas.width, canvas.height).data
      let edges = 0
      let lumSum = 0
      let prev = 0
      for (let i = 0; i < d.length; i += 4) {
        const y = d[i] * 0.299 + d[i + 1] * 0.587 + d[i + 2] * 0.114
        lumSum += y
        if (i > 0 && Math.abs(y - prev) > 28) edges++
        prev = y
      }
      const pixels = d.length / 4
      const mean = lumSum / pixels
      const edgeDensity = edges / pixels
      const score = edgeDensity * 100 + Math.max(0, 180 - Math.abs(145 - mean)) / 100
      const stable = prevScore.current == null || Math.abs(score - prevScore.current) < 4.2
      prevScore.current = score
      const detected = edgeDensity > 0.28 && mean > 35 && mean < 225
      setTicketInFrame(detected)
      if (detected && stable) {
        stableHits.current += 1
        if (stableHits.current >= 3) {
          stableHits.current = 0
          void capture()
        }
      } else {
        stableHits.current = 0
      }
    }
    autoTimer.current = window.setInterval(scan, 700)
    return () => {
      if (autoTimer.current) window.clearInterval(autoTimer.current)
      autoTimer.current = null
    }
  }, [autoCapture, capture, live])

  return (
    <div>
      {/* Khung ngang 4:3 (vé cũng nằm ngang) thay vì cao theo camera dọc của điện thoại —
          đỡ chiếm gần hết màn hình, nút Chụp và ô Chọn ảnh hiện ngay không cần cuộn. */}
      <div className="relative aspect-[4/3] overflow-hidden rounded-xl bg-slate-900">
        {live && (
          <>
            <Webcam
              ref={webcamRef}
              videoConstraints={{ facingMode: 'environment' }}
              onUserMedia={() => { startedThisSession = true; setCam('ready') }}
              // NotAllowedError: bấm Chặn, hoặc bấm X đóng hộp hỏi (Chrome) — cả hai đều hỏi lại được.
              onUserMediaError={e => setCam(e instanceof DOMException && e.name === 'NotAllowedError'
                                            ? 'denied' : 'error')}
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
          </>
        )}
        {cam !== 'ready' && cam !== 'checking' && (
          <div className="absolute inset-0 flex flex-col items-center justify-center gap-2 p-6 text-center
                          text-sm text-white/70">
            {cam === 'starting' ? (
              <>
                <span className="w-8 h-8 rounded-full border-2 border-white/25 border-t-white motion-safe:animate-spin" />
                Đang mở camera...
              </>
            ) : (
              <>
                <Icon name="camera" className="w-8 h-8 text-white/60" />
                <span className="text-base font-semibold text-white">{CAM_TEXT[cam].title}</span>
                <span className="max-w-xs">{CAM_TEXT[cam].body}</span>
              </>
            )}
          </div>
        )}
      </div>
      {/* Một nút chính cho mọi trạng thái, cùng chỗ để bấm: Bật camera → Chụp vé, lỗi thì Thử lại. */}
      <button onClick={live ? capture : () => setCam('starting')}
              disabled={cam === 'checking' || cam === 'starting'}
              className="btn btn-primary mt-3 w-full py-3.5 text-lg">
        {cam === 'denied' || cam === 'error'
          ? <><Icon name="retry" className="w-6 h-6" /> Thử lại</>
          : <><Icon name="camera" className="w-6 h-6" /> {cam === 'off' ? 'Bật camera' : 'Chụp vé'}</>}
      </button>
      {live && (
        <div className="mt-3 rounded-xl border border-line bg-surface/70 p-3 text-sm">
          <label className="flex items-center justify-between gap-3">
            <span className="font-medium">Tự chụp khi phát hiện vé trong khung</span>
            <input type="checkbox" checked={autoCapture}
                   onChange={e => setAutoCapture(e.target.checked)} />
          </label>
          <div className={`mt-2 text-xs ${ticketInFrame ? 'text-ok' : 'text-ink-faint'}`}>
            {ticketInFrame ? 'Đã thấy vé trong khung, giữ yên để tự chụp.' : 'Chưa thấy vé rõ trong khung.'}
          </div>
        </div>
      )}
    </div>
  )
}
