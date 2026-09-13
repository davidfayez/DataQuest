import type { LabelHTMLAttributes, ReactNode } from 'react';
import { cn } from '../lib/cn';

export function Label({
  className,
  ...props
}: LabelHTMLAttributes<HTMLLabelElement>) {
  return (
    <label
      className={cn('block text-sm font-medium text-foreground', className)}
      {...props}
    />
  );
}

export interface FieldProps {
  label: string;
  htmlFor: string;
  /** Rendered in the error colour and announced via `role="alert"`. */
  error?: string;
  hint?: string;
  required?: boolean;
  children: ReactNode;
  className?: string;
}

/**
 * Label + control + message, wired for accessibility. Every form in both apps uses this so the
 * error position and spacing never drift between screens.
 */
export function Field({
  label,
  htmlFor,
  error,
  hint,
  required,
  children,
  className,
}: FieldProps) {
  const describedBy = error ? `${htmlFor}-error` : hint ? `${htmlFor}-hint` : undefined;

  return (
    <div className={cn('space-y-1.5', className)}>
      <Label htmlFor={htmlFor}>
        {label}
        {required && (
          <span aria-hidden="true" className="ms-1 text-destructive">
            *
          </span>
        )}
      </Label>

      <div aria-describedby={describedBy}>{children}</div>

      {error ? (
        <p id={`${htmlFor}-error`} role="alert" className="text-sm text-destructive">
          {error}
        </p>
      ) : hint ? (
        <p id={`${htmlFor}-hint`} className="text-sm text-muted-foreground">
          {hint}
        </p>
      ) : null}
    </div>
  );
}
