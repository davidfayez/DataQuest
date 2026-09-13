import { isSupportedLanguage } from '@dv/i18n';
import { Navigate } from 'react-router-dom';
import { getActiveLanguage } from '@/shared/config/i18n';

/**
 * Sends a path without a language segment to the locale i18next resolved, so `/` lands on
 * `/{lang}` and every subsequent URL carries its language.
 */
export function LocaleRedirect() {
  const detected = getActiveLanguage();
  const language = isSupportedLanguage(detected) ? detected : 'en';
  return <Navigate to={`/${language}`} replace />;
}
