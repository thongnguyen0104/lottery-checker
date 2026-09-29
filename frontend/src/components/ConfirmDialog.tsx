import { useEffect } from 'react'
import { createPortal } from 'react-dom'

type Props = {
  title: string
  message?: string
  confirmLabel: string
  onConfirm: () => void
  onClose: () => void
}

/** Hộp xác nhận thay cho window.confirm — cùng kiểu với DonateDialog/ThemePicker. */
export default function ConfirmDialog({ title, message, confirmLabel, onConfirm, onClose }: Props) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  // Portal ra body: nơi gọi (AppHeader) có backdrop-blur, sẽ nhốt position:fixed trong header.
  return createPortal(
    <div className="fixed inset-0 z-50 flex items-end sm:items-center justify-center">
      <div className="absolute inset-0 bg-black/40" onClick={onClose} aria-hidden />

      <div role="alertdialog" aria-modal="true" aria-label={title}
           className="sheet-up relative w-full sm:w-[360px] bg-surface text-ink rounded-t-3xl sm:rounded-2xl
                      border border-line shadow-2xl px-5 pt-3 sm:pt-5
                      pb-[calc(1.25rem+env(safe-area-inset-bottom))] sm:pb-5">
        <div className="mx-auto mb-3 h-1.5 w-10 rounded-full bg-line sm:hidden" aria-hidden />
        <h2 className="text-lg font-bold">{title}</h2>
        {message && <p className="mt-1 text-sm text-ink-soft">{message}</p>}
        <div className="mt-5 grid grid-cols-2 gap-2">
          <button onClick={onClose}
                  className="py-2.5 rounded-xl font-semibold bg-muted text-ink-soft hover:text-ink active:scale-95 transition">
            Huỷ
          </button>
          <button onClick={onConfirm} autoFocus
                  className="py-2.5 rounded-xl font-semibold bg-gradient-to-r from-primary to-primary-end
                             text-on-primary shadow-md shadow-primary/25 active:scale-95 transition">
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>,
    document.body,
  )
}
