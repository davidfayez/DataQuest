import { AdminPanel, Alert, Button, Field, Input, Select, cn } from '@dv/ui';
import { ExternalLink, Eye, EyeOff, KeyRound, ShieldCheck } from 'lucide-react';
import { useState, type TextareaHTMLAttributes } from 'react';
import { useTranslation } from 'react-i18next';
import {
  GatewayFieldType,
  useMethodGatewaySecret,
  type PaymentGateway,
  type PaymentGatewayField,
} from '@/features/payments/integrations';

export const TEXTAREA_CLASS =
  'flex w-full rounded-xl border-0 bg-white px-3.5 py-2.5 text-sm text-ink-900 shadow-soft ring-1 ring-ink-200 transition-shadow placeholder:text-ink-300 focus:outline-none focus:ring-2 focus:ring-brand-500';

/** A multi-line box matching the package's inputs; the shared UI has no textarea of its own. */
export function TextArea(props: TextareaHTMLAttributes<HTMLTextAreaElement>) {
  return <textarea rows={3} maxLength={2000} {...props} className={cn(TEXTAREA_CLASS, props.className)} />;
}

type Translate = (key: string, options?: Record<string, unknown>) => string;

/** Checks a value the way the server will, so a mistake shows beside its field before saving. */
export function problemWith(
  field: PaymentGatewayField,
  value: string,
  live: boolean,
  t: Translate,
): string | null {
  const trimmed = value.trim();
  if (trimmed === '') return null;

  if (trimmed.length > field.maxLength) return t('integrations.tooLong', { max: field.maxLength });

  if (field.type === GatewayFieldType.Url) {
    try {
      const url = new URL(trimmed);
      if (url.protocol !== 'http:' && url.protocol !== 'https:') return t('integrations.badUrl');
      if (live && url.protocol !== 'https:') return t('integrations.httpsInLive');
    } catch {
      return t('integrations.badUrl');
    }
  }

  if (field.type === GatewayFieldType.Email && !/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(trimmed)) {
    return t('integrations.badEmail');
  }

  if (field.type === GatewayFieldType.Number && !Number.isFinite(Number(trimmed))) {
    return t('integrations.badNumber');
  }

  if (field.pattern) {
    try {
      if (!new RegExp(field.pattern, 'i').test(trimmed)) return t('integrations.badFormat');
    } catch {
      // A pattern the browser cannot compile is left to the server.
    }
  }

  return null;
}

/**
 * Everything the gateway would refuse, per field key. The same three-state rule the server applies
 * to secrets: undefined keeps what is stored, an empty string removes it, anything else replaces it.
 */
export function gatewayFieldErrors(
  gateway: PaymentGateway | undefined,
  settings: Record<string, string>,
  secrets: Record<string, string>,
  storedSecrets: ReadonlySet<string>,
  live: boolean,
  t: Translate,
): Record<string, string> {
  const errors: Record<string, string> = {};
  if (!gateway) return errors;

  for (const field of gateway.fields) {
    if (field.type === GatewayFieldType.Secret) {
      const submitted = secrets[field.key];
      const willExist = submitted === undefined ? storedSecrets.has(field.key) : submitted.trim() !== '';
      if (field.isRequired && !willExist) errors[field.key] = t('integrations.required');
      else if (submitted && submitted.length > field.maxLength) {
        errors[field.key] = t('integrations.tooLong', { max: field.maxLength });
      }
      continue;
    }

    if (field.type === GatewayFieldType.Boolean) continue;

    const value = settings[field.key] ?? '';
    if (field.isRequired && value.trim() === '') {
      errors[field.key] = t('integrations.required');
      continue;
    }

    const problem = problemWith(field, value, live, t);
    if (problem) errors[field.key] = problem;
  }

  return errors;
}

/** The settings the save endpoint takes: the chosen gateway's own keys, trimmed, blanks dropped. */
export function settingsToSubmit(
  gateway: PaymentGateway | undefined,
  settings: Record<string, string>,
): Record<string, string> {
  return Object.fromEntries(
    (gateway?.fields ?? [])
      .filter((field) => field.type !== GatewayFieldType.Secret)
      .map((field) => [field.key, (settings[field.key] ?? '').trim()] as const)
      .filter(([, value]) => value !== ''),
  );
}

