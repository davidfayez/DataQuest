import type { LucideIcon } from 'lucide-react';
import type { ReactNode } from 'react';
import { cn } from '../lib/cn';

/** Page title row — one job: headline, optional subtitle, optional actions. */
export function AdminPageHeader({
  title,
  subtitle,
  actions,
}: {
  title: string;
  subtitle?: string;
  actions?: ReactNode;
}) {
  return (
    <div className="mb-8 flex flex-wrap items-end justify-between gap-4">
      <div>
        <h1 className="font-display text-2xl font-semibold tracking-tight text-ink-950 sm:text-3xl">{title}</h1>
        {subtitle && (
          <p className="mt-1.5 max-w-3xl text-sm leading-relaxed text-ink-500">{subtitle}</p>
        )}
      </div>
      {actions && <div className="flex flex-wrap items-center gap-3">{actions}</div>}
    </div>
  );
}

/* Semantic tint names map onto the prototype's four-colour KPI palette. */
type AdminStatTint = 'primary' | 'info' | 'success' | 'warning';

const statSurface: Record<AdminStatTint, string> = {
  success: 'from-emerald-500/15 via-emerald-500/5 to-transparent ring-emerald-500/20',
  info: 'from-sky-500/15 via-sky-500/5 to-transparent ring-sky-500/20',
  primary: 'from-violet-500/15 via-violet-500/5 to-transparent ring-violet-500/20',
  warning: 'from-amber-500/15 via-amber-500/5 to-transparent ring-amber-500/20',
};

const statIconSurface: Record<AdminStatTint, string> = {
  success: 'bg-emerald-500/15 text-emerald-600 ring-emerald-500/25',
  info: 'bg-sky-500/15 text-sky-600 ring-sky-500/25',
  primary: 'bg-violet-500/15 text-violet-600 ring-violet-500/25',
  warning: 'bg-amber-500/15 text-amber-600 ring-amber-500/25',
};

/** KPI tile — presentation only; data comes from the page layer. */
export function AdminStatCard({
  icon: Icon,
  label,
  value,
  hint,
  tint = 'primary',
}: {
  icon: LucideIcon;
  label: string;
  value: string;
  hint?: string;
  tint?: AdminStatTint;
}) {
  return (
    <div
      className={cn(
        'relative overflow-hidden rounded-2xl bg-gradient-to-br p-5 ring-1 transition-all duration-300 hover:-translate-y-0.5 hover:shadow-lift',
        statSurface[tint],
      )}
    >
      <div
        className="pointer-events-none absolute -end-8 -top-8 h-32 w-32 rounded-full bg-white/40 blur-2xl"
        aria-hidden
      />
      <div className={cn('relative flex h-11 w-11 items-center justify-center rounded-xl ring-1', statIconSurface[tint])}>
        <Icon className="size-5" aria-hidden />
      </div>
      <p className="relative mt-5 font-display text-3xl font-semibold tracking-tight text-ink-950">{value}</p>
      <p className="relative mt-1 text-sm font-medium text-ink-600">{label}</p>
      {hint && <p className="relative mt-0.5 text-xs text-ink-400">{hint}</p>}
    </div>
  );
}

/** Content panel with optional titled header — reusable across admin modules. */
export function AdminPanel({
  title,
  subtitle,
  actions,
  children,
  className,
  noPadding,
  allowOverflow,
}: {
  title?: string;
  subtitle?: string;
  actions?: ReactNode;
  children: ReactNode;
  className?: string;
  noPadding?: boolean;
  /**
   * Lets children escape the panel's bounds. Needed whenever the panel holds a dropdown: the
   * default clip rounds the corners, but it also slices the open option list off at the panel
   * edge. The header then rounds its own top corners, since the parent no longer does it.
   */
  allowOverflow?: boolean;
}) {
  return (
    <section
      className={cn(
        'overflow-hidden rounded-2xl bg-white shadow-soft ring-1 ring-ink-100/80',
        allowOverflow && 'overflow-visible',
        className,
      )}
    >
      {(title || actions) && (
        <div
          className={cn(
            'flex flex-wrap items-center justify-between gap-3 border-b border-ink-100/80 bg-cream/40 px-5 py-4 sm:px-6',
            allowOverflow && 'rounded-t-2xl',
          )}
        >
          <div>
            {title && <h2 className="font-semibold text-ink-950">{title}</h2>}
            {subtitle && <p className="mt-0.5 text-xs text-ink-400">{subtitle}</p>}
          </div>
          {actions}
        </div>
      )}
      <div className={noPadding ? undefined : 'p-5 sm:p-6'}>{children}</div>
    </section>
  );
}

export function AdminFilterBar({ children }: { children: ReactNode }) {
  return (
    <div className="mb-6 flex flex-wrap items-center gap-2 rounded-2xl bg-white/70 p-2 shadow-soft ring-1 ring-ink-100/80 backdrop-blur-sm">
      {children}
    </div>
  );
}

export function AdminFilterPill({
  active,
  onClick,
  children,
}: {
  active: boolean;
  onClick: () => void;
  children: ReactNode;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'cursor-pointer rounded-xl px-4 py-2 text-xs font-semibold transition-all',
        active ? 'bg-ink-950 text-white shadow-soft' : 'text-ink-500 hover:bg-ink-50 hover:text-ink-800',
      )}
    >
      {children}
    </button>
  );
}

export function AdminTableWrap({ children }: { children: ReactNode }) {
  return <div className="overflow-x-auto">{children}</div>;
}

export const adminTh =
  'px-5 py-3.5 text-start text-[11px] font-bold uppercase tracking-[0.14em] text-ink-400 whitespace-nowrap';
export const adminTd = 'px-5 py-4 align-middle';
export const adminThead = 'border-b border-ink-100 bg-ink-950/[0.03]';
