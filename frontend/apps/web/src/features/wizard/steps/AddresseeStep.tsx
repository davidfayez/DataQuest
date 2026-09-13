import { zodResolver } from '@hookform/resolvers/zod';
import { Button, Field, Input, Select } from '@dv/ui';
import { useEffect, useState } from 'react';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { useAddressees } from '@/entities/lookup/api';
import { addresseeSchema, type AddresseeValues } from '../schemas';
import { useStepHeading } from '../api';

interface Props {
  /** Persists whatever has been entered so far and leaves the wizard. */
  onSaveDraft?: (values: AddresseeValues) => void;
  isSavingDraft?: boolean;
  defaultValues: AddresseeValues | null;
  onNext: (values: AddresseeValues) => void;
}

/** The select's value for "not on the list", which reveals the free-text box. */
const OTHER = '__other__';

export function AddresseeStep({ onSaveDraft, isSavingDraft, defaultValues, onNext }: Props) {
  const { t } = useTranslation();
  const addressees = useAddressees();

  const form = useForm<AddresseeValues>({
    resolver: zodResolver(addresseeSchema(t)),
    defaultValues: defaultValues ?? { addressedTo: '' },
  });

  // Which control the applicant is using. Held separately from the value because "typed the same
  // words that happen to be on the list" and "picked it from the list" submit the same string.
  const [choice, setChoice] = useState<string>('');

  // Settled once, when the list arrives: an existing draft addressed to something on the list
  // reopens with it selected, and anything else reopens in the free-text box with the text kept.
  const options = addressees.data;
  useEffect(() => {
    if (!options || choice !== '') return;

    const saved = form.getValues('addressedTo').trim();
    if (saved === '') return;

    setChoice(options.some((option) => option.name === saved) ? saved : OTHER);
  }, [options, choice, form]);

  const onChoiceChange = (value: string) => {
    setChoice(value);
    // Picking a name is the answer; picking "other" clears the field so the box starts empty
    // rather than holding the name that was selected a moment ago.
    form.setValue('addressedTo', value === OTHER ? '' : value, { shouldValidate: value !== OTHER });
  };

  // With no list configured there is nothing to choose from, so the field stays what it was: a
  // plain text box. The same fallback covers a lookup that failed to load.
  const hasList = (options?.length ?? 0) > 0;
  const isCustom = !hasList || choice === OTHER;

  // Nothing is rendered until the list has been asked for and answered. Showing the text box first
  // and swapping it for the select a moment later changes the control under whoever is already
  // typing in it, and drops what they typed.
  const isLoading = addressees.isPending;

  const heading = useStepHeading('addressee');

  return (
    <form noValidate className="space-y-6" onSubmit={form.handleSubmit(onNext)}>
      <header className="space-y-1">
        <h2 className="text-lg font-semibold">{heading.title}</h2>
        <p className="text-sm text-muted-foreground">{heading.subtitle}</p>
      </header>

      <Field
        label={t('wizard.addressee.label')}
        htmlFor={isCustom ? 'addressedTo' : 'addressedToChoice'}
        required
        error={form.formState.errors.addressedTo?.message}
      >
        <div className="space-y-2">
          {isLoading && (
            <div
              className="h-11 w-full animate-pulse rounded-xl bg-ink-100"
              data-testid="addressedToLoading"
            />
          )}

          {!isLoading && hasList && (
            <Select
              id="addressedToChoice"
              value={choice}
              onChange={(event) => onChoiceChange(event.target.value)}
              invalid={Boolean(form.formState.errors.addressedTo)}
              data-testid="addressedToChoice"
            >
              <option value="" disabled>
                {t('wizard.addressee.choose')}
              </option>
              {options!.map((option) => (
                <option key={option.id} value={option.name}>
                  {option.name}
                </option>
              ))}
              <option value={OTHER}>{t('wizard.addressee.other')}</option>
            </Select>
          )}

          {!isLoading && isCustom && (
            <Input
              id="addressedTo"
              placeholder={t('wizard.addressee.placeholder')}
              invalid={Boolean(form.formState.errors.addressedTo)}
              {...form.register('addressedTo')}
            />
          )}
        </div>
      </Field>

      <div className="flex justify-end gap-3">
        {onSaveDraft && (
          <Button type="button" variant="outline" onClick={() => onSaveDraft(form.getValues())} disabled={isSavingDraft}>
            {t('wizard.saveDraft')}
          </Button>
        )}
        <Button type="submit">{t('wizard.next')}</Button>
      </div>
    </form>
  );
}
