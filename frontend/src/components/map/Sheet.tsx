import { useEffect, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { useTranslation } from 'react-i18next'
import Icon from '../Icon'

/**
 * Hộp thoại của bản đồ: trượt từ đáy lên trên điện thoại, giữa màn hình trên màn rộng — cùng kiểu
 * ConfirmDialog. Portal ra body để thoát stacking context của bản đồ (Leaflet dùng z-index rất cao).
 */
export default function Sheet({ title, onClose, children, wide }: {
  title: string; onClose: () => void; children: ReactNode; wide?: boolean
}) {
  const { t } = useTranslation('map')
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  return createPortal(
    <div className="fixed inset-0 z-50 flex items-end sm:items-center justify-center">
      <div className="absolute inset-0 bg-black/40" onClick={onClose} aria-hidden />
      <div role="dialog" aria-modal="true" aria-label={title}
           className={`sheet-up relative w-full ${wide ? 'sm:w-[480px]' : 'sm:w-[400px]'} max-h-[92dvh] flex flex-col
                       bg-surface text-ink rounded-t-3xl sm:rounded-2xl border border-line shadow-2xl`}>
        <div className="mx-auto mt-3 h-1.5 w-10 rounded-full bg-line sm:hidden" aria-hidden />
        <div className="flex items-center gap-2 px-5 pt-3 sm:pt-5">
          <h2 className="flex-1 text-lg font-bold">{title}</h2>
          <button onClick={onClose} aria-label={t('detail.close')}
                  className="w-9 h-9 -mr-2 rounded-xl flex items-center justify-center text-ink-faint hover:bg-muted">
            <Icon name="close" />
          </button>
        </div>
        <div className="overflow-y-auto px-5 pt-3 pb-[calc(1.25rem+env(safe-area-inset-bottom))] sm:pb-5">
          {children}
        </div>
      </div>
    </div>,
    document.body,
  )
}

/** Nút chính trong hộp thoại bản đồ. */
export const primaryBtn = `py-2.5 px-4 rounded-xl font-semibold bg-gradient-to-r from-primary to-primary-end text-on-primary
  shadow-md shadow-primary/25 active:scale-95 transition disabled:opacity-50 disabled:pointer-events-none`

export const secondaryBtn = `py-2 px-3 rounded-xl text-sm font-semibold bg-muted text-ink-soft hover:text-ink active:scale-95
  transition disabled:opacity-50 disabled:pointer-events-none inline-flex items-center justify-center gap-1.5`
