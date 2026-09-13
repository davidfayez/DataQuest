import { cn } from '@dv/ui';
import { Check, ChevronsUpDown, X } from 'lucide-react';
import { useEffect, useId, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';

export interface MultiSelectOption {
  value: string;
  label: string;
}

interface Props {
  id?: string;
  label?: string;
  options: MultiSelectOption[];
  value: string[];
  onChange: (value: string[]) => void;
  placeholder?: string;
  className?: string;
  disabled?: boolean;
}

/**
 * Compact multi-select used by admin list filters (country, currency, …). Keeps the selected
 * chips visible and closes on outside click.
 */
export function MultiSelect({
  id,
  label,
  options,
  value,
  onChange,
  placeholder,
  className,
  disabled,
}: Props) {
  const { t } = useTranslation();
  const generatedId = useId();
  const controlId = id ?? generatedId;
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    function onPointerDown(event: MouseEvent) {
      if (!rootRef.current?.contains(event.target as Node)) setOpen(false);
    }
    document.addEventListener('mousedown', onPointerDown);
    return () => document.removeEventListener('mousedown', onPointerDown);
  }, [open]);

  const selected = options.filter((option) => value.includes(option.value));

  function toggle(optionValue: string) {
    onChange(
      value.includes(optionValue)
        ? value.filter((entry) => entry !== optionValue)
        : [...value, optionValue],
    );
  }

  return (
    <div ref={rootRef} className={cn('relative flex w-56 flex-col gap-1.5', className)}>
      {label && (
        <label htmlFor={controlId} className="text-sm font-medium text-ink-700">
          {label}
        </label>
      )}

      <button
        id={controlId}
        type="button"
        disabled={disabled}
        aria-haspopup="listbox"
        aria-expanded={open}
        onClick={() => setOpen((previous) => !previous)}
        className={cn(
          'flex min-h-10 w-full items-center gap-2 rounded-xl bg-white px-3 py-2 text-start text-sm text-ink-900 shadow-soft ring-1 ring-ink-200',
          'hover:ring-ink-300 focus:outline-none focus:ring-2 focus:ring-brand-500',
          'disabled:cursor-not-allowed disabled:opacity-50',
        )}
      >
        <span className="flex min-w-0 flex-1 flex-wrap gap-1">
          {selected.length === 0 ? (
            <span className="text-ink-300">{placeholder ?? t('common.all')}</span>
          ) : (
            selected.map((option) => (
              <span
                key={option.value}
                className="inline-flex max-w-full items-center gap-1 rounded-md bg-ink-950/5 px-1.5 py-0.5 text-xs font-medium text-ink-800"
              >
                <span className="truncate">{option.label}</span>
                <span
                  role="button"
                  tabIndex={-1}
                  className="rounded p-0.5 hover:bg-ink-200/60"
                  onClick={(event) => {
                    event.stopPropagation();
                    toggle(option.value);
                  }}
                  onKeyDown={(event) => {
                    if (event.key === 'Enter' || event.key === ' ') {
                      event.preventDefault();
                      event.stopPropagation();
                      toggle(option.value);
                    }
                  }}
                  aria-label={t('common.delete')}
                >
                  <X className="size-3" aria-hidden />
                </span>
              </span>
            ))
          )}
        </span>
        <ChevronsUpDown className="size-4 shrink-0 text-ink-400" aria-hidden />
      </button>

      {open && (
        <ul
          role="listbox"
          aria-multiselectable
          className="absolute inset-x-0 top-[calc(100%+0.35rem)] z-40 max-h-56 overflow-auto rounded-xl bg-white p-1 shadow-lift ring-1 ring-ink-100"
        >
          {options.length === 0 ? (
            <li className="px-3 py-2 text-sm text-ink-400">{t('table.empty')}</li>
          ) : (
            options.map((option) => {
              const checked = value.includes(option.value);
              return (
                <li key={option.value}>
                  <button
                    type="button"
                    role="option"
                    aria-selected={checked}
                    className={cn(
                      'flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-start text-sm hover:bg-ink-50',
                      checked && 'bg-brand-50 text-brand-800',
                    )}
                    onClick={() => toggle(option.value)}
                  >
                    <span
                      className={cn(
                        'flex size-4 shrink-0 items-center justify-center rounded border',
                        checked
                          ? 'border-brand-600 bg-brand-600 text-white'
                          : 'border-ink-300 bg-white',
                      )}
                    >
                      {checked && <Check className="size-3" aria-hidden />}
                    </span>
                    <span className="truncate">{option.label}</span>
                  </button>
                </li>
              );
            })
          )}
        </ul>
      )}
    </div>
  );
}
