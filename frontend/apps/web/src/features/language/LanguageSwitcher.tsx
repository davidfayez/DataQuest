import { SUPPORTED_LANGUAGES, type LanguageCode } from '@dv/i18n';
import { SearchableSelect } from '@dv/ui';
import { Languages } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useLocation, useNavigate, useParams } from 'react-router-dom';

/**
 * Switches locale and rewrites the `/{lang}/` prefix so the URL stays shareable in the chosen
 * language. i18next persists the choice and flips `dir` on `<html>` for ar and ur.
 */
export function LanguageSwitcher() {
  const { i18n, t } = useTranslation();
  const navigate = useNavigate();
  const location = useLocation();
  const { lang } = useParams<{ lang: string }>();

  const active = (i18n.resolvedLanguage ?? 'en') as LanguageCode;

  async function handleChange(next: string) {
    if (!next) return;
    await i18n.changeLanguage(next);

    // Swap the language segment in place, preserving the rest of the path and query.
    const segments = location.pathname.split('/').filter(Boolean);
    if (lang && segments[0] === lang) {
      segments[0] = next;
    } else {
      segments.unshift(next);
    }

    navigate(`/${segments.join('/')}${location.search}`, { replace: true });
  }

  return (
    <label className="flex items-center gap-2">
      <span className="sr-only">{t('common.changeLanguage')}</span>
      <Languages className="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
      <SearchableSelect
        value={active}
        onChange={(value) => void handleChange(value)}
        className="w-44"
        aria-label={t('common.changeLanguage')}
        searchPlaceholder={t('common.search')}
        emptyMessage={t('common.noResults')}
        options={SUPPORTED_LANGUAGES.map((language) => ({
          value: language.code,
          label: language.name,
        }))}
      />
    </label>
  );
}
