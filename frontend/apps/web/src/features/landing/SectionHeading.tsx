import { cn } from '@dv/ui';

export function SectionHeading({ eyebrow, title, dark }: { eyebrow: string; title: string; dark?: boolean }) {
  return (
    <div className="mx-auto max-w-2xl text-center">
      <span className={cn('text-xs font-bold uppercase tracking-[0.2em]', dark ? 'text-brand-400' : 'text-brand-700')}>
        {eyebrow}
      </span>
      <h2
        className={cn(
          'mt-3 font-display text-3xl font-semibold tracking-tight sm:text-4xl',
          dark ? 'text-white' : 'text-ink-950',
        )}
      >
        {title}
      </h2>
    </div>
  );
}
