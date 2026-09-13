import { Field, Input } from '@dv/ui';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { adminSession, Permissions } from '@/features/auth/session';
import {
  useClients,
  useDeleteClient,
  useSaveClient,
  type ClientDto,
  type ClientListParams,
  type DeleteOutcome,
  type UpsertClientBody,
} from '@/features/clients/api';
import { NewButton } from '@/pages/lookups/LookupsPage';
import { Notice } from '@/pages/lookups/tabs/shared';
import { ConfirmDialog, CrudDialog } from '@/shared/ui/CrudDialog';
import { DataTable, type Column } from '@/shared/ui/DataTable';
import { RowActions } from '@/shared/ui/RowActions';

const EMPTY: UpsertClientBody = { code: '', name: '', isActive: true, isLocalOrder: false };

export function ClientsPage() {
  const { t } = useTranslation();

  const canCreate = adminSession.has(Permissions.ClientsCreate);
  const canUpdate = adminSession.has(Permissions.ClientsUpdate);
  const canDelete = adminSession.has(Permissions.ClientsDelete);

  const [params, setParams] = useState<ClientListParams>({ page: 1, pageSize: 25 });
  const list = useClients(params);

  const save = useSaveClient();
  const remove = useDeleteClient();

  const [editing, setEditing] = useState<ClientDto | null>(null);
  const [isCreating, setIsCreating] = useState(false);
  const [form, setForm] = useState<UpsertClientBody>(EMPTY);
  const [toDelete, setToDelete] = useState<ClientDto | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const isDialogOpen = isCreating || editing !== null;

  useEffect(() => {
    if (editing) {
      setForm({
        id: editing.id,
        code: editing.code,
        name: editing.name,
        isActive: editing.isActive,
        isLocalOrder: editing.isLocalOrder,
      });
    } else if (isCreating) {
      setForm(EMPTY);
    }
  }, [editing, isCreating]);

  function closeDialog() {
    setEditing(null);
    setIsCreating(false);
    save.reset();
  }

  function submit() {
    save.mutate(form, {
      onSuccess: () => {
        closeDialog();
        setNotice(t('lookups.saved'));
      },
    });
  }

  function confirmDelete() {
    if (!toDelete) return;
    remove.mutate(toDelete.id, {
      onSuccess: (outcome: DeleteOutcome) => {
        setToDelete(null);
        setNotice(outcome === 1 ? t('lookups.deactivated') : t('lookups.deleted'));
      },
    });
  }

  const columns: Column<ClientDto>[] = [
    {
      key: 'code',
      header: t('clients.code'),
      getValue: (row) => row.code,
      render: (row) => <span className="font-mono text-xs font-semibold">{row.code}</span>,
    },
    {
      key: 'name',
      header: t('clients.name'),
      getValue: (row) => row.name,
      render: (row) => <span className="font-medium">{row.name}</span>,
    },
    {
      key: 'orderCount',
      header: t('clients.orders'),
      align: 'center',
      getValue: (row) => row.orderCount,
      render: (row) => <span>{row.orderCount}</span>,
    },
    {
      key: 'isLocalOrder',
      header: t('clients.localOrder'),
      align: 'center',
      getValue: (row) => (row.isLocalOrder ? '1' : '0'),
      // Only the one client that holds it is marked; a column of "no" against every other row
      // says nothing worth the width.
      render: (row) =>
        row.isLocalOrder ? (
          <span
            className="inline-block rounded-md bg-brand-50 px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide text-brand-700"
            data-testid={`local-order-${row.code}`}
          >
            {t('clients.localOrder')}
          </span>
        ) : null,
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
    {
      key: 'actions',
      header: '',
      align: 'end',
      sortable: false,
      filterable: false,
      render: (row) =>
        canUpdate || canDelete ? (
          <RowActions
            onEdit={canUpdate ? () => setEditing(row) : undefined}
            onDelete={canDelete ? () => setToDelete(row) : undefined}
            editTestId={`edit-${row.code}`}
            deleteTestId={`delete-${row.code}`}
          />
        ) : null,
    },
  ];

  return (
    <div className="space-y-4">
      <Notice message={notice} onDismiss={() => setNotice(null)} />

      <p className="max-w-3xl text-sm text-ink-500">{t('clients.intro')}</p>

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
        columns={columns}
        toolbar={
          canCreate ? (
            <NewButton
              label={t('lookups.createTitle', { entity: t('clients.entity') })}
              onClick={() => setIsCreating(true)}
            />
          ) : null
        }
      />

      <CrudDialog
        open={isDialogOpen}
        title={
          editing
            ? t('lookups.editTitle', { entity: t('clients.entity') })
            : t('lookups.createTitle', { entity: t('clients.entity') })
        }
        onClose={closeDialog}
        onSubmit={submit}
        isPending={save.isPending}
        error={save.error}
      >
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t('clients.code')} htmlFor="client-code" required>
            <Input
              id="client-code"
              dir="ltr"
              value={form.code}
              onChange={(event) => setForm({ ...form, code: event.target.value })}
            />
          </Field>

          <Field label={t('clients.name')} htmlFor="client-name" required>
            <Input
              id="client-name"
              value={form.name}
              onChange={(event) => setForm({ ...form, name: event.target.value })}
            />
          </Field>
        </div>

        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            className="size-4 rounded border-border"
            checked={form.isActive}
            onChange={(event) => setForm({ ...form, isActive: event.target.checked })}
          />
          {t('lookups.isActive')}
        </label>

        {/* Exactly one client is the local one, so this only ever moves. Ticking it takes the flag
            from whoever holds it; the client that already holds it cannot untick — there would
            then be none — which is why the box is fixed once it is on. */}
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            className="size-4 rounded border-border"
            checked={form.isLocalOrder}
            disabled={editing?.isLocalOrder}
            onChange={(event) => setForm({ ...form, isLocalOrder: event.target.checked })}
            data-testid="client-local-order"
          />
          {t('clients.localOrder')}
        </label>

        <p className="text-xs text-subtle">
          {editing?.isLocalOrder ? t('clients.localOrderCurrent') : t('clients.localOrderHint')}
        </p>
      </CrudDialog>

      <ConfirmDialog
        open={toDelete !== null}
        title={t('lookups.deleteTitle')}
        body={t('lookups.deleteBody')}
        confirmLabel={t('common.delete')}
        onClose={() => setToDelete(null)}
        onConfirm={confirmDelete}
        isPending={remove.isPending}
        destructive
      />
    </div>
  );
}
