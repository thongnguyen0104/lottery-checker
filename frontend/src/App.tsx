import { useEffect, useState } from 'react'
import axios from 'axios'

export default function App() {
  const [ping, setPing] = useState<string>('Đang kết nối backend...')

  useEffect(() => {
    axios.get(`${import.meta.env.VITE_API_URL}/api/ping`)
      .then(r => setPing(`✅ Backend OK: ${JSON.stringify(r.data)}`))
      .catch(e => setPing(`❌ Lỗi: ${e.message}`))
  }, [])

  return (
    <div className="min-h-screen flex items-center justify-center p-4">
      <div className="max-w-md w-full bg-white rounded-2xl shadow-xl p-8 text-center">
        <h1 className="text-3xl font-bold text-brand-500 mb-2">🎫 Dò Vé Số</h1>
        <p className="text-gray-500 mb-6">Setup test page</p>
        <div className="text-sm bg-gray-50 p-4 rounded-lg break-all">{ping}</div>
      </div>
    </div>
  )
}