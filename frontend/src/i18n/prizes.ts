import i18n from './index'

// Backend (LotteryMatcher) trả tên giải tiếng Việt — đổi sang key để dịch.
const TIER_KEYS: Record<string, string> = {
  'Giải Đặc Biệt': 'DB', 'Giải Phụ Đặc Biệt': 'subDB', 'Giải Khuyến Khích': 'consolation',
  'Giải Nhất': '1', 'Giải Nhì': '2', 'Giải Ba': '3', 'Giải Tư': '4',
  'Giải Năm': '5', 'Giải Sáu': '6', 'Giải Bảy': '7', 'Giải Tám': '8',
}

/** Tên giải đầy đủ ("Giải Nhất" / "First prize") từ tierName backend trả về. */
export const prizeName = (tierName: string): string => {
  const k = TIER_KEYS[tierName]
  return k ? i18n.t(`prize.${k}` as 'prize.DB') : tierName
}

/** Nhãn ngắn của hàng giải trong bảng kết quả: tier 'DB' | '1'..'8' → "ĐB"/"G.1" hoặc "Special"/"1st". */
export const tierShort = (tier: string): string =>
  tier === 'DB' ? i18n.t('prizeShort.DB') : i18n.t('prizeShort.n', { n: tier })
