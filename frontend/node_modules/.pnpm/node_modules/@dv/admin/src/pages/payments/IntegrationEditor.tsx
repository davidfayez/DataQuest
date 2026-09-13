import { AdminPanel, Alert, Button, Field, Input, Select, cn } from '@dv/ui';
import { Check, KeyRound, ShieldAlert } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import {
  PaymentIntegrationMode,
  type PaymentIntegrationDto,
  type PaymentIntegrationInput,
} from '@/features/payments/api';

interface Props {
  /** What the server currently holds — the source of "is this secret already set?". */
  stored: PaymentIntegrationDto | null;
  value: PaymentIntegrationInput;
  onChange: (value: PaymentIntegrationInput) => void;
  disabled?: boolean;
}

type SecretField = 'apiKey' | 'password' | 'webhookSecret';

/**
 * The credentials and endpoints an online payment method uses to talk to its provider.
 *
 * The awkward part of this editor is that it can never show what is already stored — the API will
 * not hand a secret back, by design. So each secret renders as one of two things: an input for one
 * that has never been set, or a "configured" row with the choice to replace or remove it. Leaving
 * a configured secret alone sends nothing at all for it, which is what stops an unrelated edit
 * here from wiping live credentials.
 */
export function IntegrationEditor({ stored, value, onChange, disabled }: Props) {
  const { t } = useTranslation();

  const patch = (fields: Partial<PaymentIntegrationInput>) => onChange({ ...value, ...fields });

  const isLive = value.mode === PaymentIntegrationMode.Live;

  // Warn on what is about to be true after saving, not on what the server last stored: an operator
  // who has just cleared the signing secret should see the warning before they press save.
  const willHaveWebhookSecret =
    value.webhookSecret === null ? Boolean(stored?.hasWebhookSecret) : value.webhookSecret !== '';
  const callbackUnverified = Boolean(value.callbackUrl?.trim()) && !willHaveWebhookSecret;

  const httpUrls = (
    [value.baseUrl, value.redirectUrl, value.cancelUrl, value.callbackUrl] as (string | null)[]
  ).filter((url) => url?.trim().toLowerCase().startsWith('http://'));

  return (
    <AdminPanel
      title={t('payments.sectionIntegration')}
      subtitle={t('payments.sectionIntegrationHint')}
    >
      <div className="space-y-5">
        {/* Which environment this talks to is the one setting worth seeing before anything else:
            it is the difference between a test payment and real money. */}
        <div
          className={cn(
            'rounded-xl border p-4',
            isLive ? 'border-danger/40 bg-danger/5' : 'border-border bg-ink-50/60',
          )}
        >
          <Field label={t('payments.integrationMode')} htmlFor="integration-mode">
            <Select
              id="integration-mode"
              disabled={disabled}
              value={String(value.mode)}
              onChange={(event) =>
                patch({ mode: Number(event.target.value) as PaymentIntegrationMode })
              }
              data-testid="integration-mode"
            >
              <option value={PaymentIntegrationMode.Sandbox}>{t('payments.modeSandbox')}</option>
              <option value={PaymentIntegrationMode.Live}>{t('payments.modeLive')}</option>
            </Select>
          </Field>

          <p className="mt-2 text-xs text-subtle">
            {t(isLive ? 'payments.modeLiveHint' : 'payments.modeSandboxHint')}
          </p>
        </div>

        {isLive && httpUrls.length > 0 && (
          <Alert variant="error">{t('payments.integrationHttpInLive')}</Alert>
        )}

        {callbackUnverified && (
          <Alert variant="warning">
            <span className="flex items-start gap-2">
              <ShieldAlert className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
              {t('payments.callbackUnverified')}
            </span>
          </Alert>
        )}

        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t('payments.provider')} htmlFor="integration-provider">
            <Input
              id="integration-provider"
              disabled={disabled}
              value={value.provider ?? ''}
              placeholder={t('payments.providerPlaceholder')}
              onChange={(event) => patch({ provider: event.target.value || null })}
              data-testid="integration-provider"
            />
          </Field>

          <Field
            label={t('payments.merchantId')}
            htmlFor="integration-merchant"
            hint={t('payments.merchantIdHint')}
          >
            <Input
              id="integration-merchant"
              dir="ltr"
              disabled={disabled}
              value={value.merchantId ?? ''}
              onChange={(event) => patch({ merchantId: event.target.value || null })}
              data-testid="integration-merchant"
            />
          </Field>

          <Field
            label={t('payments.integrationId')}
            htmlFor="integration-integration-id"
            hint={t('payments.integrationIdHint')}
          >
            <Input
              id="integration-integration-id"
              dir="ltr"
              disabled={disabled}
              value={value.integrationId ?? ''}
              onChange={(event) => patch({ integrationId: event.target.value || null })}
              data-testid="integration-integration-id"
            />
          </Field>

          <Field
            label={t('payments.sessionTimeout')}
            htmlFor="integration-timeout"
            hint={t('payments.sessionTimeoutHint')}
          >
            <Input
              id="integration-timeout"
              type="number"
              min={1}
              max={1440}
              disabled={disabled}
              value={value.sessionTimeoutMinutes ?? ''}
              onChange={(event) =>
                patch({
                  sessionTimeoutMinutes: event.target.value ? Number(event.target.value) : null,
                })
              }
              data-testid="integration-timeout"
            />
          </Field>
        </div>

        <fieldset className="space-y-4">
          <legend className="text-sm font-medium">{t('payments.credentials')}</legend>
          <p className="text-xs text-subtle">{t('payments.credentialsHint')}</p>

          <SecretRow
            field="apiKey"
            label={t('payments.apiKey')}
            hint={t('payments.apiKeyHint')}
            isStored={Boolean(stored?.hasApiKey)}
            value={value.apiKey}
            onChange={(next) => patch({ apiKey: next })}
            disabled={disabled}
          />

          <SecretRow
            field="password"
            label={t('payments.integrationPassword')}
            hint={t('payments.integrationPasswordHint')}
            isStored={Boolean(stored?.hasPassword)}
            value={value.password}
            onChange={(next) => patch({ password: next })}
            disabled={disabled}
          />

          <SecretRow
            field="webhookSecret"
            label={t('payments.webhookSecret')}
            hint={t('payments.webhookSecretHint')}
            isStored={Boolean(stored?.hasWebhookSecret)}
            value={value.webhookSecret}
            onChange={(next) => patch({ webhookSecret: next })}
            disabled={disabled}
          />
        </fieldset>

        <fieldset className="space-y-4">
          <legend className="text-sm font-medium">{t('payments.endpoints')}</legend>

          <Field
            label={t('payments.baseUrl')}
            htmlFor="integration-base-url"
            hint={t('payments.baseUrlHint')}
          >
            <Input
              id="integration-base-url"
              dir="ltr"
              type="url"
              disabled={disabled}
              placeholder="https://accept.provider.com/api"
              value={value.baseUrl ?? ''}
              onChange={(event) => patch({ baseUrl: event.target.value || null })}
              data-testid="integration-base-url"
            />
          </Field>

          <div className="grid gap-4 sm:grid-cols-2">
            <Field
              label={t('payments.redirectUrl')}
              htmlFor="integration-redirect-url"
              hint={t('payments.redirectUrlHint')}
            >
              <Input
                id="integration-redirect-url"
                dir="ltr"
                type="url"
                disabled={disabled}
                placeholder="https://nen-global.org/payment/done"
                value={value.redirectUrl ?? ''}
                onChange={(event) => patch({ redirectUrl: event.target.value || null })}
                data-testid="integration-redirect-url"
              />
            </Field>

            <Field
              label={t('payments.cancelUrl')}
              htmlFor="integration-cancel-url"
              hint={t('payments.cancelUrlHint')}
            >
              <Input
                id="integration-cancel-url"
                dir="ltr"
                type="url"
                disabled={disabled}
                placeholder="https://nen-global.org/payment/cancelled"
                value={value.cancelUrl ?? ''}
                onChange={(event) => patch({ cancelUrl: event.target.value || null })}
                data-testid="integration-cancel-url"
              />
            </Field>
          </div>

          <Field
            label={t('payments.callbackUrl')}
            htmlFor="integration-callback-url"
            hint={t('payments.callbackUrlHint')}
          >
            <Input
              id="integration-callback-url"
              dir="ltr"
              type="url"
              disabled={disabled}
              placeholder="https://api.nen-global.org/hooks/provider"
              value={value.callbackUrl ?? ''}
              onChange={(event) => patch({ callbackUrl: event.target.value || null })}
              data-testid="integration-callback-url"
            />
          </Field>
        </fieldset>
      </div>
    </AdminPanel>
  );
}

