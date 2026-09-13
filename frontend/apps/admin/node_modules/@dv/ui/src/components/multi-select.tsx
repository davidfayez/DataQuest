import { Check, ChevronsUpDown, Search, X } from 'lucide-react';
import { useEffect, useId, useMemo, useRef, useState, type KeyboardEvent } from 'react';
import { cn } from '../lib/cn';

export interface MultiSelectOption {
  value: string;
  label: string;
  /** Extra text the search matches on but does not display, e.g. a code or a parent name. */
  keywords?: string;
}

export interface MultiSelectProps {
  id?: string;
  options: MultiSelectOption[];
  values: string[];
  onChange: (values: string[]) => void;
  placeholder?: string;
  searchPlaceholder?: string;
  emptyMessage?: string;
  /** Rendered in the trigger once more than this many are picked, instead of every label. */
  summaryThreshold?: number;
  summaryLabel?: (count: number) => string;
  disabled?: boolean;
  invalid?: boolean;
  className?: string;
  'aria-label'?: string;
}

/**
 * Searchable multi-select. The dropdown twin of {@link SearchableSelect}, sharing its chrome and
 * keyboard model, with checkboxes instead of single choice.
 *
 * Picked options are shown as removable chips in the trigger so a long selection stays legible
 * without opening the list; past a threshold they collapse to a count.
 */
