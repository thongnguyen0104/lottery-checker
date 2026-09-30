import axios from 'axios'
import i18n, { currentLang } from '../i18n'
import {
  compressImage, DEFAULT_COMPRESS, type CompressOptions, type CompressStages,
} from '../utils/compressImage'

// Rỗng = gọi cùng origin (/api/...) → đi qua Vite proxy sang backend.
// Đặt VITE_API_URL chỉ khi muốn trỏ thẳng tới backend ở host khác.
const API_BASE = import.meta.env.VITE_API_URL || ''

const api = axios.create({
  baseURL: API_BASE,
  timeout: 15_000, // tra DB/kết quả chỉ vài chục ms — quá 15s là mạng có vấn đề; quét ảnh đặt riêng
})

// Báo ngôn ngữ đang chọn để máy chủ trả lời nhắn lỗi (và luận giấc mơ) đúng tiếng.
api.interceptors.request.use(cfg => { cfg.headers.set('Accept-Language', currentLang()); return cfg })

// Mạng tới VM hay rớt gói lúc bắt tay TLS (đo được 1–4s, có lúc treo tới hết timeout) trong khi máy
// chủ xử lý chỉ vài trăm ms → GET quá hạn / mất kết nối thì tự gửi lại 1 lần, thường lần 2 qua ngay.
// Chỉ GET (đọc, gửi lại vô hại); POST/DELETE không lặp để khỏi đăng bài / trừ tiền 2 lần.
api.interceptors.response.use(undefined, async e => {
  const cfg = axios.isAxiosError(e) ? e.config as (typeof e.config & { _retried?: boolean }) | undefined : undefined
  const retriable = axios.isAxiosError(e) && !e.response && !axios.isCancel(e)
    && ['ECONNABORTED', 'ETIMEDOUT', 'ERR_NETWORK'].includes(e.code ?? '')
  if (!cfg || !retriable || cfg.method !== 'get' || cfg._retried || cfg.signal?.aborted) throw e
  cfg._retried = true
  return api.request(cfg)
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
 * được; Gemini: 'timeout' | 'rate_limited' | 'http_429' | 'http_503' | 'http_<mã>' | 'empty' | 'bad_json' | 'network';
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

/** User bấm Huỷ (AbortController) — bên gọi lặng lẽ quay lại, không hiện màn lỗi. */
/** Khách hết lượt dò thử (máy chủ trả 401 login_required) → mở form đăng nhập. */
export const isLoginRequired = (e: unknown) => e instanceof Error && e.name === 'LoginRequiredError'

export const isCanceled = (e: unknown) => e instanceof Error && e.name === 'CanceledError'

// Câu cho người dùng khi máy chủ không gửi kèm lời nhắn (error) — không lộ mã/thuật ngữ kỹ thuật.
const statusText = (status: number) =>
  status === 429 ? i18n.t('errors.tooMany')
  : status === 413 ? i18n.t('errors.tooLarge')
  : status >= 500 ? i18n.t('errors.server')
  : i18n.t('errors.badRequest')

// Chuẩn hoá lỗi axios thành thông báo dễ hiểu theo ngôn ngữ đang chọn
function toFriendlyError(e: unknown): Error {
  if (axios.isAxiosError(e)) {
    if (axios.isCancel(e)) return Object.assign(new Error(i18n.t('errors.canceled')), { name: 'CanceledError' })
    if (e.response) {
      // Server có trả lời (4xx/5xx) — lấy lời nhắn từ body nếu có. 5xx thì bỏ qua body: trang lỗi
      // mặc định (title "Internal Server Error"...) là tiếng Anh kỹ thuật.
      const { status } = e.response
      const data = e.response.data as { error?: string; code?: string } | undefined
      if (data?.code === 'login_required')
        return Object.assign(new Error(data.error ?? i18n.t('auth.loginRequired')), { name: 'LoginRequiredError' })
      return new Error((status < 500 || status === 503) && data?.error ? data.error : statusText(status))
    }
    if (e.code === 'ECONNABORTED' || e.code === 'ETIMEDOUT')
      return new Error(i18n.t('errors.timeout'))
    // Chi tiết cho dev xem ở console, user chỉ cần biết kiểm tra mạng.
    console.warn('API không phản hồi', API_BASE || '(cùng origin)', e.message)
    return new Error(navigator.onLine === false
      ? i18n.t('errors.offline')
      : i18n.t('errors.network'))
  }
  return new Error((e as Error)?.message ?? i18n.t('errors.unknown'))
}

// Nén (nếu bật) rồi gửi ảnh lên `url` — dùng chung cho quét 1 vé và quét nhiều vé.
async function uploadImage<T>(url: string, blob: Blob, onUploadProgress?: (ratio: number) => void,
                              timeout?: number, signal?: AbortSignal) {
  const options = compressEnabled() ? await loadCompressOptions() : undefined
  const c = options
    ? await compressImage(blob, options)
    : { blob, originalBytes: blob.size, sentBytes: blob.size, compressMs: 0 }
  const fd = new FormData()
  fd.append('image', c.blob, 'ticket.jpg')
  // performance.now() chứ không Date.now(): monotonic, không nhảy khi máy đồng bộ giờ.
  const startedAt = performance.now()
  // KHÔNG set Content-Type thủ công: để trình duyệt tự thêm boundary cho multipart
  const { data } = await api.post(url, fd, {
    timeout,
    signal,
    onUploadProgress: e => { if (e.total) onUploadProgress?.(e.loaded / e.total) },
  })
  return {
    ...(data as T),
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
}

// Bước 1: upload ảnh → nhận info OCR.
// onUploadProgress: tỉ lệ 0..1 byte đã gửi (1 = gửi xong, máy chủ bắt đầu xử lý) — cho màn chờ.
export async function scanImage(
  blob: Blob,
  onUploadProgress?: (ratio: number) => void,
  signal?: AbortSignal,
): Promise<ScanResponse> {
  try {
    // Gemini tự timeout 15s (+1 lượt gọi lại) rồi lùi về OCR.space → 45s đủ cho đường chậm nhất.
    return await uploadImage<Omit<ScanResponse, 'clientMs' | 'upload'>>(
      '/api/scan', blob, onUploadProgress, 45_000, signal)
  } catch (e) {
    throw toFriendlyError(e)
  }
}

/**
 * Một vé trong ảnh nhiều vé. result = kết quả dò luôn (máy chủ chắc cả số, đài, ngày); null = còn
 * nghi ngờ trường nào đó (needsReview) → user sửa trên form rồi dò bằng checkTicket.
 */
export type MultiTicket = {
  ticketNumber: string | null
  drawDate: string | null
  province: string | null
  needsReview: ReviewField[]
  result: CheckResult | null
}

export type MultiScanResponse = {
  /** Theo thứ tự trên ảnh (trên xuống, trái sang). Rỗng = AI không thấy vé nào. */
  tickets: MultiTicket[]
  cloudAttempts?: CloudAttempt[] | null
  timings?: Record<string, number | null>
  clientMs: number
}

/** Ảnh chụp nhiều vé: AI đọc tất cả, máy chủ dò luôn vé nào đọc chắc (POST /api/scan-multi). */
export async function scanMultiImage(
  blob: Blob,
  onUploadProgress?: (ratio: number) => void,
  signal?: AbortSignal,
): Promise<MultiScanResponse> {
  try {
    // Nhiều vé = Gemini sinh nhiều chữ hơn + dò từng vé → cho thêm thời gian so với quét 1 vé.
    return await uploadImage<Omit<MultiScanResponse, 'clientMs'>>('/api/scan-multi', blob, onUploadProgress, 60_000, signal)
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
  winnings: { tierName: string; amount: number }[]
  totalPrize: number
  ocrConfidence: number
}

// Bước 2: dò với info đã xác nhận
export async function checkTicket(payload: TicketQuery, signal?: AbortSignal) {
  try {
    const { data } = await api.post('/api/check', payload, { signal })
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

/** Kết quả Luận số — số luôn tra từ sổ mơ ở backend; mainNumber null = không mục nào khớp. */
export type DreamResult = {
  summary: string
  entries: { key: string; label: string; numbers: string[] }[]
  mainNumber: string | null
  secondaryNumbers: string[]
  explanation: string
  disclaimer: string
  /** 'ai' = Gemini hiểu câu; 'local' = so khớp chữ (Gemini tắt/lỗi). */
  source: 'ai' | 'local'
  aiError: string | null
}

export async function interpretDream(message: string) {
  try {
    const { data } = await api.post('/api/ai/dream', { message }, { timeout: 20_000 })
    return data as DreamResult
  } catch (e) {
    throw toFriendlyError(e)
  }
}

/** Một giải có 2 số cuối trùng số đang dò. */
export type TailHit = { drawDate: string; province: string; tier: string; number: string }

export async function searchTail(tail: string) {
  try {
    const { data } = await api.get('/api/results/search', { params: { tail } })
    return data as TailHit[]
  } catch (e) {
    throw toFriendlyError(e)
  }
}

// ── Tài khoản: phiên nằm trong cookie HttpOnly do máy chủ đặt (cùng origin qua proxy /api) ──
export type Account = {
  username: string
  /** Vào được trang quản trị. */
  isAdmin?: boolean
  /** Phải đổi mật khẩu ngay (tài khoản tạo sẵn / admin vừa đặt lại) — app mở form đổi mật khẩu, không cho đóng. */
  mustChangePassword?: boolean
}

export async function changePassword(currentPassword: string, newPassword: string) {
  try {
    const { data } = await api.post('/api/auth/change-password', { currentPassword, newPassword })
    return data as Account
  } catch (e) {
    throw toFriendlyError(e)
  }
}

// ── Cờ tính năng: tắt = user thường không thấy (máy chủ cũng chặn API); admin luôn xem trước được ──
export type FeatureKey = 'checkHistory' | 'scratchTickets'

/** available = người đang xem dùng được; enabled = đã bật cho mọi người (admin xem trước khi available && !enabled). */
export type Features = { available: Record<FeatureKey, boolean>; enabled: Record<FeatureKey, boolean> }

const NO_FEATURES: Record<FeatureKey, boolean> = { checkHistory: false, scratchTickets: false }
export const FEATURES_OFF: Features = { available: NO_FEATURES, enabled: NO_FEATURES }

/** Lỗi mạng → coi như tắt hết: tính năng mới thà ẩn nhầm còn hơn hiện ra rồi gọi API lỗi. */
export async function getFeatures() {
  try {
    const { data } = await api.get('/api/features', { timeout: 5_000 })
    return data as Features
  } catch {
    return FEATURES_OFF
  }
}

export type AdminFeature = {
  key: FeatureKey
  name: string
  description: string
  enabled: boolean
  updatedAt: string | null
  updatedBy: string | null
}

// ── Trang quản trị (chỉ admin) ──
export type AdminOverview = {
  users: number
  admins: number
  totalBalance: number
  ticketsSold: number
  ticketsWon: number
  totalPaidOut: number
  totalTopUp: number
}

export type AdminUserRow = { id: number; username: string; createdAt: string; balance: number; isAdmin: boolean; tickets: number }

export type AdminUserDetail = {
  id: number
  username: string
  createdAt: string
  balance: number
  isAdmin: boolean
  mustChangePassword: boolean
  tickets: { bought: number; won: number; pending: number; spent: number; winnings: number }
  checks: number
}

export type WalletTransaction = {
  id: number
  kind: 'TopUp' | 'Purchase' | 'Win' | 'Refund' | 'Adjust'
  amount: number
  balanceAfter: number
  note: string | null
  createdAt: string
}

async function adminCall<T>(fn: () => Promise<{ data: unknown }>) {
  try {
    return (await fn()).data as T
  } catch (e) {
    throw toFriendlyError(e)
  }
}

export const getAdminFeatures = () => adminCall<AdminFeature[]>(() => api.get('/api/admin/features'))

export const setAdminFeature = (key: FeatureKey, enabled: boolean) =>
  adminCall<AdminFeature[]>(() => api.post(`/api/admin/features/${key}`, { enabled }))

export const getAdminOverview = () => adminCall<AdminOverview>(() => api.get('/api/admin/overview'))

export const getAdminUsers = (search: string, sort: 'new' | 'balance', page: number) =>
  adminCall<{ items: AdminUserRow[]; hasMore: boolean; total: number }>(
    () => api.get('/api/admin/users', { params: { search: search || undefined, sort, page } }))

export const getAdminUser = (id: number) => adminCall<AdminUserDetail>(() => api.get(`/api/admin/users/${id}`))

export const getAdminTransactions = (id: number, page: number) =>
  adminCall<{ items: WalletTransaction[]; hasMore: boolean }>(
    () => api.get(`/api/admin/users/${id}/transactions`, { params: { page } }))

/** amount > 0 cộng, < 0 trừ. */
export const adjustBalance = (id: number, amount: number, note: string) =>
  adminCall<{ balance: number }>(() => api.post(`/api/admin/users/${id}/balance`, { amount, note }))

export const resetUserPassword = (id: number, password: string) =>
  adminCall<void>(() => api.post(`/api/admin/users/${id}/password`, { password }))

export const setUserAdmin = (id: number, isAdmin: boolean) =>
  adminCall<void>(() => api.post(`/api/admin/users/${id}/role`, { isAdmin }))

/** Phiên hiện tại; null = chưa đăng nhập (hoặc không gọi được máy chủ). */
export async function getMe() {
  try {
    const { data } = await api.get('/api/auth/me', { timeout: 5_000 })
    return data as Account
  } catch {
    return null
  }
}

export async function checkUsername(username: string, signal?: AbortSignal) {
  try {
    const { data } = await api.get('/api/auth/check-username', { params: { username }, signal })
    return data as { available: boolean; error: string | null }
  } catch (e) {
    throw toFriendlyError(e)
  }
}

export async function login(username: string, password: string) {
  try {
    const { data } = await api.post('/api/auth/login', { username, password })
    return data as Account
  } catch (e) {
    throw toFriendlyError(e)
  }
}

export async function register(username: string, password: string) {
  try {
    const { data } = await api.post('/api/auth/register', { username, password })
    return data as Account
  } catch (e) {
    throw toFriendlyError(e)
  }
}

export async function logout() {
  try {
    await api.post('/api/auth/logout')
  } catch (e) {
    throw toFriendlyError(e)
  }
}

// ── Trang Tài khoản (cần đăng nhập) ──
export type Profile = {
  username: string
  createdAt: string
  /** Số dư ví (VNĐ) — hiện chỉ hiển thị, chưa có nạp/rút. */
  balance: number
  /** Tổng lượt dò; winners/totalPrize tính mỗi vé trúng 1 lần dù dò lại nhiều lần. */
  checks: { checks: number; winners: number; totalPrize: number }
}

/** Một lượt dò trong lịch sử — drawDate/province null khi lượt đó thiếu thông tin. */
export type CheckHistoryItem = {
  id: number
  ticketNumber: string
  drawDate: string | null
  province: string | null
  status: CheckResult['status']
  isWinner: boolean
  prize: number
  checkedAt: string
}

export async function getProfile() {
  try {
    const { data } = await api.get('/api/profile')
    return data as Profile
  } catch (e) {
    throw toFriendlyError(e)
  }
}

export async function getCheckHistory(page: number) {
  try {
    const { data } = await api.get('/api/profile/history', { params: { page } })
    return data as { items: CheckHistoryItem[]; hasMore: boolean }
  } catch (e) {
    throw toFriendlyError(e)
  }
}

// ── Vé cào 2 số (trúng khi trùng giải tám của đài đã chọn) ──
export type TicketShop = {
  drawDate: string
  /** Giờ VN (không kèm múi giờ) — hiện bằng slice(11, 16). */
  closesAt: string
  drawsAt: string
  provinces: string[]
  price: number
  prize: number
  payoutMultiplier: number
  maxQuantity: number
  balance: number
}

export type ScratchTicket = {
  id: number
  drawDate: string
  province: string
  number: string
  price: number
  status: 'Pending' | 'Won' | 'Lost' | 'Refunded'
  /** Giải tám của đài — có khi đã chốt Won/Lost. */
  winningNumber: string | null
  prize: number
  /** Đã cào xem chưa — chưa thì phủ lớp cào lên kết quả. */
  scratched: boolean
  /** Giờ VN. */
  drawsAt: string
  purchasedAt: string
}

export async function getTicketShop() {
  try {
    const { data } = await api.get('/api/tickets/shop')
    return data as TicketShop
  } catch (e) {
    throw toFriendlyError(e)
  }
}

export async function getMyTickets(page: number) {
  try {
    const { data } = await api.get('/api/tickets', { params: { page } })
    return data as { items: ScratchTicket[]; hasMore: boolean }
  } catch (e) {
    throw toFriendlyError(e)
  }
}

export async function buyTickets(body: { province: string; drawDate: string; quantity: number }) {
  try {
    const { data } = await api.post('/api/tickets', body)
    return data as { tickets: ScratchTicket[]; balance: number }
  } catch (e) {
    throw toFriendlyError(e)
  }
}

export async function scratchTicket(id: number) {
  try {
    const { data } = await api.post(`/api/tickets/${id}/scratch`)
    return data as ScratchTicket
  } catch (e) {
    throw toFriendlyError(e)
  }
}

// ── Dự đoán: thống kê 2 số cuối trong 1 năm, gợi ý số cho từng đài của một ngày ──
export type NumberStat = {
  number: string
  hits: number
  /** Số kỳ có số này (xuất hiện ≥ 1 lần trong 18 giải). */
  draws: number
  probability: number
  recentProbability: number
  /** Đã bao nhiêu kỳ liền chưa về. */
  gap: number
  score: number
}

export type ProvincePrediction = {
  province: string
  draws: number
  from: string | null
  to: string | null
  top: NumberStat[]
  overdue: NumberStat[]
  special: string | null
  all: NumberStat[]
  /** 2 số cuối của 18 giải nếu ngày đó đã có kết quả — để đối chiếu gợi ý. */
  actual: string[] | null
}

export type Prediction = { date: string; baseline: number; provinces: ProvincePrediction[] }

/** date bỏ trống = kỳ xổ kế tiếp (máy chủ tự chọn hôm nay/ngày mai). */
export async function getPrediction(date?: string) {
  try {
    const { data } = await api.get('/api/predict', { params: date ? { date } : undefined })
    return data as Prediction
  } catch (e) {
    throw toFriendlyError(e)
  }
}

/** Thống kê vé đã dò của cả hệ thống (mỗi vé tính 1 lần). */
export type CheckSummary = { tickets: number; winners: number; totalPrize: number }

export async function getCheckStats() {
  try {
    const { data } = await api.get('/api/stats', { timeout: 5_000 })
    return data as CheckSummary
  } catch (e) {
    throw toFriendlyError(e)
  }
}

// ── Blog ──
export type BlogAuthorMode = 'Account' | 'Anonymous' | 'Custom'

export type BlogPost = {
  id: number
  /** Id công khai cho link chia sẻ /blog/{publicId}. */
  publicId: string
  title: string
  content: string
  authorMode: BlogAuthorMode
  /** Username (Account), tên tự đặt (Custom), null khi Ẩn danh. */
  authorName: string | null
  createdAt: string
  likes: number
  dislikes: number
  /** Lượt của mình: 1 thích, −1 không thích, 0 chưa bấm. */
  myVote: number
  /** Bài mình đăng (lúc đăng đã đăng nhập) → được xoá. */
  mine: boolean
  /** Số bình luận, tính cả trả lời. */
  commentCount: number
  /** Đường dẫn ảnh đính kèm (tương đối, /api/blog/images/...) — ghép API_BASE bằng blogImageUrl. */
  images: string[]
}

/** Ảnh blog đi qua backend (cache RAM + cache trình duyệt 1 năm) — cần API_BASE khi FE ở host khác. */
export const blogImageUrl = (path: string) => `${API_BASE}${path}`

export type BlogOptions = { imagesEnabled: boolean; maxImages: number; maxImageBytes: number }

let blogOptions: Promise<BlogOptions> | null = null

/** Máy chủ có bật đăng ảnh không (chưa cấu hình bucket thì ẩn nút). Hỏi 1 lần mỗi phiên. */
export function getBlogOptions(): Promise<BlogOptions> {
  blogOptions ??= api.get('/api/blog/options').then(r => r.data as BlogOptions)
    .catch(() => { blogOptions = null; return { imagesEnabled: false, maxImages: 0, maxImageBytes: 0 } })
  return blogOptions
}

/** Nén rồi upload 1 ảnh chờ đăng → id gửi kèm lúc đăng bài. */
export async function uploadBlogImage(file: Blob, signal?: AbortSignal) {
  try {
    // Nén về 1600px như ảnh vé: ảnh 12MP từ điện thoại còn vài trăm KB, gửi nhanh trên 4G.
    const c = await compressImage(file, DEFAULT_COMPRESS)
    const fd = new FormData()
    fd.append('image', c.blob, 'image.jpg')
    const { data } = await api.post('/api/blog/images', fd, { timeout: 60_000, signal })
    return data as { id: number; url: string }
  } catch (e) {
    throw toFriendlyError(e)
  }
}

/** parentId null = bình luận gốc; có = trả lời (luôn trỏ về bình luận gốc — luồng 1 cấp). */
export type BlogComment = {
  id: number
  parentId: number | null
  content: string
  authorMode: BlogAuthorMode
  authorName: string | null
  createdAt: string
  /** Người viết (đã đăng nhập lúc viết) hoặc admin. */
  canDelete: boolean
}

export async function getBlogComments(postId: number) {
  try {
    const { data } = await api.get(`/api/blog/posts/${postId}/comments`)
    return data as BlogComment[]
  } catch (e) {
    throw toFriendlyError(e)
  }
}

export async function createBlogComment(postId: number,
  body: { content: string; authorMode: BlogAuthorMode; authorName?: string; parentId?: number }) {
  try {
    const { data } = await api.post(`/api/blog/posts/${postId}/comments`, body)
    return data as { comment: BlogComment; commentCount: number }
  } catch (e) {
    throw toFriendlyError(e)
  }
}

export async function deleteBlogComment(id: number) {
  try {
    const { data } = await api.delete(`/api/blog/comments/${id}`)
    return data as { postId: number; commentCount: number }
  } catch (e) {
    throw toFriendlyError(e)
  }
}

export async function getBlogPosts(sort: 'new' | 'top', page: number) {
  try {
    const { data } = await api.get('/api/blog/posts', { params: { sort, page } })
    return data as { items: BlogPost[]; hasMore: boolean }
  } catch (e) {
    throw toFriendlyError(e)
  }
}

export async function createBlogPost(post: {
  title: string; content: string; authorMode: BlogAuthorMode; authorName?: string; imageIds?: number[]
}) {
  try {
    const { data } = await api.post('/api/blog/posts', post)
    return data as BlogPost
  } catch (e) {
    throw toFriendlyError(e)
  }
}

export async function voteBlogPost(id: number, value: number) {
  try {
    const { data } = await api.post(`/api/blog/posts/${id}/vote`, { value })
    return data as { likes: number; dislikes: number; myVote: number }
  } catch (e) {
    throw toFriendlyError(e)
  }
}

export async function deleteBlogPost(id: number) {
  try {
    await api.delete(`/api/blog/posts/${id}`)
  } catch (e) {
    throw toFriendlyError(e)
  }
}

/** 1 bài — mở thẳng từ chuông thông báo (id số) hoặc link chia sẻ (publicId dạng guid). */
export async function getBlogPost(id: number | string) {
  try {
    const { data } = await api.get(`/api/blog/posts/${id}`)
    return data as BlogPost
  } catch (e) {
    throw toFriendlyError(e)
  }
}

// ── Chuông thông báo (cần đăng nhập); thông báo mới đẩy realtime qua SignalR — xem NotificationBell ──
export type AppNotification = {
  id: number
  kind: 'PostComment' | 'CommentReply'
  postId: number
  commentId: number
  postTitle: string
  /** null = người viết ký Ẩn danh. */
  actorName: string | null
  snippet: string
  createdAt: string
  isRead: boolean
}

/** Địa chỉ hub SignalR — cùng tiền tố /api nên đi chung proxy (Vite / Caddy) với API. */
export const NOTIFICATION_HUB_URL = `${API_BASE}/api/hubs/notifications`

export async function getNotifications() {
  try {
    const { data } = await api.get('/api/notifications')
    return data as { items: AppNotification[]; unread: number }
  } catch (e) {
    throw toFriendlyError(e)
  }
}

/** ids bỏ trống = đánh dấu đọc hết. Trả số chưa đọc còn lại. */
export async function markNotificationsRead(ids?: number[]) {
  try {
    const { data } = await api.post('/api/notifications/read', { ids: ids ?? null })
    return (data as { unread: number }).unread
  } catch (e) {
    throw toFriendlyError(e)
  }
}
