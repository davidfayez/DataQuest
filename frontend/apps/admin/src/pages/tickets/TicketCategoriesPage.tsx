import { AdminPageHeader, Alert, Button, Field, Input, Select } from '@dv/ui';
import { Plus } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useAdminSession } from '@/features/auth/useAdminSession';
import type { ListParams } from '@/features/lookups/api';
import {
  useDeleteTicketCategory,
  useSaveTicketCategory,
  useTicketCategories,
  type TicketCategoryDto,
} from '@/features/tickets/api';
import { ConfirmDialog, CrudDialog } from '@/shared/ui/CrudDialog';
import { DataTable } from '@/shared/ui/DataTable';
import { actionColumn, nameColumns, type LookupCaps } from '../lookups/tabs/columns';
import { Notice } from '../lookups/tabs/shared';

interface CategoryForm {
  id?: string;
  nameAr: string;
  nameEn: string;
  sortOrder: number;
  isActive: boolean;
}

const BLANK: CategoryForm = { nameAr: '', nameEn: '', sortOrder: 0, isActive: true };

/**
 * The subjects the public contact form offers.
 *
 * An ordinary lookup — edited in a dialog like every other one on the platform, because it is four
 * fields rather than a screenful. What it decides is not ordinary though: this list is the first
 * thing someone outside the platform sees when they need help, so a category retired here stops
 * being offered immediately while the tickets already filed under it keep their subject.
 */
export function TicketCategoriesPage() {
  const { t } = useTranslation();
  const session = useAdminSession();

  const has = (name: string) => session?.permissions.has(name) ?? false;
  const caps: LookupCaps = {
    canCreate: has('TicketCategories.Create'),
    canUpdate: has('TicketCategories.Update'),
    canDelete: has('TicketCategories.Delete'),
  };

  const [params, setParams] = useState<ListParams>({ page: 1, pageSize: 25 });
  const [form, setForm] = useState<CategoryForm | null>(null);
  const [toDelete, setToDelete] = useState<TicketCategoryDto | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const list = useTicketCategories(params);
  const save = useSaveTicketCategory();
  const remove = useDeleteTicketCategory();

  function submit() {
    if (!form) return;

    save.mutate(form, {
      onSuccess: () => {
        setForm(null);
        setNotice(t('lookups.saved'));
      },
    });
  }

  function confirmDelete() {
    if (!toDelete) return;

    remove.mutate(toDelete.id, {
      onSuccess: (outcome) => {
        setToDelete(null);
        // Tickets already filed under it keep their category, so the API deactivates instead.
        setNotice(outcome === 1 ? t('lookups.deactivated') : t('lookups.deleted'));
      },
    });
  }

  const canManageAny = caps.canCreate || caps.canUpdate || caps.canDelete;

  return (
    <div className="animate-fade-in space-y-6">
      <AdminPageHeader
        title={t('ticketCategories.title')}
        subtitle={t('ticketCategories.subtitle')}
      />

      {!canManageAny && <Alert variant="info">{t('roles.viewOnlyNotice')}</Alert>}

      <Notice message={notice} onDismiss={() => setNotice(null)} />

      <DataTable
        data={list.data}
        isPending={list.isPending}
        rowKey={(row) => row.id}
        onSearch={(search) => setParams((p) => ({ ...p, search, page: 1 }))}
        onPageChange={(page) => setParams((p) => ({ ...p, page }))}
        onPageSizeChange={(pageSize) => setParams((p) => ({ ...p, pageSize, page: 1 }))}
        // Back to the first page: the row that sorts first belongs on page one.
        onSortChange={(sort) =>
          setParams((p) => ({
            ...p,
            sortBy: sort?.key,
            sortDescending: sort?.descending,
            page: 1,
          }))
        }
        toolbar={
          caps.canCreate ? (
            <Button onClick={() => setForm({ ...BLANK })} data-testid="category-new">
              <Plus className="size-4" aria-hidden="true" />
              {t('ticketCategories.newCategory')}
            </Button>
          ) : null
        }
        columns={[
          ...nameColumns<TicketCategoryDto>(t),
          {
            key: 'sortOrder',
            header: t('lookups.sortOrder'),
            getValue: (row) => row.sortOrder,
            render: (row) => row.sortOrder,
          },
          actionColumn<TicketCategoryDto>(
            t,
            caps,
            (row) =>
              setForm({
                id: row.id,
                nameAr: row.nameAr,
                nameEn: row.nameEn,
                sortOrder: row.sortOrder,
                isActive: row.isActive,
              }),
            setToDelete,
            (row) => row.nameEn,
          ),
        ]}
      />

      <CrudDialog
        open={form !== null}
        title={form?.id ? t('ticketCategories.editCategory') : t('ticketCategories.newCategory')}
        onClose={() => setForm(null)}
        onSubmit={submit}
        isPending={save.isPending}
        error={save.error}
        canSubmit={Boolean(form?.nameEn.trim() && form?.nameAr.trim())}
      >
        {form && (
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label={t('lookups.nameEn')} htmlFor="category-name-en" required>
              <Input
                id="category-name-en"
                dir="ltr"
                value={form.nameEn}
                onChange={(event) => setForm({ ...form, nameEn: event.target.value })}
                data-testid="category-name-en"
              />
            </Field>

            <Field label={t('lookups.nameAr')} htmlFor="category-name-ar" required>
              <Input
                id="category-name-ar"
                dir="rtl"
                value={form.nameAr}
                onChange={(event) => setForm({ ...form, nameAr: event.target.value })}
                data-testid="category-name-ar"
              />
            </Field>

            <Field
              label={t('lookups.sortOrder')}
              htmlFor="category-sort"
              hint={t('ticketCategories.sortHint')}
            >
              <Input
                id="category-sort"
                type="number"
                min={0}
                value={form.sortOrder}
                onChange={(event) =>
                  setForm({ ...form, sortOrder: Number(event.target.value) || 0 })
                }
                data-testid="category-sort"
              />
            </Field>

            <Field
              label={t('lookups.status')}
              htmlFor="category-active"
              hint={t('ticketCategories.activeHint')}
            >
              <Select
                id="category-active"
                value={form.isActive ? 'true' : 'false'}
                onChange={(event) =>
                  setForm({ ...form, isActive: event.target.value === 'true' })
                }
                data-testid="category-active"
              >
                <option value="true">{t('lookups.active')}</option>
                <option value="false">{t('lookups.inactive')}</option>
              </Select>
            </Field>
          </div>
        )}
      </CrudDialog>

      <ConfirmDialog
        open={toDelete !== null}
        title={t('ticketCategories.deleteTitle')}
        body={t('ticketCategories.deleteBody')}
        confirmLabel={t('common.delete')}
        onClose={() => setToDelete(null)}
        onConfirm={confirmDelete}
        isPending={remove.isPending}
        destructive
      />
    </div>
  );
}
