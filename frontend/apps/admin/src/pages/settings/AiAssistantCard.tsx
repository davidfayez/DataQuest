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
  Select,
  Spinner,
} from '@dv/ui';
import { Eye, EyeOff } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { adminSession, Permissions } from '@/features/auth/session';
import { useAiSettings, useSaveAiSettings } from '@/features/settings/api';
import { useApiErrorMessage } from '@/shared/lib/useApiError';

/**
 * The assistant behind "AI Mode" on the tools and guides page.
 *
 * The key is write-only: the API never sends it back, so the field is always empty on load and a
 * blank one means "keep the stored key". That is what lets the switch or the model be changed
 * without pasting the key again, and it means a screenshot of this page gives nothing away.
 */
export function AiAssistantCard() {
  const { t } = useTranslation();
  const settings = useAiSettings();
  const save = useSaveAiSettings();
  const toMessage = useApiErrorMessage();

  const canUpdate = adminSession.has(Permissions.SettingsUpdate);

  const [enabled, setEnabled] = useState(false);
  const [model, setModel] = useState('gemini-3.6-flash');
  const [apiKey, setApiKey] = useState('');
  const [revealed, setRevealed] = useState(false);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    if (!settings.data) return;

    setEnabled(settings.data.isEnabled);
    setModel(settings.data.model);
  }, [settings.data]);

  if (settings.isPending) return null;

  if (settings.isError || !settings.data) {
    return <Alert variant="error">{t('errors.genericTitle')}</Alert>;
  }

  const stored = settings.data.hasApiKey;
  const canStore = settings.data.canStore;

  // Turning it on needs a key from somewhere: this form, or one saved earlier.
  const canEnable = stored || apiKey.trim().length > 0;

  function submit(clearApiKey = false) {
    setSaved(false);

    save.mutate(
      {
        isEnabled: clearApiKey ? false : enabled && canEnable,
        model: model.trim(),
        apiKey: apiKey.trim() || undefined,
        clearApiKey,
      },
      {
        onSuccess: () => {
          setApiKey('');
          setRevealed(false);
          setSaved(true);
        },
      },
    );
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t('settings.ai.title')}</CardTitle>
        <CardDescription>{t('settings.ai.hint')}</CardDescription>
      </CardHeader>

      <CardContent className="space-y-4">
        <Alert variant={stored ? 'success' : 'warning'} data-testid="ai-status">
          {stored ? t('settings.ai.keyStored') : t('settings.ai.keyMissing')}
        </Alert>

        {/* Without the platform encryption key there is nowhere safe to put a provider key, so
            saying so here beats letting an operator paste one into a form that will refuse it. */}
        {!canStore && <Alert variant="error">{t('settings.ai.cannotStore')}</Alert>}

        <Field label={t('settings.ai.apiKey')} htmlFor="ai-key" hint={t('settings.ai.apiKeyHint')}>
          <div className="flex gap-2">
            <Input
              id="ai-key"
              type={revealed ? 'text' : 'password'}
              dir="ltr"
              autoComplete="off"
              maxLength={200}
              value={apiKey}
              onChange={(event) => setApiKey(event.target.value)}
              placeholder={stored ? t('settings.ai.keyPlaceholderStored') : 'AIza…'}
              disabled={!canUpdate || !canStore}
              data-testid="ai-key"
            />
            <Button
              type="button"
              variant="outline"
              onClick={() => setRevealed((current) => !current)}
              aria-label={t(revealed ? 'settings.ai.hideKey' : 'settings.ai.showKey')}
            >
              {revealed ? <EyeOff className="size-4" /> : <Eye className="size-4" />}
            </Button>
          </div>
        </Field>

        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t('settings.ai.model')} htmlFor="ai-model">
            <Input
              id="ai-model"
              dir="ltr"
              maxLength={100}
              value={model}
              onChange={(event) => setModel(event.target.value)}
              disabled={!canUpdate}
              data-testid="ai-model"
            />
          </Field>

          <Field label={t('settings.ai.status')} htmlFor="ai-enabled">
            <Select
              id="ai-enabled"
              value={enabled ? '1' : '0'}
              onChange={(event) => setEnabled(event.target.value === '1')}
              disabled={!canUpdate}
              data-testid="ai-enabled"
            >
              <option value="1">{t('settings.ai.on')}</option>
              <option value="0">{t('settings.ai.off')}</option>
            </Select>
          </Field>
        </div>

        {enabled && !canEnable && <Alert variant="warning">{t('settings.ai.needsKey')}</Alert>}

        {save.isError && <Alert variant="error">{toMessage(save.error)}</Alert>}
        {saved && <Alert variant="success">{t('settings.ai.saved')}</Alert>}

        {canUpdate && (
          <div className="flex justify-end gap-3">
            {/* A stored secret you cannot take back out is worse than one you can. Removing it
                switches the assistant off in the same save, so the site never offers a button
                that has no key behind it. */}
            {stored && (
              <Button
                variant="outline"
                onClick={() => submit(true)}
                disabled={save.isPending}
                data-testid="ai-clear"
              >
                {t('settings.ai.removeKey')}
              </Button>
            )}
            <Button
              onClick={() => submit()}
              disabled={save.isPending || !model.trim() || (enabled && !canEnable)}
              data-testid="ai-save"
            >
              {save.isPending && <Spinner />}
              {t('common.save')}
            </Button>
          </div>
        )}
      </CardContent>
    </Card>
  );
}
