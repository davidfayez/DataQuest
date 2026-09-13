import { useTranslation } from 'react-i18next';

/**
 * The active locale, for use in query keys and `Intl` formatting. Server-localized data must be
 * cached per language, otherwise switching language serves stale names from the previous one.
 */
export function useLanguage(): string {
  const { i18n } = useTranslation();
  return i18n.resolvedLanguage ?? 'en';
}
