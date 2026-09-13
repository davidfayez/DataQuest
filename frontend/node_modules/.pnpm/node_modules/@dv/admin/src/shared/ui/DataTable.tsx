import { Button, Input, LoadingState, cn } from '@dv/ui';
import {
  flexRender,
  getCoreRowModel,
  getFilteredRowModel,
  getSortedRowModel,
  useReactTable,
  type ColumnDef,
  type ColumnFiltersState,
  type SortingState,
} from '@tanstack/react-table';
import {
  ArrowDown,
  ArrowUp,
  ArrowUpDown,
  ChevronLeft,
  ChevronRight,
  ChevronsLeft,
  ChevronsRight,
  Search,
} from 'lucide-react';
import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';

export interface Column<T> {
  key: string;
  header: string;
  /** Cell renderer. Returning a node keeps formatting decisions with the column. */
  render: (row: T) => ReactNode;
  align?: 'start' | 'end' | 'center';
  className?: string;
  /**
   * Value used for client-side sort and column filter. Defaults to `row[key]` when the key
   * matches a property on the row. Pass explicitly for computed / nested columns.
   */
  getValue?: (row: T) => unknown;
  /** Defaults to true for non-action columns that have a resolvable value. */
  sortable?: boolean;
  /** Defaults to true for non-action columns that have a resolvable value. */
  filterable?: boolean;
}

/**
 * The sort half of a list's query parameters. Sorting is done by the server across the whole list,
 * so every paged list carries these alongside its page and search.
 */
export interface SortParams {
  /** Column key, as the table knows it. Undefined leaves the handler's own ordering in place. */
  sortBy?: string;
  sortDescending?: boolean;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasPrevious: boolean;
  hasNext: boolean;
}

interface Props<T> {
  columns: Column<T>[];
  data: PagedResult<T> | undefined;
  isPending: boolean;
  rowKey: (row: T) => string;
  onSearch?: (term: string) => void;
  onPageChange?: (page: number) => void;
  /** When set, the footer offers a page-size selector that round-trips to the server. */
  onPageSizeChange?: (pageSize: number) => void;
  /**
   * When set, sorting is done by the server across the whole list rather than in the browser
   * across the page on screen. Called with null when the sort is cleared.
   *
   * A list that is fetched whole — no paging — can leave this off and sort locally, which is
   * correct there because the browser already holds every row.
   */
  onSortChange?: (sort: { key: string; descending: boolean } | null) => void;
  toolbar?: ReactNode;
  filters?: ReactNode;
  emptyMessage?: string;
}

const PAGE_SIZE_OPTIONS = [10, 25, 50, 100] as const;

function resolveValue<T>(column: Column<T>, row: T): unknown {
  if (column.getValue) return column.getValue(row);
  if (row && typeof row === 'object' && column.key in (row as object)) {
    return (row as Record<string, unknown>)[column.key];
  }
  return undefined;
}

function isActionColumn<T>(column: Column<T>): boolean {
  return column.key === 'actions';
}

/**
 * Shared admin data grid built on TanStack Table.
 *
 * Server-driven paging + global search stay as they are; sorting and per-column filters run on
 * the loaded page so every list gets those features without backend changes. The pagination bar
 * always renders when paging is wired (even for a single page), which was the previous silent
 * failure mode when `totalPages <= 1`.
 */
