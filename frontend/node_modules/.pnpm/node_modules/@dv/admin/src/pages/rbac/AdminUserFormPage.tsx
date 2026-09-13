import { AdminPageHeader, AdminPanel, Alert, Button, Field, Input, LoadingState, Select } from '@dv/ui';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import { adminSession, Permissions } from '@/features/auth/session';
import { adminKeys, apiClient } from '@/shared/api/client';
import { useApiErrorMessage } from '@/shared/lib/useApiError';
import { FormPageLayout, SummaryPanel } from '@/shared/ui/FormPageLayout';
import type { PagedResult } from '@/shared/ui/DataTable';
import { PermissionGrid, type PermissionGroup } from './PermissionGrid';
import type { AdminUserDto } from './AdminUsersPage';

interface UserBody {
  id?: string;
  email: string;
  username: string;
  fullName: string;
  password: string;
  isActive: boolean;
  languageCode: string;
  permissions: string[];
}

const EMPTY: UserBody = {
  email: '',
  username: '',
  fullName: '',
  password: '',
  isActive: true,
  languageCode: 'en',
  permissions: [],
};

const LIST_PATH = '/users';

/**
 * Create or edit an administrator on its own page rather than in a dialog.
 *
 * The row is handed over in router state when arriving from the list. Opening the URL directly
 * has no state, so the list is fetched and the record found in it.
 *
 * Activation is deliberately absent: it is a one-click decision on the list, behind its own
 * confirmation and its own endpoint, so the form neither shows nor changes it.
 */
