import { useEffect, useState } from 'react'
import { getAvailableDraws } from '../api/client'
import { provinceName } from '../data/provinces'

const formatDate = (iso: string) => {
  const [y, m, d] = iso.split('-')
  return `${d}/${m}/${y}`
}

export default function AvailableData({ onBack }: { onBack: () => void }) {
  const [data, setData] = useState<{ drawDate: string; provinces: string[] }[] | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    getAvailableDraws()
      .then(setData)
      .catch(e => setError(e?.message ?? 'Lỗi không xác định'))
      .finally(() => setLoading(false))
  }, [])

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h2 className="text-lg font-bold">📅 Dữ liệu đã có</h2>
        <button onClick={onBack} className="text-sm text-blue-600">← Quay lại</button>
      </div>

      {loading && <div className="p-6 text-center text-gray-500">Đang tải...</div>}
      {error && <div className="p-4 text-center text-red-600">❌ {error}</div>}

      {!loading && !error && data?.length === 0 && (
        <div className="bg-gray-50 border border-gray-200 rounded-2xl p-6 text-center text-sm text-gray-600">
          Chưa có dữ liệu. Hãy chạy cào kết quả (POST <code>/api/admin/fetch</code>) hoặc đợi worker tự cào lúc 19h.
        </div>
      )}

      {!loading && !error && data?.map(d => (
        <div key={d.drawDate} className="bg-white rounded-2xl shadow p-4">
          <div className="font-semibold text-brand-600 mb-2">{formatDate(d.drawDate)}</div>
          <div className="flex flex-wrap gap-2">
            {d.provinces.map(code => (
              <span key={code}
                    className="bg-brand-50 text-brand-700 text-sm px-2.5 py-1 rounded-full">
                {provinceName(code)}
              </span>
            ))}
          </div>
        </div>
      ))}
    </div>
  )
}
