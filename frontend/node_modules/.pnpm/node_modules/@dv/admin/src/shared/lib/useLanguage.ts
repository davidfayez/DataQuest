import { useTranslation } from 'react-i18next';

/**
 * The active locale, used in query keys for anything the server localizes. Without it, switching
 * language would serve names cached under the previous one.
 */
export function useLanguage(): string {
  const { i18n } = useTranslation();
  return i18n.resolvedLanguage ?? 'en';
}
