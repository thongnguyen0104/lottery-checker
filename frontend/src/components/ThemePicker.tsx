import { useEffect, type ReactNode } from 'react'
import Icon, { type IconName } from './Icon'
import { ACCENTS, BACKGROUNDS, MODES, type Mode, type Theme } from '../theme'

const MODE_ICONS: Record<Mode, IconName> = { light: 'sun', dark: 'moon', system: 'monitor' }

type Props = {
  theme: Theme
  onChange: (patch: Partial<Theme>) => void
  onClose: () => void
}

/** Điện thoại: tấm trượt từ đáy lên; màn rộng: bảng nổi góc trên phải, cạnh nút mở.
 *  Đổi gì áp dụng ngay (xem trực tiếp trên trang phía sau) — không cần nút Lưu. */
export default function ThemePicker({ theme, onChange, onClose }: Props) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  return (
    <div className="fixed inset-0 z-50">
      {/* Chạm ra ngoài là đóng. Lớp phủ nhạt để vẫn thấy trang đổi màu phía sau. */}
      <div className="absolute inset-0 bg-black/25 sm:bg-black/10" onClick={onClose} aria-hidden />

      <div role="dialog" aria-modal="true" aria-label="Tuỳ chỉnh giao diện"
           className="sheet-up absolute inset-x-0 bottom-0 max-h-[85vh] overflow-y-auto
                      bg-surface text-ink rounded-t-[28px] border border-line shadow-2xl
                      px-5 pt-3 pb-[calc(1.25rem+env(safe-area-inset-bottom))]
                      sm:inset-x-auto sm:bottom-auto sm:right-4 sm:top-[calc(4.5rem+env(safe-area-inset-top))]
                      sm:w-[400px] sm:rounded-3xl sm:pt-5">
        <div className="mx-auto mb-3 h-1.5 w-10 rounded-full bg-line sm:hidden" aria-hidden />

        <div className="flex items-center justify-between mb-4">
          <h2 className="text-lg font-bold">🎨 Giao diện của bạn</h2>
          <button onClick={onClose} aria-label="Đóng"
                  className="w-9 h-9 rounded-full flex items-center justify-center bg-muted text-ink-soft hover:text-ink">
            <Icon name="close" className="w-4 h-4" />
          </button>
        </div>

        <Section title="Chế độ">
          <div className="grid grid-cols-3 gap-1 p-1 rounded-2xl bg-muted">
            {MODES.map(m => {
              const active = theme.mode === m.id
              return (
                <button key={m.id} onClick={() => onChange({ mode: m.id })} aria-pressed={active}
                        className={`flex items-center justify-center gap-1.5 py-2 rounded-xl text-sm font-semibold transition
                                    ${active ? 'bg-surface dark:bg-white/10 shadow-sm text-brand-600 dark:text-brand-300' : 'text-ink-soft'}`}>
                  <Icon name={MODE_ICONS[m.id]} className="w-4 h-4" />
                  {m.name}
                </button>
              )
            })}
          </div>
        </Section>

        <Section title="Màu chủ đạo">
          <div className="grid grid-cols-3 gap-2">
            {ACCENTS.map(a => {
              const active = theme.accent === a.id
              return (
                <button key={a.id} onClick={() => onChange({ accent: a.id })} aria-pressed={active}
                        className={`flex flex-col items-center gap-1.5 p-2.5 rounded-2xl border-2 transition
                                    ${active ? 'border-brand-500 bg-brand-500/5' : 'border-transparent hover:bg-muted'}`}>
                  <span className="w-10 h-10 rounded-full flex items-center justify-center text-white shadow-md"
                        style={{ backgroundImage: `linear-gradient(135deg, ${a.from}, ${a.to})` }}>
                    {active && <Icon name="check" className="w-5 h-5" />}
                  </span>
                  <span className="text-xs font-medium text-ink-soft text-center leading-tight">{a.name}</span>
                </button>
              )
            })}
          </div>
        </Section>

        <Section title="Hình nền">
          <div className="grid grid-cols-4 gap-2">
            {BACKGROUNDS.map(b => {
              const active = theme.bg === b.id
              return (
                <button key={b.id} onClick={() => onChange({ bg: b.id })} aria-pressed={active}
                        className="flex flex-col items-center gap-1.5">
                  <span className={`bgfx-${b.id} block w-full aspect-[3/4] rounded-2xl bg-canvas border-2 transition
                                    ${active ? 'border-brand-500 ring-4 ring-brand-500/15' : 'border-line'}`} />
                  <span className={`text-xs font-medium ${active ? 'text-brand-600 dark:text-brand-300' : 'text-ink-soft'}`}>
                    {b.name}
                  </span>
                </button>
              )
            })}
          </div>
        </Section>

        <p className="text-xs text-ink-faint text-center">Lựa chọn được lưu trên máy này cho lần sau.</p>
      </div>
    </div>
  )
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="mb-5">
      <h3 className="text-xs font-bold uppercase tracking-wider text-ink-faint mb-2">{title}</h3>
      {children}
    </section>
  )
}