export function DataTable<T>({
  columns,
  data,
  isPending,
  rowKey,
  onSearch,
  onPageChange,
  onPageSizeChange,
  onSortChange,
  toolbar,
  filters,
  emptyMessage,
}: Props<T>) {
  const { t } = useTranslation();
  const [term, setTerm] = useState('');
  const [sorting, setSorting] = useState<SortingState>([]);
  const [columnFilters, setColumnFilters] = useState<ColumnFiltersState>([]);

  // Keep the latest onSearch without putting it in the debounce deps — callers typically pass an
  // inline arrow that would otherwise reset the timer on every render.
  const onSearchRef = useRef(onSearch);
  useEffect(() => {
    onSearchRef.current = onSearch;
  }, [onSearch]);

  useEffect(() => {
    if (!onSearchRef.current) return;
    const handle = setTimeout(() => onSearchRef.current?.(term), 300);
    return () => clearTimeout(handle);
  }, [term]);

  const columnDefs = useMemo<ColumnDef<T, unknown>[]>(
    () =>
      columns.map((column) => {
        const action = isActionColumn(column);
        const canResolve = Boolean(column.getValue) || !action;
        const sortable = column.sortable ?? (!action && canResolve);
        const filterable = column.filterable ?? (!action && canResolve);

        return {
          id: column.key,
          accessorFn: (row) => resolveValue(column, row),
          header: column.header,
          cell: ({ row }) => column.render(row.original),
          enableSorting: sortable,
          enableColumnFilter: filterable,
          meta: {
            align: column.align,
            className: column.className,
            isAction: action,
          },
        } satisfies ColumnDef<T, unknown>;
      }),
    [columns],
  );

  const items = data?.items ?? [];

  // Sorting one page of twenty-five in the browser reorders that page and nothing else — ask for
  // countries Z-first and you get the last of the A's. So when the caller can sort server-side,
  // the click is forwarded and the rows arrive already ordered.
  const sortsOnServer = Boolean(onSortChange);

  function changeSorting(updater: SortingState | ((old: SortingState) => SortingState)) {
    const next = typeof updater === 'function' ? updater(sorting) : updater;
    setSorting(next);

    if (!onSortChange) return;

    const first = next[0];
    onSortChange(first ? { key: first.id, descending: first.desc } : null);
  }

  const table = useReactTable({
    data: items,
    columns: columnDefs,
    state: { sorting, columnFilters },
    onSortingChange: changeSorting,
    onColumnFiltersChange: setColumnFilters,
    getCoreRowModel: getCoreRowModel(),
    getSortedRowModel: getSortedRowModel(),
    getFilteredRowModel: getFilteredRowModel(),
    getRowId: (row) => rowKey(row),
    // Paging is owned by the server, and so is sorting whenever the caller wired it up.
    manualPagination: true,
    manualSorting: sortsOnServer,
    // Always ascending on the first click. Left to itself TanStack guesses the direction from the
    // first row's value — and while a refetch has the rows empty it guesses descending, so the
    // second click cleared the sort instead of reversing it.
    sortDescFirst: false,
  });

  const page = data?.page ?? 1;
  const pageSize = data?.pageSize ?? 25;
  const totalPages = Math.max(data?.totalPages ?? 0, data && data.totalCount > 0 ? 1 : 0);
  const totalCount = data?.totalCount ?? 0;
  const hasPrevious = data?.hasPrevious ?? page > 1;
  const hasNext = data?.hasNext ?? (totalPages > 0 && page < totalPages);
  const showPager = Boolean(onPageChange && data && !isPending);

  return (
    <div className="space-y-4">
      {(onSearch || toolbar || filters) && (
        <div className="flex flex-wrap items-end justify-between gap-3">
          <div className="flex flex-wrap items-end gap-3">
            {onSearch && (
              <div className="flex flex-col gap-1.5">
                <span aria-hidden="true" className="text-sm leading-5 opacity-0">
                  {t('table.search')}
                </span>
                <div className="relative">
                  <Search
                    className="pointer-events-none absolute inset-inline-start-3 top-1/2 size-4 -translate-y-1/2 text-subtle"
                    aria-hidden="true"
                  />
                  <Input
                    value={term}
                    onChange={(event) => setTerm(event.target.value)}
                    placeholder={t('table.searchPlaceholder')}
                    aria-label={t('table.search')}
                    className="w-64 ps-9"
                    data-testid="table-search"
                  />
                </div>
              </div>
            )}
            {filters}
          </div>

          {toolbar}
        </div>
      )}

      <div className="overflow-x-auto rounded-2xl bg-white shadow-soft ring-1 ring-ink-100/80">
        <table className="w-full min-w-[42rem] text-sm">
          <thead className="border-b border-ink-100 bg-ink-950/[0.03]">
            {table.getHeaderGroups().map((headerGroup) => (
              <tr key={headerGroup.id}>
                {headerGroup.headers.map((header) => {
                  const meta = header.column.columnDef.meta as
                    | { align?: Column<T>['align']; isAction?: boolean }
                    | undefined;
                  const align = meta?.align;
                  const label = flexRender(header.column.columnDef.header, header.getContext());
                  const canSort = header.column.getCanSort();
                  const sorted = header.column.getIsSorted();

                  return (
                    <th
                      key={header.id}
                      scope="col"
                      className={cn(
                        'px-5 py-3.5 text-[11px] font-bold uppercase tracking-[0.14em] text-ink-400',
                        align === 'end' && 'text-end',
                        align === 'center' && 'text-center',
                        (!align || align === 'start') && 'text-start',
                        meta?.isAction && 'w-28',
                      )}
                    >
                      {header.isPlaceholder ? null : canSort ? (
                        <button
                          type="button"
                          className={cn(
                            'inline-flex items-center gap-1.5 whitespace-nowrap transition-colors hover:text-ink-700',
                            sorted && 'text-ink-700',
                          )}
                          onClick={header.column.getToggleSortingHandler()}
                        >
                          <span>{label}</span>
                          {sorted === 'asc' ? (
                            <ArrowUp className="size-3.5 shrink-0" aria-hidden />
                          ) : sorted === 'desc' ? (
                            <ArrowDown className="size-3.5 shrink-0" aria-hidden />
                          ) : (
                            <ArrowUpDown className="size-3.5 shrink-0 opacity-40" aria-hidden />
                          )}
                        </button>
                      ) : (
                        <span className="whitespace-nowrap">{label}</span>
                      )}
                    </th>
                  );
                })}
              </tr>
            ))}
            {/* Per-column filter row — only when at least one column is filterable. */}
            {table.getAllColumns().some((column) => column.getCanFilter()) && (
              <tr className="border-t border-ink-100/80">
                {table.getVisibleLeafColumns().map((column) => {
                  const meta = column.columnDef.meta as
                    | { align?: Column<T>['align']; isAction?: boolean }
                    | undefined;
                  if (!column.getCanFilter()) {
                    return <th key={column.id} className="px-5 pb-3 pt-0" />;
                  }
                  return (
                    <th key={column.id} className="px-5 pb-3 pt-0 font-normal normal-case tracking-normal">
                      <Input
                        value={(column.getFilterValue() as string) ?? ''}
                        onChange={(event) => column.setFilterValue(event.target.value)}
                        placeholder={t('table.filterColumn')}
                        aria-label={t('table.filterColumn')}
                        className={cn(
                          'h-8 text-xs font-normal',
                          meta?.align === 'end' && 'text-end',
                          meta?.align === 'center' && 'text-center',
                        )}
                      />
                    </th>
                  );
                })}
              </tr>
            )}
          </thead>

          <tbody>
            {isPending ? (
              <tr>
                <td colSpan={columns.length}>
                  <LoadingState label={t('common.loading')} />
                </td>
              </tr>
            ) : table.getRowModel().rows.length === 0 ? (
              <tr>
                <td
                  colSpan={columns.length}
                  className="p-10 text-center text-sm text-muted-foreground"
                >
                  {emptyMessage ?? t('table.empty')}
                </td>
              </tr>
            ) : (
              table.getRowModel().rows.map((row) => (
                <tr
                  key={row.id}
                  className="border-t border-ink-100/80 transition-colors hover:bg-ink-50/50"
                  data-testid="table-row"
                >
                  {row.getVisibleCells().map((cell) => {
                    const meta = cell.column.columnDef.meta as
                      | { align?: Column<T>['align']; className?: string }
                      | undefined;
                    return (
                      <td
                        key={cell.id}
                        className={cn(
                          'px-5 py-4 align-middle',
                          meta?.align === 'end' && 'text-end',
                          meta?.align === 'center' && 'text-center',
                          meta?.className,
                        )}
                      >
                        {flexRender(cell.column.columnDef.cell, cell.getContext())}
                      </td>
                    );
                  })}
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>

      {showPager && (
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex flex-wrap items-center gap-3">
            <p className="text-sm text-muted-foreground">
              {t('table.pageOf', { page: Math.max(page, 1), total: Math.max(totalPages, 1) })} ·{' '}
              {t('table.totalRows', { count: totalCount })}
            </p>
            {onPageSizeChange && (
              <label className="flex items-center gap-2 text-sm text-muted-foreground">
                <span>{t('table.pageSize')}</span>
                <select
                  className="h-8 rounded-lg bg-white px-2 text-sm text-ink-900 ring-1 ring-ink-200 focus:outline-none focus:ring-2 focus:ring-brand-500"
                  value={pageSize}
                  onChange={(event) => onPageSizeChange(Number(event.target.value))}
                  aria-label={t('table.pageSize')}
                >
                  {PAGE_SIZE_OPTIONS.map((size) => (
                    <option key={size} value={size}>
                      {size}
                    </option>
                  ))}
                </select>
              </label>
            )}
          </div>

          <div className="flex gap-1">
            <Button
              variant="outline"
              size="sm"
              disabled={!hasPrevious}
              onClick={() => onPageChange?.(1)}
              aria-label={t('table.first')}
            >
              <ChevronsLeft className="size-4" aria-hidden />
            </Button>
            <Button
              variant="outline"
              size="sm"
              disabled={!hasPrevious}
              onClick={() => onPageChange?.(page - 1)}
              aria-label={t('table.previous')}
            >
              <ChevronLeft className="size-4" aria-hidden />
            </Button>
            <Button
              variant="outline"
              size="sm"
              disabled={!hasNext}
              onClick={() => onPageChange?.(page + 1)}
              aria-label={t('table.next')}
            >
              <ChevronRight className="size-4" aria-hidden />
            </Button>
            <Button
              variant="outline"
              size="sm"
              disabled={!hasNext}
              onClick={() => onPageChange?.(Math.max(totalPages, 1))}
              aria-label={t('table.last')}
            >
              <ChevronsRight className="size-4" aria-hidden />
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}
