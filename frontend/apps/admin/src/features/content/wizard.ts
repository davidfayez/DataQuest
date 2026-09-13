import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/shared/api/client';
import type { Translations } from './api';

/**
 * The steps of the applicant's New application wizard whose heading is editable here.
 *
 * The documents and review steps are absent on purpose: their copy carries a placeholder for the
 * upload limit, which free text could silently drop.
 */
export const WIZARD_STEPS = ['addressee', 'personal', 'details', 'summary'] as const;

export type WizardStepId = (typeof WIZARD_STEPS)[number];

/** One step's heading, in every language somebody has written it in. */
export interface WizardStepHeadingDto {
  step: WizardStepId;
  title: Translations;
  subtitle: Translations;
}

export interface WizardContentDto {
  steps: WizardStepHeadingDto[];
}

/**
 * Both fields optional; omitting one leaves the stored value alone. A language sent blank drops the
 * override, and that language falls back to the wording the site ships with.
 */
export interface UpdateWizardStepHeadingBody {
  step: WizardStepId;
  title?: Translations;
  subtitle?: Translations;
}

const wizardKey = ['admin', 'content', 'wizard'] as const;

export function useAdminWizardContent() {
  return useQuery({
    queryKey: wizardKey,
    queryFn: () => apiClient.get<WizardContentDto>('admin/content/wizard'),
  });
}

export function useUpdateWizardStepHeading() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (body: UpdateWizardStepHeadingBody) =>
      apiClient.put<void>('admin/content/wizard/heading', body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: wizardKey }),
  });
}
