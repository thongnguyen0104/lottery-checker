import Icon, { BusyDot } from './Icon'
import { useTranslation } from 'react-i18next'
import { BOTTOM_NAV, VIEWS, type View } from '../views'

const TABS = BOTTOM_NAV.map(id => VIEWS.find(v => v.id === id)!)

/** Thanh tab dưới đáy cho điện thoại (ẩn từ md trở lên — màn rộng dùng tab trên AppHeader). */
/** busy = Dò vé đang quét/dò → chấm nhấp nháy trên tab đó khi user đang ở tab khác. */
export default function BottomNav({ view, busy, onChange }: { view: View; busy?: boolean; onChange: (v: View) => void }) {
  const { t } = useTranslation()
  return (
    <nav aria-label={t('app.features')}
         className="md:hidden fixed inset-x-0 bottom-0 z-30 pb-[env(safe-area-inset-bottom)]
                    bg-surface/85 backdrop-blur-xl border-t border-line/70">
      {/* items-end: nút giữa nhô lên thấp hơn các tab khác một chút — căn đáy để chữ thẳng hàng */}
      <div className="grid items-end max-w-md mx-auto px-1" style={{ gridTemplateColumns: `repeat(${TABS.length}, minmax(0, 1fr))` }}>
        {TABS.map(v => {
          const active = v.id === view
          // Dò vé (tính năng chính, nằm giữa): nút tròn màu primary nhô lên khỏi thanh.
          if (v.id === 'check') return (
            <button key={v.id} onClick={() => onChange(v.id)} aria-current={active ? 'page' : undefined}
                    className={`flex flex-col items-center gap-0.5 pb-2.5 text-[11px] font-bold whitespace-nowrap transition active:scale-95
                                ${active ? 'text-brand-700 dark:text-brand-400' : 'text-ink-soft'}`}>
              <span className={`relative -mt-5 w-14 h-14 rounded-full flex items-center justify-center
                                bg-gradient-to-br from-primary to-primary-end text-on-primary
                                shadow-lg shadow-primary/30 ring-4 ring-surface transition
                                ${active ? 'scale-105' : ''}`}>
                <Icon name={v.icon} className={`w-7 h-7 ${active ? 'wiggle' : ''}`} />
                {busy && !active && <BusyDot />}
              </span>
              {t(`views.${v.id}.short`)}
              {busy && !active && <span className="sr-only">{t('app.busy')}</span>}
            </button>
          )
          return (
            <button key={v.id} onClick={() => onChange(v.id)} aria-current={active ? 'page' : undefined}
                    className={`flex flex-col items-center gap-0.5 pt-2 pb-2.5 text-[11px] font-semibold whitespace-nowrap transition active:scale-95
                                ${active ? 'text-brand-700 dark:text-brand-400' : 'text-ink-faint'}`}>
              {/* Đang chọn: icon + chữ màu brand trên viên nền nhạt; chưa chọn: xám slate */}
              <span className={`relative w-12 h-8 rounded-full flex items-center justify-center transition
                                ${active ? 'bg-brand-500/15' : ''}`}>
                <Icon name={v.icon} className={`w-[22px] h-[22px] ${active ? 'wiggle' : ''}`} />
              </span>
              {t(`views.${v.id}.short`)}
            </button>
          )
        })}
      </div>
    </nav>
  )
}
