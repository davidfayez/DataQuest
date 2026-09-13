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
import { Eye, EyeOff } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { adminSession, Permissions } from '@/features/auth/session';
import {
  useEmailSettings,
  useUpdateEmailSettings,
  type EmailKeySource,
} from '@/features/settings/api';
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
  const describeError = useApiErrorMessage();

  const canUpdate = adminSession.has(Permissions.SettingsUpdate);

  const [apiKey, setApiKey] = useState('');
  const [reveal, setReveal] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const data = settings.data;
  if (!data) return null;

  // Saving is only possible with an encryption key on the server; without one the key would have
  // nowhere safe to live, so the form says so rather than failing on submit.
  const disabled = !canUpdate || !data.canEdit;

  async function submit(value: string | null) {
    setError(null);
    setNotice(null);

    try {
      await update.mutateAsync({ apiKey: value });
      setApiKey('');
      setReveal(false);
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
        {error && <Alert variant="error">{error}</Alert>}
        {notice && <Alert variant="success">{notice}</Alert>}

        <form
          className="space-y-4"
          onSubmit={(event) => {
            event.preventDefault();
            if (apiKey.trim()) void submit(apiKey.trim());
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
                // A secret in a text box: masked by default, and never pre-filled with the stored
                // value — the server does not return it.
                type={reveal ? 'text' : 'password'}
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
                onClick={() => setReveal((current) => !current)}
              >
                {reveal ? <EyeOff className="size-4" /> : <Eye className="size-4" />}
              </Button>
            </div>
          </Field>

          <div className="flex flex-wrap gap-2">
            <Button type="submit" disabled={disabled || !apiKey.trim() || update.isPending}>
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
