import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'
import LanguageDetector from 'i18next-browser-languagedetector'
import viCommon from './locales/vi/common.json'
import viCheck from './locales/vi/check.json'
import viLucky from './locales/vi/lucky.json'
import viResults from './locales/vi/results.json'
import enCommon from './locales/en/common.json'
import enCheck from './locales/en/check.json'
import enLucky from './locales/en/lucky.json'
import enResults from './locales/en/results.json'

export const LANGS = ['vi', 'en'] as const
export type Lang = typeof LANGS[number]

// Namespace theo khu vực: common (khung app, header, popup chung, tên đài/giải), check (Dò vé),
// lucky (6 số may mắn + luận giấc mơ), results (Kết quả xổ số).
export const resources = {
  vi: { common: viCommon, check: viCheck, lucky: viLucky, results: viResults },
  en: { common: enCommon, check: enCheck, lucky: enLucky, results: enResults },
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
    ns: ['common', 'check', 'lucky', 'results'],
    interpolation: { escapeValue: false }, // React đã tự escape
    // Lần đầu theo ngôn ngữ trình duyệt; đã chọn thì nhớ trong localStorage.
    detection: { order: ['localStorage', 'navigator'], lookupLocalStorage: 'dvs.lang', caches: ['localStorage'] },
  })

const syncHtmlLang = (lng: string) => { document.documentElement.lang = lng.startsWith('en') ? 'en' : 'vi' }
syncHtmlLang(i18n.resolvedLanguage ?? 'vi')
i18n.on('languageChanged', syncHtmlLang)

/** Ngôn ngữ đang dùng, rút gọn về 'vi' | 'en'. */
export const currentLang = (): Lang => (i18n.resolvedLanguage?.startsWith('en') ? 'en' : 'vi')
/** Locale cho Intl/toLocaleString. */
export const currentLocale = () => (currentLang() === 'en' ? 'en-US' : 'vi-VN')

export default i18n
