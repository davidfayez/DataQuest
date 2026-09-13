import { Alert, LoadingState } from '@dv/ui';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useAdminLandingContent } from '@/features/content/api';
import { Notice } from '@/pages/lookups/tabs/shared';
import { LandingPageShell, useEditingLanguage } from './landingEditing';
import { LandingStatsSection } from './LandingStatsSection';

/** The statistics strip across the top of the home page, on its own page. */
export function LandingStatsPage() {
  const { t } = useTranslation();
  const content = useAdminLandingContent();

  const [lang, setLang] = useEditingLanguage();
  const [notice, setNotice] = useState<string | null>(null);

  if (content.isPending) {
    return <LoadingState label={t('common.loading')} />;
  }

  if (content.isError || !content.data) {
    return <Alert variant="error">{t('errors.genericTitle')}</Alert>;
  }

  const stats = content.data.stats;

  return (
    <LandingPageShell
      title={t('landingContent.statsTitle')}
      subtitle={t('landingContent.statsHint')}
      lang={lang}
      onLangChange={setLang}
      // A language counts as written here only when a figure has both halves in it.
      filled={(code) =>
        stats.some((stat) => stat.values[code]?.trim() && stat.labels[code]?.trim())
      }
    >
      <Notice message={notice} onDismiss={() => setNotice(null)} />

      <LandingStatsSection
        stats={stats}
        lang={lang}
        onLangChange={setLang}
        onNotice={setNotice}
      />
    </LandingPageShell>
  );
}
