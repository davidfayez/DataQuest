import { AdminPanel, Alert, Button, Field, Input, LoadingState } from '@dv/ui';
import { Plus, Send, Trash2 } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { adminSession, Permissions } from '@/features/auth/session';
import {
  useEmailDeliveryStatus,
  useEmailRouting,
  useSaveEmailRouting,
  useSendTestEmail,
  type EmailBccRecipientInput,
  type EmailRoutingDto,
} from '@/features/settings/api';
import { useApiErrorMessage } from '@/shared/lib/useApiError';

/** What the editor holds for one kind of email while it is being changed. */
interface RoutingForm {
  fromAddress: string;
  fromName: string;
  bcc: EmailBccRecipientInput[];
}

const MAX_BCC = 20;

/**
 * Who each kind of outgoing email comes from, and who is blind-copied on it.
 *
 * One panel per kind rather than a single platform-wide sender: the mailbox that should answer a
 * contact enquiry is rarely the one that should appear on a credentials email, and the people who
 * need to watch registrations are rarely those who watch password resets.
 */
export function EmailRoutingCard() {
  const { t } = useTranslation();
  const routing = useEmailRouting();
  const toMessage = useApiErrorMessage();

  if (routing.isPending) {
    return <LoadingState label={t('common.loading')} />;
  }

  if (routing.isError || !routing.data) {
    return <Alert variant="error">{toMessage(routing.error)}</Alert>;
  }

  return (
    <div className="space-y-6">
      {/* Whether mail can be delivered at all applies to every panel below, so it spans them. */}
      <DeliveryBanner />

      {/* Two per row: each panel is a short form, and stacked full-width they made a page of
          mostly empty space that had to be scrolled to compare one sender against another —
          which is the main thing anyone does here. Aligned to the top rather than stretched, so
          a panel with several BCC recipients does not leave a void inside its neighbour. */}
      <div className="grid items-start gap-6 lg:grid-cols-2">
        {routing.data.map((entry) => (
          <RoutingPanel key={entry.typeName} entry={entry} />
        ))}
      </div>
    </div>
  );
}

/**
 * Whether email can be delivered at all.
 *
 * The first thing to say on this page, because every panel below it is meaningless if the answer
 * is no — and the usual cause, a missing provider key, otherwise fails silently and is
 * indistinguishable from a broken feature.
 */
function DeliveryBanner() {
  const { t } = useTranslation();
  const status = useEmailDeliveryStatus();

  if (status.isPending || !status.data) {
    return null;
  }

  const { transport, defaultFromAddress, canSend, reason } = status.data;

  if (!canSend) {
    return (
      <Alert variant="error" data-testid="delivery-blocked">
        {t('settings.routing.cannotSend', { transport })}
      </Alert>
    );
  }

  if (reason === 'LogOnly') {
    return (
      <Alert variant="warning" data-testid="delivery-log-only">
        {t('settings.routing.logOnly')}
      </Alert>
    );
  }

  return (
    <Alert variant="info" data-testid="delivery-ready">
      {t('settings.routing.deliveryReady', { transport, address: defaultFromAddress })}
    </Alert>
  );
}

