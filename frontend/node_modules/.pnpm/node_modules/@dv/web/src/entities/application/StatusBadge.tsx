import { cn } from '@dv/ui';
import type { HTMLAttributes } from 'react';
import { useTranslation } from 'react-i18next';
import { statusLabelKey } from './statusLabels';
import { ApplicationStatus } from './types';

/**
 * Each status gets a visually distinct treatment. Colour is never the only signal — the label
 * always carries the meaning, so the badge stays readable for colour-blind users.
 */
const STYLES: Record<ApplicationStatus, string> = {
  [ApplicationStatus.Draft]: 'bg-muted text-muted-foreground border-border',
  [ApplicationStatus.PendingPayment]: 'bg-warning-muted text-warning border-warning-border',
  [ApplicationStatus.Pending]: 'bg-info-muted text-info border-info-border',
  [ApplicationStatus.InProgress]: 'bg-primary-muted text-primary border-primary-border',
  [ApplicationStatus.MissedInfo]: 'bg-warning-muted text-warning border-warning-border',
  [ApplicationStatus.Success]: 'bg-success-muted text-success border-success-border',
  [ApplicationStatus.Failed]: 'bg-destructive-muted text-destructive border-destructive-border',
  [ApplicationStatus.Refunded]: 'bg-muted text-muted-foreground border-border',
};

/* A filled dot carries the same state as the tint, so the badge still reads at a glance in
 * greyscale or for a colour-blind reader — the label alone requires actually reading it. */
const DOTS: Record<ApplicationStatus, string> = {
  [ApplicationStatus.Draft]: 'bg-subtle',
  [ApplicationStatus.PendingPayment]: 'bg-warning',
  [ApplicationStatus.Pending]: 'bg-info',
  [ApplicationStatus.InProgress]: 'bg-primary',
  [ApplicationStatus.MissedInfo]: 'bg-warning',
  [ApplicationStatus.Success]: 'bg-success',
  [ApplicationStatus.Failed]: 'bg-destructive',
  [ApplicationStatus.Refunded]: 'bg-subtle',
};

/** Extends the span props so callers can attach ids and test hooks. */
export type StatusBadgeProps = HTMLAttributes<HTMLSpanElement> & { status: ApplicationStatus };

export function StatusBadge({ status, className, ...rest }: StatusBadgeProps) {
  const { t } = useTranslation();

  return (
    <span
      className={cn(
        'inline-flex items-center gap-1.5 whitespace-nowrap rounded border px-2 py-0.5 text-xs font-medium',
        STYLES[status] ?? STYLES[ApplicationStatus.Draft],
        className,
      )}
      {...rest}
    >
      <span
        className={cn(
          'size-1.5 shrink-0 rounded-full',
          DOTS[status] ?? DOTS[ApplicationStatus.Draft],
        )}
        aria-hidden="true"
      />
      {t(statusLabelKey(status))}
    </span>
  );
}