/** Where the gateway operates and where its documentation lives. */
export function GatewaySummary({ gateway }: { gateway: PaymentGateway }) {
  const { t } = useTranslation();
  const required = gateway.fields.filter((field) => field.isRequired).length;

  return (
    <AdminPanel title={gateway.name}>
      <dl className="space-y-2 text-sm">
        <div>
          <dt className="text-xs text-ink-400">{t('integrations.categoryLabel')}</dt>
          <dd>{t(`integrations.category.${gateway.categoryName}`)}</dd>
        </div>
        <div>
          <dt className="text-xs text-ink-400">{t('integrations.region')}</dt>
          <dd>{gateway.region}</dd>
        </div>
        <div>
          <dt className="text-xs text-ink-400">{t('integrations.fieldCount')}</dt>
          <dd>{t('integrations.fieldCountValue', { total: gateway.fields.length, required })}</dd>
        </div>
      </dl>
      {gateway.website && (
        <a
          href={gateway.website}
          target="_blank"
          rel="noopener noreferrer"
          className="mt-3 inline-flex items-center gap-1.5 text-sm font-medium text-primary hover:underline"
        >
          <ExternalLink className="size-4" aria-hidden="true" />
          {t('integrations.docs')}
        </a>
      )}
    </AdminPanel>
  );
}

