import axios from 'axios'
import { compressImage, type CompressStages } from '../utils/compressImage'

// Rỗng = gọi cùng origin (/api/...) → đi qua Vite proxy sang backend.
// Đặt VITE_API_URL chỉ khi muốn trỏ thẳng tới backend ở host khác.
const API_BASE = import.meta.env.VITE_API_URL || ''

const api = axios.create({
  baseURL: API_BASE,
  timeout: 60_000, // OCR có thể mất vài giây
})

/**
 * Thời gian từng chặng phía máy chủ (ms) — xem StageTimer/ScanController ở backend.
 * Các chặng chạy NỐI TIẾP; chặng cloud = null khi OCR cục bộ đã đủ tin (không gọi OCR.space).
 */
export type ScanTimings = {
  upload?: number               // nhận ảnh upload + đọc vào bộ nhớ
  preprocess?: number           // giải mã + xoay + (resize nếu >1600px) + lọc ảnh
  localOcr?: number             // OCR trên máy chủ (PP-OCRv5) + parse + validate
  ocrDetect?: number            //   ↳ trong localOcr: model dò vùng chữ
  ocrRecognize?: number         //   ↳ trong localOcr: cắt + nhận dạng các dòng
  ocrLines?: number             //   ↳ số dòng chữ tìm thấy (không phải ms)
  localRetry?: number | null    // đọc lại trên ảnh lọc khác — chỉ khi lượt chính thiếu/sai ngày hoặc đài
  cloudPrepare?: number | null  // nén JPEG cho cloud — chỉ khi local không qua validate
  cloudOcr?: number | null      // gọi OCR.space
  merge?: number | null         // gộp kết quả cloud vào local
  total: number                 // tổng thời gian máy chủ xử lý request
}

/** Đường đi của kết quả: local đủ tin, hay phải nhờ cloud (và cloud có trả lời không). */
export type OcrPath =
  | 'local' | 'local-expired' | 'local-review' | 'cloud' | 'cloud-failed' | 'cloud-disabled'

/** Trường của kết quả cuối mà user nên kiểm tra lại (đọc được nhưng chưa chắc chắn). */
export type ReviewField = 'number' | 'date' | 'province'

export type ScanResponse = {
  ticketNumber: string | null
  drawDate: string | null
  province: string | null
  confidence: number
  lowConfidence: boolean
  ticketNumberFromCloud: boolean
  allProvinces: string[] | null
  warning: string | null
  ocrPath?: OcrPath
  /** Mã lý do OCR cục bộ không qua (vd 'low_confidence', 'province_fuzzy') — rỗng khi passed. */
  localValidation?: { passed: boolean; reasons: string[] }
  /** Lượt đọc lại lấp được trường nào (null = không cần đọc lại). localValidation là kết quả SAU khi lấp. */
  localRetry?: {
    mode: string
    strategy?: 'Full' | 'Lines'   // Lines = chỉ cắt dòng nghi ngờ đọc lại, không đủ mới đọc cả ảnh
    croppedLines?: number
    usedFull?: boolean
    filled: Exclude<ReviewField, 'number'>[]
  } | null
  needsReview?: ReviewField[]
  timings?: ScanTimings
  /**
   * Tổng thời gian đo ở trình duyệt: bấm gửi ảnh → nhận kết quả. Lớn hơn timings.total
   * đúng bằng phần mạng (4G/tunnel) — đây mới là con số user thực sự cảm nhận.
   */
  clientMs: number
  /** Nén ảnh ở trình duyệt trước khi gửi (xem compressImage) — để so sánh có/không nén. */
  upload: {
    originalBytes: number
    sentBytes: number
    compressMs: number
    compressed: boolean
    stages?: CompressStages
  }
}

// Thêm ?nocompress vào URL để gửi ảnh gốc — dùng để A/B đo xem nén ở FE có nhanh hơn không.
const compressEnabled = () => !new URLSearchParams(window.location.search).has('nocompress')

// Chuẩn hoá lỗi axios thành thông báo tiếng Việt dễ hiểu
function toFriendlyError(e: unknown): Error {
  if (axios.isAxiosError(e)) {
    if (e.response) {
      // Server có trả lời (4xx/5xx) — lấy message từ body nếu có
      const data = e.response.data as { error?: string; title?: string } | undefined
      return new Error(data?.error || data?.title || `Máy chủ trả lỗi ${e.response.status}`)
    }
    // Không nhận được phản hồi: backend chưa chạy, sai địa chỉ, hoặc timeout
    return new Error(
      `Không kết nối được tới máy chủ${API_BASE ? ` (${API_BASE})` : ''}. ` +
      `Kiểm tra: backend (cổng 5177) đã chạy chưa? Vite proxy /api có hoạt động không?`
    )
  }
  return new Error((e as Error)?.message ?? 'Lỗi không xác định')
}

// Bước 1: upload ảnh → nhận info OCR.
// onUploadProgress: tỉ lệ 0..1 byte đã gửi (1 = gửi xong, máy chủ bắt đầu xử lý) — cho màn chờ.
export async function scanImage(
  blob: Blob,
  onUploadProgress?: (ratio: number) => void,
): Promise<ScanResponse> {
  try {
    const c = compressEnabled()
      ? await compressImage(blob)
      : { blob, originalBytes: blob.size, sentBytes: blob.size, compressMs: 0 }
    const fd = new FormData()
    fd.append('image', c.blob, 'ticket.jpg')
    // performance.now() chứ không Date.now(): monotonic, không nhảy khi máy đồng bộ giờ.
    const startedAt = performance.now()
    // KHÔNG set Content-Type thủ công: để trình duyệt tự thêm boundary cho multipart
    const { data } = await api.post('/api/scan', fd, {
      onUploadProgress: e => { if (e.total) onUploadProgress?.(e.loaded / e.total) },
    })
    return {
      ...(data as Omit<ScanResponse, 'clientMs' | 'upload'>),
      clientMs: Math.round(performance.now() - startedAt),
      upload: {
        originalBytes: c.originalBytes,
        sentBytes: c.sentBytes,
        compressMs: c.compressMs,
        compressed: c.blob !== blob,
        stages: 'stages' in c ? c.stages : undefined,
      },
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
      status: 'Checked' | 'NotDrawnYet' | 'NoData' | 'Expired'
      drawsAt: string | null
      claimDeadline: string | null
      isWinner: boolean
      winnings: { tierName: string; amount: number }[]
      totalPrize: number
      ocrConfidence: number
    }
  } catch (e) {
    throw toFriendlyError(e)
  }
}

// Danh sách (ngày → đài) đang có kết quả trong DB
export async function getAvailableDraws() {
  try {
    const { data } = await api.get('/api/results/available')
    return data as { drawDate: string; provinces: string[] }[]
  } catch (e) {
    throw toFriendlyError(e)
  }
}
