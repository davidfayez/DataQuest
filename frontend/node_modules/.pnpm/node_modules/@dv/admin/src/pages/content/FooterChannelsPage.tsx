import { AdminPageHeader, Alert, Button, Field, Input, Select } from '@dv/ui';
import { AlertTriangle, Plus } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useAdminSession } from '@/features/auth/useAdminSession';
import {
  PLATFORM_LABELS,
  SocialLinkPlacement,
  SocialPlatform,
  useDeleteSocialLink,
  useSaveSocialLink,
  useSocialLinks,
  type SocialLinkDto,
  type SocialLinkParams,
  type UpsertSocialLinkInput,
} from '@/features/content/socialLinks';
import { ConfirmDialog, CrudDialog } from '@/shared/ui/CrudDialog';
import { DataTable } from '@/shared/ui/DataTable';
import { RowActions } from '@/shared/ui/RowActions';
import { Notice } from '../lookups/tabs/shared';

const BLANK: UpsertSocialLinkInput = {
  placement: SocialLinkPlacement.FollowUs,
  platform: SocialPlatform.Facebook,
  url: '',
  isActive: true,
  sortOrder: 0,
};

const PLATFORM_OPTIONS = Object.values(SocialPlatform).filter(
  (value): value is SocialPlatform => typeof value === 'number',
);

/**
 * The "Follow us" and "Message us" rows in the public site footer.
 *
 * One page for both rows because they are one list to maintain, and because the same platform can
 * legitimately appear in each — Telegram is a channel to follow and an address to message.
 *
 * Only the platform and the address are edited here. The icon and the brand colour belong to the
 * site that draws them, so adding a channel is choosing from a list rather than hunting down a hex
 * code and an SVG.
 */
