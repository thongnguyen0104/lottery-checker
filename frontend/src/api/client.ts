import axios from 'axios'
import {
  compressImage, DEFAULT_COMPRESS, type CompressOptions, type CompressStages,
} from '../utils/compressImage'

// Rỗng = gọi cùng origin (/api/...) → đi qua Vite proxy sang backend.
// Đặt VITE_API_URL chỉ khi muốn trỏ thẳng tới backend ở host khác.
const API_BASE = import.meta.env.VITE_API_URL || ''

const api = axios.create({
  baseURL: API_BASE,
  timeout: 60_000, // OCR có thể mất vài giây
})

/**
 * Thời gian từng chặng phía máy chủ (ms) — xem StageTimer/ScanController ở backend.
 * Các chặng chạy NỐI TIẾP; chặng cloud = null khi OCR cục bộ đã đủ tin (không gọi cloud),
 * chặng local = null khi OCR cục bộ bị tắt (cloud đọc thẳng).
 */
export type ScanTimings = {
  upload?: number               // nhận ảnh upload + đọc vào bộ nhớ
  preprocess?: number | null    // giải mã + xoay + (resize nếu >1600px) + lọc ảnh — null khi gửi nguyên ảnh cho cloud
  localOcr?: number | null      // OCR trên máy chủ (PP-OCRv5) + parse + validate — null khi OCR cục bộ tắt
  ocrDetect?: number            //   ↳ trong localOcr: model dò vùng chữ
  ocrRecognize?: number         //   ↳ trong localOcr: cắt + nhận dạng các dòng
  ocrLines?: number             //   ↳ số dòng chữ tìm thấy (không phải ms)
  localRetry?: number | null    // đọc lại trên ảnh lọc khác — chỉ khi lượt chính thiếu/sai ngày hoặc đài
  cloudPrepare?: number | null  // chuẩn bị ảnh JPEG cho cloud — chỉ khi gọi cloud
  cloudOcr?: number | null      // gọi cloud (Gemini, lỗi thì OCR.space) — xem cloudProvider
  merge?: number | null         // gộp kết quả cloud vào local
  total: number                 // tổng thời gian máy chủ xử lý request
}

/**
 * Đường đi của kết quả: local đủ tin, hay phải nhờ cloud (và cloud có trả lời không).
 * 'cloud-only*' = OCR cục bộ bị tắt, cloud là đường chính.
 */
export type OcrPath =
  | 'local' | 'local-expired' | 'local-review' | 'cloud' | 'cloud-failed' | 'cloud-disabled'
  | 'cloud-only' | 'cloud-only-failed'

/** Nguồn cloud đã cho kết quả. */
export type CloudProvider = 'gemini' | 'ocrspace'

/**
 * Một lượt gọi một nguồn cloud (backend thử Gemini trước, lỗi thì OCR.space). error null = trả lời
 * được; Gemini: 'timeout' | 'http_429' | 'http_503' | 'http_<mã>' | 'empty' | 'bad_json' | 'network';
 * OCR.space: 'failed'. retried = mã lỗi các lượt Gemini đã tự gọi lại (vd 503 quá tải) — ms gồm cả chúng.
 */
export type CloudAttempt = { provider: CloudProvider; ms: number; error: string | null; retried?: string[] | null }

/** Trường của kết quả cuối mà user nên kiểm tra lại (đọc được nhưng chưa chắc chắn). */
export type ReviewField = 'number' | 'date' | 'province'

export type ScanResponse = {
  ticketNumber: string | null
  drawDate: string | null
  province: string | null
  /** Độ tin cậy của OCR cục bộ (0..1); null khi OCR cục bộ không chạy — cloud không trả điểm này. */
  confidence: number | null
  lowConfidence: boolean
  ticketNumberFromCloud: boolean
  allProvinces: string[] | null
  warning: string | null
  ocrPath?: OcrPath
  cloudProvider?: CloudProvider | null
  /** Từng nguồn cloud đã thử, theo thứ tự — timings.cloudOcr là tổng các lượt. null = không gọi cloud. */
  cloudAttempts?: CloudAttempt[] | null
  /**
   * Mã lý do OCR cục bộ không qua (vd 'low_confidence', 'province_fuzzy') — rỗng khi passed.
   * null khi OCR cục bộ không chạy.
   */
  localValidation?: { passed: boolean; reasons: string[] } | null
  /** Lượt đọc lại lấp được trường nào (null = không cần đọc lại). localValidation là kết quả SAU khi lấp. */
  localRetry?: {
    mode: string
    strategy?: 'Full' | 'Lines'   // Lines = chỉ cắt dòng nghi ngờ đọc lại, không đủ mới đọc cả ảnh
    croppedLines?: number
    usedFull?: boolean
    filled: Exclude<ReviewField, 'number'>[]
  } | null
  needsReview?: ReviewField[]
  /**
   * Máy chủ chắc cả số vé, đài, ngày (xem TicketResultValidator.CanAutoCheck) → dò luôn, bỏ qua form
   * xác nhận. Backend cũ không có trường này → undefined → vẫn hỏi lại như trước.
   */
  autoCheck?: boolean
  rejectedNonTicket?: boolean
  rejectionReason?: string | null
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
    /** Cỡ/chất lượng đã dùng để nén (backend quyết, xem loadCompressOptions). */
    options?: CompressOptions
  }
}

