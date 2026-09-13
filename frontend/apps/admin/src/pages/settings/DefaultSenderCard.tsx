import { AdminPanel, Alert, Button, Field, Input, LoadingState } from '@dv/ui';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { adminSession, Permissions } from '@/features/auth/session';
import { useEmailDefaultSender, useSaveEmailDefaultSender } from '@/features/settings/api';
import { useApiErrorMessage } from '@/shared/lib/useApiError';

/**
 * The sender every kind of email falls back to.
 *
 * Above the per-kind panels because it is the one that decides what most email goes out as: a
 * platform sends far more kinds than anyone configures individually, and until now the fallback
 * lived in the server's configuration file — visible on this page, but only changeable by whoever
 * could redeploy.
 *
 * Clearing the address restores the configured one rather than leaving the platform with no sender,
 * so there is no way to switch email off from here by accident.
 */
export function DefaultSenderCard() {
  const { t } = useTranslation();
  const query = useEmailDefaultSender();
  const save = useSaveEmailDefaultSender();
  const toMessage = useApiErrorMessage();

  const canUpdate = adminSession.has(Permissions.SettingsUpdate);

  const [address, setAddress] = useState('');
  const [name, setName] = useState('');
  const [saved, setSaved] = useState(false);

  // Only the stored value is loaded into the fields. Showing the configured fallback there would
  // make "inherited" and "overridden" look identical, and saving would then pin the inherited
  // address into the database the first time anyone touched anything else on the form.
  useEffect(() => {
    if (!query.data) return;

    const stored = query.data.source === 'Database';
    setAddress(stored ? query.data.fromAddress : '');
    setName(stored ? (query.data.fromName ?? '') : '');
  }, [query.data]);

  if (query.isPending) {
    return <LoadingState label={t('common.loading')} />;
  }

  if (query.isError || !query.data) {
    return <Alert variant="error">{toMessage(query.error)}</Alert>;
  }

  const data = query.data;
  const usingConfigured = data.source !== 'Database';

  // The server refuses a name with no address; the form holds the same line.
  const canSubmit = canUpdate && !(name.trim() !== '' && address.trim() === '');

  function submit() {
    setSaved(false);

    save.mutate(
      {
        fromAddress: address.trim() === '' ? null : address.trim(),
        fromName: name.trim() === '' ? null : name.trim(),
      },
      { onSuccess: () => setSaved(true) },
    );
  }

  return (
    <AdminPanel
      title={t('settings.defaultSender.title')}
      subtitle={t('settings.defaultSender.description')}
    >
      <div className="space-y-4" data-testid="default-sender-panel">
        {/* Which of the two is actually in force. Without this the field reads the same whether an
            operator set the address or merely inherited it. */}
        {usingConfigured ? (
          <Alert variant="info" data-testid="default-sender-inherited">
            {t('settings.defaultSender.usingConfigured', { address: data.configuredFromAddress })}
          </Alert>
        ) : (
          <Alert variant="success" data-testid="default-sender-overridden">
            {t('settings.defaultSender.usingStored', { address: data.fromAddress })}
          </Alert>
        )}

        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t('settings.defaultSender.address')} htmlFor="default-from-address">
            <Input
              id="default-from-address"
              type="email"
              dir="ltr"
              maxLength={256}
              disabled={!canUpdate}
              placeholder={data.configuredFromAddress}
              value={address}
              onChange={(event) => {
                setAddress(event.target.value);
                setSaved(false);
              }}
              data-testid="default-from-address"
            />
          </Field>

          <Field label={t('settings.defaultSender.name')} htmlFor="default-from-name">
            <Input
              id="default-from-name"
              maxLength={200}
              disabled={!canUpdate}
              placeholder={data.configuredFromName ?? ''}
              value={name}
              onChange={(event) => {
                setName(event.target.value);
                setSaved(false);
              }}
              data-testid="default-from-name"
            />
          </Field>
        </div>

        <p className="text-xs text-subtle">
          {t('settings.defaultSender.clearHint', { address: data.configuredFromAddress })}
        </p>

        {save.isError && <Alert variant="error">{toMessage(save.error)}</Alert>}
        {saved && !save.isPending && (
          <Alert variant="success" data-testid="default-sender-saved">
            {t('settings.defaultSender.saved')}
          </Alert>
        )}

        {canUpdate && (
          <div className="flex justify-end">
            <Button
              onClick={submit}
              disabled={!canSubmit || save.isPending}
              data-testid="save-default-sender"
            >
              {t('common.save')}
            </Button>
          </div>
        )}
      </div>
    </AdminPanel>
  );
}
