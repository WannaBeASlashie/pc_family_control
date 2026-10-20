import { createI18n } from 'vue-i18n'
import en from './en'
import zh from './zh'

export type AppLocale = 'en' | 'zh-CN'

const LOCALE_KEY = 'tb_locale'

function initialLocale(): AppLocale {
  const saved = localStorage.getItem(LOCALE_KEY)
  if (saved === 'en' || saved === 'zh-CN') return saved
  return navigator.language.toLowerCase().startsWith('zh') ? 'zh-CN' : 'en'
}

export const i18n = createI18n({
  legacy: false,
  locale: initialLocale(),
  fallbackLocale: 'en',
  messages: { en, 'zh-CN': zh }
})

export function setLocale(locale: AppLocale): void {
  i18n.global.locale.value = locale
  localStorage.setItem(LOCALE_KEY, locale)
}

export function currentLocale(): AppLocale {
  return i18n.global.locale.value as AppLocale
}
