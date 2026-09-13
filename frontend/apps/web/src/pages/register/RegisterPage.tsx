import { zodResolver } from '@hookform/resolvers/zod';
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
  Spinner,
} from '@dv/ui';
import { useMutation } from '@tanstack/react-query';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router-dom';
import { z } from 'zod';
import { registerOrder, type RegisterOrderResponse } from '@/features/auth/api';
import { useApiErrorMessage } from '@/shared/lib/useApiError';

export function RegisterPage() {
  const { t, i18n } = useTranslation();
  const { lang = 'en' } = useParams<{ lang: string }>();
  const toMessage = useApiErrorMessage();
  const [result, setResult] = useState<RegisterOrderResponse | null>(null);

  // Messages are resolved through `t` at validation time so they follow the active locale.
  const schema = z.object({
    email: z
      .string()
      .min(1, t('validation.emailRequired'))
      .email(t('validation.emailInvalid')),
  });

  type FormValues = z.infer<typeof schema>;

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { email: '' },
  });

  // The credentials email follows the language the applicant is already viewing the site in, so no
  // separate picker is needed on the form.
  const mutation = useMutation({
    mutationFn: (values: FormValues) =>
      registerOrder(values.email, i18n.resolvedLanguage ?? 'en'),
    onSuccess: setResult,
  });

  if (result) {
    // `from=register` marks the arrival so the sign-in page can drop its "create an order"
    // invitation — the applicant has just created one. It is set independently of the order
    // number, which is only echoed in development, so the marker works in production too.
    const loginQuery = new URLSearchParams({ from: 'register' });
    if (result.orderNumber) {
      loginQuery.set('order', result.orderNumber);
    }

    return (
      <div className="mx-auto max-w-lg px-4 py-16 sm:px-6">
        <Card>
          <CardHeader>
            <CardTitle>{t('register.successTitle')}</CardTitle>
            <CardDescription>{t('register.successBody', { email: result.email })}</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <Alert variant="info">{t('register.successSpam')}</Alert>

            {/* Only present when the API runs with credential echo enabled for local development. */}
            {result.orderNumber && result.password && (
              <Alert variant="warning" title={t('register.devCredentials')}>
                <dl className="mt-2 space-y-1 font-mono text-sm">
                  <div className="flex gap-2">
                    <dt className="text-muted-foreground">{t('login.orderNumberLabel')}:</dt>
                    <dd dir="ltr">{result.orderNumber}</dd>
                  </div>
                  <div className="flex gap-2">
                    <dt className="text-muted-foreground">{t('login.passwordLabel')}:</dt>
                    <dd dir="ltr">{result.password}</dd>
                  </div>
                </dl>
              </Alert>
            )}

            <Link
              to={`/${lang}/login?${loginQuery}`}
              className="inline-flex h-11 items-center justify-center rounded-lg bg-primary px-4 text-sm font-medium text-primary-foreground hover:bg-primary/90"
            >
              {t('register.goToLogin')}
            </Link>
          </CardContent>
        </Card>
      </div>
    );
  }

  return (
    <div className="mx-auto max-w-lg px-4 py-16 sm:px-6">
      <Card>
        <CardHeader>
          <CardTitle>{t('register.title')}</CardTitle>
          <CardDescription>{t('register.subtitle')}</CardDescription>
        </CardHeader>

        <CardContent>
          <form
            noValidate
            className="space-y-5"
            onSubmit={form.handleSubmit((values) => mutation.mutate(values))}
          >
            {mutation.isError && (
              <Alert variant="error" title={t('errors.genericTitle')}>
                {toMessage(mutation.error)}
              </Alert>
            )}

            <Field
              label={t('register.emailLabel')}
              htmlFor="email"
              required
              error={form.formState.errors.email?.message}
            >
              <Input
                id="email"
                type="email"
                autoComplete="email"
                dir="ltr"
                placeholder={t('register.emailPlaceholder')}
                invalid={Boolean(form.formState.errors.email)}
                {...form.register('email')}
              />
            </Field>

            <Button type="submit" size="lg" className="w-full" disabled={mutation.isPending}>
              {mutation.isPending && <Spinner />}
              {t('register.submit')}
            </Button>

            <p className="text-center text-sm text-muted-foreground">
              {t('register.haveOrder')}{' '}
              <Link to={`/${lang}/login`} className="font-medium text-primary hover:underline">
                {t('register.signIn')}
              </Link>
            </p>
          </form>
        </CardContent>
      </Card>
    </div>
  );
}
