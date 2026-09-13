import { AdminPanel, Alert, Button, Field, Input } from '@dv/ui';
import { Mail, Plus, Trash2 } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import type { PaymentNotificationEmailInput } from '@/features/payments/api';

interface Props {
  recipients: PaymentNotificationEmailInput[];
  onChange: (recipients: PaymentNotificationEmailInput[]) => void;
}

/** The three moments a mailbox can be told about, and the field each one toggles. */
const EVENTS = [
  { key: 'notifyOnSubmitted', label: 'payments.notify.submitted' },
  { key: 'notifyOnApproved', label: 'payments.notify.approved' },
  { key: 'notifyOnRejected', label: 'payments.notify.rejected' },
] as const;

const MAX_RECIPIENTS = 25;

/**
 * Who hears about money moving through this method.
 *
 * Per method rather than one platform-wide list: whoever reconciles InstaPay is rarely whoever
 * reconciles the bank account. Each row picks its own events, because a finance mailbox usually
 * wants all three while a manager only wants to hear about rejections.
 */
export function NotificationEmailsEditor({ recipients, onChange }: Props) {
  const { t } = useTranslation();

  function replace(index: number, patch: Partial<PaymentNotificationEmailInput>) {
    onChange(recipients.map((row, i) => (i === index ? { ...row, ...patch } : row)));
  }

  function add() {
    onChange([
      ...recipients,
      {
        email: '',
        displayName: null,
        notifyOnSubmitted: true,
        notifyOnApproved: true,
        notifyOnRejected: true,
      },
    ]);
  }

  // A row with no event ticked would sit in the list looking configured and never send anything.
  const silent = recipients.filter(
    (row) =>
      row.email.trim() !== ''
      && !row.notifyOnSubmitted
      && !row.notifyOnApproved
      && !row.notifyOnRejected,
  );

  return (
    <AdminPanel
      title={t('payments.sectionNotifications')}
      subtitle={t('payments.sectionNotificationsHint')}
      actions={
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={add}
          disabled={recipients.length >= MAX_RECIPIENTS}
          data-testid="notification-add"
        >
          <Plus className="size-4" aria-hidden="true" />
          {t('payments.addRecipient')}
        </Button>
      }
    >
      {silent.length > 0 && (
        <Alert variant="warning">{t('payments.notificationSilentWarning')}</Alert>
      )}

      {recipients.length === 0 ? (
        <p className="flex items-center gap-2 rounded-xl bg-ink-50 px-4 py-3 text-sm text-ink-500">
          <Mail className="size-4 shrink-0" aria-hidden="true" />
          {t('payments.noRecipientsYet')}
        </p>
      ) : (
        <ul className="space-y-4">
          {recipients.map((row, index) => (
            <li
              key={row.id ?? `new-${index}`}
              className="rounded-xl border border-border p-4"
              data-testid={`notification-row-${index}`}
            >
              <div className="grid gap-4 sm:grid-cols-2">
                <Field label={t('payments.recipientEmail')} htmlFor={`recipient-email-${index}`} required>
                  <Input
                    id={`recipient-email-${index}`}
                    type="email"
                    dir="ltr"
                    value={row.email}
                    onChange={(event) => replace(index, { email: event.target.value })}
                    data-testid={`recipient-email-${index}`}
                  />
                </Field>

                <Field label={t('payments.recipientName')} htmlFor={`recipient-name-${index}`}>
                  <Input
                    id={`recipient-name-${index}`}
                    value={row.displayName ?? ''}
                    onChange={(event) =>
                      replace(index, { displayName: event.target.value || null })
                    }
                  />
                </Field>
              </div>

              <fieldset className="mt-3">
                <legend className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                  {t('payments.notifyWhen')}
                </legend>

                <div className="mt-2 flex flex-wrap gap-x-5 gap-y-2">
                  {EVENTS.map((event) => (
                    <label
                      key={event.key}
                      className="flex cursor-pointer items-center gap-2 text-sm"
                      htmlFor={`recipient-${event.key}-${index}`}
                    >
                      <input
                        id={`recipient-${event.key}-${index}`}
                        type="checkbox"
                        className="size-4 rounded border-border"
                        checked={row[event.key]}
                        onChange={(input) =>
                          replace(index, { [event.key]: input.target.checked })
                        }
                        data-testid={`recipient-${event.key}-${index}`}
                      />
                      {t(event.label)}
                    </label>
                  ))}
                </div>
              </fieldset>

              <div className="mt-3 flex justify-end">
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  onClick={() => onChange(recipients.filter((_, i) => i !== index))}
                  data-testid={`notification-remove-${index}`}
                >
                  <Trash2 className="size-4 text-danger" aria-hidden="true" />
                  {t('common.remove')}
                </Button>
              </div>
            </li>
          ))}
        </ul>
      )}
    </AdminPanel>
  );
}
