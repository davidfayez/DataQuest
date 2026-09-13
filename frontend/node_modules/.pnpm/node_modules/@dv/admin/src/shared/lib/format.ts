/** Locale-aware formatting via `Intl`, so Arabic renders its own digit and date conventions. */

export function formatNumber(value: number, locale: string): string {
  return new Intl.NumberFormat(locale, { maximumFractionDigits: 2 }).format(value);
}

export function formatCurrency(amount: number, currencyCode: string, locale: string): string {
  if (!currencyCode) return formatNumber(amount, locale);

  try {
    return new Intl.NumberFormat(locale, {
      style: 'currency',
      currency: currencyCode,
      maximumFractionDigits: 2,
    }).format(amount);
  } catch {
    // An unrecognised ISO code must not break the screen it appears on.
    return `${formatNumber(amount, locale)} ${currencyCode}`;
  }
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

/** Just the time of day, for the "signed in at" line in the header. */
export function formatTime(value: string | Date, locale: string): string {
  const date = typeof value === 'string' ? new Date(value) : value;
  return new Intl.DateTimeFormat(locale, { timeStyle: 'short' }).format(date);
}

/**
 * The full weekday-and-month date in UTC, e.g. "Saturday 30 August 2026". Explicitly pinned to
 * UTC rather than the viewer's zone, because the header clock is there to show server time.
 */
export function formatUtcDate(value: Date, locale: string): string {
  // en-GB rather than plain "en": the latter resolves to US ordering ("Friday, July 31, 2026"),
  // where the header is specified as day-before-month ("Friday 31 July 2026"). Arabic already
  // orders it that way.
  const dateLocale = locale === 'en' ? 'en-GB' : locale;

  // Weekday and date are formatted separately and joined with a space: asking Intl for all four
  // fields at once inserts a comma after the weekday ("Friday, 31 July 2026").
  const weekday = new Intl.DateTimeFormat(dateLocale, {
    weekday: 'long',
    timeZone: 'UTC',
  }).format(value);

  const date = new Intl.DateTimeFormat(dateLocale, {
    day: 'numeric',
    month: 'long',
    year: 'numeric',
    timeZone: 'UTC',
  }).format(value);

  return `${weekday} ${date}`;
}

/** The 24-hour clock time in UTC, e.g. "22:45:07". */
export function formatUtcTime(value: Date, locale: string): string {
  return new Intl.DateTimeFormat(locale, {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hourCycle: 'h23',
    timeZone: 'UTC',
  }).format(value);
}

export function formatFileSize(bytes: number, locale: string): string {
  if (bytes < 1024) return `${formatNumber(bytes, locale)} B`;
  if (bytes < 1024 * 1024) return `${formatNumber(bytes / 1024, locale)} KB`;
  return `${formatNumber(bytes / (1024 * 1024), locale)} MB`;
}