/**
 * One secret.
 *
 * `null` means "not touched" and is what a configured secret sends until someone chooses to
 * replace it. `''` means "remove it". Anything else is a new value.
 */
function SecretRow({
  field,
  label,
  hint,
  isStored,
  value,
  onChange,
  disabled,
}: {
  field: SecretField;
  label: string;
  hint: string;
  isStored: boolean;
  value: string | null;
  onChange: (next: string | null) => void;
  disabled?: boolean;
}) {
  const { t } = useTranslation();

  // Untouched and already stored: show what is true, and the two things that can be done about it.
  if (isStored && value === null) {
    return (
      <div className="rounded-xl border border-border p-3" data-testid={`secret-${field}`}>
        <div className="flex flex-wrap items-center justify-between gap-2">
          <span className="flex items-center gap-2 text-sm font-medium">
            <KeyRound className="size-4 text-subtle" aria-hidden="true" />
            {label}
            <span className="inline-flex items-center gap-1 rounded-full bg-success/10 px-2 py-0.5 text-xs font-semibold text-success">
              <Check className="size-3" aria-hidden="true" />
              {t('payments.secretConfigured')}
            </span>
          </span>

          <span className="flex gap-2">
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={disabled}
              onClick={() => onChange('')}
              data-testid={`secret-replace-${field}`}
            >
              {t('payments.secretReplace')}
            </Button>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              disabled={disabled}
              onClick={() => onChange('')}
              data-testid={`secret-remove-${field}`}
            >
              {t('payments.secretRemove')}
            </Button>
          </span>
        </div>

        <p className="mt-1 text-xs text-subtle">{hint}</p>
      </div>
    );
  }

  return (
    <Field label={label} htmlFor={`integration-${field}`} hint={hint}>
      <div className="space-y-2">
        <Input
          id={`integration-${field}`}
          type="password"
          dir="ltr"
          autoComplete="new-password"
          disabled={disabled}
          value={value ?? ''}
          placeholder={t('payments.secretPlaceholder')}
          onChange={(event) => onChange(event.target.value)}
          data-testid={`integration-${field}`}
        />

        {isStored && (
          <p className="flex flex-wrap items-center gap-2 text-xs text-subtle">
            {value === ''
              ? t('payments.secretWillBeRemoved')
              : t('payments.secretWillBeReplaced')}
            <button
              type="button"
              className="font-medium text-primary hover:underline"
              onClick={() => onChange(null)}
              data-testid={`secret-keep-${field}`}
            >
              {t('payments.secretKeep')}
            </button>
          </p>
        )}
      </div>
    </Field>
  );
}
