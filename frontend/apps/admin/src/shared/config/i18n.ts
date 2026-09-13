import { ADMIN_LANGUAGES, createI18n } from '@dv/i18n';

/**
 * Inferred from `createI18n` rather than imported from `i18next` directly: the workspace can
 * resolve more than one copy of the i18next types, and inferring keeps this file bound to the
 * exact instance type the shared package produces.
 */
type I18nInstance = Awaited<ReturnType<typeof createI18n>>;

/**
 * The admin panel ships Arabic and English. The underlying infrastructure supports all seven
 * locales, so adding another is a matter of dropping in a bundle and extending this map.
 */
const loaders: Record<string, () => Promise<{ default: Record<string, unknown> }>> = {
  ar: () => import('@dv/i18n/locales/admin/ar.json'),
  en: () => import('@dv/i18n/locales/admin/en.json'),
};

let activeInstance: I18nInstance | null = null;

/**
 * The active locale, for anything outside React that needs it — notably the API client's
 * `Accept-Language` header, which decides the language of server-resolved lookup names.
 */
export function getActiveLanguage(): string {
  return activeInstance?.resolvedLanguage ?? 'en';
}

export async function setupI18n(): Promise<I18nInstance> {
  const instance = await createI18n({
    supportedLngs: ADMIN_LANGUAGES,
    storageKey: 'dv.admin.lang',
    loadResources: async (lng) => {
      const load = loaders[lng] ?? loaders.en;
      const module = await load();
      return module.default;
    },
  });

  activeInstance = instance;
  return instance;
}
