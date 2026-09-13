import type { SelectHTMLAttributes } from 'react';
import { forwardRef } from 'react';
import { cn } from '../lib/cn';

export type SelectProps = SelectHTMLAttributes<HTMLSelectElement> & {
  invalid?: boolean;
};

/**
 * A native select. Deliberately not a custom listbox: native selects get correct RTL mirroring,
 * keyboard behaviour and mobile pickers for free across all seven locales.
 */
export const Select = forwardRef<HTMLSelectElement, SelectProps>(function Select(
  { className, invalid, children, ...props },
  ref,
) {
  return (
    <select
      ref={ref}
      aria-invalid={invalid || undefined}
      className={cn(
        'flex h-11 w-full appearance-none cursor-pointer rounded-xl border-0 bg-white px-3.5 py-2 text-sm text-ink-900',
        'ring-1 ring-ink-200 shadow-soft transition-shadow',
        'focus:outline-none focus:ring-2 focus:ring-brand-500',
        'disabled:cursor-not-allowed disabled:opacity-50',
        invalid && 'ring-red-300 focus:ring-red-500',
        className,
      )}
      {...props}
    >
      {children}
    </select>
  );
});
