import { useState } from 'react'
import CameraCapture from '../components/CameraCapture'
import ImageUpload from '../components/ImageUpload'
import TicketInfoConfirm from '../components/TicketInfoConfirm'
import ResultDisplay from '../components/ResultDisplay'
import AvailableData from '../components/AvailableData'
import { scanImage, checkTicket } from '../api/client'
import { ALL_PROVINCES } from '../data/provinces'

type Stage = 'capture' | 'confirm' | 'result' | 'data'

export default function Home() {
  const [stage, setStage] = useState<Stage>('capture')
  const [scanned, setScanned] = useState<any>(null)
  const [result, setResult] = useState<any>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const handleCapture = async (blob: Blob) => {
    setLoading(true); setError(null)
    try {
      const data = await scanImage(blob)
      setScanned(data)
      setStage('confirm')
    } catch (e: any) {
      setError(e?.message ?? 'Lỗi không xác định')
    } finally {
      setLoading(false)
    }
  }

  const handleConfirm = async (info: { ticketNumber: string; drawDate: string; province: string }) => {
    setLoading(true); setError(null)
    try {
      const res = await checkTicket(info)
      setResult(res)
      setStage('result')
    } catch (e: any) {
      setError(e?.message ?? 'Lỗi không xác định')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="min-h-screen">
      <header className="bg-brand-500 text-white py-4 text-center shadow">
        <h1 className="text-xl font-bold">🎫 Dò Vé Số</h1>
      </header>

      <main className="max-w-md mx-auto p-4">
        {loading && <div className="p-8 text-center">Đang xử lý...</div>}

        {!loading && error && (
          <div className="p-8 text-center text-red-600">
            <div className="mb-2">❌ {error}</div>
            <button onClick={() => { setError(null); setStage('capture') }}
                    className="bg-blue-600 text-white px-4 py-2 rounded">
              Thử lại
            </button>
          </div>
        )}

        {!loading && !error && (
          <>
            {stage === 'capture' && (
              <>
                <CameraCapture onCapture={handleCapture} />
                <div className="my-4 text-center text-gray-500">— hoặc —</div>
                <ImageUpload onSelect={f => handleCapture(f)} />
                <button onClick={() => setStage('data')}
                        className="mt-4 w-full border border-gray-300 text-gray-700 py-2.5 rounded-lg">
                  📅 Dữ liệu đã có
                </button>
              </>
            )}
            {stage === 'confirm' && (
              <TicketInfoConfirm
                scanned={scanned}
                allProvinces={ALL_PROVINCES}
                onConfirm={handleConfirm}
                onRescan={() => setStage('capture')}
              />
            )}
            {stage === 'result' && (
              <ResultDisplay result={result} onRescan={() => setStage('capture')} />
            )}
            {stage === 'data' && (
              <AvailableData onBack={() => setStage('capture')} />
            )}
          </>
        )}
      </main>
    </div>
  )
}
