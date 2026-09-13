import { zodResolver } from '@hookform/resolvers/zod';
import { Button, Field, Input } from '@dv/ui';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { NameLanguageType } from '@/entities/application/types';
import { DEFAULT_PHONE_COUNTRY } from '@/shared/lib/phoneCountries';
import { PhoneNumberInput } from '@/shared/ui/PhoneNumberInput';
import { personalSchema, type PersonalValues } from '../schemas';
import { useStepHeading } from '../api';

interface Props {
  /** Persists whatever has been entered so far and leaves the wizard. */
  onSaveDraft?: (values: PersonalValues) => void;
  isSavingDraft?: boolean;
  defaultValues: PersonalValues | null;
  onNext: (values: PersonalValues) => void;
  onBack: () => void;
}

export function PersonalStep({ onSaveDraft, isSavingDraft, defaultValues, onNext, onBack }: Props) {
  const { t } = useTranslation();

  const form = useForm<PersonalValues>({
    resolver: zodResolver(personalSchema(t)),
    defaultValues: defaultValues ?? {
      arabicName: {
        languageType: NameLanguageType.Arabic,
        firstName: '',
        middleName: '',
        lastName: '',
      },
      englishName: {
        languageType: NameLanguageType.English,
        firstName: '',
        middleName: '',
        lastName: '',
      },
      birthDate: '',
      email: '',
      phoneCountry: DEFAULT_PHONE_COUNTRY,
      phoneNumber: '',
    },
  });

  const errors = form.formState.errors;
  const phoneCountry = form.watch('phoneCountry');
  const phoneNumber = form.watch('phoneNumber');

  // Today, so the browser's own date picker cannot offer a future date of birth either.
  const today = new Date().toISOString().slice(0, 10);

  const heading = useStepHeading('personal');

  return (
    <form noValidate className="space-y-6" onSubmit={form.handleSubmit(onNext)}>
      <header className="space-y-1">
        <h2 className="text-lg font-semibold">{heading.title}</h2>
        <p className="text-sm text-muted-foreground">{heading.subtitle}</p>
      </header>

      {/* Arabic name labels stay in Arabic regardless of UI language, so the block always reads
          as an Arabic form. The English block keeps the active locale's labels. */}
      <fieldset className="space-y-4 rounded-lg border border-border p-4">
        <legend className="px-1 text-sm font-medium" dir="rtl" lang="ar">
          الاسم بالعربية
        </legend>
        <div className="grid gap-4 sm:grid-cols-3" dir="rtl" lang="ar">
          <Field
            label="الاسم الأول"
            htmlFor="ar-first"
            required
            error={errors.arabicName?.firstName?.message}
          >
            <Input
              id="ar-first"
              invalid={Boolean(errors.arabicName?.firstName)}
              {...form.register('arabicName.firstName')}
            />
          </Field>
          <Field label="الاسم الأوسط" htmlFor="ar-middle">
            <Input id="ar-middle" {...form.register('arabicName.middleName')} />
          </Field>
          <Field
            label="اسم العائلة"
            htmlFor="ar-last"
            required
            error={errors.arabicName?.lastName?.message}
          >
            <Input
              id="ar-last"
              invalid={Boolean(errors.arabicName?.lastName)}
              {...form.register('arabicName.lastName')}
            />
          </Field>
        </div>
      </fieldset>

      <fieldset className="space-y-4 rounded-lg border border-border p-4">
        <legend className="px-1 text-sm font-medium">{t('wizard.personal.englishName')}</legend>
        <div className="grid gap-4 sm:grid-cols-3" dir="ltr">
          <Field
            label={t('wizard.personal.firstName')}
            htmlFor="en-first"
            required
            error={errors.englishName?.firstName?.message}
          >
            <Input
              id="en-first"
              invalid={Boolean(errors.englishName?.firstName)}
              {...form.register('englishName.firstName')}
            />
          </Field>
          <Field label={t('wizard.personal.middleName')} htmlFor="en-middle">
            <Input id="en-middle" {...form.register('englishName.middleName')} />
          </Field>
          <Field
            label={t('wizard.personal.lastName')}
            htmlFor="en-last"
            required
            error={errors.englishName?.lastName?.message}
          >
            <Input
              id="en-last"
              invalid={Boolean(errors.englishName?.lastName)}
              {...form.register('englishName.lastName')}
            />
          </Field>
        </div>
      </fieldset>

      <Field
        label={t('wizard.personal.birthDate')}
        htmlFor="birthDate"
        required
        error={errors.birthDate?.message}
        className="max-w-xs"
      >
        <Input
          id="birthDate"
          type="date"
          max={today}
          invalid={Boolean(errors.birthDate)}
          {...form.register('birthDate')}
        />
      </Field>

      {/* How the applicant themselves is reached about this application — distinct from the
          order's login email and its contact person. */}
      <fieldset className="space-y-4 rounded-lg border border-border p-4">
        <legend className="px-1 text-sm font-medium">{t('wizard.personal.contact')}</legend>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field
            label={t('wizard.personal.email')}
            htmlFor="applicantEmail"
            required
            error={errors.email?.message}
          >
            <Input
              id="applicantEmail"
              type="email"
              autoComplete="email"
              dir="ltr"
              maxLength={320}
              invalid={Boolean(errors.email)}
              placeholder={t('wizard.personal.emailPlaceholder')}
              {...form.register('email')}
            />
          </Field>

          <Field
            label={t('wizard.personal.phone')}
            htmlFor="applicantPhoneCountry"
            required
            error={errors.phoneNumber?.message ?? errors.phoneCountry?.message}
          >
            <PhoneNumberInput
              id="applicantPhoneCountry"
              country={phoneCountry}
              number={phoneNumber}
              onCountryChange={(code) =>
                form.setValue('phoneCountry', code, { shouldValidate: true })
              }
              onNumberChange={(digits) =>
                form.setValue('phoneNumber', digits, {
                  shouldValidate: form.formState.isSubmitted,
                })
              }
              invalid={Boolean(errors.phoneNumber ?? errors.phoneCountry)}
              placeholder={t('wizard.personal.phonePlaceholder')}
            />
          </Field>
        </div>
      </fieldset>

      <div className="flex flex-wrap justify-end gap-3">
        <Button type="button" variant="outline" onClick={onBack}>
          {t('wizard.previous')}
        </Button>
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
