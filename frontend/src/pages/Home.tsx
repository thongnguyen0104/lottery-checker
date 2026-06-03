import { useState } from 'react'
import CameraCapture from '../components/CameraCapture'
import ImageUpload from '../components/ImageUpload'
import TicketInfoConfirm from '../components/TicketInfoConfirm'
import ResultDisplay from '../components/ResultDisplay'
import { scanImage, checkTicket } from '../api/client'

// Danh sách đài đầy đủ — hard-code (frontend cache, không gọi API mỗi lần).
// `code` PHẢI khớp đúng code mà backend ProvinceMatcher sinh ra.
const ALL_PROVINCES = [
  // Miền Nam
  { code: 'TPHCM', name: 'TP.HCM' },
  { code: 'DongThap', name: 'Đồng Tháp' },
  { code: 'CaMau', name: 'Cà Mau' },
  { code: 'BenTre', name: 'Bến Tre' },
  { code: 'VungTau', name: 'Vũng Tàu' },
  { code: 'BacLieu', name: 'Bạc Liêu' },
  { code: 'DongNai', name: 'Đồng Nai' },
  { code: 'CanTho', name: 'Cần Thơ' },
  { code: 'SocTrang', name: 'Sóc Trăng' },
  { code: 'TayNinh', name: 'Tây Ninh' },
  { code: 'AnGiang', name: 'An Giang' },
  { code: 'BinhThuan', name: 'Bình Thuận' },
  { code: 'VinhLong', name: 'Vĩnh Long' },
  { code: 'BinhDuong', name: 'Bình Dương' },
  { code: 'TraVinh', name: 'Trà Vinh' },
  { code: 'LongAn', name: 'Long An' },
  { code: 'HauGiang', name: 'Hậu Giang' },
  { code: 'KienGiang', name: 'Kiên Giang' },
  { code: 'TienGiang', name: 'Tiền Giang' },
  { code: 'DaLat', name: 'Đà Lạt' },
  { code: 'LamDong', name: 'Lâm Đồng' },
  // Miền Trung
  { code: 'PhuYen', name: 'Phú Yên' },
  { code: 'Hue', name: 'Huế' },
  { code: 'DakLak', name: 'Đắk Lắk' },
  { code: 'QuangNam', name: 'Quảng Nam' },
  { code: 'KhanhHoa', name: 'Khánh Hòa' },
  { code: 'DaNang', name: 'Đà Nẵng' },
  { code: 'BinhDinh', name: 'Bình Định' },
  { code: 'QuangTri', name: 'Quảng Trị' },
  { code: 'QuangBinh', name: 'Quảng Bình' },
  { code: 'GiaLai', name: 'Gia Lai' },
  { code: 'NinhThuan', name: 'Ninh Thuận' },
  { code: 'KonTum', name: 'Kon Tum' },
  { code: 'QuangNgai', name: 'Quảng Ngãi' },
  // Miền Bắc (cơ cấu giải khác — hiện chưa hỗ trợ dò, xem §11)
  { code: 'MB', name: 'Miền Bắc' },
]

type Stage = 'capture' | 'confirm' | 'result'

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
          </>
        )}
      </main>
    </div>
  )
}
