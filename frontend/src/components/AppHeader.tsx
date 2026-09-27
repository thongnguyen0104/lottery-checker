import { useEffect, useState } from 'react'

export type View = 'check' | 'lucky' | 'results'

const VIEWS: { id: View; icon: string; title: string; hint: string }[] = [
  { id: 'check', icon: '🎫', title: 'Dò Vé Số', hint: 'Chụp vé, tự đọc số và dò giải' },
  { id: 'lucky', icon: '🍀', title: '6 Số May Mắn', hint: 'Chọn ngẫu nhiên kiểu Vietlott 6/45, 6/55' },
  { id: 'results', icon: '📅', title: 'Kết Quả Xổ Số', hint: 'Xem bảng kết quả chi tiết từng đài' },
]

/** Tiêu đề = tính năng đang mở; bấm vào (hoặc ▾ bên cạnh) để chọn tính năng khác. */
export default function AppHeader({ view, onChange }: { view: View; onChange: (v: View) => void }) {
  const [open, setOpen] = useState(false)
  const current = VIEWS.find(v => v.id === view)!

  useEffect(() => {
    if (!open) return
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setOpen(false) }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [open])

  const select = (v: View) => {
    setOpen(false)
    onChange(v)
  }

  return (
    <header className="relative z-20 flex justify-center bg-brand-500 text-white shadow">
      <button onClick={() => setOpen(o => !o)} aria-haspopup="menu" aria-expanded={open}
              className="inline-flex items-center gap-2 py-4 px-3 text-xl font-bold">
        {current.icon} {current.title}
        <span aria-hidden
              className={`text-xs opacity-80 transition-transform ${open ? 'rotate-180' : ''}`}>▼</span>
      </button>

      {open && (
        <>
          {/* Lớp phủ trong suốt: chạm ra ngoài menu là đóng (kể cả trên điện thoại). */}
          <div className="fixed inset-0" onClick={() => setOpen(false)} aria-hidden />
          <ul role="menu"
              className="absolute top-full mt-2 w-72 max-w-[calc(100vw-2rem)] bg-white text-gray-800
                         rounded-2xl shadow-lg border border-gray-100 overflow-hidden">
            {VIEWS.map(v => (
              <li key={v.id} role="none">
                <button role="menuitem" onClick={() => select(v.id)}
                        className={`w-full flex items-center gap-3 px-4 py-3 text-left hover:bg-gray-50
                                    ${v.id === view ? 'bg-brand-50' : ''}`}>
                  <span className="text-xl">{v.icon}</span>
                  <span className="flex-1">
                    <span className="block font-semibold">{v.title}</span>
                    <span className="block text-xs text-gray-500">{v.hint}</span>
                  </span>
                  {v.id === view && <span className="text-brand-600 font-bold">✓</span>}
                </button>
              </li>
            ))}
          </ul>
        </>
      )}
    </header>
  )
}
