import i18next, { type BackendModule, type i18n as I18nInstance } from 'i18next';
import LanguageDetector from 'i18next-browser-languagedetector';
import { initReactI18next } from 'react-i18next';
import { DEFAULT_LANGUAGE, directionOf, type LanguageCode } from './languages';

export interface CreateI18nOptions {
  /** Locales this app exposes in its switcher. */
  supportedLngs: readonly LanguageCode[];
  /**
   * Resolves a locale's translation bundle. Apps pass dynamic imports so each locale ships as its
   * own lazily fetched chunk.
   */
  loadResources: (lng: string) => Promise<Record<string, unknown>>;
  storageKey?: string;
}

/**
 * Bridges the app's dynamic imports into i18next's backend contract.
 *
 * This has to be a backend rather than a `languageChanged` listener that calls
 * `addResourceBundle`: i18next only awaits resource loading when it owns the fetch. Loading a
 * bundle after the fact means `changeLanguage()` resolves — and React re-renders — while the new
 * locale is still missing, so the UI silently falls back to English even though `lang` and `dir`
 * have already switched.
 */
function createLazyBackend(
  loadResources: (lng: string) => Promise<Record<string, unknown>>,
): BackendModule {
  return {
    type: 'backend',
    init: () => undefined,
    read: (language, _namespace, callback) => {
      loadResources(language)
        .then((resources) => callback(null, resources))
        .catch((error: unknown) => callback(error as Error, false));
    },
  };
}

/**
 * Builds an i18next instance wired for lazy locale bundles and RTL-aware document updates.
 */
export async function createI18n(options: CreateI18nOptions): Promise<I18nInstance> {
  const { supportedLngs, loadResources, storageKey = 'dv.lang' } = options;

  const instance = i18next.createInstance();

  await instance
    .use(createLazyBackend(loadResources))
    .use(LanguageDetector)
    .use(initReactI18next)
    .init({
      fallbackLng: DEFAULT_LANGUAGE,
      supportedLngs: [...supportedLngs],
      nonExplicitSupportedLngs: true,
      load: 'languageOnly',
      ns: ['translation'],
      defaultNS: 'translation',
      interpolation: { escapeValue: false },
      detection: {
        // URL prefix (/{lang}/...) wins, then the persisted choice, then the browser.
        order: ['path', 'localStorage', 'navigator', 'htmlTag'],
        lookupFromPathIndex: 0,
        lookupLocalStorage: storageKey,
        caches: ['localStorage'],
      },
    });

  applyDocumentDirection(instance.resolvedLanguage ?? DEFAULT_LANGUAGE);

  // By the time this fires, i18next has already loaded the bundle for the new language.
  instance.on('languageChanged', (lng) => applyDocumentDirection(lng));

  return instance;
}

/** Mirrors the whole layout by setting `dir`/`lang` on `<html>` for the active locale. */
export function applyDocumentDirection(lng: string): void {
  if (typeof document === 'undefined') return;
  document.documentElement.lang = lng;
  document.documentElement.dir = directionOf(lng);
}
