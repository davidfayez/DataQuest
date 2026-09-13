import { Alert, Card, CardContent, LoadingState } from '@dv/ui';
import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams } from 'react-router-dom';
import {
  useApplication,
  useCreateApplication,
  useSubmitApplication,
  useUpdateApplication,
} from '@/entities/application/api';
import { useWallet } from '@/entities/wallet/api';
import { StepIndicator } from '@/features/wizard/StepIndicator';
import { AddresseeStep } from '@/features/wizard/steps/AddresseeStep';
import { DetailsStep } from '@/features/wizard/steps/DetailsStep';
import { FilesStep } from '@/features/wizard/steps/FilesStep';
import { PersonalStep } from '@/features/wizard/steps/PersonalStep';
import { ReviewStep } from '@/features/wizard/steps/ReviewStep';
import { SummaryStep } from '@/features/wizard/steps/SummaryStep';
import {
  fromApplication,
  toWriteModel,
  useWizardState,
  WIZARD_STEPS,
  type WizardState,
  type WizardStep,
} from '@/features/wizard/useWizardState';
import type { AddresseeValues, DetailsValues, PersonalValues } from '@/features/wizard/schemas';
import { useApiErrorMessage } from '@/shared/lib/useApiError';

/**
 * Orchestrates the six-step wizard.
 *
 * Steps 1–4 collect data client-side. Entering step 5 persists the draft, because uploads attach
 * to service lines and those ids only exist once the server has stored the application. Step 6
 * reads the saved application back and submits it.
 */
