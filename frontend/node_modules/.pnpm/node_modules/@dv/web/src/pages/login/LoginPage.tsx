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
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { z } from 'zod';
import { loginOrder } from '@/features/auth/api';
import { sessionStore } from '@/features/auth/session';
import { useApiErrorMessage } from '@/shared/lib/useApiError';

export function LoginPage() {
  const { t } = useTranslation();
  const { lang = 'en' } = useParams<{ lang: string }>();
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const toMessage = useApiErrorMessage();

  // The credentials email links here with ?order=..., so the field arrives prefilled. Failing
  // that, the order number from a previous visit on this device is offered.
  const prefilledOrderNumber =
    searchParams.get('order') ?? sessionStore.getRememberedOrderNumber() ?? '';

  // Set by the "Check your inbox" screen. Someone who has just registered does not need to be
  // invited to register, so the prompt is dropped for them and kept for every other arrival.
  const cameFromRegister = searchParams.get('from') === 'register';

  const schema = z.object({
    // The length varies with the client code that prefixes the number, so this is a sanity
    // bound rather than an exact match — the server is what actually resolves the number.
    orderNumber: z
      .string()
      .min(1, t('validation.orderNumberRequired'))
      .min(10, t('validation.orderNumberLength'))
      .max(29, t('validation.orderNumberLength')),
    password: z.string().min(1, t('validation.passwordRequired')),
  });

  type FormValues = z.infer<typeof schema>;

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { orderNumber: prefilledOrderNumber, password: '' },
  });

  const mutation = useMutation({
    mutationFn: (values: FormValues) => loginOrder(values.orderNumber, values.password),
    onSuccess: (session) => {
      navigate(session.isSetupComplete ? `/${lang}/applications` : `/${lang}/setup`, {
        replace: true,
      });
    },
  });

  return (
    <div className="mx-auto max-w-lg px-4 py-16 sm:px-6">
      <Card>
        <CardHeader>
          <CardTitle>{t('login.title')}</CardTitle>
          <CardDescription>{t('login.subtitle')}</CardDescription>
        </CardHeader>

        <CardContent>
          <form
            noValidate
            className="space-y-5"
            onSubmit={form.handleSubmit((values) => mutation.mutate(values))}
          >
            {mutation.isError && (
              <Alert variant="error">{toMessage(mutation.error)}</Alert>
            )}

            <Field
              label={t('login.orderNumberLabel')}
              htmlFor="orderNumber"
              required
              error={form.formState.errors.orderNumber?.message}
            >
              <Input
                id="orderNumber"
                autoComplete="username"
                // The code is Latin and case-insensitive on the server; forcing LTR and upper case
                // keeps it legible even when the surrounding page is right-to-left.
                dir="ltr"
                autoCapitalize="characters"
                spellCheck={false}
                maxLength={12}
                placeholder={t('login.orderNumberPlaceholder')}
                className="font-mono tracking-widest uppercase"
                invalid={Boolean(form.formState.errors.orderNumber)}
                {...form.register('orderNumber')}
              />
            </Field>

            <Field
              label={t('login.passwordLabel')}
              htmlFor="password"
              required
              error={form.formState.errors.password?.message}
            >
              <Input
                id="password"
                type="password"
                autoComplete="current-password"
                dir="ltr"
                className="font-mono tracking-widest"
                invalid={Boolean(form.formState.errors.password)}
                {...form.register('password')}
              />
            </Field>

            <Button type="submit" size="lg" className="w-full" disabled={mutation.isPending}>
              {mutation.isPending && <Spinner />}
              {t('login.submit')}
            </Button>

            {/* Carries whatever order number has been typed, so it is not asked for twice. */}
            <p className="text-center text-sm">
              <Link
                to={`/${lang}/forgot-password${
                  form.watch('orderNumber')
                    ? `?order=${encodeURIComponent(form.watch('orderNumber').trim().toUpperCase())}`
                    : ''
                }`}
                className="font-medium text-primary hover:underline"
                data-testid="forgot-password-link"
              >
                {t('login.forgotPassword')}
              </Link>
            </p>

            {!cameFromRegister && (
              <p className="text-center text-sm text-muted-foreground">
                {t('login.noOrder')}{' '}
                <Link to={`/${lang}/register`} className="font-medium text-primary hover:underline">
                  {t('login.createOne')}
                </Link>
              </p>
            )}
          </form>
        </CardContent>
      </Card>
    </div>
  );
}
