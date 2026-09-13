import type { InputHTMLAttributes } from 'react';
import { forwardRef } from 'react';
import { cn } from '../lib/cn';

export type InputProps = InputHTMLAttributes<HTMLInputElement> & {
  /** Renders the error ring and wires `aria-invalid` for assistive technology. */
  invalid?: boolean;
};

export const Input = forwardRef<HTMLInputElement, InputProps>(function Input(
  { className, invalid, ...props },
  ref,
) {
  return (
    <input
      ref={ref}
      aria-invalid={invalid || undefined}
      className={cn(
        'flex h-11 w-full rounded-xl border-0 bg-white px-3.5 text-sm text-ink-900',
        'ring-1 ring-ink-200 shadow-soft transition-shadow',
        'placeholder:text-ink-300',
        'focus:outline-none focus:ring-2 focus:ring-brand-500',
        'disabled:cursor-not-allowed disabled:bg-ink-50 disabled:opacity-60',
        invalid && 'ring-red-300 focus:ring-red-500',
        className,
      )}
      {...props}
    />
  );
});
