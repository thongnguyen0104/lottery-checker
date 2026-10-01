import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'
import LanguageDetector from 'i18next-browser-languagedetector'
import viCommon from './locales/vi/common.json'
import viCheck from './locales/vi/check.json'
import viLucky from './locales/vi/lucky.json'
import viResults from './locales/vi/results.json'
import viPredict from './locales/vi/predict.json'
import viBlog from './locales/vi/blog.json'
import viProfile from './locales/vi/profile.json'
import viAdmin from './locales/vi/admin.json'
import viMap from './locales/vi/map.json'
import enCommon from './locales/en/common.json'
import enCheck from './locales/en/check.json'
import enLucky from './locales/en/lucky.json'
import enResults from './locales/en/results.json'
import enPredict from './locales/en/predict.json'
import enBlog from './locales/en/blog.json'
import enProfile from './locales/en/profile.json'
import enAdmin from './locales/en/admin.json'
import enMap from './locales/en/map.json'

export const LANGS = ['vi', 'en'] as const
export type Lang = typeof LANGS[number]

// Namespace theo khu vực: common (khung app, header, popup chung, tên đài/giải), check (Dò vé),
// lucky (6 số may mắn + luận giấc mơ), results (Kết quả xổ số), predict (Dự đoán), blog (Blog),
// profile (Tài khoản), admin (Quản trị), map (Bản đồ điểm bán).
export const resources = {
  vi: { common: viCommon, check: viCheck, lucky: viLucky, results: viResults, predict: viPredict, blog: viBlog, profile: viProfile, admin: viAdmin, map: viMap },
  en: { common: enCommon, check: enCheck, lucky: enLucky, results: enResults, predict: enPredict, blog: enBlog, profile: enProfile, admin: enAdmin, map: enMap },
} as const

i18n
  .use(LanguageDetector)
  .use(initReactI18next)
  .init({
    resources,
    fallbackLng: 'vi',
    supportedLngs: LANGS,
    nonExplicitSupportedLngs: true, // en-US → en
    defaultNS: 'common',
    ns: ['common', 'check', 'lucky', 'results', 'predict', 'blog', 'profile', 'admin', 'map'],
    interpolation: { escapeValue: false }, // React đã tự escape
    // Mặc định tiếng Việt (fallbackLng), KHÔNG theo ngôn ngữ trình duyệt; đã chọn thì nhớ trong localStorage.
    // Key v2: bản cũ dò theo trình duyệt rồi lưu luôn (máy tiếng Anh bị lưu 'en') — đổi key để về tiếng Việt.
    detection: { order: ['localStorage'], lookupLocalStorage: 'dvs.lang.v2', caches: ['localStorage'] },
  })

const syncHtmlLang = (lng: string) => { document.documentElement.lang = lng.startsWith('en') ? 'en' : 'vi' }
syncHtmlLang(i18n.resolvedLanguage ?? 'vi')
i18n.on('languageChanged', syncHtmlLang)

/** Ngôn ngữ đang dùng, rút gọn về 'vi' | 'en'. */
export const currentLang = (): Lang => (i18n.resolvedLanguage?.startsWith('en') ? 'en' : 'vi')
/** Locale cho Intl/toLocaleString. */
export const currentLocale = () => (currentLang() === 'en' ? 'en-US' : 'vi-VN')

export default i18n
