import { Field, Input } from '@dv/ui';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useAddressees, type AddresseeDto } from '@/features/lookups/api';
import { ConfirmDialog, CrudDialog } from '@/shared/ui/CrudDialog';
import { DataTable } from '@/shared/ui/DataTable';
import { NewButton } from '../LookupsPage';
import { useLookupTab } from '../useLookupTab';
import { LocalizedNameFields, Notice } from './shared';
import { actionColumn, nameColumns, type LookupCaps } from './columns';

interface AddresseeBody {
  id?: string;
  nameAr: string;
  nameEn: string;
  sortOrder: number;
  isActive: boolean;
}

const EMPTY: AddresseeBody = { nameAr: '', nameEn: '', sortOrder: 0, isActive: true };

/**
 * The list offered on a new application's "Addressed to" field.
 *
 * Edited in a dialog rather than on its own page: an entry is two names, a position and a flag,
 * which is little enough to fit in a popup. It is also the one lookup that constrains nothing —
 * an application stores the text it was addressed to, and an applicant who cannot find their
 * addressee here types their own, so removing an entry changes only what is suggested next.
 */
export function AddresseesTab({ caps }: { caps: LookupCaps }) {
  const { t } = useTranslation();
  const tab = useLookupTab<AddresseeDto, AddresseeBody>('addressees');
  const list = useAddressees(tab.params);

  const [form, setForm] = useState<AddresseeBody>(EMPTY);

  // The dialog opens either empty or filled from the row being edited.
  useEffect(() => {
    if (!tab.isDialogOpen) return;

    setForm(
      tab.editing
        ? {
            id: tab.editing.id,
            nameAr: tab.editing.nameAr,
            nameEn: tab.editing.nameEn,
            sortOrder: tab.editing.sortOrder,
            isActive: tab.editing.isActive,
          }
        : EMPTY,
    );
  }, [tab.isDialogOpen, tab.editing]);

  const canSubmit = form.nameAr.trim() !== '' && form.nameEn.trim() !== '';

  return (
    <div className="space-y-4">
      <Notice message={tab.notice} onDismiss={tab.dismissNotice} />

      <DataTable
        data={list.data}
        isPending={list.isPending}
        rowKey={(row) => row.id}
        onSearch={tab.setSearch}
        onPageChange={tab.setPage}
        onPageSizeChange={tab.setPageSize}
        onSortChange={tab.setSort}
        toolbar={
          caps.canCreate ? (
            <NewButton
              label={t('lookups.createTitle', { entity: t('lookups.addressees') })}
              onClick={() => tab.setIsCreating(true)}
            />
          ) : null
        }
        columns={[
          ...nameColumns<AddresseeDto>(t),
          {
            key: 'sortOrder',
            header: t('lookups.sortOrder'),
            align: 'center',
            getValue: (row) => row.sortOrder,
            render: (row) => <span className="text-xs text-muted-foreground">{row.sortOrder}</span>,
          },
          actionColumn<AddresseeDto>(
            t,
            caps,
            tab.setEditing,
            tab.setToDelete,
            (row) => row.nameEn,
          ),
        ]}
      />

      <CrudDialog
        open={tab.isDialogOpen}
        title={
          tab.editing
            ? t('lookups.editTitle', { entity: t('lookups.addressees') })
            : t('lookups.createTitle', { entity: t('lookups.addressees') })
        }
        onClose={tab.closeDialog}
        onSubmit={() =>
          tab.submit({
            ...form,
            nameAr: form.nameAr.trim(),
            nameEn: form.nameEn.trim(),
          })
        }
        isPending={tab.save.isPending}
        error={tab.save.error}
        canSubmit={canSubmit}
      >
        <LocalizedNameFields
          nameAr={form.nameAr}
          nameEn={form.nameEn}
          isActive={form.isActive}
          onChange={(patch) => setForm((current) => ({ ...current, ...patch }))}
        />

        <Field label={t('lookups.sortOrder')} htmlFor="sortOrder" hint={t('lookups.sortOrderHint')}>
          <Input
            id="sortOrder"
            type="number"
            dir="ltr"
            min={0}
            max={10000}
            value={form.sortOrder}
            onChange={(event) =>
              setForm((current) => ({ ...current, sortOrder: Number(event.target.value) || 0 }))
            }
            data-testid="addressee-sort-order"
          />
        </Field>
      </CrudDialog>

      <ConfirmDialog
        open={tab.toDelete !== null}
        title={t('lookups.deleteTitle')}
        body={t('lookups.deleteBody')}
        confirmLabel={t('common.delete')}
        onClose={() => tab.setToDelete(null)}
        onConfirm={tab.confirmDelete}
        isPending={tab.remove.isPending}
        destructive
      />
    </div>
  );
}
