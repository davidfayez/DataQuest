import { cn } from '@dv/ui';

/**
 * The queue is scanned, not read. A tinted pill with a filled dot lets a reviewer find the rows
 * that need action without reading every label — and the dot means the distinction survives in
 * greyscale or for a colour-blind reader, where the tint alone would not.
 */
const TONES: Record<string, { chip: string; dot: string }> = {
  Draft: { chip: 'bg-ink-100 text-ink-600 ring-ink-200', dot: 'bg-ink-400' },
  PendingPayment: { chip: 'bg-amber-50 text-amber-700 ring-amber-200', dot: 'bg-amber-500' },
  Pending: { chip: 'bg-sky-50 text-sky-700 ring-sky-200', dot: 'bg-sky-500' },
  InProgress: { chip: 'bg-indigo-50 text-indigo-700 ring-indigo-200', dot: 'bg-indigo-500' },
  MissedInfo: { chip: 'bg-orange-50 text-orange-700 ring-orange-200', dot: 'bg-orange-500' },
  Success: { chip: 'bg-brand-50 text-brand-700 ring-brand-200', dot: 'bg-brand-500' },
  Failed: { chip: 'bg-red-50 text-red-700 ring-red-200', dot: 'bg-red-500' },
  Refunded: { chip: 'bg-purple-50 text-purple-700 ring-purple-200', dot: 'bg-purple-500' },

  // Support tickets share this pill; Pending and InProgress above already carry their meaning.
  Answered: { chip: 'bg-teal-50 text-teal-700 ring-teal-200', dot: 'bg-teal-500' },
  Closed: { chip: 'bg-ink-100 text-ink-600 ring-ink-200', dot: 'bg-ink-400' },
};

export function StatusPill({ statusName, label }: { statusName: string; label: string }) {
  const tone = TONES[statusName] ?? TONES.Draft;

  return (
    <span
      className={cn(
        'inline-flex items-center gap-1.5 whitespace-nowrap rounded-full px-2.5 py-1 text-xs font-semibold ring-1',
        tone.chip,
      )}
    >
      <span
        className={cn(
          'size-1.5 shrink-0 rounded-full',
          tone.dot,
          statusName === 'InProgress' && 'animate-pulse',
        )}
        aria-hidden="true"
      />
      {label}
    </span>
  );
}
