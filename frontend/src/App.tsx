import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import AppHeader from './components/AppHeader'
import BottomNav from './components/BottomNav'
import ThemePicker from './components/ThemePicker'
import DonateDialog from './components/DonateDialog'
import AuthDialog from './components/AuthDialog'
import Icon from './components/Icon'
import { getMe, logout, type Account } from './api/client'
import AvailableData, { type ResultsFocus } from './components/AvailableData'
import LuckyNumbers from './components/LuckyNumbers'
import Predictions from './components/Predictions'
import Blog from './components/Blog'
import Home from './pages/Home'
import { useTheme } from './theme'
import { viewFromPath, viewPath, type View } from './views'

export default function App() {
  const { t } = useTranslation()
  // Mở đúng màn theo URL (link chia sẻ / F5); đường dẫn lạ về Dò vé và sửa luôn URL. Xoá state lịch
  // sử còn sót từ trước khi tải lại (vd đang ở bảng 1 đài) — bước bên trong không nằm trên URL.
  // Chạy trước effect của các con để Home ghi stage đầu tiên lên đúng mục này.
  const [view, setView] = useState<View>(() => {
    const v = viewFromPath(location.pathname)
    history.replaceState({ view: v }, '', viewPath(v) + location.search + location.hash)
    return v
  })
  const [resultsFocus, setResultsFocus] = useState<ResultsFocus | null>(null)
  // Các màn đã mở ít nhất 1 lần — màn nặng (Dự đoán) chỉ mount khi cần rồi giữ lại.
  const [seen, setSeen] = useState<ReadonlySet<View>>(() => new Set([view]))
  if (!seen.has(view)) setSeen(new Set(seen).add(view))
  const [theme, setTheme] = useTheme()
  const [themeOpen, setThemeOpen] = useState(false)
  const closeTheme = useCallback(() => setThemeOpen(false), [])
  const [donateOpen, setDonateOpen] = useState(false)
  const closeDonate = useCallback(() => setDonateOpen(false), [])
  // Dò vé đang quét/dò — đánh dấu tab Dò vé để user ghé tab khác biết là máy vẫn đang chạy.
  const [checking, setChecking] = useState(false)
  // Tài khoản: undefined = đang hỏi máy chủ, null = khách. Khách chỉ xem Kết quả + dò thử 1 lần.
  const [account, setAccount] = useState<Account | null | undefined>(undefined)
  // Form đăng nhập: undefined = đóng, chuỗi = lý do mở (hiện ở đầu form).
  const [authReason, setAuthReason] = useState<string | null | undefined>(undefined)
  const closeAuth = useCallback(() => setAuthReason(undefined), [])
  useEffect(() => { getMe().then(setAccount) }, [])
  // Đăng xuất: về màn mặc định (Dò vé) và dựng lại Home/LuckyNumbers (key theo tài khoản) để
  // không còn sót vé/kết quả/bộ số của phiên vừa rồi.
  const onLogout = async () => {
    await logout().catch(() => {})
    setAccount(null)
    go('check')
  }
  const sessionKey = account?.username ?? 'guest'

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
                 onOpenDonate={() => setDonateOpen(true)}
                 account={account ?? null} onOpenAuth={() => setAuthReason(null)} onLogout={onLogout} />

      {/* Điện thoại: 1 cột + chừa chỗ cho BottomNav (và vạch home của iPhone). Màn rộng: khung
          rộng hơn, từng màn tự chia cột. */}
      <main className="w-full max-w-md md:max-w-3xl lg:max-w-5xl mx-auto px-4 pt-4 md:pt-8
                       pb-[calc(6.5rem+env(safe-area-inset-bottom))] md:pb-12">
        {/* Dò vé và 6 số may mắn chỉ ẩn đi chứ không unmount: đang xác nhận vé mà ghé xem kết quả
            đài rồi quay lại thì vẫn còn vé vừa quét (camera thì tắt khi ẩn — xem Home.active).
            fade-up chạy lại mỗi lần hiện ra vì trình duyệt khởi động lại animation khi hết display:none. */}
        <div hidden={view !== 'check'} className="fade-up">
          <Home key={sessionKey} active={view === 'check'} onShowResults={focus => go('results', focus)} onBusyChange={setChecking}
                onRequireLogin={() => setAuthReason(t('auth.trialUsedUp'))} />
        </div>
        <div hidden={view !== 'lucky'} className="fade-up">
          {account ? <LuckyNumbers key={sessionKey} onShowResults={focus => go('results', focus)} /> : (
            <div className="card p-6 text-center space-y-3">
              <Icon name="user" className="w-10 h-10 mx-auto text-brand-700 dark:text-brand-400" />
              <p className="font-semibold">{t('auth.luckyTitle')}</p>
              <p className="text-sm text-ink-soft">{t('auth.luckyBody')}</p>
              <button onClick={() => setAuthReason(null)}
                      className="px-5 py-2.5 rounded-xl font-semibold bg-gradient-to-r from-primary to-primary-end
                                 text-on-primary shadow-md shadow-primary/25 active:scale-95 transition">
                {t('auth.loginTitle')}
              </button>
            </div>
          )}
        </div>
        {/* Dự đoán: mount lần đầu mở tới rồi giữ lại (ẩn) — quay lại vẫn đúng ngày đang xem. */}
        {seen.has('predict') && (
          <div hidden={view !== 'predict'} className="fade-up">
            <Predictions />
          </div>
        )}
        {/* Blog: key theo tài khoản — đăng nhập/xuất thì tải lại (lượt like, bài "của mình" đổi theo). */}
        {seen.has('blog') && (
          <div hidden={view !== 'blog'} className="fade-up">
            <Blog key={sessionKey} account={account ?? null} onRequireLogin={() => setAuthReason(null)} />
          </div>
        )}
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
      {authReason !== undefined && (
        <AuthDialog reason={authReason ?? undefined} onClose={closeAuth}
                    onDone={a => { setAccount(a); closeAuth() }} />
      )}
    </div>
  )
}
