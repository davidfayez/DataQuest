import { AdminPageHeader, Alert, Button } from '@dv/ui';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Ban, CheckCircle2, Pencil, Plus } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useLocation, useNavigate } from 'react-router-dom';
import { Permissions } from '@/features/auth/session';
import { usePermission } from '@/features/auth/useAdminSession';
import { adminKeys, apiClient } from '@/shared/api/client';
import { formatDateTime } from '@/shared/lib/format';
import { ConfirmDialog } from '@/shared/ui/CrudDialog';
import { DataTable, type PagedResult, type SortParams } from '@/shared/ui/DataTable';

export interface AdminUserDto {
  id: string;
  email: string;
  username: string;
  fullName: string;
  isActive: boolean;
  languageCode: string;
  lastLoginAtUtc: string | null;
  createdAtUtc: string;
  permissions: string[];
}

/**
 * The administrator list. Creating and editing happen on their own pages rather than in a dialog —
 * the form carries the account details plus the whole permission matrix.
 *
 * Activating and deactivating stay here: they are a one-click decision on a row, behind their own
 * confirmation and their own endpoint.
 */
export function AdminUsersPage() {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage ?? 'en';
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const location = useLocation();

  const canCreate = usePermission(Permissions.AdminUsersCreate);
  const canUpdate = usePermission(Permissions.AdminUsersUpdate);

  const [params, setParams] = useState<{ page: number; pageSize: number; search?: string } & SortParams>({
    page: 1,
    pageSize: 25,
  });
  const [toToggle, setToToggle] = useState<AdminUserDto | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  // The form page saves and navigates back, handing its confirmation over in router state.
  useEffect(() => {
    const handedOver = (location.state as { notice?: string } | null)?.notice;
    if (!handedOver) return;

    setNotice(handedOver);
    navigate(location.pathname, { replace: true, state: null });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [location.state]);

  const users = useQuery({
    queryKey: adminKeys.users(params),
    queryFn: () =>
      apiClient.get<PagedResult<AdminUserDto>>('admin/users', {
        query: {
          page: params.page,
          pageSize: params.pageSize,
          sortBy: params.sortBy,
          sortDescending: params.sortDescending,
          search: params.search || undefined,
        },
      }),
  });

  const setActive = useMutation({
    mutationFn: ({ id, isActive }: { id: string; isActive: boolean }) =>
      apiClient.post(`admin/users/${id}/active`, { isActive }),
    onSuccess: () => {
      setToToggle(null);
      void queryClient.invalidateQueries({ queryKey: ['admin', 'users'] });
    },
  });

  return (
    <div className="animate-fade-in space-y-6">
      <AdminPageHeader title={t('users.title')} subtitle={t('users.subtitle')} />

      {notice && (
        <Alert variant="success" data-testid="user-notice">
          <div className="flex items-center justify-between gap-3">
            <span>{notice}</span>
            <Button variant="ghost" size="sm" onClick={() => setNotice(null)}>
              {t('common.close')}
            </Button>
          </div>
        </Alert>
      )}

      <DataTable
        data={users.data}
        isPending={users.isPending}
        rowKey={(row) => row.id}
        onSearch={(search) => setParams({ page: 1, pageSize: params.pageSize, search })}
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
          canCreate ? (
            <Button onClick={() => navigate('/users/new')} data-testid="new-user">
              <Plus className="size-4" aria-hidden="true" />
              {t('users.createTitle')}
            </Button>
          ) : null
        }
        columns={[
          {
            key: 'fullName',
            header: t('users.fullName'),
            getValue: (row) => row.fullName,
            render: (row) => row.fullName,
          },
          {
            key: 'username',
            header: t('users.username'),
            getValue: (row) => row.username,
            render: (row) => <span dir="ltr">{row.username}</span>,
          },
          {
            key: 'email',
            header: t('users.email'),
            getValue: (row) => row.email,
            render: (row) => <span dir="ltr">{row.email}</span>,
          },
          {
            key: 'permissions',
            header: t('users.permissions'),
            getValue: (row) => String(row.permissions?.length ?? 0),
            render: (row) => t('users.permissionCount', { count: row.permissions?.length ?? 0 }),
          },
          {
            key: 'isActive',
            header: t('users.status'),
            getValue: (row) => (row.isActive ? '1' : '0'),
            render: (row) => (
              <span className={row.isActive ? 'text-success' : 'text-muted-foreground'}>
                {row.isActive ? t('common.active') : t('common.inactive')}
              </span>
            ),
          },
          {
            key: 'lastLoginAtUtc',
            header: t('users.lastLogin'),
            getValue: (row) => row.lastLoginAtUtc ?? '',
            render: (row) =>
              row.lastLoginAtUtc ? formatDateTime(row.lastLoginAtUtc, locale) : '—',
          },
          {
            key: 'actions',
            header: '',
            align: 'end',
            sortable: false,
            filterable: false,
            render: (row) =>
              canUpdate ? (
                <div className="flex justify-end gap-0.5">
                  <button
                    type="button"
                    aria-label={t('common.edit')}
                    title={t('common.edit')}
                    data-testid={`edit-user-${row.email}`}
                    // The row travels with the navigation, so the form opens without a second fetch.
                    onClick={() => navigate(`/users/${row.id}/edit`, { state: { user: row } })}
                    className="inline-flex size-8 items-center justify-center rounded-lg text-ink-400 transition-colors hover:bg-ink-50 hover:text-ink-800"
                  >
                    <Pencil className="size-4" aria-hidden />
                  </button>
                  <button
                    type="button"
                    aria-label={row.isActive ? t('users.deactivate') : t('users.activate')}
                    title={row.isActive ? t('users.deactivate') : t('users.activate')}
                    onClick={() => setToToggle(row)}
                    className="inline-flex size-8 items-center justify-center rounded-lg text-ink-400 transition-colors hover:bg-ink-50 hover:text-ink-800"
                  >
                    {row.isActive ? (
                      <Ban className="size-4" aria-hidden />
                    ) : (
                      <CheckCircle2 className="size-4" aria-hidden />
                    )}
                  </button>
                </div>
              ) : null,
          },
        ]}
      />

      <ConfirmDialog
        open={toToggle !== null}
        title={toToggle?.isActive ? t('users.deactivateTitle') : t('users.activate')}
        body={t('users.deactivateBody', { name: toToggle?.fullName ?? '' })}
        confirmLabel={toToggle?.isActive ? t('users.deactivate') : t('users.activate')}
        onClose={() => setToToggle(null)}
        onConfirm={() =>
          toToggle && setActive.mutate({ id: toToggle.id, isActive: !toToggle.isActive })
        }
        isPending={setActive.isPending}
        destructive={toToggle?.isActive}
      />
    </div>
  );
}
