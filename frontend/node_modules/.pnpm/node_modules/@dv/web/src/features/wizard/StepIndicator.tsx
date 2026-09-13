import { cn } from '@dv/ui';
import { Check } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { WIZARD_STEPS, type WizardStep } from './useWizardState';

interface Props {
  current: WizardStep;
  /** Steps the user has completed, so finished ones read as done rather than merely passed. */
  completed: ReadonlySet<WizardStep>;
  onNavigate: (step: WizardStep) => void;
}

export function StepIndicator({ current, completed, onNavigate }: Props) {
  const { t } = useTranslation();
  const currentIndex = WIZARD_STEPS.indexOf(current);

  return (
    <nav aria-label={t('wizard.title')} className="mb-8">
      <p className="mb-3 text-sm text-muted-foreground sm:hidden">
        {t('wizard.stepCounter', { current: currentIndex + 1, total: WIZARD_STEPS.length })}
      </p>

      <ol className="flex flex-nowrap items-center gap-x-1 overflow-x-auto pb-1">
        {WIZARD_STEPS.map((step, index) => {
          const isCurrent = step === current;
          const isDone = completed.has(step);
          // Only a visited step is navigable; jumping ahead would skip validation.
          const isReachable = isDone || index <= currentIndex;

          return (
            <li key={step} className="flex shrink-0 items-center">
              <button
                type="button"
                disabled={!isReachable}
                onClick={() => isReachable && onNavigate(step)}
                aria-current={isCurrent ? 'step' : undefined}
                className={cn(
                  'flex items-center gap-2 rounded-lg px-2.5 py-1.5 text-sm transition-colors',
                  isCurrent && 'bg-primary/10 font-medium text-primary',
                  !isCurrent && isReachable && 'text-muted-foreground hover:bg-muted',
                  !isReachable && 'cursor-not-allowed text-muted-foreground/50',
                )}
              >
                <span
                  className={cn(
                    'flex size-6 shrink-0 items-center justify-center rounded-full border text-xs',
                    isCurrent && 'border-primary bg-primary text-primary-foreground',
                    isDone && !isCurrent && 'border-success bg-success text-white',
                    !isCurrent && !isDone && 'border-border',
                  )}
                >
                  {isDone && !isCurrent ? <Check className="size-3.5" aria-hidden="true" /> : index + 1}
                </span>
                <span className="whitespace-nowrap">{t(`wizard.steps.${step}`)}</span>
              </button>

              {index < WIZARD_STEPS.length - 1 && (
                <span aria-hidden="true" className="mx-1 h-px w-4 shrink-0 bg-border" />
              )}
            </li>
          );
        })}
      </ol>
    </nav>
  );
}
