import { createI18n, SUPPORTED_LANGUAGES, type LanguageCode } from '@dv/i18n';
import type { i18n as I18nInstance } from 'i18next';

/**
 * Each locale is its own dynamic import, so Vite emits one chunk per language and a first paint
 * never downloads the locales the visitor is not using.
 */
const loaders: Record<LanguageCode, () => Promise<{ default: Record<string, unknown> }>> = {
  ar: () => import('@dv/i18n/locales/ar.json'),
  en: () => import('@dv/i18n/locales/en.json'),
  ru: () => import('@dv/i18n/locales/ru.json'),
  tr: () => import('@dv/i18n/locales/tr.json'),
  uz: () => import('@dv/i18n/locales/uz.json'),
  de: () => import('@dv/i18n/locales/de.json'),
  hi: () => import('@dv/i18n/locales/hi.json'),
  zh: () => import('@dv/i18n/locales/zh.json'),
  ja: () => import('@dv/i18n/locales/ja.json'),
  pl: () => import('@dv/i18n/locales/pl.json'),
};

export const WEB_LANGUAGES = SUPPORTED_LANGUAGES.map((language) => language.code);

/**
 * The app runs its own i18next instance rather than the global singleton, so anything outside
 * React that needs the active locale — notably the API client's `Accept-Language` header — must
 * read it from here. Reading the global instance instead silently yields the wrong language and
 * the server resolves lookup names into English.
 */
let activeInstance: I18nInstance | null = null;

export function getActiveLanguage(): string {
  return activeInstance?.resolvedLanguage ?? 'en';
}

export async function setupI18n(): Promise<I18nInstance> {
  const instance = await createI18n({
    supportedLngs: WEB_LANGUAGES,
    loadResources: async (lng) => {
      const load = loaders[lng as LanguageCode] ?? loaders.en;
      const module = await load();
      return module.default;
    },
  });

  activeInstance = instance;
  return instance;
}
