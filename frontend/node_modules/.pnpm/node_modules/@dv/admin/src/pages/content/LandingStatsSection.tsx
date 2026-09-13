import { Alert, Button, Field, Input, SearchableSelect, Select, cn } from '@dv/ui';
import { Pencil, Plus, Trash2 } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { adminSession, Permissions } from '@/features/auth/session';
import {
  LANDING_ICON_KEYS,
  useDeleteLandingStat,
  useSaveLandingStat,
  type LandingStatDto,
  type UpsertLandingStatBody,
} from '@/features/content/api';
import { ConfirmDialog, CrudDialog } from '@/shared/ui/CrudDialog';
import { DEFAULT_LANG, LandingIcon, LanguageTabs, pick, withLang } from './landingEditing';

const EMPTY: UpsertLandingStatBody = {
  icon: null,
  values: {},
  labels: {},
  sortOrder: 0,
  isPublished: true,
};

/**
 * The statistics strip across the top of the landing page — "48,000+ documents verified".
 *
 * Both halves are written per language rather than only the caption. The figure is not always a
 * bare number: "7 days" carries a word, and a locale may want its own numerals. An operator writes
 * what the card should read and sees exactly that.
 */
export function LandingStatsSection({
  stats,
  lang,
  onLangChange,
  onNotice,
}: {
  stats: LandingStatDto[];
  lang: string;
  onLangChange: (lang: string) => void;
  onNotice: (message: string) => void;
}) {
  const { t } = useTranslation();

  const canCreate = adminSession.has(Permissions.LandingContentCreate);
  const canUpdate = adminSession.has(Permissions.LandingContentUpdate);
  const canDelete = adminSession.has(Permissions.LandingContentDelete);

  const save = useSaveLandingStat();
  const remove = useDeleteLandingStat();

  const [editing, setEditing] = useState<LandingStatDto | null>(null);
  const [isCreating, setIsCreating] = useState(false);
  const [form, setForm] = useState<UpsertLandingStatBody>(EMPTY);
  const [toDelete, setToDelete] = useState<LandingStatDto | null>(null);

  useEffect(() => {
    if (editing) {
      setForm({
        id: editing.id,
        icon: editing.icon,
        values: { ...editing.values },
        labels: { ...editing.labels },
        sortOrder: editing.sortOrder,
        isPublished: editing.isPublished,
      });
    } else if (isCreating) {
      setForm({ ...EMPTY, sortOrder: stats.length });
    }
  }, [editing, isCreating, stats.length]);

  const ordered = [...stats].sort((a, b) => a.sortOrder - b.sortOrder);
  const isDialogOpen = isCreating || editing !== null;

  // English is the guaranteed fallback, so it is what makes a figure publishable at all.
  const complete = Boolean(form.values[DEFAULT_LANG]?.trim() && form.labels[DEFAULT_LANG]?.trim());

  function closeDialog() {
    setEditing(null);
    setIsCreating(false);
    save.reset();
  }

  function submit() {
    save.mutate(form, {
      onSuccess: () => {
        closeDialog();
        onNotice(t('landingContent.statSaved'));
      },
    });
  }

  function confirmDelete() {
    if (!toDelete) return;

    remove.mutate(toDelete.id, {
      onSuccess: () => {
        setToDelete(null);
        onNotice(t('landingContent.statDeleted'));
      },
    });
  }

  return (
    <section className="space-y-4">
      {/* The title and hint come from the page header now, so only the action lives here. */}
      {canCreate && (
        <div className="flex justify-end">
          <Button onClick={() => setIsCreating(true)} data-testid="new-stat">
            <Plus className="size-4" />
            {t('landingContent.newStat')}
          </Button>
        </div>
      )}

      {ordered.length === 0 ? (
        <Alert variant="info">{t('landingContent.statsEmpty')}</Alert>
      ) : (
        <ul className="grid gap-3 sm:grid-cols-3" data-testid="stat-list">
          {ordered.map((stat) => (
            <li
              key={stat.id}
              className="flex gap-4 rounded-2xl bg-white p-5 shadow-soft ring-1 ring-ink-100"
            >
              <div className="min-w-0 flex-1">
                <span
                  className={cn(
                    'rounded-md px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide',
                    stat.isPublished ? 'bg-brand-50 text-brand-700' : 'bg-ink-100 text-ink-400',
                  )}
                >
                  {stat.isPublished ? t('landingContent.published') : t('landingContent.hidden')}
                </span>

                {/* Rendered the way the landing page renders it, so the preview is the product. */}
                <LandingIcon icon={stat.icon} className="mt-2" />

                <p className="mt-2 font-display text-2xl font-semibold text-ink-950" dir="auto">
                  {pick(stat.values, lang)}
                </p>
                <p className="mt-0.5 truncate text-sm text-ink-500" dir="auto">
                  {pick(stat.labels, lang)}
                </p>
              </div>

              <div className="flex shrink-0 flex-col gap-1">
                {canUpdate && (
                  <button
                    type="button"
                    onClick={() => setEditing(stat)}
                    className="flex size-8 items-center justify-center rounded-lg text-ink-400 hover:bg-ink-50 hover:text-ink-700"
                    aria-label={t('common.edit')}
                    data-testid={`edit-stat-${stat.id}`}
                  >
                    <Pencil className="size-4" />
                  </button>
                )}
                {canDelete && (
                  <button
                    type="button"
                    onClick={() => setToDelete(stat)}
                    className="flex size-8 items-center justify-center rounded-lg text-ink-400 hover:bg-red-50 hover:text-red-600"
                    aria-label={t('common.delete')}
                    data-testid={`delete-stat-${stat.id}`}
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
        title={editing ? t('landingContent.editStat') : t('landingContent.newStat')}
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
            filled={(code) => Boolean(form.values[code]?.trim() && form.labels[code]?.trim())}
          />

          {/* Every row draws its own glyph, so the list is browsed by eye. A native <select>
              cannot render an SVG inside an <option>, which is why this is the searchable
              control rather than the plain one used elsewhere in the form. */}
          <Field label={t('landingContent.statIcon')} htmlFor="stat-icon">
            <SearchableSelect
              id="stat-icon"
              value={form.icon ?? ''}
              onChange={(value) => setForm({ ...form, icon: value || null })}
              placeholder={t('landingContent.noIcon')}
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

          <Field label={t('landingContent.statValue')} htmlFor="stat-value" required>
            <Input
              id="stat-value"
              dir="auto"
              maxLength={50}
              placeholder="48,000+"
              value={form.values[lang] ?? ''}
              onChange={(event) =>
                setForm({ ...form, values: withLang(form.values, lang, event.target.value) })
              }
              data-testid="stat-value"
            />
          </Field>

          <Field label={t('landingContent.statLabel')} htmlFor="stat-label" required>
            <Input
              id="stat-label"
              dir="auto"
              maxLength={200}
              placeholder={t('landingContent.statLabelPlaceholder')}
              value={form.labels[lang] ?? ''}
              onChange={(event) =>
                setForm({ ...form, labels: withLang(form.labels, lang, event.target.value) })
              }
              data-testid="stat-label"
            />
          </Field>

          {!complete && (
            <Alert variant="info">{t('landingContent.englishRequired')}</Alert>
          )}

          <div className="grid gap-4 sm:grid-cols-2">
            <Field label={t('lookups.sortOrder')} htmlFor="stat-sort">
              <Input
                id="stat-sort"
                type="number"
                min={0}
                value={form.sortOrder}
                onChange={(event) =>
                  setForm({ ...form, sortOrder: Number(event.target.value) || 0 })
                }
              />
            </Field>

            <Field label={t('lookups.status')} htmlFor="stat-published">
              <Select
                id="stat-published"
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
        title={t('landingContent.deleteStatTitle')}
        body={t('landingContent.deleteStatBody')}
        confirmLabel={t('common.delete')}
        onClose={() => setToDelete(null)}
        onConfirm={confirmDelete}
        isPending={remove.isPending}
        destructive
      />
    </section>
  );
}
