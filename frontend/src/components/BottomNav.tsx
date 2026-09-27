import Icon from './Icon'
import { VIEWS, type View } from '../views'

/** Thanh tab dưới đáy cho điện thoại (ẩn từ md trở lên — màn rộng dùng tab trên AppHeader). */
export default function BottomNav({ view, onChange }: { view: View; onChange: (v: View) => void }) {
  return (
    <nav aria-label="Tính năng"
         className="md:hidden fixed inset-x-0 bottom-0 z-30 pb-[env(safe-area-inset-bottom)]
                    bg-surface/85 backdrop-blur-xl border-t border-line/70">
      <div className="grid grid-cols-3 max-w-md mx-auto px-2">
        {VIEWS.map(v => {
          const active = v.id === view
          return (
            <button key={v.id} onClick={() => onChange(v.id)} aria-current={active ? 'page' : undefined}
                    className={`flex flex-col items-center gap-0.5 pt-2 pb-2.5 text-[11px] font-semibold transition
                                ${active ? 'text-brand-700 dark:text-brand-400' : 'text-ink-faint'}`}>
              {/* Đang chọn: icon + chữ màu brand trên viên nền nhạt; chưa chọn: xám slate */}
              <span className={`w-14 h-8 rounded-full flex items-center justify-center transition
                                ${active ? 'bg-brand-500/15' : ''}`}>
                <Icon name={v.icon} className="w-[22px] h-[22px]" />
              </span>
              {v.short}
            </button>
          )
        })}
      </div>
    </nav>
  )
}