export function SettingField({
  field,
  value,
  error,
  onChange,
}: {
  field: PaymentGatewayField;
  value: string;
  error?: string;
  onChange: (value: string) => void;
}) {
  const { t } = useTranslation();
  const id = `setting-${field.key}`;
  const wide = field.multiline;

  if (field.type === GatewayFieldType.Boolean) {
    return (
      <label className={cn('flex items-start gap-3 rounded-xl p-3 hover:bg-ink-50', wide && 'sm:col-span-2')}>
        <input
          id={id}
          type="checkbox"
          className="mt-0.5 size-4 rounded border-ink-300"
          checked={value === 'true'}
          onChange={(event) => onChange(event.target.checked ? 'true' : 'false')}
          data-testid={id}
        />
        <span>
          <span className="block text-sm font-semibold text-ink-900">{field.label}</span>
          {field.hint && <span className="mt-0.5 block text-xs text-ink-500">{field.hint}</span>}
        </span>
      </label>
    );
  }

  return (
    <div className={cn(wide && 'sm:col-span-2')}>
      <Field label={field.label} htmlFor={id} required={field.isRequired} hint={field.hint ?? undefined}>
        {field.type === GatewayFieldType.Select ? (
          <Select id={id} value={value} onChange={(event) => onChange(event.target.value)} data-testid={id}>
            <option value="">{t('payments.choosePlaceholder')}</option>
            {field.options.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </Select>
        ) : field.multiline ? (
          <TextArea
            id={id}
            dir="ltr"
            maxLength={field.maxLength}
            value={value}
            placeholder={field.placeholder ?? undefined}
            onChange={(event) => onChange(event.target.value)}
            data-testid={id}
          />
        ) : (
          <Input
            id={id}
            dir="ltr"
            type={
              field.type === GatewayFieldType.Url
                ? 'url'
                : field.type === GatewayFieldType.Email
                  ? 'email'
                  : field.type === GatewayFieldType.Number
                    ? 'number'
                    : 'text'
            }
            maxLength={field.maxLength}
            placeholder={field.placeholder ?? (field.type === GatewayFieldType.Url ? 'https://' : undefined)}
            value={value}
            invalid={Boolean(error)}
            autoComplete="off"
            onChange={(event) => onChange(event.target.value)}
            data-testid={id}
          />
        )}
      </Field>
      {error && <p className="mt-1 text-xs text-destructive">{error}</p>}
    </div>
  );
}

/**
 * One secret. Three states, as the server reads them: untouched (undefined — keep what is stored),
 * a new value (replace), or an empty string (remove). A stored secret is shown only when the eye
 * button asks for it, which fetches it then and never before.
 */
export function SecretField({
  methodId,
  field,
  stored,
  value,
  canReveal,
  disabled,
  error,
  onChange,
}: {
  /** The saved payment method whose secret this is; absent while it is still being created. */
  methodId: string | undefined;
  field: PaymentGatewayField;
  stored: boolean;
  value: string | undefined;
  canReveal: boolean;
  disabled: boolean;
  error?: string;
  onChange: (value: string | undefined) => void;
}) {
  const { t } = useTranslation();
  const id = `secret-${field.key}`;
  const [visible, setVisible] = useState(false);
  const [editing, setEditing] = useState(!stored);

  const revealStored = visible && stored && value === undefined;
  const revealed = useMethodGatewaySecret(methodId, field.key, revealStored && canReveal);

  const removing = stored && value === '';
  const shownValue = value ?? (revealStored ? (revealed.data ?? '') : '');

  return (
    <div className="rounded-xl border border-border p-3" data-testid={`secret-row-${field.key}`}>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <label htmlFor={id} className="flex items-center gap-2 text-sm font-medium">
          <KeyRound className="size-4 text-ink-400" aria-hidden="true" />
          {field.label}
          {field.isRequired && <span className="text-destructive">*</span>}
        </label>
        {stored && (
          <span
            className={cn(
              'inline-flex items-center gap-1 rounded px-1.5 py-0.5 text-[11px] font-semibold',
              removing ? 'bg-red-50 text-red-700' : 'bg-emerald-50 text-emerald-700',
            )}
            data-testid={`secret-status-${field.key}`}
          >
            <ShieldCheck className="size-3" aria-hidden="true" />
            {removing
              ? t('payments.secretWillBeRemoved')
              : value
                ? t('payments.secretWillBeReplaced')
                : t('payments.secretConfigured')}
          </span>
        )}
      </div>
      {field.hint && <p className="mt-1 text-xs text-muted-foreground">{field.hint}</p>}

      {editing || !stored ? (
        <div className="mt-2 flex items-start gap-2">
          <div className="min-w-0 flex-1">
            {field.multiline ? (
              <TextArea
                id={id}
                dir="ltr"
                rows={4}
                maxLength={field.maxLength}
                disabled={disabled}
                placeholder={stored ? t('integrations.secretKeepPlaceholder') : t('payments.secretPlaceholder')}
                className={cn(!visible && '[-webkit-text-security:disc]')}
                value={removing ? '' : shownValue}
                onChange={(event) => onChange(event.target.value === '' ? undefined : event.target.value)}
                data-testid={id}
              />
            ) : (
              <Input
                id={id}
                dir="ltr"
                type={visible ? 'text' : 'password'}
                autoComplete="new-password"
                maxLength={field.maxLength}
                disabled={disabled}
                placeholder={stored ? t('integrations.secretKeepPlaceholder') : t('payments.secretPlaceholder')}
                value={removing ? '' : shownValue}
                invalid={Boolean(error)}
                // Clearing the box goes back to "keep": removal is its own, explicit button.
                onChange={(event) => onChange(event.target.value === '' ? undefined : event.target.value)}
                data-testid={id}
              />
            )}
          </div>
          <EyeButton visible={visible} onToggle={() => setVisible((v) => !v)} />
        </div>
      ) : (
        <div className="mt-2 flex items-center gap-2">
          <Input
            id={id}
            dir="ltr"
            readOnly
            type={visible && canReveal ? 'text' : 'password'}
            value={visible && canReveal ? (revealed.isPending ? t('common.loading') : shownValue) : '••••••••••••'}
            className="flex-1 bg-ink-50"
            data-testid={id}
          />
          {canReveal && <EyeButton visible={visible} onToggle={() => setVisible((v) => !v)} />}
        </div>
      )}

      {revealed.isError && visible && (
        <p className="mt-1 text-xs text-destructive">{t('integrations.revealFailed')}</p>
      )}

      {stored && !disabled && (
        <div className="mt-2 flex flex-wrap gap-2">
          {!editing && (
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() => setEditing(true)}
              data-testid={`secret-replace-${field.key}`}
            >
              {t('payments.secretReplace')}
            </Button>
          )}
          {(editing || removing) && (
            <Button
              type="button"
              variant="ghost"
              size="sm"
              onClick={() => {
                onChange(undefined);
                setEditing(false);
              }}
            >
              {t('payments.secretKeep')}
            </Button>
          )}
          {!removing && !field.isRequired && (
            <Button
              type="button"
              variant="ghost"
              size="sm"
              className="text-destructive"
              onClick={() => onChange('')}
              data-testid={`secret-remove-${field.key}`}
            >
              {t('payments.secretRemove')}
            </Button>
          )}
        </div>
      )}

      {error && <p className="mt-1 text-xs text-destructive">{error}</p>}
    </div>
  );
}

