import axios from 'axios'

const api = axios.create({ baseURL: import.meta.env.VITE_API_URL })

// Bước 1: upload ảnh → nhận info OCR
export async function scanImage(blob: Blob) {
  const fd = new FormData()
  fd.append('image', blob, 'ticket.jpg')
  const { data } = await api.post('/api/scan', fd,
    { headers: { 'Content-Type': 'multipart/form-data' } })
  return data as {
    ticketNumber: string | null
    drawDate: string | null
    province: string | null
    confidence: number
    lowConfidence: boolean
    allProvinces: string[] | null
    warning: string | null
  }
}

// Bước 2: dò với info đã xác nhận
export async function checkTicket(payload: {
  ticketNumber: string
  drawDate: string
  province: string
}) {
  const { data } = await api.post('/api/check', payload)
  return data as {
    extractedNumber: string
    drawDate: string | null
    province: string | null
    isWinner: boolean
    winnings: { tierName: string; amount: number }[]
    totalPrize: number
    ocrConfidence: number
  }
}
