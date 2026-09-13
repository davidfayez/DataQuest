import { AdminPageHeader, Alert, Button, Field, Input, SearchableSelect, Select } from '@dv/ui';
import { Plus } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useAdminSession } from '@/features/auth/useAdminSession';
import {
  ContactEntryKind,
  useContactDirectory,
  useDeleteContactDirectoryEntry,
  useSaveContactDirectoryEntry,
  type ContactDirectoryEntryDto,
  type ContactDirectoryParams,
  type UpsertContactDirectoryEntryInput,
} from '@/features/content/contactDirectory';
import { useCountries } from '@/features/lookups/api';
import { ConfirmDialog, CrudDialog } from '@/shared/ui/CrudDialog';
import { DataTable } from '@/shared/ui/DataTable';
import { RowActions } from '@/shared/ui/RowActions';
import { Notice } from '../lookups/tabs/shared';

const BLANK: UpsertContactDirectoryEntryInput = {
  kind: ContactEntryKind.AuthorizedAgent,
  countryId: null,
  titleAr: null,
  titleEn: null,
  addressAr: null,
  addressEn: null,
  phone: null,
  email: null,
  isActive: true,
  sortOrder: 0,
};

/**
 * The agents and office details shown on the public contact page.
 *
 * One page for both groups because they are one list to maintain — an operator adding a new
 * country's agent and an operator correcting the head office address are doing the same job, and
 * splitting them into two screens would only mean two places to look.
 */
