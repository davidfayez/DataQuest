import type { VariantProps } from 'class-variance-authority';
import type { ButtonHTMLAttributes } from 'react';
import { cn } from '../lib/cn';
import { buttonVariants } from './button-variants';

export type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> &
  VariantProps<typeof buttonVariants>;

/**
 * `type` defaults to "button", not to HTML's "submit".
 *
 * A `<button>` inside a `<form>` submits it unless it says otherwise, so an ordinary action button
 * that happens to sit in a form — "Add image", "Add another", a step control — silently becomes a
 * second submit button. That is a bug you only find by clicking, and it cost this codebase one:
 * choosing a footer mark's image saved the mark before the file had been picked. Submitting is the
 * rarer intent and the one worth spelling out, so `type="submit"` is opt-in.
 */
export function Button({ className, variant, size, type = 'button', ...props }: ButtonProps) {
  return (
    <button type={type} className={cn(buttonVariants({ variant, size }), className)} {...props} />
  );
}
