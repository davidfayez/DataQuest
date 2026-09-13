import { applyDocumentDirection, isSupportedLanguage } from '@dv/i18n';
import { useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { Navigate, Outlet, useParams } from 'react-router-dom';

/**
 * Treats the `/{lang}/` URL segment as the source of truth for the active locale. Landing on a
 * translated URL directly — from a shared link or the credentials email — switches the app into
 * that language and flips the document direction before anything renders.
 */
export function LanguageBoundary() {
  const { lang } = useParams<{ lang: string }>();
  const { i18n } = useTranslation();

  const supported = lang && isSupportedLanguage(lang);

  useEffect(() => {
    if (!supported || i18n.resolvedLanguage === lang) return;
    void i18n.changeLanguage(lang);
  }, [lang, supported, i18n]);

  useEffect(() => {
    if (supported) applyDocumentDirection(lang);
  }, [lang, supported]);

  // An unknown segment is not a language — fall back to English rather than rendering a shell
  // with missing translations.
  if (!supported) {
    return <Navigate to="/en" replace />;
  }

  return <Outlet />;
}
