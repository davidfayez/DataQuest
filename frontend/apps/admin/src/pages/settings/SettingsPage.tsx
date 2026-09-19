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
import { BrandingCard } from './BrandingCard';
import { AiAssistantCard } from './AiAssistantCard';
import { useQueryClient } from '@tanstack/react-query';
import { Eye, EyeOff } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { adminSession, Permissions } from '@/features/auth/session';
import {
  useEmailSettings,
  useUpdateEmailSettings,
  type EmailKeySource,
} from '@/features/settings/api';
import { storedEmailApiKeyKey, useStoredEmailApiKey } from '@/features/settings/storedApiKeys';
import { useApiErrorMessage } from '@/shared/lib/useApiError';

/** Maps the server's source to the tone the status line is shown in. */
const SOURCE_VARIANT: Record<EmailKeySource, 'success' | 'warning' | 'error'> = {
  Database: 'success',
  Configuration: 'warning',
  Unreadable: 'error',
  None: 'error',
};

export function SettingsPage() {
  const { t } = useTranslation();
  const settings = useEmailSettings();

  if (settings.isPending) {
    return <LoadingState label={t('common.loading')} />;
  }

  if (settings.isError || !settings.data) {
    return <Alert variant="error">{t('errors.genericTitle')}</Alert>;
  }

  return (
    <div className="animate-fade-in space-y-6">
      <AdminPageHeader title={t('settings.title')} subtitle={t('settings.subtitle')} />
      <div className="max-w-2xl space-y-6">
        <BrandingCard />
        <EmailCard />
        <AiAssistantCard />
      </div>
    </div>
  );
}

function EmailCard() {
  const { t } = useTranslation();
  const settings = useEmailSettings();
  const update = useUpdateEmailSettings();
  const queryClient = useQueryClient();
  const describeError = useApiErrorMessage();

  const canUpdate = adminSession.has(Permissions.SettingsUpdate);
  const data = settings.data;

  // Only a key saved from this page can be shown; one from server configuration stays there.
  const hasSavedKey = data?.source === 'Database';
  const savedKeyQuery = useStoredEmailApiKey(Boolean(canUpdate && data?.canEdit && hasSavedKey));
  const savedKey = hasSavedKey ? (savedKeyQuery.data ?? '') : '';

  const [apiKey, setApiKey] = useState('');
  const [reveal, setReveal] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  // The field holds the saved key, masked, so the eye button shows what is stored.
  useEffect(() => {
    setApiKey(savedKey);
  }, [savedKey]);

  if (!data) return null;

  // Saving is only possible with an encryption key on the server; without one the key would have
  // nowhere safe to live, so the form says so rather than failing on submit.
  const disabled = !canUpdate || !data.canEdit;
  const unchanged = apiKey.trim() === savedKey;

  async function submit(value: string | null) {
    setError(null);
    setNotice(null);

    try {
      await update.mutateAsync({ apiKey: value });
      setReveal(false);
      if (!value) setApiKey('');
      await queryClient.invalidateQueries({ queryKey: storedEmailApiKeyKey });
      setNotice(value ? t('settings.email.saved') : t('settings.email.cleared'));
    } catch (caught) {
      setError(describeError(caught));
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t('settings.email.title')}</CardTitle>
        <CardDescription>{t('settings.email.description')}</CardDescription>
      </CardHeader>

      <CardContent className="space-y-4">
        <Alert variant={SOURCE_VARIANT[data.source]}>
          {t(`settings.email.source.${data.source}`)}
          {data.maskedApiKey && (
            <span className="ms-1 font-mono text-xs">({data.maskedApiKey})</span>
          )}
        </Alert>

        {!data.canEdit && <Alert variant="warning">{t('settings.email.noEncryptionKey')}</Alert>}
        {!canUpdate && <Alert variant="info">{t('settings.email.readOnly')}</Alert>}
        {savedKeyQuery.isError && (
          <Alert variant="error">{describeError(savedKeyQuery.error)}</Alert>
        )}
        {error && <Alert variant="error">{error}</Alert>}
        {notice && <Alert variant="success">{notice}</Alert>}

        <form
          className="space-y-4"
          onSubmit={(event) => {
            event.preventDefault();
            if (apiKey.trim() && !unchanged) void submit(apiKey.trim());
          }}
        >
          <Field
            label={t('settings.email.apiKey')}
            htmlFor="sendgrid-api-key"
            hint={t('settings.email.apiKeyHint')}
          >
            <div className="flex gap-2">
              <Input
                id="sendgrid-api-key"
                // A secret in a text box: masked until the eye button is pressed.
                type={reveal ? 'text' : 'password'}
                dir="ltr"
                value={apiKey}
                autoComplete="off"
                spellCheck={false}
                disabled={disabled}
                placeholder={t('settings.email.apiKeyPlaceholder')}
                onChange={(event) => setApiKey(event.target.value)}
              />
              <Button
                type="button"
                variant="ghost"
                aria-label={t(reveal ? 'settings.email.hide' : 'settings.email.show')}
                aria-pressed={reveal}
                onClick={() => setReveal((current) => !current)}
              >
                {reveal ? <EyeOff className="size-4" /> : <Eye className="size-4" />}
              </Button>
            </div>
          </Field>

          <div className="flex flex-wrap gap-2">
            <Button
              type="submit"
              disabled={disabled || !apiKey.trim() || unchanged || update.isPending}
            >
              {update.isPending && <Spinner className="size-4" />}
              {t('common.save')}
            </Button>

            {/* Only meaningful once something is stored here; clearing falls back to config. */}
            {data.source === 'Database' || data.source === 'Unreadable' ? (
              <Button
                type="button"
                variant="secondary"
                disabled={disabled || update.isPending}
                onClick={() => void submit(null)}
              >
                {t('settings.email.clear')}
              </Button>
            ) : null}
          </div>
        </form>
      </CardContent>
    </Card>
  );
}
