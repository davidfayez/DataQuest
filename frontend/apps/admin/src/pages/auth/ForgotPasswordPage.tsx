import { zodResolver } from '@hookform/resolvers/zod';
import { Alert, Button, Field, Input, Spinner } from '@dv/ui';
import { useMutation } from '@tanstack/react-query';
import { ArrowLeft, MailCheck } from 'lucide-react';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { z } from 'zod';
import { requestPasswordReset } from '@/features/auth/api';
import { useApiErrorMessage } from '@/shared/lib/useApiError';
import { AuthScreen } from './AuthScreen';

export function ForgotPasswordPage() {
  const { t } = useTranslation();
  const toMessage = useApiErrorMessage();

  const schema = z.object({
    email: z.string().min(1, t('login.emailRequired')).email(t('login.emailInvalid')),
  });
  type FormValues = z.infer<typeof schema>;

  const form = useForm<FormValues>({ resolver: zodResolver(schema), defaultValues: { email: '' } });
  const mutation = useMutation({
    mutationFn: (values: FormValues) => requestPasswordReset(values.email),
  });

  // The confirmation is deliberately neutral: it never says whether the address had an account,
  // so the page cannot be used to discover which emails are registered.
  if (mutation.isSuccess) {
    return (
      <AuthScreen title={t('forgot.sentTitle')} description={t('forgot.sentBody')}>
        <div className="space-y-5">
          <Alert variant="success" title={t('forgot.checkInbox')}>
            {t('forgot.sentDetail')}
          </Alert>
          <Link
            to="/login"
            className="inline-flex items-center gap-1.5 text-sm font-medium text-primary hover:underline"
          >
            <ArrowLeft className="size-4" aria-hidden="true" />
            {t('forgot.backToSignIn')}
          </Link>
        </div>
      </AuthScreen>
    );
  }

  return (
    <AuthScreen title={t('forgot.title')} description={t('forgot.subtitle')}>
      <form
        noValidate
        className="space-y-5"
        onSubmit={form.handleSubmit((values) => mutation.mutate(values))}
      >
        {mutation.isError && <Alert variant="error">{toMessage(mutation.error)}</Alert>}

        <Field
          label={t('login.email')}
          htmlFor="email"
          required
          error={form.formState.errors.email?.message}
        >
          <Input
            id="email"
            type="email"
            dir="ltr"
            autoComplete="username"
            invalid={Boolean(form.formState.errors.email)}
            {...form.register('email')}
          />
        </Field>

        <Button type="submit" size="lg" className="w-full" disabled={mutation.isPending}>
          {mutation.isPending ? <Spinner /> : <MailCheck className="size-4" aria-hidden="true" />}
          {t('forgot.submit')}
        </Button>

        <Link
          to="/login"
          className="inline-flex items-center gap-1.5 text-sm font-medium text-primary hover:underline"
        >
          <ArrowLeft className="size-4" aria-hidden="true" />
          {t('forgot.backToSignIn')}
        </Link>
      </form>
    </AuthScreen>
  );
}
