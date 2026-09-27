/**
 * Cỡ + chất lượng ảnh gửi đi. Backend quyết theo đường đọc đang bật (GET /api/scan/options):
 * OCR cục bộ cần 1600px (khớp ImagePreprocessor.MaxWidth — thu nhỏ hơn thì đọc sai), Gemini
 * đọc đúng với 1280px mà nhanh hơn. Mặc định ở đây là cho OCR cục bộ — an toàn cho cả hai.
 */
export type CompressOptions = { maxWidth: number; quality: number }
export const DEFAULT_COMPRESS: CompressOptions = { maxWidth: 1600, quality: 0.85 }

/** Thời gian từng khâu nén (ms) — để biết khâu nào chậm trên máy thật của user. */
export type CompressStages = {
  decode: number   // giải mã ảnh gốc (12MP từ camera điện thoại)
  resize: number   // vẽ thu nhỏ lên canvas
  encode: number   // mã hoá JPEG
  decoder: 'img' | 'bitmap'
}

export type CompressResult = {
  blob: Blob
  originalBytes: number
  sentBytes: number
  compressMs: number
  stages?: CompressStages
}

type Decoded = { source: CanvasImageSource; width: number; height: number; release: () => void }

/**
 * Giải mã bằng thẻ <img>: trình duyệt dùng bộ giải mã phần cứng/đa luồng của chính nó, và tự
 * xoay theo EXIF (CSS image-orientation mặc định from-image), naturalWidth/Height đã là kích
 * thước sau khi xoay. Nhanh hơn hẳn createImageBitmap(blob) trên Safari — đo trên iPhone thật,
 * bản dùng createImageBitmap + imageSmoothingQuality 'high' mất tới 5,2s cho ảnh 12MP.
 */
async function decodeWithImg(input: Blob): Promise<Decoded> {
  const url = URL.createObjectURL(input)
  const img = new Image()
  img.src = url
  try {
    await img.decode()
  } catch (e) {
    URL.revokeObjectURL(url)
    throw e
  }
  return { source: img, width: img.naturalWidth, height: img.naturalHeight,
           release: () => URL.revokeObjectURL(url) }
}

// Dự phòng khi <img>.decode() không dùng được. imageOrientation 'from-image': JPEG xuất ra không
// còn EXIF, không xoay ở đây thì backend (AutoOrient) sẽ nhận ảnh nằm ngang.
async function decodeWithBitmap(input: Blob): Promise<Decoded> {
  const bitmap = await createImageBitmap(input, { imageOrientation: 'from-image' })
  return { source: bitmap, width: bitmap.width, height: bitmap.height, release: () => bitmap.close() }
}

/**
 * Thu nhỏ + nén JPEG ảnh vé trước khi upload. Lỗi gì (trình duyệt cũ, ảnh HEIC không giải mã
 * được...) thì trả nguyên ảnh gốc — nén chỉ là tối ưu, không được làm hỏng luồng quét.
 */
export async function compressImage(
  input: Blob,
  { maxWidth, quality }: CompressOptions = DEFAULT_COMPRESS,
): Promise<CompressResult> {
  const startedAt = performance.now()
  const passthrough = (): CompressResult => ({
    blob: input, originalBytes: input.size, sentBytes: input.size,
    compressMs: Math.round(performance.now() - startedAt),
  })

  try {
    let t = performance.now()
    let decoder: CompressStages['decoder'] = 'img'
    let decoded: Decoded
    try {
      decoded = await decodeWithImg(input)
    } catch {
      decoder = 'bitmap'
      decoded = await decodeWithBitmap(input)
    }
    const decode = performance.now() - t

    t = performance.now()
    const scale = Math.min(1, maxWidth / decoded.width)
    const w = Math.round(decoded.width * scale)
    const h = Math.round(decoded.height * scale)
    const canvas = document.createElement('canvas')
    canvas.width = w
    canvas.height = h
    const ctx = canvas.getContext('2d')
    if (!ctx) { decoded.release(); return passthrough() }
    // KHÔNG đặt imageSmoothingQuality 'high': trên Safari nó chuyển sang thu nhỏ bằng phần mềm,
    // rất chậm với ảnh 12MP. Tỉ lệ thu chỉ ~1,9–2,4 lần (3024 → 1600/1280) nên nội suy mặc định
    // (GPU) vẫn đủ nét cho OCR.
    ctx.drawImage(decoded.source, 0, 0, w, h)
    decoded.release()
    const resize = performance.now() - t

    t = performance.now()
    const blob = await new Promise<Blob | null>(r => canvas.toBlob(r, 'image/jpeg', quality))
    const encode = performance.now() - t

    // Ảnh gốc đã nhỏ sẵn (vd. screenshot webcam) thì nén lại có khi còn to hơn → gửi gốc.
    if (!blob || blob.size >= input.size) return passthrough()

    return {
      blob, originalBytes: input.size, sentBytes: blob.size,
      compressMs: Math.round(performance.now() - startedAt),
      stages: { decode: Math.round(decode), resize: Math.round(resize), encode: Math.round(encode), decoder },
    }
  } catch {
    return passthrough()
  }
}
