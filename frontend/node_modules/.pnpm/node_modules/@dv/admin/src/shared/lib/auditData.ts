import type { TFunction } from 'i18next';
import { formatFileSize, formatNumber } from './format';

/**
 * Turns an audit entry's raw JSON `data` blob into readable label/value rows.
 *
 * The blob is small and structured (e.g. {"applicationNumber":"APP-…","from":3,"to":4}), but shown
 * raw it reads as JSON with braces, quotes and status *numbers*. Here each field gets a translated
 * label and its value is humanised — status codes become status names, byte counts become sizes,
 * visibility codes become words — so the detail dialog is legible at a glance.
 */
export interface AuditDetailRow {
  label: string;
  value: string;
  /** LTR values (references, emails, codes) render left-to-right even in an RTL dialog. */
  ltr?: boolean;
}

const STATUS_KEY: Record<number, string> = {
  0: 'draft',
  1: 'pendingPayment',
  2: 'pending',
  3: 'inProgress',
  4: 'missedInfo',
  5: 'success',
  6: 'failed',
  7: 'refunded',
};

// Fields whose numeric value is an application-status code.
const STATUS_FIELDS = new Set(['from', 'to', 'status', 'fromStatus', 'toStatus']);
// Fields that read left-to-right regardless of locale.
const LTR_FIELDS = new Set([
  'applicationNumber',
  'email',
  'code',
  'orderId',
  'contentType',
  'fileName',
  'currency',
  'languageCode',
]);

export function formatAuditData(
  raw: string | null | undefined,
  t: TFunction,
  locale: string,
): AuditDetailRow[] {
  if (!raw) return [];

  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch {
    return [{ label: t('audit.rawData'), value: raw, ltr: true }];
  }

  if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) {
    return [{ label: t('audit.rawData'), value: String(parsed), ltr: true }];
  }

  return Object.entries(parsed as Record<string, unknown>).map(([key, value]) => ({
    label: fieldLabel(key, t),
    value: fieldValue(key, value, t, locale),
    ltr: LTR_FIELDS.has(key),
  }));
}

function fieldLabel(key: string, t: TFunction): string {
  const translated = t(`audit.field.${key}`);
  if (translated !== `audit.field.${key}`) return translated;

  // Fallback for an unmapped key: "totalCost" -> "Total cost".
  const spaced = key.replace(/([a-z0-9])([A-Z])/g, '$1 $2').toLowerCase();
  return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}

function fieldValue(key: string, value: unknown, t: TFunction, locale: string): string {
  if (value === null || value === undefined || value === '') return '—';

  if (STATUS_FIELDS.has(key) && typeof value === 'number') {
    return t(`status.${STATUS_KEY[value] ?? 'draft'}`);
  }

  if (key === 'visibility' && typeof value === 'number') {
    return value === 1 ? t('audit.visInternal') : t('audit.visForUser');
  }

  if (key === 'sizeBytes' && typeof value === 'number') {
    return formatFileSize(value, locale);
  }

  if (typeof value === 'boolean') {
    return value ? t('common.yes') : t('common.no');
  }

  if (typeof value === 'number') {
    return formatNumber(value, locale);
  }

  if (Array.isArray(value)) {
    return value.length > 0 ? value.map(String).join(', ') : '—';
  }

  return String(value);
}