// Thêm ?nocompress vào URL để gửi ảnh gốc — dùng để A/B đo xem nén ở FE có nhanh hơn không.
const compressEnabled = () => !new URLSearchParams(window.location.search).has('nocompress')

let compressOptions: Promise<CompressOptions> | null = null

/**
 * Hỏi backend nên nén ảnh cỡ nào (GET /api/scan/options) — 1600px khi OCR cục bộ đọc, 1280px khi
 * Gemini đọc thẳng. Gọi sớm lúc mở app để lượt quét đầu không phải chờ; kết quả dùng lại cho mọi
 * lượt sau. Lỗi (mạng, backend cũ chưa có endpoint) → mặc định 1600px, an toàn cho cả hai đường,
 * và lần sau hỏi lại. Timeout ngắn: đây chỉ là tối ưu, không được bắt user chờ.
 */
export function loadCompressOptions(): Promise<CompressOptions> {
  compressOptions ??= api.get('/api/scan/options', { timeout: 3_000 })
    .then(({ data }) => {
      const o = data as Partial<CompressOptions>
      const valid = typeof o.maxWidth === 'number' && o.maxWidth >= 320
        && typeof o.quality === 'number' && o.quality > 0 && o.quality <= 1
      return valid ? { maxWidth: o.maxWidth!, quality: o.quality! } : DEFAULT_COMPRESS
    })
    .catch(() => {
      compressOptions = null
      return DEFAULT_COMPRESS
    })
  return compressOptions
}

// Chuẩn hoá lỗi axios thành thông báo tiếng Việt dễ hiểu
function toFriendlyError(e: unknown): Error {
  if (axios.isAxiosError(e)) {
    if (e.response) {
      if (e.response.status === 429) {
        return new Error('Bạn thao tác quá nhanh. Vui lòng đợi một chút rồi thử lại.')
      }
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
    const options = compressEnabled() ? await loadCompressOptions() : undefined
    const c = options
      ? await compressImage(blob, options)
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
        options,
      },
    }
  } catch (e) {
    throw toFriendlyError(e)
  }
}

/** Thông tin vé đem đi dò: user xác nhận trên form, hoặc lấy thẳng kết quả quét khi autoCheck. */
export type TicketQuery = { ticketNumber: string; drawDate: string; province: string }

export type CheckResult = {
  extractedNumber: string
  drawDate: string | null
  province: string | null
  status: 'Checked' | 'NotDrawnYet' | 'NoData' | 'Expired'
  drawsAt: string | null
  claimDeadline: string | null
  isWinner: boolean
  isSpecialPrizeWinner?: boolean
  winnings: { tierName: string; amount: number }[]
  totalPrize: number
  ocrConfidence: number
}

// Bước 2: dò với info đã xác nhận
export async function checkTicket(payload: TicketQuery) {
  try {
    const { data } = await api.post('/api/check', payload)
    return data as CheckResult
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

/** Bảng kết quả 1 đài 1 ngày. tier: 'DB' | '1'..'8', xếp ĐB → 8; numbers giữ thứ tự trên trang nguồn. */
export type ProvinceResult = {
  drawDate: string
  province: string
  region: string
  prizes: { tier: string; numbers: string[] }[]
}

export async function getProvinceResult(drawDate: string, province: string) {
  try {
    const { data } = await api.get(
      `/api/results/${encodeURIComponent(drawDate)}/${encodeURIComponent(province)}`)
    return data as ProvinceResult
  } catch (e) {
    throw toFriendlyError(e)
  }
}
