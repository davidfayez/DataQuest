import { ADMIN_LANGUAGES, SUPPORTED_LANGUAGES } from '@dv/i18n';
import { Select } from '@dv/ui';
import { Languages } from 'lucide-react';
import { useTranslation } from 'react-i18next';

/**
 * Switches the panel between Arabic and English. i18next persists the choice and flips `dir` on
 * `<html>`, so the whole layout mirrors for Arabic.
 */
export function LanguageSwitcher() {
  const { i18n, t } = useTranslation();

  const options = SUPPORTED_LANGUAGES.filter((language) =>
    ADMIN_LANGUAGES.includes(language.code),
  );

  return (
    <label className="flex items-center gap-2">
      <span className="sr-only">{t('common.changeLanguage')}</span>
      <Languages className="size-4 text-muted-foreground" aria-hidden="true" />
      <Select
        value={i18n.resolvedLanguage ?? 'en'}
        onChange={(event) => void i18n.changeLanguage(event.target.value)}
        className="h-9 w-auto min-w-28 ps-2 pe-8 text-sm"
        aria-label={t('common.changeLanguage')}
      >
        {options.map((language) => (
          <option key={language.code} value={language.code}>
            {language.name}
          </option>
        ))}
      </Select>
    </label>
  );
}
