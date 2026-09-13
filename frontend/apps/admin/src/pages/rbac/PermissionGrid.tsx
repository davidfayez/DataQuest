import { useTranslation } from 'react-i18next';

export interface PermissionDto {
  id: string;
  name: string;
  group: string;
  action: string;
  description: string | null;
}

export interface PermissionGroup {
  group: string;
  order: number;
  permissions: PermissionDto[];
}

/** The four columns of the grid; anything else (Review, Credit, …) shows in an "Other" cell. */
export const STANDARD_ACTIONS = ['View', 'Create', 'Update', 'Delete'] as const;

/** The View / Create / Update / Delete grid, one row per admin page. */
export function PermissionGrid({
  groups,
  selected,
  onToggle,
  onToggleGroup,
}: {
  groups: PermissionGroup[];
  selected: Set<string>;
  onToggle: (name: string) => void;
  onToggleGroup: (group: PermissionGroup) => void;
}) {
  const { t } = useTranslation();

  function groupLabel(key: string) {
    const translated = t(`permGroup.${key}`);
    return translated === `permGroup.${key}` ? key : translated;
  }
  function actionLabel(action: string) {
    const translated = t(`permAction.${action}`);
    return translated === `permAction.${action}` ? action : translated;
  }

  return (
    <div className="overflow-x-auto rounded-md border border-border">
      <table className="w-full min-w-[36rem] text-sm">
        <thead className="border-b border-border bg-muted">
          <tr>
            <th className="px-3 py-2 text-start text-2xs font-semibold uppercase tracking-[0.06em] text-muted-foreground">
              {t('roles.page')}
            </th>
            {STANDARD_ACTIONS.map((action) => (
              <th
                key={action}
                className="px-2 py-2 text-center text-2xs font-semibold uppercase tracking-[0.06em] text-muted-foreground"
              >
                {actionLabel(action)}
              </th>
            ))}
            <th className="px-3 py-2 text-start text-2xs font-semibold uppercase tracking-[0.06em] text-muted-foreground">
              {t('roles.otherActions')}
            </th>
          </tr>
        </thead>

        <tbody>
          {groups.map((group) => {
            const byAction = new Map(group.permissions.map((p) => [p.action, p]));
            const others = group.permissions.filter(
              (p) => !STANDARD_ACTIONS.includes(p.action as (typeof STANDARD_ACTIONS)[number]),
            );
            const names = group.permissions.map((p) => p.name);
            const allOn = names.every((name) => selected.has(name));
            const someOn = names.some((name) => selected.has(name));

            return (
              <tr key={group.group} className="border-t border-border">
                <td className="px-3 py-2">
                  <label className="flex items-center gap-2 font-medium">
                    <input
                      type="checkbox"
                      className="size-4 rounded border-border-strong"
                      checked={allOn}
                      ref={(el) => {
                        if (el) el.indeterminate = someOn && !allOn;
                      }}
                      onChange={() => onToggleGroup(group)}
                      aria-label={`${groupLabel(group.group)} — all`}
                    />
                    {groupLabel(group.group)}
                  </label>
                </td>

                {STANDARD_ACTIONS.map((action) => {
                  const permission = byAction.get(action);
                  return (
                    <td key={action} className="px-2 py-2 text-center">
                      {permission ? (
                        <input
                          type="checkbox"
                          className="size-4 rounded border-border-strong"
                          checked={selected.has(permission.name)}
                          onChange={() => onToggle(permission.name)}
                          data-testid={`permission-${permission.name}`}
                          aria-label={`${groupLabel(group.group)} ${actionLabel(action)}`}
                        />
                      ) : (
                        <span className="text-subtle">—</span>
                      )}
                    </td>
                  );
                })}

                <td className="px-3 py-2">
                  {others.length > 0 ? (
                    <div className="flex flex-wrap gap-x-4 gap-y-1">
                      {others.map((permission) => (
                        <label
                          key={permission.name}
                          className="flex items-center gap-1.5 text-xs"
                          title={permission.description ?? undefined}
                        >
                          <input
                            type="checkbox"
                            className="size-3.5 rounded border-border-strong"
                            checked={selected.has(permission.name)}
                            onChange={() => onToggle(permission.name)}
                            data-testid={`permission-${permission.name}`}
                          />
                          {actionLabel(permission.action)}
                        </label>
                      ))}
                    </div>
                  ) : (
                    <span className="text-subtle">—</span>
                  )}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