export function NewApplicationPage() {
  const { t } = useTranslation();
  const { lang = 'en', id: editingId } = useParams<{ lang: string; id: string }>();
  const navigate = useNavigate();
  const toMessage = useApiErrorMessage();

  const { state, patch, reset, replace } = useWizardState();
  const [step, setStep] = useState<WizardStep>('addressee');
  const [completed, setCompleted] = useState<Set<WizardStep>>(new Set());

  // Saving no longer navigates away, so the only sign it worked is on this page.
  const [draftNotice, setDraftNotice] = useState<'saved' | 'empty' | null>(null);

  // Editing an existing draft: load it and seed the wizard from it. A new application needs no
  // such step — the wizard holds nothing between visits, so it opens blank every time.
  const editing = useApplication(editingId);
  const [isHydrated, setIsHydrated] = useState(!editingId);

  useEffect(() => {
    if (!editingId || isHydrated || !editing.data) return;

    replace(fromApplication(editing.data));
    // Everything before the upload step is already answered, so the applicant can jump straight
    // to whatever they came to change.
    setCompleted(new Set<WizardStep>(['addressee', 'personal', 'details', 'summary']));
    setIsHydrated(true);
  }, [editingId, isHydrated, editing.data, replace]);

  const wallet = useWallet();
  const currencyCode = wallet.data?.wallet.currencyCode ?? '';

  const createApplication = useCreateApplication();
  const updateApplication = useUpdateApplication(state.applicationId ?? undefined);
  const submitApplication = useSubmitApplication();

  const markDone = useCallback((done: WizardStep) => {
    setCompleted((previous) => new Set(previous).add(done));
  }, []);

  const goTo = useCallback((next: WizardStep) => {
    setStep(next);
    // The notice belongs to the step it was raised on; carrying it forward would look like the
    // new step had just been saved.
    setDraftNotice(null);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }, []);

  const advance = useCallback(
    (from: WizardStep) => {
      markDone(from);
      goTo(WIZARD_STEPS[WIZARD_STEPS.indexOf(from) + 1]);
    },
    [goTo, markDone],
  );

  const back = useCallback(
    (from: WizardStep) => goTo(WIZARD_STEPS[WIZARD_STEPS.indexOf(from) - 1]),
    [goTo],
  );

  /**
   * Persists the collected steps. Creates on first save and updates thereafter, so navigating
   * back from the upload step and changing something does not orphan the original draft.
   */
  const saveDraft = useCallback(
    (nextState = state) => {
      const model = toWriteModel(nextState);
      if (!model) return;

      const mutation = nextState.applicationId ? updateApplication : createApplication;

      mutation.mutate(model, {
        onSuccess: (application) => patch({ applicationId: application.id }),
      });
    },
    [state, createApplication, updateApplication, patch],
  );

  const saveMutation = state.applicationId ? updateApplication : createApplication;

  /**
   * Persists everything entered so far and stays where the applicant is, so saving is a checkpoint
   * rather than an exit. The server accepts a partial Draft, and the id it returns is kept so the
   * next save updates that draft instead of creating a second one.
   *
   * `stepValues` carries what is currently typed on the step being saved. The wizard state does
   * not hold it until Next is pressed, so without it a save from the first step would find nothing
   * to write and do nothing at all.
   */
  const saveDraftHere = useCallback(
    (stepValues?: Partial<WizardState>) => {
      const nextState = { ...state, ...stepValues };
      const model = toWriteModel(nextState);

      // Only the addressee is genuinely required; below that there is nothing worth storing yet.
      if (!model) {
        setDraftNotice('empty');
        return;
      }

      const mutation = nextState.applicationId ? updateApplication : createApplication;

      mutation.mutate(model, {
        onSuccess: (application) => {
          patch({ ...stepValues, applicationId: application.id });
          setDraftNotice('saved');
        },
      });
    },
    [state, createApplication, updateApplication, patch],
  );

  function handleDetailsNext(values: DetailsValues) {
    patch({ details: values });
    advance('details');
  }

  function handleSummaryNext() {
    markDone('summary');
    goTo('files');
    // The draft is saved on entry to the upload step, which is the first point that needs ids.
    saveDraft();
  }

  // Rendering the steps before the saved application has loaded would briefly show whatever the
  // previous session left behind, and let the applicant edit the wrong values.
  if (editingId && !isHydrated) {
    return (
      <div className="mx-auto max-w-7xl px-4 py-10 sm:px-6">
        {editing.isError ? (
          <Alert variant="error" title={t('errors.genericTitle')}>
            {t('errors.notFound')}
          </Alert>
        ) : (
          <LoadingState label={t('common.loading')} />
        )}
      </div>
    );
  }

  return (
    <div className="mx-auto max-w-7xl px-4 py-10 sm:px-6">
      <h1 className="mb-6 text-2xl font-semibold">
        {editingId ? t('wizard.editTitle') : t('wizard.title')}
      </h1>

      <StepIndicator current={step} completed={completed} onNavigate={goTo} />

      {/* Saving keeps the applicant where they are, so the outcome has to be said here. */}
      {draftNotice === 'saved' && (
        <Alert variant="success" className="mb-4" data-testid="draft-saved">
          {t('wizard.draftSaved')}
        </Alert>
      )}

      {draftNotice === 'empty' && (
        <Alert variant="warning" className="mb-4" data-testid="draft-empty">
          {t('wizard.draftNothingToSave')}
        </Alert>
      )}

      {saveMutation.isError && (
        <Alert variant="error" className="mb-4" data-testid="draft-error">
          {toMessage(saveMutation.error)}
        </Alert>
      )}

      <Card>
        <CardContent className="p-6 sm:p-8">
          {step === 'addressee' && (
            <AddresseeStep
              defaultValues={state.addressee}
              isSavingDraft={saveMutation.isPending}
              onSaveDraft={(values) => saveDraftHere({ addressee: values })}
              onNext={(values: AddresseeValues) => {
                patch({ addressee: values });
                advance('addressee');
              }}
            />
          )}

          {step === 'personal' && (
            <PersonalStep
              defaultValues={state.personal}
              isSavingDraft={saveMutation.isPending}
              onSaveDraft={(values) => saveDraftHere({ personal: values })}
              onBack={() => back('personal')}
              onNext={(values: PersonalValues) => {
                patch({ personal: values });
                advance('personal');
              }}
            />
          )}

          {step === 'details' && (
            <DetailsStep
              defaultValues={state.details}
              isSavingDraft={saveMutation.isPending}
              onSaveDraft={(values) => saveDraftHere({ details: values })}
              currencyCode={currencyCode}
              onBack={() => back('details')}
              onNext={handleDetailsNext}
            />
          )}

          {step === 'summary' && state.details && (
            <SummaryStep
              details={state.details}
              isSavingDraft={saveMutation.isPending}
              onSaveDraft={() => saveDraftHere()}
              currencyCode={currencyCode}
              onBack={() => back('summary')}
              onNext={handleSummaryNext}
            />
          )}

          {step === 'files' && (
            <FilesStep
              applicationId={state.applicationId}
              isSavingDraft={saveMutation.isPending}
              saveError={saveMutation.error}
              onRetrySave={() => saveDraft()}
              onSaveDraft={() => saveDraftHere()}
              onBack={() => back('files')}
              onNext={() => advance('files')}
            />
          )}

          {step === 'review' && state.applicationId && (
            <ReviewStep
              applicationId={state.applicationId}
              isSubmitting={submitApplication.isPending}
              submitError={submitApplication.error}
              onBack={() => back('review')}
              onSaveDraft={() => saveDraftHere()}
              onSubmit={() =>
                submitApplication.mutate(state.applicationId!, {
                  onSuccess: () => {
                    reset();
                    navigate(`/${lang}/applications`);
                  },
                })
              }
            />
          )}
        </CardContent>
      </Card>
    </div>
  );
}
