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
  buttonVariants,
} from '@dv/ui';
import { useMutation } from '@tanstack/react-query';
import { MailCheck } from 'lucide-react';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { z } from 'zod';
import { forgotOrderPassword } from '@/features/auth/api';
import { useApiErrorMessage } from '@/shared/lib/useApiError';

/**
 * "I have my order number but not my password."
 *
 * The order number is the only thing an applicant reliably keeps — they never chose a password, so
 * there is nothing for a reset link to let them set. A fresh one is generated and emailed instead,
 * and the screen names the mailbox it went to, masked: enough to recognise which of your addresses
 * to open, useless to anyone who is not you.
 */
export function ForgotPasswordPage() {
  const { t } = useTranslation();
  const { lang = 'en' } = useParams<{ lang: string }>();
  const [searchParams] = useSearchParams();
  const toMessage = useApiErrorMessage();

  // Arriving from the login screen carries the number already typed there.
  const prefilled = searchParams.get('order') ?? '';

  const [sentTo, setSentTo] = useState<{
    orderNumber: string;
    maskedEmail: string;
    validityMinutes: number;
  } | null>(null);

  const schema = z.object({
    orderNumber: z
      .string()
      .min(1, t('validation.orderNumberRequired'))
      .min(10, t('validation.orderNumberLength')),
  });

  const form = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: { orderNumber: prefilled },
  });

  const mutation = useMutation({
    mutationFn: (values: z.infer<typeof schema>) => forgotOrderPassword(values.orderNumber),
    onSuccess: (result, values) => {
      if (result.maskedEmail) {
        setSentTo({
          orderNumber: values.orderNumber.trim().toUpperCase(),
          maskedEmail: result.maskedEmail,
          validityMinutes: result.validityMinutes,
        });
      }
    },
  });

  // No mask came back, so no such order — said without confirming which numbers exist.
  const notFound = mutation.isSuccess && !mutation.data?.maskedEmail;

  if (sentTo) {
    return (
      <div className="mx-auto max-w-lg px-4 py-16 sm:px-6">
        <Card>
          <CardHeader>
            <div className="mb-2 flex size-11 items-center justify-center rounded-full bg-success-muted">
              <MailCheck className="size-5 text-success" aria-hidden="true" />
            </div>
            <CardTitle>{t('forgot.sentTitle')}</CardTitle>
            <CardDescription>{t('forgot.sentSubtitle')}</CardDescription>
          </CardHeader>

          <CardContent className="space-y-5">
            <div className="rounded-xl border border-border bg-muted/50 p-4" data-testid="sent-to">
              <p className="text-xs text-muted-foreground">{t('forgot.sentToLabel')}</p>
              {/* Latin and masked, so it stays readable in a right-to-left page. */}
              <p className="mt-1 font-mono text-base font-medium" dir="ltr">
                {sentTo.maskedEmail}
              </p>

              <p className="mt-3 text-xs text-muted-foreground">{t('login.orderNumberLabel')}</p>
              <p className="mt-1 font-mono tracking-widest" dir="ltr">
                {sentTo.orderNumber}
              </p>
            </div>

            <Alert variant="info">
              {t('forgot.validityNote', { count: sentTo.validityMinutes })}
            </Alert>

            <Link
              to={`/${lang}/login?order=${encodeURIComponent(sentTo.orderNumber)}`}
              className={buttonVariants({ size: 'lg', className: 'w-full' })}
              data-testid="forgot-go-to-login"
            >
              {t('forgot.goToLogin')}
            </Link>

            <p className="text-center text-sm text-muted-foreground">
              {t('forgot.noEmail')}{' '}
              <button
                type="button"
                className="font-medium text-primary hover:underline"
                onClick={() => {
                  setSentTo(null);
                  mutation.reset();
                }}
              >
                {t('forgot.tryAgain')}
              </button>
            </p>
          </CardContent>
        </Card>
      </div>
    );
  }

  return (
    <div className="mx-auto max-w-lg px-4 py-16 sm:px-6">
      <Card>
        <CardHeader>
          <CardTitle>{t('forgot.title')}</CardTitle>
          <CardDescription>{t('forgot.subtitle')}</CardDescription>
        </CardHeader>

        <CardContent>
          <form
            noValidate
            className="space-y-5"
            onSubmit={form.handleSubmit((values) => mutation.mutate(values))}
          >
            {mutation.isError && <Alert variant="error">{toMessage(mutation.error)}</Alert>}

            {notFound && (
              <Alert variant="error" data-testid="forgot-not-found">
                {t('forgot.notFound')}
              </Alert>
            )}

            <Field
              label={t('login.orderNumberLabel')}
              htmlFor="orderNumber"
              required
              hint={t('forgot.orderNumberHint')}
              error={form.formState.errors.orderNumber?.message}
            >
              <Input
                id="orderNumber"
                autoComplete="username"
                dir="ltr"
                autoCapitalize="characters"
                spellCheck={false}
                maxLength={12}
                placeholder={t('login.orderNumberPlaceholder')}
                className="font-mono tracking-widest uppercase"
                invalid={Boolean(form.formState.errors.orderNumber)}
                data-testid="forgot-order-number"
                {...form.register('orderNumber')}
              />
            </Field>

            <Button
              type="submit"
              size="lg"
              className="w-full"
              disabled={mutation.isPending}
              data-testid="forgot-submit"
            >
              {mutation.isPending && <Spinner />}
              {t('forgot.submit')}
            </Button>

            <p className="text-center text-sm text-muted-foreground">
              <Link
                to={`/${lang}/login`}
                className="font-medium text-primary hover:underline"
              >
                {t('forgot.backToLogin')}
              </Link>
            </p>
          </form>
        </CardContent>
      </Card>
    </div>
  );
}
