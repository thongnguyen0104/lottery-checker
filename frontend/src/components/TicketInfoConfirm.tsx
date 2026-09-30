import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import ScanFeedback from './ScanFeedback'
import Icon from './Icon'
import type { ScanResponse, TicketQuery } from '../api/client'
import { CLAIM_DAYS, claimDeadline, isExpired } from '../utils/claim'
import { provinceName } from '../data/provinces'

const fmtDate = (iso: string) => iso.split('-').reverse().join('/')

type Props = {
  // Dùng thẳng kiểu của API: thêm trường mới (vd timings) là tự chảy tới ScanFeedback,
  // khỏi phải khai lại ở đây rồi quên mất một chỗ.
  scanned: ScanResponse
  /** Thông tin đã đem dò (mở lại form để sửa sau khi dò) — điền thay cho kết quả quét. */
  initial?: TicketQuery | null
  /** Ảnh vé vừa quét (object URL) — hiện kèm để user so từng số khi sửa tay. */
  imageUrl?: string | null
  allProvinces: { code: string; name: string }[]
  onConfirm: (data: { ticketNumber: string; drawDate: string; province: string }) => void
  onRescan: () => void
  /** Ẩn khung "máy đọc được gì" — vé lấy từ ảnh nhiều vé không có số liệu quét riêng từng vé. */
  hideFeedback?: boolean
  /** Nhãn nút quay lại (mặc định: chụp lại) — vé trong ảnh nhiều vé thì quay về danh sách. */
  rescanLabel?: string
}

export default function TicketInfoConfirm({
  scanned, initial, imageUrl, allProvinces, onConfirm, onRescan, hideFeedback, rescanLabel,
}: Props) {
  const { t } = useTranslation('check')
  const [ticket, setTicket] = useState(initial?.ticketNumber ?? scanned.ticketNumber ?? '')
  const [date, setDate] = useState(initial?.drawDate ?? scanned.drawDate ?? new Date().toISOString().slice(0, 10))
  const [province, setProvince] = useState(initial?.province ?? scanned.province ?? '')
  // HEIC trên Chrome không giải mã được → ẩn ô ảnh thay vì hiện icon ảnh vỡ.
  const [imgFailed, setImgFailed] = useState(false)

  // Đỏ = OCR không đọc được; vàng = đọc được nhưng chưa chắc (backend needsReview) — nhất là khi
  // máy chủ KHÔNG gọi AI đọc lại cho đài/ngày, user chính là người xác nhận cuối cùng.
  const review = new Set(scanned.needsReview ?? [])
  // .field có sẵn bg-surface: không đặt nền thì iOS tô xám ô ngày và ô chọn đài, lệch tông ô số vé.
  const fieldClass = (missing: boolean, uncertain = false) =>
    `field ${missing ? 'border-bad/60 bg-bad/5' : uncertain ? 'border-warn/70 bg-warn/5' : ''}`
  const reviewHint = (show: boolean) => show && (
    <span className="flex items-center gap-1 text-xs text-warn mt-1">
      <Icon name="warn" className="w-3.5 h-3.5 shrink-0" /> {t('confirm.unsure')}
    </span>
  )

  return (
    <div className="space-y-4">
      <div>
        <h1 className="text-xl md:text-3xl font-extrabold tracking-tight">{t('confirm.heading')}</h1>
        <p className="text-sm md:text-base text-ink-soft mt-0.5">{t('confirm.subtitle')}</p>
      </div>

      <div className="space-y-4 md:space-y-0 md:grid md:grid-cols-2 md:gap-6 md:items-start">
        <div className="space-y-4">
          {imageUrl && !imgFailed && (
            <div className="card p-2">
              <img src={imageUrl} alt={t('confirm.imageAlt')} decoding="async" onError={() => setImgFailed(true)}
                   className="block w-full max-h-48 md:max-h-80 object-contain rounded-xl bg-slate-900" />
            </div>
          )}
          {!hideFeedback && <ScanFeedback scanned={scanned} />}
        </div>

        <div className="card p-4 md:p-5 space-y-4">
          <label className="block">
            <span className="text-sm font-medium text-ink-soft">{t('confirm.number')}</span>
            <input value={ticket}
                   onChange={e => setTicket(e.target.value.replace(/\D/g, '').slice(0, 6))}
                   className={`${fieldClass(!scanned.ticketNumber, review.has('number'))} mt-1
                               text-center text-2xl font-extrabold tracking-[.3em] tabular-nums`}
                   inputMode="numeric" placeholder="123456" />
            {reviewHint(!!scanned.ticketNumber && review.has('number'))}
          </label>

          <label className="block">
            <span className="text-sm font-medium text-ink-soft">{t('confirm.drawDate')}</span>
            <input type="date" value={date} onChange={e => setDate(e.target.value)}
                   className={`${fieldClass(!scanned.drawDate, review.has('date'))} mt-1`} />
            {reviewHint(!!scanned.drawDate && review.has('date'))}
          </label>

          {/* Báo sớm ngay khi ngày trên vé (đọc được hoặc sửa tay) đã quá hạn — khỏi bấm dò mới biết */}
          {isExpired(date) && (
            <div className="alert flex gap-2 bg-bad/10 border-bad/30 text-bad">
              <Icon name="expired" className="w-4 h-4 shrink-0 mt-0.5" />
              <span>
                <b>{t('confirm.expiredTitle')}</b>
                {t('confirm.expiredBody', { days: CLAIM_DAYS, deadline: fmtDate(claimDeadline(date)) })}
              </span>
            </div>
          )}

          <label className="block">
            <span className="text-sm font-medium text-ink-soft">{t('confirm.province')}</span>
            <select value={province} onChange={e => setProvince(e.target.value)}
                    className={`${fieldClass(!scanned.province, review.has('province'))} mt-1`}>
              <option value="">{t('confirm.chooseProvince')}</option>
              {allProvinces.map(p => (
                <option key={p.code} value={p.code}>{provinceName(p.code)}</option>
              ))}
            </select>
            {reviewHint(!!scanned.province && review.has('province'))}
          </label>

          <div className="flex gap-2 sm:gap-3 pt-1">
            <button onClick={onRescan} className="btn btn-secondary flex-1 min-w-0">
              <Icon name={rescanLabel ? 'back' : 'camera'} /> {rescanLabel ?? t('confirm.rescan')}
            </button>
            <button
              onClick={() => onConfirm({ ticketNumber: ticket, drawDate: date, province })}
              disabled={!ticket || ticket.length !== 6 || !province}
              className="btn btn-primary flex-[1.4] min-w-0">
              <Icon name="search" /> {t('confirm.checkNow')}
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}