export function AdminUserFormPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { id } = useParams<{ id: string }>();
  const location = useLocation();
  const queryClient = useQueryClient();
  const toMessage = useApiErrorMessage();

  const isEdit = Boolean(id);
  const handedOver = (location.state as { user?: AdminUserDto } | null)?.user;

  const list = useQuery({
    // Only consulted when the record was not handed over, which is the direct-URL case.
    enabled: isEdit && !handedOver,
    queryKey: adminKeys.users({ page: 1, pageSize: 200 }),
    queryFn: () =>
      apiClient.get<PagedResult<AdminUserDto>>('admin/users', {
        query: { page: 1, pageSize: 200 },
      }),
  });
  const existing = handedOver ?? (isEdit ? list.data?.items.find((row) => row.id === id) : undefined);

  const permissions = useQuery({
    queryKey: adminKeys.permissions,
    queryFn: () => apiClient.get<PermissionGroup[]>('admin/permissions'),
  });

  const [form, setForm] = useState<UserBody>(EMPTY);

  useEffect(() => {
    if (!existing) return;

    setForm({
      id: existing.id,
      email: existing.email,
      username: existing.username,
      fullName: existing.fullName,
      password: '',
      isActive: existing.isActive,
      languageCode: existing.languageCode,
      permissions: [...(existing.permissions ?? [])],
    });
  }, [existing]);

  const canSubmit = isEdit
    ? adminSession.has(Permissions.AdminUsersUpdate)
    : adminSession.has(Permissions.AdminUsersCreate);

  const save = useMutation({
    mutationFn: (body: UserBody) =>
      apiClient.post<AdminUserDto>('admin/users', {
        ...body,
        // An empty password on edit means "keep the current one".
        password: body.password || null,
      }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['admin', 'users'] });
      navigate(LIST_PATH, { state: { notice: t('lookups.saved') } });
    },
  });

  const groups = useMemo(() => permissions.data ?? [], [permissions.data]);
  const allNames = useMemo(
    () => groups.flatMap((group) => group.permissions.map((permission) => permission.name)),
    [groups],
  );
  const selected = new Set(form.permissions);

  function setSelected(next: Set<string>) {
    setForm((previous) => ({ ...previous, permissions: [...next] }));
  }

  function toggle(name: string) {
    const next = new Set(selected);
    if (next.has(name)) next.delete(name);
    else next.add(name);
    setSelected(next);
  }

  function toggleGroup(group: PermissionGroup) {
    const names = group.permissions.map((permission) => permission.name);
    const allOn = names.every((name) => selected.has(name));
    const next = new Set(selected);
    for (const name of names) {
      if (allOn) next.delete(name);
      else next.add(name);
    }
    setSelected(next);
  }

  function setAll(on: boolean) {
    setSelected(on ? new Set(allNames) : new Set());
  }

  function goBack() {
    navigate(LIST_PATH);
  }

  if (isEdit && !existing && !list.isPending) {
    return (
      <div className="animate-fade-in space-y-6">
        <AdminPageHeader title={t('users.title')} subtitle={t('users.subtitle')} />
        <Alert variant="error">{t('errors.notFound')}</Alert>
        <Button type="button" variant="outline" onClick={goBack}>
          {t('common.back')}
        </Button>
      </div>
    );
  }

  if (isEdit && !existing) {
    return <LoadingState label={t('common.loading')} />;
  }

  // The aside lists the groups the account can reach at all, which is the readable shape of a
  // 60-odd permission list. The grid below carries the detail.
  const reachableGroups = groups
    .filter((group) => group.permissions.some((permission) => selected.has(permission.name)))
    .map((group) => {
      const translated = t(`permGroup.${group.group}`);
      return translated === `permGroup.${group.group}` ? group.group : translated;
    });

  return (
    <FormPageLayout
      title={isEdit ? t('users.editTitle') : t('users.createTitle')}
      subtitle={isEdit ? t('users.formHintEdit') : t('users.formHintCreate')}
      listLabel={t('users.title')}
      onBack={goBack}
      onSubmit={() => save.mutate(form)}
      isPending={save.isPending}
      canSubmit={canSubmit}
      error={save.isError ? save.error : null}
      errorMessage={toMessage}
      aside={
        <SummaryPanel
          title={t('users.permissionsSummary')}
          items={reachableGroups}
          emptyLabel={t('users.noPermissions')}
        />
      }
      wide={
        <AdminPanel
          title={t('users.sectionPermissions')}
          subtitle={t('users.sectionPermissionsHint')}
          actions={
            <div className="flex gap-2">
              <Button type="button" variant="ghost" size="sm" onClick={() => setAll(true)}>
                {t('roles.selectAll')}
              </Button>
              <Button type="button" variant="ghost" size="sm" onClick={() => setAll(false)}>
                {t('roles.clearAll')}
              </Button>
            </div>
          }
        >
          <PermissionGrid
            groups={groups}
            selected={selected}
            onToggle={toggle}
            onToggleGroup={toggleGroup}
          />
        </AdminPanel>
      }
    >
      <AdminPanel title={t('users.sectionIdentity')} subtitle={t('users.sectionIdentityHint')}>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t('users.fullName')} htmlFor="fullName" required>
            <Input
              id="fullName"
              value={form.fullName}
              onChange={(event) => setForm({ ...form, fullName: event.target.value })}
            />
          </Field>

          <Field label={t('users.email')} htmlFor="userEmail" required>
            <Input
              id="userEmail"
              type="email"
              dir="ltr"
              value={form.email}
              onChange={(event) => setForm({ ...form, email: event.target.value })}
            />
          </Field>

          <Field
            label={t('users.username')}
            htmlFor="userUsername"
            required
            hint={t('users.usernameHint')}
          >
            <Input
              id="userUsername"
              dir="ltr"
              autoComplete="off"
              value={form.username}
              onChange={(event) => setForm({ ...form, username: event.target.value })}
            />
          </Field>
        </div>
      </AdminPanel>

      <AdminPanel title={t('users.sectionAccess')} subtitle={t('users.sectionAccessHint')}>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field
            label={t('users.password')}
            htmlFor="userPassword"
            required={!isEdit}
            hint={isEdit ? t('users.passwordHintEdit') : t('users.passwordHintCreate')}
          >
            <Input
              id="userPassword"
              type="password"
              dir="ltr"
              autoComplete="new-password"
              value={form.password}
              onChange={(event) => setForm({ ...form, password: event.target.value })}
            />
          </Field>

          <Field label={t('users.language')} htmlFor="userLanguage">
            <Select
              id="userLanguage"
              value={form.languageCode}
              onChange={(event) => setForm({ ...form, languageCode: event.target.value })}
            >
              <option value="en">English</option>
              <option value="ar">العربية</option>
            </Select>
          </Field>
        </div>
      </AdminPanel>
    </FormPageLayout>
  );
}
