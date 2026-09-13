import { AlertCircle, CheckCircle2, Info, Loader2, TriangleAlert } from 'lucide-react';
import type { HTMLAttributes, ReactNode } from 'react';
import { cn } from '../lib/cn';

/* Each variant is tinted from its own semantic family rather than a transparency of the accent,
 * so the meaning survives on any background and does not shift with opacity. */
const alertStyles = {
  info: 'border-info-border bg-info-muted text-foreground',
  success: 'border-success-border bg-success-muted text-foreground',
  warning: 'border-warning-border bg-warning-muted text-foreground',
  error: 'border-destructive-border bg-destructive-muted text-foreground',
} as const;

const alertIconStyles = {
  info: 'text-info',
  success: 'text-success',
  warning: 'text-warning',
  error: 'text-destructive',
} as const;

const alertIcons = {
  info: Info,
  success: CheckCircle2,
  warning: TriangleAlert,
  error: AlertCircle,
} as const;

/** Extends the div props so callers can pass ids, test hooks and ARIA attributes through. */
export interface AlertProps extends Omit<HTMLAttributes<HTMLDivElement>, 'title'> {
  variant?: keyof typeof alertStyles;
  title?: string;
  children?: ReactNode;
}

export function Alert({ variant = 'info', title, children, className, ...rest }: AlertProps) {
  const Icon = alertIcons[variant];

  return (
    <div
      // Errors interrupt; everything else is announced politely.
      role={variant === 'error' ? 'alert' : 'status'}
      className={cn('flex gap-3 rounded-md border p-3.5 text-sm', alertStyles[variant], className)}
      {...rest}
    >
      <Icon
        className={cn('mt-0.5 size-4 shrink-0', alertIconStyles[variant])}
        aria-hidden="true"
      />
      <div className="min-w-0 space-y-0.5">
        {title && <p className="font-semibold">{title}</p>}
        {children && <div className="text-muted-foreground">{children}</div>}
      </div>
    </div>
  );
}

export function Spinner({ className, ...props }: HTMLAttributes<SVGElement>) {
  return (
    <Loader2
      className={cn('size-4 animate-spin', className)}
      aria-hidden="true"
      {...props}
    />
  );
}

/** Full-area loading state used while a route's data resolves. */
export function LoadingState({ label }: { label: string }) {
  return (
    <div className="flex items-center justify-center gap-3 p-12 text-muted-foreground" role="status">
      <Spinner className="size-5" />
      <span className="text-sm">{label}</span>
    </div>
  );
}

/** Grey placeholder used for skeleton screens. */
export function Skeleton({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return <div className={cn('animate-pulse rounded-md bg-muted', className)} {...props} />;
}
