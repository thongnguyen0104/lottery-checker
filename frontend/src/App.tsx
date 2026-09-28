import { useCallback, useState } from 'react'
import AppHeader from './components/AppHeader'
import BottomNav from './components/BottomNav'
import ThemePicker from './components/ThemePicker'
import AvailableData, { type ResultsFocus } from './components/AvailableData'
import LuckyNumbers from './components/LuckyNumbers'
import Home from './pages/Home'
import { useTheme } from './theme'
import type { View } from './views'

export default function App() {
  const [view, setView] = useState<View>('check')
  const [resultsFocus, setResultsFocus] = useState<ResultsFocus | null>(null)
  const [theme, setTheme] = useTheme()
  const [themeOpen, setThemeOpen] = useState(false)
  const closeTheme = useCallback(() => setThemeOpen(false), [])

  // Mọi lối chuyển màn đều qua đây: mở Kết quả từ menu thì xoá focus của vé lần trước.
  const go = (v: View, focus: ResultsFocus | null = null) => {
    setResultsFocus(focus)
    setView(v)
    window.scrollTo(0, 0)
  }

  return (
    <div className="min-h-screen">
      <div aria-hidden className={`backdrop bgfx-${theme.bg}`} />
      <AppHeader view={view} onChange={v => go(v)} onOpenTheme={() => setThemeOpen(true)} />

      {/* Điện thoại: 1 cột + chừa chỗ cho BottomNav (và vạch home của iPhone). Màn rộng: khung
          rộng hơn, từng màn tự chia cột. */}
      <main className="w-full max-w-md md:max-w-3xl lg:max-w-5xl mx-auto px-4 pt-4 md:pt-8
                       pb-[calc(6.5rem+env(safe-area-inset-bottom))] md:pb-12">
        {/* Dò vé và 6 số may mắn chỉ ẩn đi chứ không unmount: đang xác nhận vé mà ghé xem kết quả
            đài rồi quay lại thì vẫn còn vé vừa quét (camera thì tắt khi ẩn — xem Home.active).
            fade-up chạy lại mỗi lần hiện ra vì trình duyệt khởi động lại animation khi hết display:none. */}
        <div hidden={view !== 'check'} className="fade-up">
          <Home active={view === 'check'} onShowResults={focus => go('results', focus)} />
        </div>
        <div hidden={view !== 'lucky'} className="fade-up">
          <LuckyNumbers onShowResults={focus => go('results', focus)} />
        </div>
        {/* Kết quả thì mount lại mỗi lần mở để lấy danh sách mới nhất. */}
        {view === 'results' && (
          <div className="fade-up">
            <AvailableData focus={resultsFocus} onBack={() => go(resultsFocus?.from ?? 'check')} />
          </div>
        )}
      </main>

      <BottomNav view={view} onChange={v => go(v)} />
      {themeOpen && <ThemePicker theme={theme} onChange={setTheme} onClose={closeTheme} />}
    </div>
  )
}
