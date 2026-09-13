import type { HTMLAttributes } from 'react';
import { cn } from '../lib/cn';

/**
 * A panel on the canvas. The canvas behind it is faintly tinted, so a plain border plus a
 * one-pixel shadow is enough to lift the card — no heavy elevation required.
 */
export function Card({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={cn('rounded-2xl bg-white shadow-soft ring-1 ring-ink-100', className)}
      {...props}
    />
  );
}

export function CardHeader({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return <div className={cn('space-y-1 px-5 pb-4 pt-5', className)} {...props} />;
}

export function CardTitle({ className, ...props }: HTMLAttributes<HTMLHeadingElement>) {
  return (
    <h2
      className={cn('text-base font-semibold tracking-[-0.011em] text-foreground', className)}
      {...props}
    />
  );
}

export function CardDescription({ className, ...props }: HTMLAttributes<HTMLParagraphElement>) {
  return <p className={cn('text-sm text-muted-foreground', className)} {...props} />;
}

export function CardContent({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return <div className={cn('px-5 pb-5', className)} {...props} />;
}

export function CardFooter({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={cn('flex items-center gap-3 border-t border-border px-5 py-4', className)}
      {...props}
    />
  );
}

/**
 * A label/value row, the unit these screens are mostly built from: an application is a list of
 * facts. Kept here so the same rhythm applies in both applications.
 */
export function CardRow({
  label,
  children,
  className,
  ...props
}: HTMLAttributes<HTMLDivElement> & { label: string }) {
  return (
    <div
      className={cn(
        'flex items-baseline justify-between gap-6 border-b border-border py-2.5 last:border-b-0',
        className,
      )}
      {...props}
    >
      <dt className="shrink-0 text-sm text-muted-foreground">{label}</dt>
      <dd className="text-end text-sm font-medium text-foreground">{children}</dd>
    </div>
  );
}
