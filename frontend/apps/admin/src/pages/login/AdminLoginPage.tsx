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
import { ShieldCheck } from 'lucide-react';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { z } from 'zod';
import { loginAdmin } from '@/features/auth/api';
import { LanguageSwitcher } from '@/features/language/LanguageSwitcher';
import { useApiErrorMessage } from '@/shared/lib/useApiError';

export function AdminLoginPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const toMessage = useApiErrorMessage();
  const [params] = useSearchParams();
  const justReset = params.get('reset') === '1';

  // Either handle is accepted, so this cannot be validated as an email address.
  const schema = z.object({
    identifier: z.string().min(1, t('login.identifierRequired')),
    password: z.string().min(1, t('login.passwordRequired')),
  });

  type FormValues = z.infer<typeof schema>;

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { identifier: '', password: '' },
  });

  const mutation = useMutation({
    mutationFn: (values: FormValues) => loginAdmin(values.identifier, values.password),
    onSuccess: () => navigate('/', { replace: true }),
  });

  return (
    <div className="flex min-h-screen flex-col">
      <header className="flex justify-end p-4">
        <LanguageSwitcher />
      </header>

      <main className="flex flex-1 items-start justify-center px-4 pb-16">
        <Card className="w-full max-w-md">
          <CardHeader>
            <div className="mb-2 flex items-center gap-2 text-primary">
              <ShieldCheck className="size-5" aria-hidden="true" />
              <span className="text-sm font-medium">{t('common.adminPanel')}</span>
            </div>
            <CardTitle>{t('login.title')}</CardTitle>
            <CardDescription>{t('login.subtitle')}</CardDescription>
          </CardHeader>

          <CardContent>
            <form
              noValidate
              className="space-y-5"
              onSubmit={form.handleSubmit((values) => mutation.mutate(values))}
            >
              {justReset && !mutation.isError && (
                <Alert variant="success">{t('reset.done')}</Alert>
              )}
              {mutation.isError && <Alert variant="error">{toMessage(mutation.error)}</Alert>}

              <Field
                label={t('login.identifier')}
                htmlFor="identifier"
                required
                error={form.formState.errors.identifier?.message}
              >
                <Input
                  id="identifier"
                  type="text"
                  dir="ltr"
                  autoComplete="username"
                  invalid={Boolean(form.formState.errors.identifier)}
                  {...form.register('identifier')}
                />
              </Field>

              <Field
                label={t('login.password')}
                htmlFor="password"
                required
                error={form.formState.errors.password?.message}
              >
                <Input
                  id="password"
                  type="password"
                  dir="ltr"
                  autoComplete="current-password"
                  invalid={Boolean(form.formState.errors.password)}
                  {...form.register('password')}
                />
              </Field>

              <Button type="submit" size="lg" className="w-full" disabled={mutation.isPending}>
                {mutation.isPending && <Spinner />}
                {t('login.submit')}
              </Button>

              <div className="text-center">
                <Link
                  to="/forgot-password"
                  className="text-sm font-medium text-primary hover:underline"
                >
                  {t('login.forgotPassword')}
                </Link>
              </div>
            </form>
          </CardContent>
        </Card>
      </main>
    </div>
  );
}
