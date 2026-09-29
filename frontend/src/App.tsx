import { useCallback, useEffect, useState } from 'react'
import AppHeader from './components/AppHeader'
import BottomNav from './components/BottomNav'
import ThemePicker from './components/ThemePicker'
import DonateDialog from './components/DonateDialog'
import AvailableData, { type ResultsFocus } from './components/AvailableData'
import LuckyNumbers from './components/LuckyNumbers'
import Home from './pages/Home'
import { useTheme } from './theme'
import { viewFromPath, viewPath, type View } from './views'

export default function App() {
  // Mở đúng màn theo URL (link chia sẻ / F5); đường dẫn lạ về Dò vé và sửa luôn URL. Xoá state lịch
  // sử còn sót từ trước khi tải lại (vd đang ở bảng 1 đài) — bước bên trong không nằm trên URL.
  // Chạy trước effect của các con để Home ghi stage đầu tiên lên đúng mục này.
  const [view, setView] = useState<View>(() => {
    const v = viewFromPath(location.pathname)
    history.replaceState({ view: v }, '', viewPath(v) + location.search + location.hash)
    return v
  })
  const [resultsFocus, setResultsFocus] = useState<ResultsFocus | null>(null)
  const [theme, setTheme] = useTheme()
  const [themeOpen, setThemeOpen] = useState(false)
  const closeTheme = useCallback(() => setThemeOpen(false), [])
  const [donateOpen, setDonateOpen] = useState(false)
  const closeDonate = useCallback(() => setDonateOpen(false), [])
  // Dò vé đang quét/dò — đánh dấu tab Dò vé để user ghé tab khác biết là máy vẫn đang chạy.
  const [checking, setChecking] = useState(false)

  // Mọi lối chuyển màn đều qua đây: mở Kết quả từ menu thì xoá focus của vé lần trước.
  // Mỗi lần chuyển = 1 mục lịch sử (kèm focus) để nút Back của trình duyệt/điện thoại quay về
  // màn trước thay vì thoát khỏi app. Bước bên trong Dò vé do Home tự ghi (stage).
  const go = (v: View, focus: ResultsFocus | null = null) => {
    setResultsFocus(focus)
    setView(v)
    window.scrollTo(0, 0)
    history.pushState({ view: v, focus }, '', viewPath(v))
  }

  useEffect(() => {
    const onPop = (e: PopStateEvent) => {
      const st = e.state as { view?: View; focus?: ResultsFocus | null } | null
      const v = st?.view ?? 'check'
      setView(prev => {
        if (prev !== v) window.scrollTo(0, 0)
        return v
      })
      setResultsFocus(st?.focus ?? null)
    }
    window.addEventListener('popstate', onPop)
    return () => window.removeEventListener('popstate', onPop)
  }, [])

  return (
    <div className="min-h-screen">
      <div aria-hidden className={`backdrop bgfx-${theme.bg}`} />
      <AppHeader view={view} busy={checking} onChange={v => go(v)} onOpenTheme={() => setThemeOpen(true)}
                 onOpenDonate={() => setDonateOpen(true)} />

      {/* Điện thoại: 1 cột + chừa chỗ cho BottomNav (và vạch home của iPhone). Màn rộng: khung
          rộng hơn, từng màn tự chia cột. */}
      <main className="w-full max-w-md md:max-w-3xl lg:max-w-5xl mx-auto px-4 pt-4 md:pt-8
                       pb-[calc(6.5rem+env(safe-area-inset-bottom))] md:pb-12">
        {/* Dò vé và 6 số may mắn chỉ ẩn đi chứ không unmount: đang xác nhận vé mà ghé xem kết quả
            đài rồi quay lại thì vẫn còn vé vừa quét (camera thì tắt khi ẩn — xem Home.active).
            fade-up chạy lại mỗi lần hiện ra vì trình duyệt khởi động lại animation khi hết display:none. */}
        <div hidden={view !== 'check'} className="fade-up">
          <Home active={view === 'check'} onShowResults={focus => go('results', focus)} onBusyChange={setChecking} />
        </div>
        <div hidden={view !== 'lucky'} className="fade-up">
          <LuckyNumbers onShowResults={focus => go('results', focus)} />
        </div>
        {/* Kết quả thì mount lại mỗi lần mở để lấy danh sách mới nhất. */}
        {view === 'results' && (
          <div className="fade-up">
            {/* key: Back từ bảng mở theo vé A về bảng theo vé B (focus khác) thì mount lại cho đúng focus */}
            <AvailableData key={resultsFocus ? `${resultsFocus.drawDate}/${resultsFocus.province}/${resultsFocus.ticketNumber}` : 'list'}
                           focus={resultsFocus} onBack={() => history.back()} />
          </div>
        )}
      </main>

      <BottomNav view={view} busy={checking} onChange={v => go(v)} />
      {themeOpen && <ThemePicker theme={theme} onChange={setTheme} onClose={closeTheme} />}
      {donateOpen && <DonateDialog onClose={closeDonate} />}
    </div>
  )
}
