import { cva } from 'class-variance-authority';

/**
 * Kept in its own module so `button.tsx` exports only a component. Mixing a component and a
 * non-component export in one file breaks React Fast Refresh for that file.
 */
export const buttonVariants = cva(
  [
    'inline-flex items-center justify-center gap-2 whitespace-nowrap rounded-xl',
    'text-sm font-medium leading-none cursor-pointer',
    'transition-all duration-200',
    'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-600',
    'disabled:pointer-events-none disabled:opacity-45 disabled:shadow-none',
    'active:scale-[0.98]',
  ].join(' '),
  {
    variants: {
      variant: {
        primary:
          'bg-brand-600 text-white hover:bg-brand-700 shadow-[0_1px_0_rgb(255_255_255/0.15)_inset,0_8px_20px_-6px_rgb(5_150_102/0.5)]',
        dark: 'bg-ink-950 text-white hover:bg-ink-800 shadow-lift',
        secondary:
          'bg-white text-ink-900 ring-1 ring-ink-200 hover:ring-ink-300 hover:bg-ink-50 shadow-soft',
        outline:
          'bg-transparent text-ink-900 ring-1 ring-ink-200 hover:ring-ink-300 hover:bg-ink-50',
        ghost: 'text-ink-600 hover:text-ink-950 hover:bg-ink-100/70',
        destructive: 'bg-white text-red-600 ring-1 ring-red-200 hover:bg-red-50',
      },
      size: {
        sm: 'h-8 px-3 text-xs',
        md: 'h-10 px-4.5',
        lg: 'h-12 px-6 text-base',
      },
    },
    defaultVariants: { variant: 'primary', size: 'md' },
  },
);