function RoutingPanel({ entry }: { entry: EmailRoutingDto }) {
  const { t } = useTranslation();
  const save = useSaveEmailRouting();
  const test = useSendTestEmail();
  const toMessage = useApiErrorMessage();

  const canUpdate = adminSession.has(Permissions.SettingsUpdate);

  const [form, setForm] = useState<RoutingForm>({
    fromAddress: entry.fromAddress ?? '',
    fromName: entry.fromName ?? '',
    bcc: entry.bcc.map((recipient) => ({
      id: recipient.id,
      email: recipient.email,
      displayName: recipient.displayName,
    })),
  });
  const [notice, setNotice] = useState<string | null>(null);
  const [testTo, setTestTo] = useState('');

  // A save refetches the list, so the panel takes the server's version as the truth afterwards.
  useEffect(() => {
    setForm({
      fromAddress: entry.fromAddress ?? '',
      fromName: entry.fromName ?? '',
      bcc: entry.bcc.map((recipient) => ({
        id: recipient.id,
        email: recipient.email,
        displayName: recipient.displayName,
      })),
    });
  }, [entry]);

  function submit() {
    setNotice(null);

    save.mutate(
      {
        type: entry.type,
        fromAddress: form.fromAddress.trim() || null,
        fromName: form.fromName.trim() || null,
        bcc: form.bcc.filter((recipient) => recipient.email.trim() !== ''),
      },
      { onSuccess: () => setNotice(t('settings.routing.saved')) },
    );
  }

  return (
    <AdminPanel
      title={t(`settings.routing.type.${entry.typeName}`)}
      subtitle={t(`settings.routing.typeHint.${entry.typeName}`)}
    >
      <div className="space-y-5">
        {/* A kind nothing sends yet is worth saying out loud, rather than letting someone
            configure it and wonder why no mail ever arrives. */}
        {!entry.isWired && <Alert variant="warning">{t('settings.routing.notWired')}</Alert>}

        {notice && <Alert variant="success">{notice}</Alert>}
        {save.isError && <Alert variant="error">{toMessage(save.error)}</Alert>}

        {/* What will really happen, rather than leaving an empty field to be interpreted. An
            operator seeing "using the platform default" knows the fallback is working; an empty
            box tells them nothing. */}
        <p className="rounded-lg bg-ink-50 px-3 py-2 text-xs text-ink-600">
          {entry.usesDefaultSender
            ? t('settings.routing.usingDefault', { address: entry.effectiveFromAddress })
            : t('settings.routing.usingOverride', { address: entry.effectiveFromAddress })}
        </p>

        <div className="grid gap-4 xl:grid-cols-2">
          <Field
            label={t('settings.routing.fromAddress')}
            htmlFor={`from-address-${entry.typeName}`}
            hint={t('settings.routing.fromAddressHint')}
          >
            <Input
              id={`from-address-${entry.typeName}`}
              type="email"
              dir="ltr"
              disabled={!canUpdate}
              value={form.fromAddress}
              placeholder={t('settings.routing.fromAddressPlaceholder')}
              onChange={(event) => setForm({ ...form, fromAddress: event.target.value })}
              data-testid={`from-address-${entry.typeName}`}
            />
          </Field>

          <Field
            label={t('settings.routing.fromName')}
            htmlFor={`from-name-${entry.typeName}`}
            hint={t('settings.routing.fromNameHint')}
          >
            <Input
              id={`from-name-${entry.typeName}`}
              disabled={!canUpdate}
              value={form.fromName}
              onChange={(event) => setForm({ ...form, fromName: event.target.value })}
              data-testid={`from-name-${entry.typeName}`}
            />
          </Field>
        </div>

        <fieldset className="space-y-3">
          <div className="flex flex-wrap items-baseline justify-between gap-2">
            <legend className="text-sm font-medium">{t('settings.routing.bcc')}</legend>
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={!canUpdate || form.bcc.length >= MAX_BCC}
              onClick={() =>
                setForm({
                  ...form,
                  bcc: [...form.bcc, { email: '', displayName: null }],
                })
              }
              data-testid={`bcc-add-${entry.typeName}`}
            >
              <Plus className="size-4" aria-hidden="true" />
              {t('settings.routing.addBcc')}
            </Button>
          </div>

          <p className="text-xs text-muted-foreground">{t('settings.routing.bccHint')}</p>

          {form.bcc.length === 0 ? (
            <p className="rounded-xl bg-ink-50 px-4 py-3 text-sm text-ink-500">
              {t('settings.routing.noBcc')}
            </p>
          ) : (
            <ul className="space-y-2">
              {form.bcc.map((recipient, index) => (
                <li key={recipient.id ?? `new-${index}`} className="flex flex-wrap gap-2">
                  <Input
                    type="email"
                    dir="ltr"
                    className="min-w-0 flex-1"
                    disabled={!canUpdate}
                    value={recipient.email}
                    placeholder={t('settings.routing.bccPlaceholder')}
                    onChange={(event) =>
                      setForm({
                        ...form,
                        bcc: form.bcc.map((row, i) =>
                          i === index ? { ...row, email: event.target.value } : row,
                        ),
                      })
                    }
                    data-testid={`bcc-email-${entry.typeName}-${index}`}
                  />

                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    aria-label={t('common.remove')}
                    disabled={!canUpdate}
                    onClick={() =>
                      setForm({ ...form, bcc: form.bcc.filter((_, i) => i !== index) })
                    }
                    data-testid={`bcc-remove-${entry.typeName}-${index}`}
                  >
                    <Trash2 className="size-4 text-danger" aria-hidden="true" />
                  </Button>
                </li>
              ))}
            </ul>
          )}
        </fieldset>

        {/* A real send down the real path. The point is the answer it brings back: an operator
            asking "why did nothing arrive" gets the provider's refusal instead of silence. */}
        <div className="space-y-2 rounded-xl border border-border p-3">
          <p className="text-xs font-medium">{t('settings.routing.testTitle')}</p>
          <p className="text-xs text-subtle">{t('settings.routing.testHint')}</p>

          <div className="flex flex-wrap items-center gap-2">
            <Input
              type="email"
              dir="ltr"
              className="min-w-0 flex-1"
              value={testTo}
              placeholder={t('settings.routing.testPlaceholder')}
              onChange={(event) => setTestTo(event.target.value)}
              data-testid={`test-to-${entry.typeName}`}
            />
            <Button
              type="button"
              variant="outline"
              disabled={!canUpdate || test.isPending || testTo.trim() === ''}
              onClick={() => test.mutate({ type: entry.type, to: testTo.trim() })}
              data-testid={`test-send-${entry.typeName}`}
            >
              <Send className="size-4" aria-hidden="true" />
              {t('settings.routing.testSend')}
            </Button>
          </div>

          {test.isError && <Alert variant="error">{toMessage(test.error)}</Alert>}

          {test.data && (
            <Alert
              variant={test.data.delivered ? 'success' : 'error'}
              data-testid={`test-result-${entry.typeName}`}
            >
              <span className="block">
                {t(`settings.routing.testStatus.${test.data.status}`, {
                  defaultValue: test.data.status,
                  from: test.data.fromAddress,
                  to: test.data.to,
                })}
              </span>
              {test.data.detail && (
                <span className="mt-1 block font-mono text-xs opacity-80">{test.data.detail}</span>
              )}
            </Alert>
          )}
        </div>

        <div className="flex justify-end">
          <Button
            type="button"
            disabled={!canUpdate || save.isPending}
            onClick={submit}
            data-testid={`save-routing-${entry.typeName}`}
          >
            {t('common.save')}
          </Button>
        </div>
      </div>
    </AdminPanel>
  );
}
