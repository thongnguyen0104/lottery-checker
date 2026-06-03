import axios from 'axios'

const api = axios.create({
  baseURL: import.meta.env.VITE_API_URL,
  timeout: 60_000, // OCR có thể mất vài giây
})

// Chuẩn hoá lỗi axios thành thông báo tiếng Việt dễ hiểu
function toFriendlyError(e: unknown): Error {
  if (axios.isAxiosError(e)) {
    if (e.response) {
      // Server có trả lời (4xx/5xx) — lấy message từ body nếu có
      const data = e.response.data as { error?: string; title?: string } | undefined
      return new Error(data?.error || data?.title || `Máy chủ trả lỗi ${e.response.status}`)
    }
    // Không nhận được phản hồi: backend chưa chạy, sai địa chỉ, CORS, hoặc timeout
    return new Error(
      `Không kết nối được tới máy chủ (${import.meta.env.VITE_API_URL}). ` +
      `Kiểm tra: backend đã chạy chưa? Đúng địa chỉ chưa? ` +
      `(Nếu test trên điện thoại, "localhost" trỏ về chính điện thoại — đặt VITE_API_URL = IP LAN của máy tính.)`
    )
  }
  return new Error((e as Error)?.message ?? 'Lỗi không xác định')
}

// Bước 1: upload ảnh → nhận info OCR
export async function scanImage(blob: Blob) {
  try {
    const fd = new FormData()
    fd.append('image', blob, 'ticket.jpg')
    // KHÔNG set Content-Type thủ công: để trình duyệt tự thêm boundary cho multipart
    const { data } = await api.post('/api/scan', fd)
    return data as {
      ticketNumber: string | null
      drawDate: string | null
      province: string | null
      confidence: number
      lowConfidence: boolean
      allProvinces: string[] | null
      warning: string | null
    }
  } catch (e) {
    throw toFriendlyError(e)
  }
}

// Bước 2: dò với info đã xác nhận
export async function checkTicket(payload: {
  ticketNumber: string
  drawDate: string
  province: string
}) {
  try {
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
  } catch (e) {
    throw toFriendlyError(e)
  }
}