export function MultiSelect({
  id,
  options,
  values,
  onChange,
  placeholder = 'Select…',
  searchPlaceholder = 'Search…',
  emptyMessage = 'No results',
  summaryThreshold = 3,
  summaryLabel,
  disabled,
  invalid,
  className,
  'aria-label': ariaLabel,
}: MultiSelectProps) {
  const generatedId = useId();
  const listboxId = `${id ?? generatedId}-listbox`;
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  const [highlight, setHighlight] = useState(0);
  const rootRef = useRef<HTMLDivElement>(null);
  const searchRef = useRef<HTMLInputElement>(null);

  const selectedSet = useMemo(() => new Set(values), [values]);
  const selected = useMemo(
    () => options.filter((option) => selectedSet.has(option.value)),
    [options, selectedSet],
  );

  const filtered = useMemo(() => {
    const term = query.trim().toLowerCase();
    if (!term) return options;
    return options.filter((option) =>
      `${option.label} ${option.keywords ?? ''}`.toLowerCase().includes(term),
    );
  }, [options, query]);

  useEffect(() => {
    if (!open) return;
    function onPointerDown(event: MouseEvent) {
      if (!rootRef.current?.contains(event.target as Node)) setOpen(false);
    }
    document.addEventListener('mousedown', onPointerDown);
    return () => document.removeEventListener('mousedown', onPointerDown);
  }, [open]);

  useEffect(() => {
    if (!open) {
      setQuery('');
      setHighlight(0);
      return;
    }
    const handle = requestAnimationFrame(() => searchRef.current?.focus());
    return () => cancelAnimationFrame(handle);
  }, [open]);

  useEffect(() => {
    setHighlight(0);
  }, [query]);

  /** The list stays open on pick: choosing several in a row is the whole point. */
  function toggle(value: string) {
    onChange(
      selectedSet.has(value) ? values.filter((entry) => entry !== value) : [...values, value],
    );
  }

  function onTriggerKeyDown(event: KeyboardEvent<HTMLButtonElement>) {
    if (disabled) return;
    if (event.key === 'ArrowDown' || event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      setOpen(true);
    }
  }

  function onSearchKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === 'Escape') {
      event.preventDefault();
      // Stopped here, or the key reaches a surrounding <dialog> and closes the whole form as
      // well as the dropdown.
      event.stopPropagation();
      setOpen(false);
      return;
    }
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      setHighlight((index) => Math.min(index + 1, Math.max(filtered.length - 1, 0)));
      return;
    }
    if (event.key === 'ArrowUp') {
      event.preventDefault();
      setHighlight((index) => Math.max(index - 1, 0));
      return;
    }
    if (event.key === 'Enter') {
      event.preventDefault();
      const option = filtered[highlight];
      if (option) toggle(option.value);
    }
  }

  const showChips = selected.length > 0 && selected.length <= summaryThreshold;

  return (
    <div ref={rootRef} className={cn('relative w-full', className)}>
      <button
        id={id}
        type="button"
        disabled={disabled}
        aria-label={ariaLabel}
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-controls={listboxId}
        aria-invalid={invalid || undefined}
        onClick={() => !disabled && setOpen((previous) => !previous)}
        onKeyDown={onTriggerKeyDown}
        className={cn(
          'flex min-h-11 w-full items-center gap-2 rounded-xl border-0 bg-white px-3.5 py-1.5 text-start text-sm text-ink-900',
          'ring-1 ring-ink-200 shadow-soft transition-shadow',
          'focus:outline-none focus:ring-2 focus:ring-brand-500',
          'disabled:cursor-not-allowed disabled:bg-ink-50 disabled:opacity-60',
          invalid && 'ring-red-300 focus:ring-red-500',
        )}
      >
        <span className="flex min-w-0 flex-1 flex-wrap items-center gap-1.5">
          {selected.length === 0 && <span className="text-ink-300">{placeholder}</span>}

          {showChips &&
            selected.map((option) => (
              <span
                key={option.value}
                className="inline-flex max-w-full items-center gap-1 rounded-md bg-brand-50 px-1.5 py-0.5 text-xs text-brand-800 ring-1 ring-brand-200"
              >
                <span className="truncate">{option.label}</span>
                {/* A span, not a button: this sits inside the trigger button, and nesting
                    interactive elements is invalid HTML. */}
                <span
                  role="button"
                  tabIndex={-1}
                  aria-label={`Remove ${option.label}`}
                  onClick={(event) => {
                    event.stopPropagation();
                    toggle(option.value);
                  }}
                  className="cursor-pointer rounded p-0.5 hover:bg-brand-100"
                >
                  <X className="size-3" aria-hidden />
                </span>
              </span>
            ))}

          {selected.length > summaryThreshold && (
            <span>{summaryLabel?.(selected.length) ?? `${selected.length} selected`}</span>
          )}
        </span>

        <ChevronsUpDown className="size-4 shrink-0 text-ink-400" aria-hidden />
      </button>

      {open && (
        <div className="absolute inset-x-0 top-[calc(100%+0.35rem)] z-50 overflow-hidden rounded-xl bg-white shadow-lift ring-1 ring-ink-100">
          <div className="relative border-b border-ink-100 p-2">
            <Search
              className="pointer-events-none absolute inset-inline-start-4 top-1/2 size-4 -translate-y-1/2 text-ink-300"
              aria-hidden
            />
            <input
              ref={searchRef}
              type="search"
              value={query}
              onChange={(event) => setQuery(event.target.value)}
              onKeyDown={onSearchKeyDown}
              placeholder={searchPlaceholder}
              aria-label={searchPlaceholder}
              className="h-9 w-full rounded-lg bg-ink-50 ps-9 pe-3 text-sm text-ink-900 placeholder:text-ink-300 focus:outline-none focus:ring-2 focus:ring-brand-500"
            />
          </div>

          <ul id={listboxId} role="listbox" aria-multiselectable className="max-h-56 overflow-auto p-1">
            {filtered.length === 0 && (
              <li className="px-3 py-2 text-sm text-ink-400">{emptyMessage}</li>
            )}

            {filtered.map((option, index) => {
              const checked = selectedSet.has(option.value);
              return (
                <li key={option.value}>
                  <button
                    type="button"
                    role="option"
                    aria-selected={checked}
                    id={`${listboxId}-option-${option.value}`}
                    onMouseEnter={() => setHighlight(index)}
                    onClick={() => toggle(option.value)}
                    className={cn(
                      'flex w-full items-center gap-2 rounded-lg px-3 py-2 text-start text-sm',
                      index === highlight ? 'bg-ink-50 text-ink-950' : 'text-ink-700',
                    )}
                  >
                    <span
                      className={cn(
                        'flex size-4 shrink-0 items-center justify-center rounded border',
                        checked ? 'border-brand-500 bg-brand-500 text-white' : 'border-ink-300',
                      )}
                      aria-hidden
                    >
                      {checked && <Check className="size-3" />}
                    </span>
                    <span className="min-w-0 flex-1 truncate">{option.label}</span>
                  </button>
                </li>
              );
            })}
          </ul>
        </div>
      )}
    </div>
  );
}
