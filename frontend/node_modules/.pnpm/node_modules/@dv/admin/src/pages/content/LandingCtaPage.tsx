import { Alert, Button, Field, Input, LoadingState } from '@dv/ui';
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
  useEditingLanguage,
  withLang,
} from './landingEditing';

/**
 * The band that closes the landing page: its heading, its line of copy, its button and where the
 * button goes.
 *
 * The destination is the one field here that is not per language — one page serves every locale —
 * so it sits below the translated fields rather than among them.
 */
export function LandingCtaPage() {
  const { t } = useTranslation();
  const content = useAdminLandingContent();
  const save = useUpdateLandingHeading();

  const canUpdate = adminSession.has(Permissions.LandingContentUpdate);

  const [lang, setLang] = useEditingLanguage();
  const [notice, setNotice] = useState<string | null>(null);
  const [title, setTitle] = useState<Translations>({});
  const [body, setBody] = useState<Translations>({});
  const [button, setButton] = useState<Translations>({});
  const [link, setLink] = useState('');

  useEffect(() => {
    if (!content.data) return;

    setTitle(content.data.ctaTitle ?? {});
    setBody(content.data.ctaBody ?? {});
    setButton(content.data.ctaButton ?? {});
    setLink(content.data.ctaLink ?? '');
  }, [content.data]);

  if (content.isPending) {
    return <LoadingState label={t('common.loading')} />;
  }

  if (content.isError || !content.data) {
    return <Alert variant="error">{t('errors.genericTitle')}</Alert>;
  }

  // English is the fallback every other language falls back to, so it is what "complete" means.
  const complete = Boolean(
    title[DEFAULT_LANG]?.trim() && body[DEFAULT_LANG]?.trim() && button[DEFAULT_LANG]?.trim(),
  ) && Boolean(link.trim());

  return (
    <LandingPageShell
      title={t('landingCta.pageTitle')}
      subtitle={t('landingCta.pageHint')}
      lang={lang}
      onLangChange={setLang}
      filled={(code) =>
        Boolean(title[code]?.trim() || body[code]?.trim() || button[code]?.trim())
      }
    >
      <Notice message={notice} onDismiss={() => setNotice(null)} />

      <section className="space-y-4 rounded-2xl bg-white p-6 shadow-soft ring-1 ring-ink-100">
        <Field label={t('landingCta.title')} htmlFor="cta-title" required>
          <Input
            id="cta-title"
            dir="auto"
            maxLength={200}
            value={title[lang] ?? ''}
            onChange={(event) => setTitle(withLang(title, lang, event.target.value))}
            data-testid="cta-title"
          />
        </Field>

        <Field label={t('landingCta.body')} htmlFor="cta-body" required>
          <Input
            id="cta-body"
            dir="auto"
            maxLength={400}
            value={body[lang] ?? ''}
            onChange={(event) => setBody(withLang(body, lang, event.target.value))}
            data-testid="cta-body"
          />
        </Field>

        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t('landingCta.button')} htmlFor="cta-button" required>
            <Input
              id="cta-button"
              dir="auto"
              maxLength={60}
              value={button[lang] ?? ''}
              onChange={(event) => setButton(withLang(button, lang, event.target.value))}
              data-testid="cta-button"
            />
          </Field>

          <Field label={t('landingCta.link')} htmlFor="cta-link" required>
            <Input
              id="cta-link"
              dir="ltr"
              maxLength={500}
              placeholder="/register"
              value={link}
              onChange={(event) => setLink(event.target.value)}
              data-testid="cta-link"
            />
          </Field>
        </div>

        <p className="text-xs text-subtle">{t('landingCta.linkHint')}</p>

        {!complete && <Alert variant="info">{t('landingCta.incomplete')}</Alert>}

        {canUpdate && (
          <div className="flex justify-end">
            <Button
              onClick={() =>
                save.mutate(
                  {
                    ctaTitle: title,
                    ctaBody: body,
                    ctaButton: button,
                    ctaLink: link.trim(),
                  },
                  { onSuccess: () => setNotice(t('landingContent.headingSaved')) },
                )
              }
              disabled={!complete || save.isPending}
              data-testid="cta-save"
            >
              {t('common.save')}
            </Button>
          </div>
        )}

        {save.isError && <Alert variant="error">{t('errors.genericTitle')}</Alert>}
      </section>
    </LandingPageShell>
  );
}
