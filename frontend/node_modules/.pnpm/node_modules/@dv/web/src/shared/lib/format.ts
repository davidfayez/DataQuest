/**
 * Locale-aware formatting. Everything goes through `Intl` so numbers, currencies and dates render
 * in the active locale's own conventions — including Eastern Arabic digits for `ar`.
 */

export function formatCurrency(amount: number, currencyCode: string, locale: string): string {
  if (!currencyCode) return formatNumber(amount, locale);

  try {
    return new Intl.NumberFormat(locale, {
      style: 'currency',
      currency: currencyCode,
      maximumFractionDigits: 2,
    }).format(amount);
  } catch {
    // An unknown ISO code must not break the page it appears on.
    return `${formatNumber(amount, locale)} ${currencyCode}`;
  }
}

export function formatNumber(value: number, locale: string): string {
  return new Intl.NumberFormat(locale, { maximumFractionDigits: 2 }).format(value);
}

export function formatDate(value: string | Date, locale: string): string {
  const date = typeof value === 'string' ? new Date(value) : value;
  return new Intl.DateTimeFormat(locale, { dateStyle: 'medium' }).format(date);
}

/**
 * A calendar date with no time of day — a date of birth, an issue date.
 *
 * Formatted from its parts rather than through a Date, because `new Date('1990-05-14')` is parsed
 * as UTC midnight and then rendered in the viewer's zone: west of Greenwich that lands on the
 * 13th. A date of birth is the same day everywhere, so it must never be shifted by a timezone.
 */
export function formatCalendarDate(value: string, locale: string): string {
  const [year, month, day] = value.slice(0, 10).split('-').map(Number);

  if (!year || !month || !day) {
    return value;
  }

  // Built as UTC and read back as UTC, so the parts survive the round trip untouched.
  return new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeZone: 'UTC' })
    .format(new Date(Date.UTC(year, month - 1, day)));
}

export function formatDateTime(value: string | Date, locale: string): string {
  const date = typeof value === 'string' ? new Date(value) : value;
  return new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short' }).format(date);
}

/** "3 days ago" style output for timeline entries. */
export function formatRelativeTime(value: string | Date, locale: string): string {
  const date = typeof value === 'string' ? new Date(value) : value;
  const seconds = Math.round((date.getTime() - Date.now()) / 1000);

  const thresholds: Array<[Intl.RelativeTimeFormatUnit, number]> = [
    ['year', 60 * 60 * 24 * 365],
    ['month', 60 * 60 * 24 * 30],
    ['day', 60 * 60 * 24],
    ['hour', 60 * 60],
    ['minute', 60],
  ];

  const formatter = new Intl.RelativeTimeFormat(locale, { numeric: 'auto' });

  for (const [unit, unitSeconds] of thresholds) {
    if (Math.abs(seconds) >= unitSeconds) {
      return formatter.format(Math.round(seconds / unitSeconds), unit);
    }
  }

  return formatter.format(seconds, 'second');
}

export function formatFileSize(bytes: number, locale: string): string {
  if (bytes < 1024) return `${formatNumber(bytes, locale)} B`;
  if (bytes < 1024 * 1024) return `${formatNumber(bytes / 1024, locale)} KB`;
  return `${formatNumber(bytes / (1024 * 1024), locale)} MB`;
}
