import { Alert, Button, Field, Input, LoadingState, Spinner } from '@dv/ui';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { adminSession, Permissions } from '@/features/auth/session';
import type { Translations } from '@/features/content/api';
import {
  WIZARD_STEPS,
  useAdminWizardContent,
  useUpdateWizardStepHeading,
  type WizardStepId,
} from '@/features/content/wizard';
import { Notice } from '@/pages/lookups/tabs/shared';
import { LandingPageShell, useEditingLanguage, withLang } from './landingEditing';

/** Multiline field chrome matching the app's inputs; @dv/ui has no textarea of its own. */
const TEXTAREA_CLASS =
  'flex w-full rounded-xl border-0 bg-white px-3.5 py-2.5 text-sm text-ink-900 shadow-soft ring-1 ring-ink-200 transition-shadow placeholder:text-ink-300 focus:outline-none focus:ring-2 focus:ring-brand-500';

type StepCopy = { title: Translations; subtitle: Translations };

type Copy = Record<WizardStepId, StepCopy>;

const EMPTY: Copy = {
  addressee: { title: {}, subtitle: {} },
  personal: { title: {}, subtitle: {} },
  details: { title: {}, subtitle: {} },
  summary: { title: {}, subtitle: {} },
};

/**
 * The headings above each step of the applicant's New application form.
 *
 * Each step saves on its own, and an empty box is not an empty heading: it drops the override and
 * the step goes back to the wording the site ships with. That is why English is not demanded here
 * the way it is on the marketing pages — the wizard is already translated into every language the
 * platform speaks, so a language left blank keeps its own copy rather than falling back to English.
 */
export function WizardContentPage() {
  const { t } = useTranslation();
  const content = useAdminWizardContent();
  const save = useUpdateWizardStepHeading();

  const canUpdate = adminSession.has(Permissions.LandingContentUpdate);

  const [lang, setLang] = useEditingLanguage();
  const [notice, setNotice] = useState<string | null>(null);
  const [copy, setCopy] = useState<Copy>(EMPTY);

  useEffect(() => {
    if (!content.data) return;

    const next = { ...EMPTY };
    for (const step of content.data.steps) {
      next[step.step] = { title: step.title ?? {}, subtitle: step.subtitle ?? {} };
    }
    setCopy(next);
  }, [content.data]);

  if (content.isPending) {
    return <LoadingState label={t('common.loading')} />;
  }

  if (content.isError || !content.data) {
    return <Alert variant="error">{t('errors.genericTitle')}</Alert>;
  }

  function set(step: WizardStepId, field: keyof StepCopy, value: string) {
    setCopy((current) => ({
      ...current,
      [step]: { ...current[step], [field]: withLang(current[step][field], lang, value) },
    }));
  }

  return (
    <LandingPageShell
      title={t('wizardContent.pageTitle')}
      subtitle={t('wizardContent.pageHint')}
      lang={lang}
      onLangChange={setLang}
      filled={(code) =>
        WIZARD_STEPS.some(
          (step) => copy[step].title[code]?.trim() || copy[step].subtitle[code]?.trim(),
        )
      }
    >
      <Notice message={notice} onDismiss={() => setNotice(null)} />

      <Alert variant="info">{t('wizardContent.blankHint')}</Alert>

      {WIZARD_STEPS.map((step) => (
        <section
          key={step}
          className="rounded-2xl bg-white p-6 shadow-soft ring-1 ring-ink-100"
          data-testid={`wizard-step-${step}`}
        >
          <h2 className="mb-4 font-display text-base font-semibold text-ink-950">
            {t(`wizardContent.step.${step}`)}
          </h2>

          <div className="space-y-4">
            <Field label={t('wizardContent.title')} htmlFor={`wizard-${step}-title`}>
              <Input
                id={`wizard-${step}-title`}
                value={copy[step].title[lang] ?? ''}
                disabled={!canUpdate}
                dir="auto"
                maxLength={200}
                onChange={(event) => set(step, 'title', event.target.value)}
                data-testid={`wizard-${step}-title`}
              />
            </Field>

            <Field label={t('wizardContent.subtitle')} htmlFor={`wizard-${step}-subtitle`}>
              <textarea
                id={`wizard-${step}-subtitle`}
                rows={2}
                dir="auto"
                maxLength={400}
                className={TEXTAREA_CLASS}
                disabled={!canUpdate}
                value={copy[step].subtitle[lang] ?? ''}
                onChange={(event) => set(step, 'subtitle', event.target.value)}
                data-testid={`wizard-${step}-subtitle`}
              />
            </Field>
          </div>

          {canUpdate && (
            <div className="mt-4 flex justify-end">
              <Button
                onClick={() =>
                  save.mutate(
                    { step, title: copy[step].title, subtitle: copy[step].subtitle },
                    { onSuccess: () => setNotice(t('wizardContent.saved')) },
                  )
                }
                disabled={save.isPending}
                data-testid={`save-${step}`}
              >
                {save.isPending && <Spinner />}
                {t('landingContent.saveHeading')}
              </Button>
            </div>
          )}
        </section>
      ))}
    </LandingPageShell>
  );
}
