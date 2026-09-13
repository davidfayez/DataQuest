import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { apiClient } from '@/shared/api/client';

/** A step's heading as an administrator wrote it, or null where they have not. */
export interface WizardStepHeading {
  title: string | null;
  subtitle: string | null;
}

export interface WizardContent {
  steps: Partial<Record<string, WizardStepHeading>>;
}

/**
 * The admin-managed headings above the wizard's steps, for the language being read.
 *
 * Keyed by language like the landing copy, and never retried: a step that cannot reach this falls
 * back to the wording bundled with the app, so a failed request never leaves a step without a
 * heading.
 */
export function useWizardContent() {
  const { i18n } = useTranslation();
  const language = i18n.resolvedLanguage ?? 'en';

  return useQuery({
    queryKey: ['content', 'wizard', language] as const,
    queryFn: () => apiClient.get<WizardContent>('content/wizard', { language }),
    staleTime: 5 * 60_000,
    retry: false,
  });
}

/**
 * One step's heading: what an administrator wrote, or the app's own translation.
 *
 * The server answers null rather than English for a language nobody has written, which is what lets
 * a Russian or Turkish reader keep their own translation instead of being handed English copy.
 */
export function useStepHeading(step: 'addressee' | 'personal' | 'details' | 'summary'): {
  title: string;
  subtitle: string;
} {
  const { t } = useTranslation();
  const content = useWizardContent();
  const configured = content.data?.steps?.[step];

  return {
    title: configured?.title?.trim() || t(`wizard.${step}.title`),
    subtitle: configured?.subtitle?.trim() || t(`wizard.${step}.subtitle`),
  };
}
