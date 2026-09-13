import { Check, ChevronsUpDown, Search } from 'lucide-react';
import {
  useEffect,
  useId,
  useMemo,
  useRef,
  useState,
  type KeyboardEvent,
  type ReactNode,
} from 'react';
import { cn } from '../lib/cn';

export interface SearchableSelectOption {
  value: string;
  label: string;
  /**
   * Shown on the trigger in place of `label` once this option is selected. For narrow controls
   * whose list rows need to say more than the button has room for.
   */
  triggerLabel?: string;
  /**
   * Rendered ahead of the label, in the row and on the trigger — a country flag, for instance.
   * Decorative: the label still has to say everything the option means.
   */
  icon?: ReactNode;
  /**
   * Extra terms the search matches on beyond the label, so a list showing dial codes can still be
   * found by country name.
   */
  keywords?: string;
}

export interface SearchableSelectProps {
  id?: string;
  options: SearchableSelectOption[];
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  searchPlaceholder?: string;
  emptyMessage?: string;
  disabled?: boolean;
  invalid?: boolean;
  className?: string;
  /**
   * Sizing for the dropdown panel. It matches the trigger's width by default, which is wrong when
   * the trigger is deliberately narrow — pass a width and an edge to anchor it to, e.g.
   * `start-0 w-80`.
   */
  menuClassName?: string;
  /**
   * When true, the placeholder is repeated as the first item so the selection can be cleared.
   * Off by default: these lists back required fields, where a "Select a country" row is noise.
   */
  clearable?: boolean;
  /**
   * Called as the search box is typed into.
   *
   * For lists too long to hold in the browser: the parent fetches matches and feeds them back as
   * `options`. Internal filtering still runs over whatever arrives, so a server-filtered list is
   * narrowed no further than the same term would narrow it anyway — put anything the server
   * matched on but the label does not show (an email, a code) in `keywords`, or those rows would
   * be filtered straight back out.
   */
  onSearchChange?: (term: string) => void;
  name?: string;
  'aria-label'?: string;
}

/**
 * Searchable single-select used across the applicant app for long option lists (countries,
 * currencies, cascade lookups). Keeps the same visual chrome as the native Select.
 */
export function SearchableSelect({
  id,
  options,
  value,
  onChange,
  placeholder = 'Select…',
  searchPlaceholder = 'Search…',
  emptyMessage = 'No results',
  disabled,
  invalid,
  className,
  menuClassName,
  clearable = false,
  onSearchChange,
  name,
  'aria-label': ariaLabel,
}: SearchableSelectProps) {
  const generatedId = useId();
  const listboxId = `${id ?? generatedId}-listbox`;
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  const [highlight, setHighlight] = useState(0);
  const rootRef = useRef<HTMLDivElement>(null);
  const searchRef = useRef<HTMLInputElement>(null);

  const selected = options.find((option) => option.value === value);

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
    // Focus the search field when the list opens so typing filters immediately.
    const handle = requestAnimationFrame(() => searchRef.current?.focus());
    return () => cancelAnimationFrame(handle);
  }, [open]);

  useEffect(() => {
    setHighlight(0);
  }, [query]);

  function choose(next: string) {
    onChange(next);
    setOpen(false);
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
      if (option) choose(option.value);
    }
  }

  return (
    <div ref={rootRef} className={cn('relative w-full', className)}>
      {name && <input type="hidden" name={name} value={value} readOnly />}

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
          'flex h-11 w-full items-center gap-2 rounded-xl border-0 bg-white px-3.5 text-start text-sm text-ink-900',
          'ring-1 ring-ink-200 shadow-soft transition-shadow',
          'focus:outline-none focus:ring-2 focus:ring-brand-500',
          'disabled:cursor-not-allowed disabled:bg-ink-50 disabled:opacity-60',
          invalid && 'ring-red-300 focus:ring-red-500',
        )}
      >
        {selected?.icon}
        <span className={cn('min-w-0 flex-1 truncate', !selected && 'text-ink-300')}>
          {selected ? (selected.triggerLabel ?? selected.label) : placeholder}
        </span>
        <ChevronsUpDown className="size-4 shrink-0 text-ink-400" aria-hidden />
      </button>

      {open && (
        <div
          className={cn(
            'absolute top-[calc(100%+0.35rem)] z-50 overflow-hidden rounded-xl bg-white shadow-lift ring-1 ring-ink-100',
            menuClassName ?? 'inset-x-0',
          )}
        >
          <div className="relative border-b border-ink-100 p-2">
            <Search
              className="pointer-events-none absolute inset-inline-start-4 top-1/2 size-4 -translate-y-1/2 text-ink-300"
              aria-hidden
            />
            <input
              ref={searchRef}
              type="search"
              value={query}
              onChange={(event) => {
                setQuery(event.target.value);
                onSearchChange?.(event.target.value);
              }}
              onKeyDown={onSearchKeyDown}
              placeholder={searchPlaceholder}
              aria-label={searchPlaceholder}
              className="h-9 w-full rounded-lg bg-ink-50 ps-9 pe-3 text-sm text-ink-900 placeholder:text-ink-300 focus:outline-none focus:ring-2 focus:ring-brand-500"
            />
          </div>

          <ul
            id={listboxId}
            role="listbox"
            aria-activedescendant={
              filtered[highlight] ? `${listboxId}-option-${filtered[highlight]!.value}` : undefined
            }
            className="max-h-56 overflow-auto p-1"
          >
            {clearable && (
              <li role="presentation">
                <button
                  type="button"
                  role="option"
                  aria-selected={value === ''}
                  className={cn(
                    'flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-start text-sm text-ink-400 hover:bg-ink-50',
                    value === '' && 'bg-brand-50 text-brand-800',
                  )}
                  onClick={() => choose('')}
                >
                  {placeholder}
                </button>
              </li>
            )}

            {filtered.length === 0 ? (
              <li className="px-3 py-2 text-sm text-ink-400">{emptyMessage}</li>
            ) : (
              filtered.map((option, index) => {
                const active = option.value === value;
                const highlighted = index === highlight;
                return (
                  <li key={option.value} role="presentation">
                    <button
                      type="button"
                      id={`${listboxId}-option-${option.value}`}
                      role="option"
                      aria-selected={active}
                      className={cn(
                        'flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-start text-sm hover:bg-ink-50',
                        (active || highlighted) && 'bg-brand-50 text-brand-800',
                      )}
                      onMouseEnter={() => setHighlight(index)}
                      onClick={() => choose(option.value)}
                    >
                      {option.icon}
                      <span className="min-w-0 flex-1 truncate">{option.label}</span>
                      {active && <Check className="size-4 shrink-0" aria-hidden />}
                    </button>
                  </li>
                );
              })
            )}
          </ul>
        </div>
      )}
    </div>
  );
}
