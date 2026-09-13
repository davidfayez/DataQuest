import { zodResolver } from '@hookform/resolvers/zod';
import {
  AdminPageHeader,
  Alert,
  Button,
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
  Field,
  Input,
  LoadingState,
  Spinner,
} from '@dv/ui';
import { ApiError } from '@dv/api-client';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Upload } from 'lucide-react';
import { useRef, useState } from 'react';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { Avatar } from '@/features/auth/Avatar';
import { changePassword, uploadAvatar } from '@/features/auth/api';
import { passwordSchema } from '@/features/auth/passwordSchema';
import { adminKeys, apiClient } from '@/shared/api/client';
import { formatDateTime } from '@/shared/lib/format';
import { useApiErrorMessage } from '@/shared/lib/useApiError';
import { useLanguage } from '@/shared/lib/useLanguage';

interface Profile {
  id: string;
  fullName: string;
  email: string;
  languageCode: string;
  hasAvatar: boolean;
  lastLoginAtUtc: string | null;
  roles: string[];
  permissions: string[];
}

const MAX_AVATAR_BYTES = 2 * 1024 * 1024;

export function ProfilePage() {
  const { t } = useTranslation();
  const locale = useLanguage();

  const profile = useQuery({
    queryKey: adminKeys.profile,
    queryFn: () => apiClient.get<Profile>('admin/profile'),
  });

  if (profile.isPending) {
    return <LoadingState label={t('common.loading')} />;
  }

  if (profile.isError || !profile.data) {
    return <Alert variant="error">{t('errors.genericTitle')}</Alert>;
  }

  return (
    <div className="animate-fade-in space-y-6">
      <AdminPageHeader title={t('account.title')} subtitle={t('account.subtitle')} />

      <div className="grid gap-6 lg:grid-cols-[1fr_1.4fr]">
        <div className="space-y-6">
          <AvatarCard profile={profile.data} />
          <DetailsCard profile={profile.data} locale={locale} />
        </div>
        <ChangePasswordCard />
      </div>
    </div>
  );
}

function AvatarCard({ profile }: { profile: Profile }) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const inputRef = useRef<HTMLInputElement>(null);
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: (file: File) => uploadAvatar(file),
    onSuccess: () => {
      setError(null);
      void queryClient.invalidateQueries({ queryKey: adminKeys.profile });
    },
  });

  function onPick(event: React.ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    // Reset the input so choosing the same file again still fires a change.
    event.target.value = '';
    if (!file) return;

    if (!['image/jpeg', 'image/png'].includes(file.type)) {
      setError(t('account.avatarType'));
      return;
    }
    if (file.size > MAX_AVATAR_BYTES) {
      setError(t('account.avatarSize'));
      return;
    }

    setError(null);
    mutation.mutate(file);
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t('account.photoTitle')}</CardTitle>
        <CardDescription>{t('account.photoHint')}</CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="flex items-center gap-4">
          <Avatar size={72} />
          <div className="space-y-2">
            <input
              ref={inputRef}
              type="file"
              accept="image/jpeg,image/png"
              className="hidden"
              data-testid="avatar-input"
              onChange={onPick}
            />
            <Button
              variant="secondary"
              size="sm"
              onClick={() => inputRef.current?.click()}
              disabled={mutation.isPending}
            >
              {mutation.isPending ? (
                <Spinner />
              ) : (
                <Upload className="size-4" aria-hidden="true" />
              )}
              {profile.hasAvatar ? t('account.replacePhoto') : t('account.uploadPhoto')}
            </Button>
            <p className="text-xs text-muted-foreground">{t('account.avatarFormats')}</p>
          </div>
        </div>

        {error && <Alert variant="error">{error}</Alert>}
        {mutation.isSuccess && !error && (
          <Alert variant="success">{t('account.photoUpdated')}</Alert>
        )}
      </CardContent>
    </Card>
  );
}

function DetailsCard({ profile, locale }: { profile: Profile; locale: string }) {
  const { t } = useTranslation();

  const rows: Array<{ label: string; value: string }> = [
    { label: t('account.name'), value: profile.fullName },
    { label: t('account.email'), value: profile.email },
    { label: t('account.roles'), value: profile.roles.join(', ') || '—' },
    {
      label: t('account.lastSignIn'),
      value: profile.lastLoginAtUtc ? formatDateTime(profile.lastLoginAtUtc, locale) : '—',
    },
  ];

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t('account.detailsTitle')}</CardTitle>
      </CardHeader>
      <CardContent>
        <dl className="text-sm">
          {rows.map((row) => (
            <div
              key={row.label}
              className="flex items-baseline justify-between gap-6 border-b border-border py-2.5 last:border-b-0"
            >
              <dt className="shrink-0 text-muted-foreground">{row.label}</dt>
              <dd className="text-end font-medium" dir={row.label === t('account.email') ? 'ltr' : undefined}>
                {row.value}
              </dd>
            </div>
          ))}
        </dl>
      </CardContent>
    </Card>
  );
}

function ChangePasswordCard() {
  const { t } = useTranslation();
  const toMessage = useApiErrorMessage();

  const schema = z
    .object({
      currentPassword: z.string().min(1, t('account.currentRequired')),
      newPassword: passwordSchema(t),
      confirmPassword: z.string().min(1, t('password.confirmRequired')),
    })
    .refine((values) => values.newPassword === values.confirmPassword, {
      path: ['confirmPassword'],
      message: t('password.mismatch'),
    })
    .refine((values) => values.newPassword !== values.currentPassword, {
      path: ['newPassword'],
      message: t('account.mustDiffer'),
    });
  type FormValues = z.infer<typeof schema>;

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { currentPassword: '', newPassword: '', confirmPassword: '' },
  });

  const mutation = useMutation({
    mutationFn: (values: FormValues) => changePassword(values.currentPassword, values.newPassword),
    onSuccess: () => form.reset(),
  });

  // A wrong current password comes back as a 400 field error on CurrentPassword. Show the
  // localized message rather than the server's English text, and never as a 401 (which would sign
  // the admin out mid-form).
  const wrongCurrent =
    mutation.error instanceof ApiError &&
    mutation.error.status === 400 &&
    Boolean(mutation.error.fieldErrors?.CurrentPassword);

  const errorMessage = wrongCurrent
    ? t('account.currentPasswordWrong')
    : mutation.error
      ? toMessage(mutation.error)
      : null;

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t('account.changePasswordTitle')}</CardTitle>
        <CardDescription>{t('account.changePasswordHint')}</CardDescription>
      </CardHeader>
      <CardContent>
        <form
          noValidate
          className="space-y-5"
          onSubmit={form.handleSubmit((values) => mutation.mutate(values))}
        >
          {errorMessage && <Alert variant="error">{errorMessage}</Alert>}
          {mutation.isSuccess && <Alert variant="success">{t('account.passwordChanged')}</Alert>}

          <Field
            label={t('account.currentPassword')}
            htmlFor="currentPassword"
            required
            error={form.formState.errors.currentPassword?.message}
          >
            <Input
              id="currentPassword"
              type="password"
              dir="ltr"
              autoComplete="current-password"
              invalid={Boolean(form.formState.errors.currentPassword)}
              {...form.register('currentPassword')}
            />
          </Field>

          <Field
            label={t('account.newPassword')}
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
            label={t('account.confirmPassword')}
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

          <Button type="submit" disabled={mutation.isPending}>
            {mutation.isPending && <Spinner />}
            {t('account.changePasswordSubmit')}
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}
