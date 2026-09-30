import Webcam from 'react-webcam'
import { useRef, useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
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
// Nội dung: check.json → camera.{off,denied,error}.{title,body}.

export default function CameraCapture({ onCapture }: { onCapture: (blob: Blob) => void }) {
  const { t } = useTranslation('check')
  const webcamRef = useRef<Webcam>(null)
  const [cam, setCam] = useState<Cam>('checking')
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
                {t('camera.alignHint')}
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
                {t('camera.opening')}
              </>
            ) : (
              <>
                <Icon name="camera" className="w-8 h-8 text-white/60" />
                <span className="text-base font-semibold text-white">{t(`camera.${cam}.title`)}</span>
                <span className="max-w-xs">{t(`camera.${cam}.body`)}</span>
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
          ? <><Icon name="retry" className="w-6 h-6" /> {t('camera.retry')}</>
          : <><Icon name="camera" className="w-6 h-6" /> {cam === 'off' ? t('camera.turnOn') : t('camera.capture')}</>}
      </button>
    </div>
  )
}