function EyeButton({ visible, onToggle }: { visible: boolean; onToggle: () => void }) {
  const { t } = useTranslation();

  return (
    <Button
      type="button"
      variant="outline"
      size="sm"
      className="h-10 shrink-0"
      aria-label={visible ? t('integrations.hideSecret') : t('integrations.showSecret')}
      aria-pressed={visible}
      onClick={onToggle}
    >
      {visible ? <EyeOff className="size-4" aria-hidden="true" /> : <Eye className="size-4" aria-hidden="true" />}
    </Button>
  );
}

/**
 * The chosen gateway's settings and credentials, as two panels. Shown on the payment method, which
 * is where an account's own values belong — two methods on one gateway are two merchant accounts.
 */
export function GatewaySettingsPanels({
  gateway,
  settings,
  secrets,
  storedSecrets,
  live,
  showErrors,
  errors,
  canStoreSecrets,
  canReveal,
  methodId,
  onSettingChange,
  onSecretChange,
}: {
  gateway: PaymentGateway;
  settings: Record<string, string>;
  secrets: Record<string, string>;
  storedSecrets: ReadonlySet<string>;
  live: boolean;
  showErrors: boolean;
  errors: Record<string, string>;
  canStoreSecrets: boolean;
  canReveal: boolean;
  /** The saved payment method these belong to; absent while it is still being created. */
  methodId: string | undefined;
  onSettingChange: (key: string, value: string) => void;
  onSecretChange: (key: string, value: string | undefined) => void;
}) {
  const { t } = useTranslation();

  const plainFields = gateway.fields.filter((field) => field.type !== GatewayFieldType.Secret);
  const secretFields = gateway.fields.filter((field) => field.type === GatewayFieldType.Secret);

  return (
    <>
      {plainFields.length > 0 && (
        <AdminPanel
          title={t('integrations.settingsSection', { gateway: gateway.name })}
          subtitle={t('integrations.settingsSectionHint')}
        >
          {live && <Alert variant="warning" className="mb-4">{t('payments.modeLiveHint')}</Alert>}

          <div className="grid gap-4 sm:grid-cols-2" data-testid="gateway-settings">
            {plainFields.map((field) => (
              <SettingField
                key={field.key}
                field={field}
                value={settings[field.key] ?? ''}
                error={showErrors ? errors[field.key] : undefined}
                onChange={(value) => onSettingChange(field.key, value)}
              />
            ))}
          </div>
        </AdminPanel>
      )}

      {secretFields.length > 0 && (
        <AdminPanel title={t('payments.credentials')} subtitle={t('integrations.credentialsHint')}>
          {!canStoreSecrets && (
            <Alert variant="warning" className="mb-4">
              {t('integrations.encryptionUnavailable')}
            </Alert>
          )}

          <div className="space-y-4" data-testid="gateway-secrets">
            {secretFields.map((field) => (
              <SecretField
                key={`${gateway.code}-${field.key}`}
                field={field}
                stored={storedSecrets.has(field.key)}
                value={secrets[field.key]}
                canReveal={canReveal}
                disabled={!canStoreSecrets}
                error={showErrors ? errors[field.key] : undefined}
                methodId={methodId}
                onChange={(value) => onSecretChange(field.key, value)}
              />
            ))}
          </div>
        </AdminPanel>
      )}
    </>
  );
}
