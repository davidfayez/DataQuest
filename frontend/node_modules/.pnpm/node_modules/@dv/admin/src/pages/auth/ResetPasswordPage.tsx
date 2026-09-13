import { zodResolver } from '@hookform/resolvers/zod';
import { Alert, Button, Field, Input, Spinner } from '@dv/ui';
import { useMutation } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { z } from 'zod';
import { resetPassword } from '@/features/auth/api';
import { passwordSchema } from '@/features/auth/passwordSchema';
import { useApiErrorMessage } from '@/shared/lib/useApiError';
import { AuthScreen } from './AuthScreen';

export function ResetPasswordPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const toMessage = useApiErrorMessage();
  const [params] = useSearchParams();
  const token = params.get('token') ?? '';

  const schema = z
    .object({
      newPassword: passwordSchema(t),
      confirmPassword: z.string().min(1, t('password.confirmRequired')),
    })
    .refine((values) => values.newPassword === values.confirmPassword, {
      path: ['confirmPassword'],
      message: t('password.mismatch'),
    });
  type FormValues = z.infer<typeof schema>;

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { newPassword: '', confirmPassword: '' },
  });

  const mutation = useMutation({
    mutationFn: (values: FormValues) => resetPassword(token, values.newPassword),
    // Sign-in carries a flag so the sign-in screen can confirm the change.
    onSuccess: () => navigate('/login?reset=1', { replace: true }),
  });

  // A link with no token is malformed — most likely opened by hand. Say so plainly.
  if (!token) {
    return (
      <AuthScreen title={t('reset.title')} description={t('reset.subtitle')}>
        <div className="space-y-5">
          <Alert variant="error">{t('reset.missingToken')}</Alert>
          <Link to="/forgot-password" className="text-sm font-medium text-primary hover:underline">
            {t('reset.requestNew')}
          </Link>
        </div>
      </AuthScreen>
    );
  }

  return (
    <AuthScreen title={t('reset.title')} description={t('reset.subtitle')}>
      <form
        noValidate
        className="space-y-5"
        onSubmit={form.handleSubmit((values) => mutation.mutate(values))}
      >
        {mutation.isError && <Alert variant="error">{toMessage(mutation.error)}</Alert>}

        <Field
          label={t('reset.newPassword')}
          htmlFor="newPassword"
          required
          hint={t('password.hint')}
          error={form.formState.errors.newPassword?.message}
        >
          <Input
            id="newPassword"
            type="password"
            dir="ltr"
            autoComplete="new-password"
            invalid={Boolean(form.formState.errors.newPassword)}
            {...form.register('newPassword')}
          />
        </Field>

        <Field
          label={t('reset.confirmPassword')}
          htmlFor="confirmPassword"
          required
          error={form.formState.errors.confirmPassword?.message}
        >
          <Input
            id="confirmPassword"
            type="password"
            dir="ltr"
            autoComplete="new-password"
            invalid={Boolean(form.formState.errors.confirmPassword)}
            {...form.register('confirmPassword')}
          />
        </Field>

        <Button type="submit" size="lg" className="w-full" disabled={mutation.isPending}>
          {mutation.isPending && <Spinner />}
          {t('reset.submit')}
        </Button>
      </form>
    </AuthScreen>
  );
}
