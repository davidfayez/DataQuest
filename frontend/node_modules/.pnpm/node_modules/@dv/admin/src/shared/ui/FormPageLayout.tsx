import { AdminPanel, Alert, Button, Spinner, cn } from '@dv/ui';
import { ArrowLeft, Check } from 'lucide-react';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';

interface Props {
  title: string;
  subtitle: string;
  /** Breadcrumb-style label for the link back to the list, e.g. "Transaction types". */
  listLabel: string;
  onBack: () => void;
  onSubmit: () => void;
  isPending: boolean;
  canSubmit: boolean;
  /**
   * False while a required field is still empty. Disables Save like `canSubmit` does, but without
   * the view-only notice — an unfinished form is not a permission problem, and telling someone
   * with full rights that they "can only view" is the wrong message.
   */
  isComplete?: boolean;
  error: unknown;
  errorMessage: (error: unknown) => string;
  /** The form's sections; each is normally an AdminPanel. */
  children: ReactNode;
  /** Status, summary and guidance — the right-hand column on wide screens. */
  aside: ReactNode;
  /** Sections that need the whole width, e.g. a permission matrix. Rendered under the columns. */
  wide?: ReactNode;
}

/**
 * The frame every full-page admin form shares.
 *
 * Two columns on wide screens — the fields carry the weight on the left, with status and a live
 * summary parked on the right — and a single column on narrow ones. The save bar is sticky at the
 * foot so it stays reachable however long the form grows, which is the thing a dialog gave for
 * free and a naive page loses.
 */
export function FormPageLayout({
  title,
  subtitle,
  listLabel,
  onBack,
  onSubmit,
  isPending,
  canSubmit,
  isComplete = true,
  error,
  errorMessage,
  children,
  aside,
  wide,
}: Props) {
  const { t } = useTranslation();

  return (
    <div className="animate-fade-in pb-24">
      <nav className="mb-4" aria-label={listLabel}>
        <button
          type="button"
          onClick={onBack}
          className="inline-flex items-center gap-1.5 rounded-lg px-2 py-1 text-sm font-medium text-ink-500 transition-colors hover:bg-ink-100 hover:text-ink-950"
        >
          <ArrowLeft className="size-4 rtl:rotate-180" aria-hidden="true" />
          {t('common.backToList', { entity: listLabel })}
        </button>
      </nav>

      <h1 className="font-display text-2xl font-semibold tracking-tight text-ink-950 sm:text-3xl">
        {title}
      </h1>
      <p className="mt-1.5 max-w-2xl text-sm leading-relaxed text-ink-500">{subtitle}</p>

      <form
        noValidate
        onSubmit={(event) => {
          event.preventDefault();
          onSubmit();
        }}
        className="mt-6"
      >
        {(error || !canSubmit) && (
          <div className="mb-6 space-y-3">
            {error ? (
              <Alert variant="error" title={t('errors.genericTitle')}>
                {errorMessage(error)}
              </Alert>
            ) : null}
            {!canSubmit && (
              <Alert variant="info" data-testid="view-only-notice">
                {t('roles.viewOnlyNotice')}
              </Alert>
            )}
          </div>
        )}

        <div className="grid items-start gap-6 lg:grid-cols-3">
          <div className="space-y-6 lg:col-span-2">{children}</div>
          <aside className="space-y-6 lg:sticky lg:top-6">{aside}</aside>
        </div>

        {wide && <div className="mt-6 space-y-6">{wide}</div>}

        {/* Sticky so Save is always one click away, however far the form scrolls. */}
        <div className="sticky bottom-0 z-20 mt-8 -mb-24 border-t border-ink-100 bg-cream/85 py-4 backdrop-blur">
          <div className="flex flex-wrap items-center justify-end gap-3">
            <Button type="button" variant="outline" onClick={onBack}>
              {t('common.cancel')}
            </Button>
            <Button
              type="submit"
              disabled={!canSubmit || !isComplete || isPending}
              data-testid="form-submit"
              className={cn(isPending && 'pointer-events-none')}
            >
              {isPending ? <Spinner /> : <Check className="size-4" aria-hidden="true" />}
              {t('common.save')}
            </Button>
          </div>
        </div>
      </form>
    </div>
  );
}

/** The status panel every lookup form shows in its right-hand column. */
export function StatusPanel({
  isActive,
  onChange,
}: {
  isActive: boolean;
  onChange: (isActive: boolean) => void;
}) {
  const { t } = useTranslation();

  return (
    <AdminPanel title={t('lookups.statusPanel')}>
      {/* One control, not a checkbox plus a badge saying the same thing: the card itself carries
          the state, so the panel reads at a glance. */}
      <label
        className={cn(
          'flex cursor-pointer items-start gap-3 rounded-xl p-3 ring-1 transition-colors',
          isActive ? 'bg-success-muted ring-success/25' : 'bg-ink-50 ring-ink-200',
        )}
      >
        <input
          type="checkbox"
          className="mt-0.5 size-4 rounded border-ink-300"
          checked={isActive}
          onChange={(event) => onChange(event.target.checked)}
        />
        <span>
          {/* The label stays put; the tick and the tint carry the state. */}
          <span className="block text-sm font-semibold text-ink-900">{t('lookups.isActive')}</span>
          <span className="mt-0.5 block text-xs leading-relaxed text-ink-500">
            {t('lookups.isActiveHint')}
          </span>
        </span>
      </label>
    </AdminPanel>
  );
}

/** A read-only "what you have chosen so far" list for the right-hand column. */
export function SummaryPanel({
  title,
  items,
  emptyLabel,
}: {
  title: string;
  items: string[];
  emptyLabel: string;
}) {
  return (
    <AdminPanel title={title}>
      {items.length === 0 ? (
        <p className="text-sm text-ink-400">{emptyLabel}</p>
      ) : (
        <ul className="flex flex-wrap gap-1.5">
          {items.map((item) => (
            <li
              key={item}
              className="rounded-md bg-ink-50 px-2 py-1 text-xs text-ink-700 ring-1 ring-ink-100"
            >
              {item}
            </li>
          ))}
        </ul>
      )}
    </AdminPanel>
  );
}
