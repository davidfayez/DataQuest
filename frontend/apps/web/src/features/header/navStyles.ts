import { cn } from '@dv/ui';

/**
 * Where a header entry is drawn: in the bar itself on wide screens, or stacked in the menu panel
 * that stands in for the bar on narrow ones.
 */
export type NavVariant = 'bar' | 'panel';

const SHELL: Record<NavVariant, string> = {
  // Never wraps: a label broken over two lines doubles the entry's height and knocks the whole bar
  // out of line, which is what happened once the signed-in entries joined it.
  bar: 'flex items-center gap-2 whitespace-nowrap rounded-lg px-3 py-2 text-sm font-medium transition-colors',
  panel: 'flex items-center gap-3 rounded-lg px-3 py-2.5 text-[0.9375rem] font-medium transition-colors',
};

/** The one look every header entry shares — the arranged ones and the account ones alike. */
export function navItemClass(variant: NavVariant, isActive: boolean): string {
  return cn(
    SHELL[variant],
    isActive ? 'bg-ink-950 text-white' : 'text-ink-600 hover:bg-ink-100 hover:text-ink-950',
  );
}
