import type { LookupBase } from '@/features/lookups/api';
import type { Column } from '@/shared/ui/DataTable';
import { RowActions } from '@/shared/ui/RowActions';

/**
 * Column builders shared by the lookup tables. Kept apart from the components in `shared.tsx`
 * because a file that exports both components and plain functions breaks React Fast Refresh.
 */

/** Name and active-state columns, common to every lookup. */
export function nameColumns<T extends LookupBase>(t: (key: string) => string): Column<T>[] {
  return [
    {
      key: 'nameEn',
      header: t('lookups.nameEn'),
      getValue: (row) => row.nameEn,
      render: (row) => <span className="font-medium">{row.nameEn}</span>,
    },
    {
      key: 'nameAr',
      header: t('lookups.nameAr'),
      getValue: (row) => row.nameAr,
      render: (row) => <span dir="rtl">{row.nameAr}</span>,
    },
    {
      key: 'isActive',
      header: t('lookups.isActive'),
      align: 'center',
      getValue: (row) => (row.isActive ? '1' : '0'),
      render: (row) => (
        <span className={row.isActive ? 'text-success' : 'text-muted-foreground'}>
          {row.isActive ? t('common.active') : t('common.inactive')}
        </span>
      ),
    },
  ];
}

/** Per-action capabilities for a lookup page, from the caller's granular permissions. */
export interface LookupCaps {
  canCreate: boolean;
  canUpdate: boolean;
  canDelete: boolean;
}

/** Icon-only edit/delete column with no header label. */
export function actionColumn<T extends { id: string }>(
  _t: (key: string) => string,
  caps: LookupCaps,
  onEdit: (row: T) => void,
  onDelete: (row: T) => void,
  testIdOf: (row: T) => string,
): Column<T> {
  return {
    key: 'actions',
    header: '',
    align: 'end',
    sortable: false,
    filterable: false,
    render: (row) =>
      caps.canUpdate || caps.canDelete ? (
        <RowActions
          onEdit={caps.canUpdate ? () => onEdit(row) : undefined}
          onDelete={caps.canDelete ? () => onDelete(row) : undefined}
          editTestId={`edit-${testIdOf(row)}`}
          deleteTestId={`delete-${testIdOf(row)}`}
        />
      ) : null,
  };
}
