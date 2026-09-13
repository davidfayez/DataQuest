import { Clock } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { formatUtcDate, formatUtcTime } from '@/shared/lib/format';
import { useLanguage } from '@/shared/lib/useLanguage';

/**
 * The current UTC date and time, ticking once a second. Server timestamps throughout the panel are
 * UTC, so showing the same clock in the header gives operators a reference they can compare against
 * without doing timezone arithmetic in their heads.
 */
export function UtcClock() {
  const { t } = useTranslation();
  const locale = useLanguage();
  const [now, setNow] = useState(() => new Date());

  useEffect(() => {
    const handle = window.setInterval(() => setNow(new Date()), 1000);
    return () => window.clearInterval(handle);
  }, []);

  return (
    <div
      data-testid="utc-clock"
      title={t('common.utcClock')}
      className="hidden items-center gap-2.5 rounded-xl bg-white px-3 py-2 text-ink-950 ring-1 ring-ink-200/70 lg:flex"
    >
      <Clock className="size-4 shrink-0 text-brand-600" aria-hidden />
      <span className="leading-tight">
        <span className="block text-xs font-semibold whitespace-nowrap">
          {formatUtcDate(now, locale)}
        </span>
        {/* Forced LTR so the clock reads 22:45:07 rather than mirrored in the Arabic layout. */}
        <span
          dir="ltr"
          className="mt-0.5 block text-[11px] font-medium tabular-nums text-ink-500 rtl:text-end"
        >
          {formatUtcTime(now, locale)} {t('common.utcClock')}
        </span>
      </span>
    </div>
  );
}
