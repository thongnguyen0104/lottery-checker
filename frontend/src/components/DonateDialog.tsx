import { useEffect } from 'react'
import Icon from './Icon'

const QR_SRC = '/donate-momo.jpg'

/** Bảng ủng hộ: ảnh QR MoMo/VietQR. Điện thoại không tự quét được màn hình của chính nó
 *  → có nút tải ảnh về để mở bằng app ngân hàng (Quét QR → chọn ảnh). Cùng kiểu với ThemePicker. */
export default function DonateDialog({ onClose }: { onClose: () => void }) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  return (
    <div className="fixed inset-0 z-50">
      <div className="absolute inset-0 bg-black/40" onClick={onClose} aria-hidden />

      <div role="dialog" aria-modal="true" aria-label="Ủng hộ Dò Vé Số"
           className="sheet-up absolute inset-x-0 bottom-0 max-h-[90vh] overflow-y-auto
                      bg-surface text-ink rounded-t-3xl border border-line shadow-2xl
                      px-5 pt-3 pb-[calc(1.25rem+env(safe-area-inset-bottom))]
                      sm:inset-x-auto sm:bottom-auto sm:right-4 sm:top-[calc(4.5rem+env(safe-area-inset-top))]
                      sm:w-[400px] sm:rounded-2xl sm:pt-5">
        <div className="mx-auto mb-3 h-1.5 w-10 rounded-full bg-line sm:hidden" aria-hidden />

        <div className="flex items-center justify-between mb-2">
          <h2 className="flex items-center gap-2 text-lg font-bold">
            <Icon name="donate" className="w-5 h-5 text-brand-700 dark:text-brand-400" /> Ủng hộ Dò Vé Số
          </h2>
          <button onClick={onClose} aria-label="Đóng"
                  className="btn-close w-9 h-9 rounded-full flex items-center justify-center bg-muted text-ink-soft">
            <Icon name="close" className="w-4 h-4" />
          </button>
        </div>

        <p className="text-sm text-ink-soft mb-4">
          Web miễn phí, không quảng cáo. Nếu thấy có ích, bạn mời mình ly cà phê để duy trì máy chủ nhé!
        </p>

        <img src={QR_SRC} alt="Mã QR MoMo / VietQR — NGUYEN HOANG THONG"
             className="w-full max-w-xs mx-auto rounded-2xl border border-line" />

        <a href={QR_SRC} download="ung-ho-do-ve-so.jpg"
           className="mt-4 flex items-center justify-center gap-2 w-full py-3 rounded-xl font-semibold
                      bg-gradient-to-r from-primary to-primary-end text-on-primary shadow-md shadow-primary/25
                      active:scale-[0.98] transition">
          <Icon name="download" className="w-5 h-5" /> Tải ảnh QR
        </a>
        <p className="mt-2 text-xs text-center text-ink-faint">
          Trên điện thoại: tải ảnh về rồi mở app ngân hàng/MoMo → Quét QR → chọn ảnh.
        </p>
      </div>
    </div>
  )
}