export function FooterChannelsPage() {
  const { t } = useTranslation();
  const session = useAdminSession();

  const has = (name: string) => session?.permissions.has(name) ?? false;
  const canCreate = has('SocialLinks.Create');
  const canUpdate = has('SocialLinks.Update');
  const canDelete = has('SocialLinks.Delete');

  const [params, setParams] = useState<SocialLinkParams>({ page: 1, pageSize: 50 });
  const [form, setForm] = useState<UpsertSocialLinkInput | null>(null);
  const [toDelete, setToDelete] = useState<SocialLinkDto | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const list = useSocialLinks(params);
  const save = useSaveSocialLink();
  const remove = useDeleteSocialLink();

  // The server refuses an address that is not http(s); the dialog holds the same line so the
  // refusal never arrives as a surprise on submit.
  const canSubmit = Boolean(form && /^https?:\/\/\S+/i.test(form.url.trim()));

  function submit() {
    if (!form) return;

    save.mutate(
      { ...form, url: form.url.trim() },
      {
        onSuccess: () => {
          setForm(null);
          setNotice(t('lookups.saved'));
        },
      },
    );
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
        title={t('footerChannels.title')}
        subtitle={t('footerChannels.subtitle')}
      />

      {!canCreate && !canUpdate && !canDelete && (
        <Alert variant="info">{t('roles.viewOnlyNotice')}</Alert>
      )}

      <Notice message={notice} onDismiss={() => setNotice(null)} />

      <DataTable
        data={list.data}
        isPending={list.isPending}
        rowKey={(row) => row.id}
        emptyMessage={t('footerChannels.empty')}
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
            aria-label={t('footerChannels.filterPlacement')}
            className="w-56"
            value={params.placement === undefined ? '' : String(params.placement)}
            onChange={(event) =>
              setParams((p) => ({
                ...p,
                placement:
                  event.target.value === ''
                    ? undefined
                    : (Number(event.target.value) as SocialLinkPlacement),
                page: 1,
              }))
            }
            data-testid="filter-placement"
          >
            <option value="">{t('footerChannels.allPlacements')}</option>
            <option value={SocialLinkPlacement.FollowUs}>
              {t('footerChannels.placement.FollowUs')}
            </option>
            <option value={SocialLinkPlacement.MessageUs}>
              {t('footerChannels.placement.MessageUs')}
            </option>
          </Select>
        }
        toolbar={
          canCreate ? (
            <Button onClick={() => setForm({ ...BLANK })} data-testid="channel-new">
              <Plus className="size-4" aria-hidden="true" />
              {t('footerChannels.newChannel')}
            </Button>
          ) : null
        }
        columns={[
          {
            key: 'placementName',
            header: t('footerChannels.placementColumn'),
            getValue: (row) => row.placementName,
            render: (row) => (
              <span className="font-medium">
                {t(`footerChannels.placement.${row.placementName}`)}
              </span>
            ),
          },
          {
            key: 'platformName',
            header: t('footerChannels.platform'),
            getValue: (row) => row.platformName,
            render: (row) => <span>{PLATFORM_LABELS[row.platform] ?? row.platformName}</span>,
          },
          {
            key: 'url',
            header: t('footerChannels.url'),
            getValue: (row) => row.url,
            render: (row) => (
              <a
                href={row.url}
                target="_blank"
                rel="noopener noreferrer"
                dir="ltr"
                className="block max-w-xs truncate text-sm text-primary hover:underline"
              >
                {row.url}
              </a>
            ),
          },
          {
            key: 'sortOrder',
            header: t('lookups.sortOrder'),
            align: 'center',
            getValue: (row) => String(row.sortOrder),
            render: (row) => <span>{row.sortOrder}</span>,
          },
          {
            key: 'isActive',
            header: t('lookups.status'),
            align: 'center',
            getValue: (row) => (row.isActive ? 'active' : 'inactive'),
            render: (row) => {
              // Active but not showable means the footer drops it silently — worth saying out loud,
              // because the row otherwise reads as live.
              if (row.isActive && !row.isShowable) {
                return (
                  <span
                    className="inline-flex items-center gap-1 text-warning"
                    title={t('footerChannels.notShowableHint')}
                  >
                    <AlertTriangle className="size-3.5" aria-hidden="true" />
                    {t('footerChannels.notShowable')}
                  </span>
                );
              }

              return (
                <span className={row.isActive ? 'text-success' : 'text-subtle'}>
                  {t(row.isActive ? 'lookups.active' : 'lookups.inactive')}
                </span>
              );
            },
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
                onEdit={
                  !canUpdate
                    ? undefined
                    : () =>
                        setForm({
                          id: row.id,
                          placement: row.placement,
                          platform: row.platform,
                          url: row.url,
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
        title={form?.id ? t('footerChannels.editChannel') : t('footerChannels.newChannel')}
        onClose={() => setForm(null)}
        onSubmit={submit}
        isPending={save.isPending}
        error={save.error}
        canSubmit={canSubmit}
      >
        {form && (
          <div className="space-y-4">
            <div className="grid gap-4 sm:grid-cols-2">
              <Field label={t('footerChannels.placementColumn')} htmlFor="channel-placement" required>
                <Select
                  id="channel-placement"
                  value={String(form.placement)}
                  onChange={(event) =>
                    setForm({
                      ...form,
                      placement: Number(event.target.value) as SocialLinkPlacement,
                    })
                  }
                >
                  <option value={SocialLinkPlacement.FollowUs}>
                    {t('footerChannels.placement.FollowUs')}
                  </option>
                  <option value={SocialLinkPlacement.MessageUs}>
                    {t('footerChannels.placement.MessageUs')}
                  </option>
                </Select>
              </Field>

              <Field label={t('footerChannels.platform')} htmlFor="channel-platform" required>
                <Select
                  id="channel-platform"
                  value={String(form.platform)}
                  onChange={(event) =>
                    setForm({ ...form, platform: Number(event.target.value) as SocialPlatform })
                  }
                >
                  {PLATFORM_OPTIONS.map((platform) => (
                    <option key={platform} value={platform}>
                      {PLATFORM_LABELS[platform]}
                    </option>
                  ))}
                </Select>
              </Field>
            </div>

            <Field label={t('footerChannels.url')} htmlFor="channel-url" required>
              <Input
                id="channel-url"
                dir="ltr"
                inputMode="url"
                maxLength={500}
                placeholder={t('footerChannels.urlPlaceholder')}
                value={form.url}
                onChange={(event) => setForm({ ...form, url: event.target.value })}
                data-testid="channel-url"
              />
            </Field>

            <p className="text-xs text-subtle">{t('footerChannels.urlHint')}</p>

            <div className="grid gap-4 sm:grid-cols-2">
              <Field label={t('lookups.sortOrder')} htmlFor="channel-sort">
                <Input
                  id="channel-sort"
                  type="number"
                  min={0}
                  value={form.sortOrder}
                  onChange={(event) =>
                    setForm({ ...form, sortOrder: Number(event.target.value) || 0 })
                  }
                />
              </Field>

              <Field label={t('lookups.status')} htmlFor="channel-active">
                <Select
                  id="channel-active"
                  value={form.isActive ? '1' : '0'}
                  onChange={(event) => setForm({ ...form, isActive: event.target.value === '1' })}
                >
                  <option value="1">{t('lookups.active')}</option>
                  <option value="0">{t('lookups.inactive')}</option>
                </Select>
              </Field>
            </div>
          </div>
        )}
      </CrudDialog>

      <ConfirmDialog
        open={toDelete !== null}
        title={t('footerChannels.deleteTitle')}
        body={t('footerChannels.deleteBody')}
        confirmLabel={t('common.delete')}
        onClose={() => setToDelete(null)}
        onConfirm={confirmDelete}
        isPending={remove.isPending}
        destructive
      />
    </div>
  );
}
