import { useState } from 'react'
import AppHeader, { type View } from './components/AppHeader'
import AvailableData, { type ResultsFocus } from './components/AvailableData'
import LuckyNumbers from './components/LuckyNumbers'
import Home from './pages/Home'

export default function App() {
  const [view, setView] = useState<View>('check')
  const [resultsFocus, setResultsFocus] = useState<ResultsFocus | null>(null)

  // Mọi lối chuyển màn đều qua đây: mở Kết quả từ menu thì xoá focus của vé lần trước.
  const go = (v: View, focus: ResultsFocus | null = null) => {
    setResultsFocus(focus)
    setView(v)
  }

  return (
    <div className="min-h-screen">
      <AppHeader view={view} onChange={v => go(v)} />

      <main className="max-w-md mx-auto p-4">
        {/* Dò vé và 6 số may mắn chỉ ẩn đi chứ không unmount: đang xác nhận vé mà ghé xem kết quả
            đài rồi quay lại thì vẫn còn vé vừa quét (camera thì tắt khi ẩn — xem Home.active). */}
        <div hidden={view !== 'check'}>
          <Home active={view === 'check'} onShowResults={focus => go('results', focus)} />
        </div>
        <div hidden={view !== 'lucky'}>
          <LuckyNumbers />
        </div>
        {/* Kết quả thì mount lại mỗi lần mở để lấy danh sách mới nhất. */}
        {view === 'results' && <AvailableData focus={resultsFocus} onBack={() => go('check')} />}
      </main>
    </div>
  )
}
