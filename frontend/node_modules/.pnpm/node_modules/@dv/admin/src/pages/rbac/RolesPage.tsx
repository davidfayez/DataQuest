import { AdminPageHeader, Alert, Button, Card, CardContent, Field, Input, LoadingState } from '@dv/ui';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Plus } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Permissions } from '@/features/auth/session';
import { usePermission } from '@/features/auth/useAdminSession';
import { adminKeys, apiClient } from '@/shared/api/client';
import { ConfirmDialog, CrudDialog } from '@/shared/ui/CrudDialog';
import { PermissionGrid, type PermissionGroup } from './PermissionGrid';

interface RoleDto {
  id: string;
  name: string;
  description: string | null;
  isSystemRole: boolean;
  userCount: number;
  permissions: string[];
}

interface RoleBody {
  id?: string;
  name: string;
  description: string;
  permissions: string[];
}

const EMPTY: RoleBody = { name: '', description: '', permissions: [] };

export function RolesPage() {
  const { t } = useTranslation();
  const queryClient = useQueryClient();

  const canCreate = usePermission(Permissions.RolesCreate);
  const canUpdate = usePermission(Permissions.RolesUpdate);
  const canDelete = usePermission(Permissions.RolesDelete);

  const [form, setForm] = useState<RoleBody>(EMPTY);
  const [editing, setEditing] = useState<RoleDto | null>(null);
  const [isCreating, setIsCreating] = useState(false);
  const [toDelete, setToDelete] = useState<RoleDto | null>(null);

  const roles = useQuery({
    queryKey: adminKeys.roles,
    queryFn: () => apiClient.get<RoleDto[]>('admin/roles'),
  });

  const permissions = useQuery({
    queryKey: adminKeys.permissions,
    queryFn: () => apiClient.get<PermissionGroup[]>('admin/permissions'),
  });

  const save = useMutation({
    mutationFn: (body: RoleBody) => apiClient.post<RoleDto>('admin/roles', body),
    onSuccess: () => {
      close();
      void queryClient.invalidateQueries({ queryKey: adminKeys.roles });
    },
  });

  const remove = useMutation({
    mutationFn: (id: string) => apiClient.delete(`admin/roles/${id}`),
    onSuccess: () => {
      setToDelete(null);
      void queryClient.invalidateQueries({ queryKey: adminKeys.roles });
    },
  });

  useEffect(() => {
    setForm(
      editing
        ? {
            id: editing.id,
            name: editing.name,
            description: editing.description ?? '',
            permissions: [...editing.permissions],
          }
        : EMPTY,
    );
  }, [editing, isCreating]);

  const groups = useMemo(() => permissions.data ?? [], [permissions.data]);
  const allNames = useMemo(
    () => groups.flatMap((group) => group.permissions.map((permission) => permission.name)),
    [groups],
  );

  function close() {
    setEditing(null);
    setIsCreating(false);
    save.reset();
  }

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

  const readOnly = editing ? !canUpdate : !canCreate;

  if (roles.isPending) return <LoadingState label={t('common.loading')} />;

  return (
    <div className="space-y-6">
      <AdminPageHeader
        title={t('roles.title')}
        subtitle={t('roles.subtitle')}
        actions={
          canCreate && (
            <Button onClick={() => setIsCreating(true)} data-testid="new-role">
              <Plus className="size-4" aria-hidden="true" />
              {t('roles.createTitle')}
            </Button>
          )
        }
      />

      {remove.isError ? (
        <Alert variant="error">
          {(remove.error as { problem?: { detail?: string } })?.problem?.detail ??
            t('errors.genericTitle')}
        </Alert>
      ) : null}

      <div className="grid gap-4 md:grid-cols-2">
        {(roles.data ?? []).map((role) => (
          <Card key={role.id} data-testid={`role-${role.name}`}>
            <CardContent className="space-y-3 p-5">
              <div className="flex items-start justify-between gap-3">
                <div>
                  <h2 className="font-semibold">
                    {role.name}
                    {role.isSystemRole && (
                      <span className="ms-2 rounded bg-muted px-2 py-0.5 text-xs font-normal text-muted-foreground">
                        {t('roles.systemRole')}
                      </span>
                    )}
                  </h2>
                  <p className="text-sm text-muted-foreground">{role.description}</p>
                </div>

                <div className="flex gap-1">
                  {(canUpdate || (role.isSystemRole && canUpdate)) && (
                    <Button variant="ghost" size="sm" onClick={() => setEditing(role)}>
                      {canUpdate ? t('common.edit') : t('common.view')}
                    </Button>
                  )}
                  {!role.isSystemRole && canDelete && (
                    <Button variant="ghost" size="sm" onClick={() => setToDelete(role)}>
                      {t('common.delete')}
                    </Button>
                  )}
                </div>
              </div>

              <p className="text-xs text-muted-foreground">
                {t('roles.users')}: {role.userCount} · {t('roles.permissions')}:{' '}
                {role.permissions.length}
              </p>
            </CardContent>
          </Card>
        ))}
      </div>

      <CrudDialog
        open={isCreating || editing !== null}
        title={editing ? t('roles.editTitle') : t('roles.createTitle')}
        onClose={close}
        onSubmit={() => save.mutate(form)}
        isPending={save.isPending}
        error={save.error}
        wide
      >
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t('roles.name')} htmlFor="role-name" required>
            <Input
              id="role-name"
              value={form.name}
              disabled={editing?.isSystemRole || readOnly}
              onChange={(event) => setForm({ ...form, name: event.target.value })}
            />
          </Field>

          <Field label={t('roles.description')} htmlFor="role-description">
            <Input
              id="role-description"
              value={form.description}
              disabled={readOnly}
              onChange={(event) => setForm({ ...form, description: event.target.value })}
            />
          </Field>
        </div>

        <fieldset className="space-y-3" disabled={readOnly}>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <legend className="text-sm font-medium">{t('roles.permissions')}</legend>
            <div className="flex gap-2">
              <Button type="button" variant="ghost" size="sm" onClick={() => setAll(true)}>
                {t('roles.selectAll')}
              </Button>
              <Button type="button" variant="ghost" size="sm" onClick={() => setAll(false)}>
                {t('roles.clearAll')}
              </Button>
            </div>
          </div>

          <PermissionGrid
            groups={groups}
            selected={selected}
            onToggle={toggle}
            onToggleGroup={toggleGroup}
          />
        </fieldset>
      </CrudDialog>

      <ConfirmDialog
        open={toDelete !== null}
        title={t('roles.deleteTitle')}
        body={t('roles.deleteBody', { name: toDelete?.name ?? '' })}
        confirmLabel={t('common.delete')}
        onClose={() => setToDelete(null)}
        onConfirm={() => toDelete && remove.mutate(toDelete.id)}
        isPending={remove.isPending}
        destructive
      />
    </div>
  );
}
