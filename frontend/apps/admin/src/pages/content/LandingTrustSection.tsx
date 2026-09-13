import { Alert, Button, Field, Input, SearchableSelect, Select, cn } from '@dv/ui';
import { Building2, Pencil, Plus, Trash2 } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { adminSession, Permissions } from '@/features/auth/session';
import {
  LANDING_ICON_KEYS,
  useDeleteLandingTrustEntry,
  useSaveLandingTrustEntry,
  type LandingTrustEntryDto,
  type UpsertLandingTrustEntryBody,
} from '@/features/content/api';
import { ConfirmDialog, CrudDialog } from '@/shared/ui/CrudDialog';
import { DEFAULT_LANG, LandingIcon, LanguageTabs, pick, withLang } from './landingEditing';

const EMPTY: UpsertLandingTrustEntryBody = {
  icon: null,
  names: {},
  sortOrder: 0,
  isPublished: true,
};

/**
 * The "trusted for verification with" strip — the bodies named across the top of the landing page.
 *
 * Separate from the verification authorities the wizard offers: this is marketing copy, and adding
 * a name here does not put it into the application cascade.
 */
export function LandingTrustSection({
  entries,
  lang,
  onLangChange,
  onNotice,
}: {
  entries: LandingTrustEntryDto[];
  lang: string;
  onLangChange: (lang: string) => void;
  onNotice: (message: string) => void;
}) {
  const { t } = useTranslation();

  const canCreate = adminSession.has(Permissions.LandingContentCreate);
  const canUpdate = adminSession.has(Permissions.LandingContentUpdate);
  const canDelete = adminSession.has(Permissions.LandingContentDelete);

  const save = useSaveLandingTrustEntry();
  const remove = useDeleteLandingTrustEntry();

  const [editing, setEditing] = useState<LandingTrustEntryDto | null>(null);
  const [isCreating, setIsCreating] = useState(false);
  const [form, setForm] = useState<UpsertLandingTrustEntryBody>(EMPTY);
  const [toDelete, setToDelete] = useState<LandingTrustEntryDto | null>(null);

  useEffect(() => {
    if (editing) {
      setForm({
        id: editing.id,
        icon: editing.icon,
        names: { ...editing.names },
        sortOrder: editing.sortOrder,
        isPublished: editing.isPublished,
      });
    } else if (isCreating) {
      setForm({ ...EMPTY, sortOrder: entries.length });
    }
  }, [editing, isCreating, entries.length]);

  const ordered = [...entries].sort((a, b) => a.sortOrder - b.sortOrder);
  const isDialogOpen = isCreating || editing !== null;

  const complete = Boolean(form.names[DEFAULT_LANG]?.trim());

  function closeDialog() {
    setEditing(null);
    setIsCreating(false);
    save.reset();
  }

  function submit() {
    save.mutate(form, {
      onSuccess: () => {
        closeDialog();
        onNotice(t('landingContent.trustSaved'));
      },
    });
  }

  function confirmDelete() {
    if (!toDelete) return;

    remove.mutate(toDelete.id, {
      onSuccess: () => {
        setToDelete(null);
        onNotice(t('landingContent.trustDeleted'));
      },
    });
  }

  return (
    <section className="space-y-4">
      {/* The title and hint come from the page header now, so only the action lives here. */}
      {canCreate && (
        <div className="flex justify-end">
          <Button onClick={() => setIsCreating(true)} data-testid="new-trust">
            <Plus className="size-4" />
            {t('landingContent.newTrust')}
          </Button>
        </div>
      )}

      {ordered.length === 0 ? (
        <Alert variant="info">{t('landingContent.trustEmpty')}</Alert>
      ) : (
        <ul className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3" data-testid="trust-list">
          {ordered.map((entry) => (
            <li
              key={entry.id}
              className="flex items-center gap-3 rounded-2xl bg-white p-4 shadow-soft ring-1 ring-ink-100"
            >
              {/* The building mark is the site's default, so an entry with no icon previews the
                  way it will actually render rather than as a gap. */}
              {entry.icon ? (
                <LandingIcon icon={entry.icon} />
              ) : (
                <span className="flex size-9 items-center justify-center rounded-full bg-ink-50 text-ink-400 ring-1 ring-ink-100">
                  <Building2 className="size-4" />
                </span>
              )}

              <div className="min-w-0 flex-1">
                <p className="truncate font-medium text-ink-950" dir="auto">
                  {pick(entry.names, lang)}
                </p>
                <span
                  className={cn(
                    'mt-1 inline-block rounded-md px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide',
                    entry.isPublished ? 'bg-brand-50 text-brand-700' : 'bg-ink-100 text-ink-400',
                  )}
                >
                  {entry.isPublished ? t('landingContent.published') : t('landingContent.hidden')}
                </span>
              </div>

              <div className="flex shrink-0 flex-col gap-1">
                {canUpdate && (
                  <button
                    type="button"
                    onClick={() => setEditing(entry)}
                    className="flex size-8 items-center justify-center rounded-lg text-ink-400 hover:bg-ink-50 hover:text-ink-700"
                    aria-label={t('common.edit')}
                    data-testid={`edit-trust-${entry.id}`}
                  >
                    <Pencil className="size-4" />
                  </button>
                )}
                {canDelete && (
                  <button
                    type="button"
                    onClick={() => setToDelete(entry)}
                    className="flex size-8 items-center justify-center rounded-lg text-ink-400 hover:bg-red-50 hover:text-red-600"
                    aria-label={t('common.delete')}
                    data-testid={`delete-trust-${entry.id}`}
                  >
                    <Trash2 className="size-4" />
                  </button>
                )}
              </div>
            </li>
          ))}
        </ul>
      )}

      <CrudDialog
        open={isDialogOpen}
        title={editing ? t('landingContent.editTrust') : t('landingContent.newTrust')}
        onClose={closeDialog}
        onSubmit={submit}
        isPending={save.isPending}
        error={save.error}
        canSubmit={complete}
      >
        <div className="space-y-4">
          <LanguageTabs
            value={lang}
            onChange={onLangChange}
            filled={(code) => Boolean(form.names[code]?.trim())}
          />

          <Field label={t('landingContent.trustIcon')} htmlFor="trust-icon">
            <SearchableSelect
              id="trust-icon"
              value={form.icon ?? ''}
              onChange={(value) => setForm({ ...form, icon: value || null })}
              placeholder={t('landingContent.defaultIcon')}
              searchPlaceholder={t('landingContent.searchIcons')}
              emptyMessage={t('landingContent.noIconMatch')}
              clearable
              options={LANDING_ICON_KEYS.map((key) => ({
                value: key,
                label: t(`landingContent.icons.${key}`),
                icon: <LandingIcon icon={key} className="size-7" />,
              }))}
            />
          </Field>

          <Field label={t('landingContent.trustName')} htmlFor="trust-name" required>
            <Input
              id="trust-name"
              dir="auto"
              maxLength={200}
              placeholder={t('landingContent.trustNamePlaceholder')}
              value={form.names[lang] ?? ''}
              onChange={(event) =>
                setForm({ ...form, names: withLang(form.names, lang, event.target.value) })
              }
              data-testid="trust-name"
            />
          </Field>

          {!complete && <Alert variant="info">{t('landingContent.englishRequired')}</Alert>}

          <div className="grid gap-4 sm:grid-cols-2">
            <Field label={t('lookups.sortOrder')} htmlFor="trust-sort">
              <Input
                id="trust-sort"
                type="number"
                min={0}
                value={form.sortOrder}
                onChange={(event) =>
                  setForm({ ...form, sortOrder: Number(event.target.value) || 0 })
                }
              />
            </Field>

            <Field label={t('lookups.status')} htmlFor="trust-published">
              <Select
                id="trust-published"
                value={form.isPublished ? '1' : '0'}
                onChange={(event) => setForm({ ...form, isPublished: event.target.value === '1' })}
              >
                <option value="1">{t('landingContent.published')}</option>
                <option value="0">{t('landingContent.hidden')}</option>
              </Select>
            </Field>
          </div>
        </div>
      </CrudDialog>

      <ConfirmDialog
        open={toDelete !== null}
        title={t('landingContent.deleteTrustTitle')}
        body={t('landingContent.deleteTrustBody')}
        confirmLabel={t('common.delete')}
        onClose={() => setToDelete(null)}
        onConfirm={confirmDelete}
        isPending={remove.isPending}
        destructive
      />
    </section>
  );
}