export function ContactDirectoryPage() {
  const { t } = useTranslation();
  const session = useAdminSession();

  const has = (name: string) => session?.permissions.has(name) ?? false;
  const canCreate = has('ContactDirectory.Create');
  const canUpdate = has('ContactDirectory.Update');
  const canDelete = has('ContactDirectory.Delete');

  const [params, setParams] = useState<ContactDirectoryParams>({ page: 1, pageSize: 50 });
  const [form, setForm] = useState<UpsertContactDirectoryEntryInput | null>(null);
  const [toDelete, setToDelete] = useState<ContactDirectoryEntryDto | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const list = useContactDirectory(params);
  const countries = useCountries({ page: 1, pageSize: 300, isActive: true });
  const save = useSaveContactDirectoryEntry();
  const remove = useDeleteContactDirectoryEntry();

  const isAgent = form?.kind === ContactEntryKind.AuthorizedAgent;

  // The server refuses an entry with nothing to contact by; the dialog holds the same line so the
  // refusal never arrives as a surprise on submit.
  const canSubmit = Boolean(
    form
      && (form.phone?.trim() || form.email?.trim() || form.addressEn?.trim() || form.addressAr?.trim())
      && (form.kind !== ContactEntryKind.AuthorizedAgent || form.countryId),
  );

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
      onSuccess: () => {
        setToDelete(null);
        setNotice(t('lookups.deleted'));
      },
    });
  }

  return (
    <div className="animate-fade-in space-y-6">
      <AdminPageHeader
        title={t('contactDirectory.title')}
        subtitle={t('contactDirectory.subtitle')}
      />

      {!canCreate && !canUpdate && !canDelete && (
        <Alert variant="info">{t('roles.viewOnlyNotice')}</Alert>
      )}

      <Notice message={notice} onDismiss={() => setNotice(null)} />

      <DataTable
        data={list.data}
        isPending={list.isPending}
        rowKey={(row) => row.id}
        emptyMessage={t('contactDirectory.empty')}
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
        filters={
          <Select
            aria-label={t('contactDirectory.filterKind')}
            className="w-56"
            value={params.kind === undefined ? '' : String(params.kind)}
            onChange={(event) =>
              setParams((p) => ({
                ...p,
                kind: event.target.value === '' ? undefined : (Number(event.target.value) as ContactEntryKind),
                page: 1,
              }))
            }
            data-testid="filter-kind"
          >
            <option value="">{t('contactDirectory.allKinds')}</option>
            <option value={ContactEntryKind.AuthorizedAgent}>{t('contactDirectory.kind.AuthorizedAgent')}</option>
            <option value={ContactEntryKind.Administration}>{t('contactDirectory.kind.Administration')}</option>
          </Select>
        }
        toolbar={
          canCreate ? (
            <Button onClick={() => setForm({ ...BLANK })} data-testid="directory-new">
              <Plus className="size-4" aria-hidden="true" />
              {t('contactDirectory.newEntry')}
            </Button>
          ) : null
        }
        columns={[
          {
            key: 'kindName',
            header: t('contactDirectory.kindColumn'),
            getValue: (row) => row.kindName,
            render: (row) => (
              <span className="font-medium">{t(`contactDirectory.kind.${row.kindName}`)}</span>
            ),
          },
          {
            key: 'countryName',
            header: t('lookups.country'),
            getValue: (row) => row.countryName ?? '',
            render: (row) => row.countryName ?? <span className="text-subtle">—</span>,
          },
          {
            key: 'titleEn',
            header: t('contactDirectory.name'),
            getValue: (row) => row.titleEn ?? '',
            render: (row) =>
              row.titleEn || row.titleAr ? (
                <div className="max-w-xs">
                  {row.titleEn && <p className="truncate">{row.titleEn}</p>}
                  {row.titleAr && (
                    <p className="truncate text-xs text-subtle" dir="rtl">
                      {row.titleAr}
                    </p>
                  )}
                </div>
              ) : (
                <span className="text-subtle">—</span>
              ),
          },
          {
            key: 'phone',
            header: t('contactDirectory.phone'),
            getValue: (row) => row.phone ?? '',
            render: (row) =>
              row.phone ? (
                <span className="font-mono text-sm" dir="ltr">
                  {row.phone}
                </span>
              ) : (
                <span className="text-subtle">—</span>
              ),
          },
          {
            key: 'email',
            header: t('contactDirectory.email'),
            getValue: (row) => row.email ?? '',
            render: (row) =>
              row.email ? (
                <span className="text-sm" dir="ltr">
                  {row.email}
                </span>
              ) : (
                <span className="text-subtle">—</span>
              ),
          },
          {
            key: 'isActive',
            header: t('lookups.status'),
            getValue: (row) => (row.isActive ? 'active' : 'inactive'),
            render: (row) => (
              <span className={row.isActive ? 'text-success' : 'text-subtle'}>
                {t(row.isActive ? 'lookups.active' : 'lookups.inactive')}
              </span>
            ),
          },
          {
            key: 'actions',
            header: '',
            align: 'end',
            sortable: false,
            filterable: false,
            render: (row) => (
              <RowActions
                editTestId={`edit-${row.id}`}
                deleteTestId={`delete-${row.id}`}
                onEdit={!canUpdate ? undefined : () =>
                  setForm({
                    id: row.id,
                    kind: row.kind,
                    countryId: row.countryId,
                    titleAr: row.titleAr,
                    titleEn: row.titleEn,
                    addressAr: row.addressAr,
                    addressEn: row.addressEn,
                    phone: row.phone,
                    email: row.email,
                    isActive: row.isActive,
                    sortOrder: row.sortOrder,
                  })
                }
                onDelete={!canDelete ? undefined : () => setToDelete(row)}
              />
            ),
          },
        ]}
      />

      <CrudDialog
        open={form !== null}
        title={form?.id ? t('contactDirectory.editEntry') : t('contactDirectory.newEntry')}
        onClose={() => setForm(null)}
        onSubmit={submit}
        isPending={save.isPending}
        error={save.error}
        canSubmit={canSubmit}
        wide
      >
        {form && (
          <div className="space-y-4">
            <div className="grid gap-4 sm:grid-cols-2">
              <Field label={t('contactDirectory.kindColumn')} htmlFor="entry-kind" required>
                <Select
                  id="entry-kind"
                  value={String(form.kind)}
                  onChange={(event) =>
                    setForm({ ...form, kind: Number(event.target.value) as ContactEntryKind })
                  }
                  data-testid="entry-kind"
                >
                  <option value={ContactEntryKind.AuthorizedAgent}>
                    {t('contactDirectory.kind.AuthorizedAgent')}
                  </option>
                  <option value={ContactEntryKind.Administration}>
                    {t('contactDirectory.kind.Administration')}
                  </option>
                </Select>
              </Field>

              {/* Only an agent belongs to a country — the administration is the organisation
                  itself, and the server drops any country set on one. */}
              {isAgent && (
                <Field
                  label={t('lookups.country')}
                  htmlFor="entry-country"
                  required
                  hint={t('contactDirectory.countryHint')}
                >
                  <SearchableSelect
                    id="entry-country"
                    options={(countries.data?.items ?? []).map((country) => ({
                      value: country.id,
                      label: country.name,
                      keywords: `${country.code} ${country.nameEn} ${country.nameAr}`,
                    }))}
                    value={form.countryId ?? ''}
                    onChange={(countryId) => setForm({ ...form, countryId: countryId || null })}
                    placeholder={t('contactDirectory.chooseCountry')}
                    searchPlaceholder={t('lookups.countriesSearch')}
                  />
                </Field>
              )}
            </div>

            <div className="grid gap-4 sm:grid-cols-2">
              <Field label={t('contactDirectory.nameEn')} htmlFor="entry-title-en">
                <Input
                  id="entry-title-en"
                  dir="ltr"
                  value={form.titleEn ?? ''}
                  onChange={(event) => setForm({ ...form, titleEn: event.target.value || null })}
                  data-testid="entry-title-en"
                />
              </Field>

              <Field label={t('contactDirectory.nameAr')} htmlFor="entry-title-ar">
                <Input
                  id="entry-title-ar"
                  dir="rtl"
                  value={form.titleAr ?? ''}
                  onChange={(event) => setForm({ ...form, titleAr: event.target.value || null })}
                  data-testid="entry-title-ar"
                />
              </Field>

              <Field label={t('contactDirectory.addressEn')} htmlFor="entry-address-en">
                <Input
                  id="entry-address-en"
                  dir="ltr"
                  value={form.addressEn ?? ''}
                  onChange={(event) => setForm({ ...form, addressEn: event.target.value || null })}
                  data-testid="entry-address-en"
                />
              </Field>

              <Field label={t('contactDirectory.addressAr')} htmlFor="entry-address-ar">
                <Input
                  id="entry-address-ar"
                  dir="rtl"
                  value={form.addressAr ?? ''}
                  onChange={(event) => setForm({ ...form, addressAr: event.target.value || null })}
                  data-testid="entry-address-ar"
                />
              </Field>

              <Field
                label={t('contactDirectory.phone')}
                htmlFor="entry-phone"
                hint={t('contactDirectory.phoneHint')}
              >
                <Input
                  id="entry-phone"
                  dir="ltr"
                  className="font-mono"
                  placeholder="+201005551234"
                  value={form.phone ?? ''}
                  onChange={(event) => setForm({ ...form, phone: event.target.value || null })}
                  data-testid="entry-phone"
                />
              </Field>

              <Field label={t('contactDirectory.email')} htmlFor="entry-email">
                <Input
                  id="entry-email"
                  type="email"
                  dir="ltr"
                  value={form.email ?? ''}
                  onChange={(event) => setForm({ ...form, email: event.target.value || null })}
                  data-testid="entry-email"
                />
              </Field>

              <Field
                label={t('lookups.sortOrder')}
                htmlFor="entry-sort"
                hint={t('contactDirectory.sortHint')}
              >
                <Input
                  id="entry-sort"
                  type="number"
                  min={0}
                  value={form.sortOrder}
                  onChange={(event) =>
                    setForm({ ...form, sortOrder: Number(event.target.value) || 0 })
                  }
                  data-testid="entry-sort"
                />
              </Field>

              <Field label={t('lookups.status')} htmlFor="entry-active">
                <Select
                  id="entry-active"
                  value={form.isActive ? 'true' : 'false'}
                  onChange={(event) =>
                    setForm({ ...form, isActive: event.target.value === 'true' })
                  }
                  data-testid="entry-active"
                >
                  <option value="true">{t('lookups.active')}</option>
                  <option value="false">{t('lookups.inactive')}</option>
                </Select>
              </Field>
            </div>

            {!canSubmit && <Alert variant="info">{t('contactDirectory.needContact')}</Alert>}
          </div>
        )}
      </CrudDialog>

      <ConfirmDialog
        open={toDelete !== null}
        title={t('contactDirectory.deleteTitle')}
        body={t('contactDirectory.deleteBody')}
        confirmLabel={t('common.delete')}
        onClose={() => setToDelete(null)}
        onConfirm={confirmDelete}
        isPending={remove.isPending}
        destructive
      />
    </div>
  );
}
