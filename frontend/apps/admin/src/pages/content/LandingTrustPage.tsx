import { Alert, LoadingState } from '@dv/ui';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { adminSession, Permissions } from '@/features/auth/session';
import {
  useAdminLandingContent,
  useUpdateLandingHeading,
  type Translations,
} from '@/features/content/api';
import { Notice } from '@/pages/lookups/tabs/shared';
import {
  DEFAULT_LANG,
  LandingPageShell,
  SectionHeadingPanel,
  useEditingLanguage,
  withLang,
} from './landingEditing';
import { LandingTrustSection } from './LandingTrustSection';

/**
 * The "trusted for verification with" strip: its heading and the bodies it names, together.
 */
export function LandingTrustPage() {
  const { t } = useTranslation();
  const content = useAdminLandingContent();
  const saveHeading = useUpdateLandingHeading();

  const canUpdate = adminSession.has(Permissions.LandingContentUpdate);

  const [lang, setLang] = useEditingLanguage();
  const [notice, setNotice] = useState<string | null>(null);
  const [trustTitle, setTrustTitle] = useState<Translations>({});

  useEffect(() => {
    if (content.data) setTrustTitle(content.data.trustTitle ?? {});
  }, [content.data]);

  if (content.isPending) {
    return <LoadingState label={t('common.loading')} />;
  }

  if (content.isError || !content.data) {
    return <Alert variant="error">{t('errors.genericTitle')}</Alert>;
  }

  const entries = content.data.trustedBy;

  return (
    <LandingPageShell
      title={t('landingContent.trustTitle')}
      subtitle={t('landingContent.trustHint')}
      lang={lang}
      onLangChange={setLang}
      filled={(code) =>
        Boolean(trustTitle[code]?.trim()) || entries.some((entry) => entry.names[code]?.trim())
      }
    >
      <Notice message={notice} onDismiss={() => setNotice(null)} />

      {/* Only this section's heading is sent, so saving here cannot disturb the features copy. */}
      <SectionHeadingPanel
        fields={[
          {
            id: 'trust-heading',
            label: t('landingContent.trustHeading'),
            value: trustTitle[lang] ?? '',
            onChange: (value) => setTrustTitle(withLang(trustTitle, lang, value)),
          },
        ]}
        canUpdate={canUpdate}
        complete={Boolean(trustTitle[DEFAULT_LANG]?.trim())}
        isPending={saveHeading.isPending}
        onSave={() =>
          saveHeading.mutate(
            { trustTitle },
            { onSuccess: () => setNotice(t('landingContent.headingSaved')) },
          )
        }
      />

      <LandingTrustSection
        entries={entries}
        lang={lang}
        onLangChange={setLang}
        onNotice={setNotice}
      />
    </LandingPageShell>
  );
}
