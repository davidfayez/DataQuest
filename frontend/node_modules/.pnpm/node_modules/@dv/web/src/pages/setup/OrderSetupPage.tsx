import {
  Alert,
  Button,
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
  Field,
  Input,
  SearchableSelect,
  Spinner,
} from '@dv/ui';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation } from '@tanstack/react-query';
import { useEffect, useRef } from 'react';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams } from 'react-router-dom';
import { z } from 'zod';
import { useCountries } from '@/entities/lookup/api';
import { setupOrder } from '@/features/auth/api';
import { useApiErrorMessage } from '@/shared/lib/useApiError';
import { PhoneNumberInput } from '@/shared/ui/PhoneNumberInput';
import { DEFAULT_PHONE_COUNTRY, findPhoneCountry } from '@/shared/lib/phoneCountries';

export function OrderSetupPage() {
  const { t } = useTranslation();
  const { lang = 'en' } = useParams<{ lang: string }>();
  const navigate = useNavigate();
  const toMessage = useApiErrorMessage();

  const schema = z.object({
    verificationCountryId: z.string().min(1, t('validation.countryRequired')),
    contactPersonName: z
      .string()
      .trim()
      .min(1, t('validation.contactNameRequired'))
      .max(200, t('validation.contactNameTooLong')),
    contactPhoneCountry: z.string().min(1, t('validation.contactPhoneRequired')),
    // Matches the server: digits only, with the dial prefix carried separately.
    contactPhoneNumber: z
      .string()
      .trim()
      .min(1, t('validation.contactPhoneRequired'))
      .regex(/^\d{4,15}$/, t('validation.contactPhoneInvalid')),
  });

  type FormValues = z.infer<typeof schema>;

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      verificationCountryId: '',
      contactPersonName: '',
      contactPhoneCountry: DEFAULT_PHONE_COUNTRY,
      contactPhoneNumber: '',
    },
  });

  const selectedCountryId = form.watch('verificationCountryId');
  const phoneCountry = form.watch('contactPhoneCountry');
  const phoneNumber = form.watch('contactPhoneNumber');

  // No currency to choose: the order is set up in the chosen country's main currency.
  const countries = useCountries();

  // The contact is usually reachable in the country being verified, so the dial code follows that
  // choice — until the applicant picks one themselves, after which it is left alone.
  const phoneCountryTouched = useRef(false);
  const verificationCountryCode = countries.data?.find(
    (country) => country.id === selectedCountryId,
  )?.code;

  useEffect(() => {
    if (phoneCountryTouched.current || !verificationCountryCode) return;
    if (findPhoneCountry(verificationCountryCode)) {
      form.setValue('contactPhoneCountry', verificationCountryCode.toUpperCase());
    }
  }, [verificationCountryCode, form]);

  const mutation = useMutation({
    mutationFn: (values: FormValues) =>
      setupOrder({
        verificationCountryId: values.verificationCountryId,
        contactPersonName: values.contactPersonName.trim(),
        contactPersonPhoneCountry: values.contactPhoneCountry,
        contactPersonPhoneCode: findPhoneCountry(values.contactPhoneCountry)?.dialCode ?? '',
        contactPersonPhoneNumber: values.contactPhoneNumber.trim(),
      }),
    onSuccess: () => navigate(`/${lang}/applications`, { replace: true }),
  });

  return (
    <div className="mx-auto max-w-lg px-4 py-16 sm:px-6">
      <Card>
        <CardHeader>
          <CardTitle>{t('setup.title')}</CardTitle>
          <CardDescription>{t('setup.subtitle')}</CardDescription>
        </CardHeader>

        <CardContent>
          <form
            noValidate
            className="space-y-5"
            onSubmit={form.handleSubmit((values) => mutation.mutate(values))}
          >
            <Alert variant="warning" title={t('setup.warningTitle')}>
              {t('setup.warningBody')}
            </Alert>

            {mutation.isError && <Alert variant="error">{toMessage(mutation.error)}</Alert>}

            <Field
              label={t('setup.countryLabel')}
              htmlFor="verificationCountryId"
              required
              hint={t('setup.countryHint')}
              error={form.formState.errors.verificationCountryId?.message}
            >
              <SearchableSelect
                id="verificationCountryId"
                disabled={countries.isPending}
                invalid={Boolean(form.formState.errors.verificationCountryId)}
                value={selectedCountryId}
                onChange={(value) =>
                  form.setValue('verificationCountryId', value, { shouldValidate: true })
                }
                placeholder={
                  countries.isPending ? t('common.loading') : t('setup.countryPlaceholder')
                }
                searchPlaceholder={t('common.search')}
                emptyMessage={t('common.noResults')}
                options={(countries.data ?? []).map((country) => ({
                  value: country.id,
                  label: country.name,
                }))}
              />
            </Field>

            <Field
              label={t('setup.contactNameLabel')}
              htmlFor="contactPersonName"
              required
              hint={t('setup.contactNameHint')}
              error={form.formState.errors.contactPersonName?.message}
            >
              <Input
                id="contactPersonName"
                autoComplete="name"
                maxLength={200}
                invalid={Boolean(form.formState.errors.contactPersonName)}
                placeholder={t('setup.contactNamePlaceholder')}
                {...form.register('contactPersonName')}
              />
            </Field>

            <Field
              label={t('setup.contactPhoneLabel')}
              htmlFor="contactPhoneCountry"
              required
              hint={t('setup.contactPhoneHint')}
              error={
                form.formState.errors.contactPhoneNumber?.message ??
                form.formState.errors.contactPhoneCountry?.message
              }
            >
              <PhoneNumberInput
                id="contactPhoneCountry"
                country={phoneCountry}
                number={phoneNumber}
                onCountryChange={(code) => {
                  phoneCountryTouched.current = true;
                  form.setValue('contactPhoneCountry', code, { shouldValidate: true });
                }}
                onNumberChange={(digits) =>
                  form.setValue('contactPhoneNumber', digits, {
                    shouldValidate: form.formState.isSubmitted,
                  })
                }
                invalid={Boolean(
                  form.formState.errors.contactPhoneNumber ??
                    form.formState.errors.contactPhoneCountry,
                )}
                placeholder={t('setup.contactPhonePlaceholder')}
              />
            </Field>

            <Button type="submit" size="lg" className="w-full" disabled={mutation.isPending}>
              {mutation.isPending && <Spinner />}
              {t('setup.submit')}
            </Button>
          </form>
        </CardContent>
      </Card>
    </div>
  );
}
