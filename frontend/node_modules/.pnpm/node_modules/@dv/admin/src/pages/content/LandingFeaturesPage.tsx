import { Alert, Button, Field, Input, LoadingState, Select, cn } from '@dv/ui';
import { GripVertical, Pencil, Plus, Trash2 } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { adminSession, Permissions } from '@/features/auth/session';
import {
  LANDING_ICON_KEYS,
  useAdminLandingContent,
  useDeleteLandingFeature,
  useSaveLandingFeature,
  useUpdateLandingHeading,
  type LandingFeatureDto,
  type Translations,
  type UpsertLandingFeatureBody,
} from '@/features/content/api';
import { ConfirmDialog, CrudDialog } from '@/shared/ui/CrudDialog';
import { Notice } from '@/pages/lookups/tabs/shared';
import {
  DEFAULT_LANG,
  LandingPageShell,
  LanguageTabs,
  SectionHeadingPanel,
  pick,
  useEditingLanguage,
  withLang,
} from './landingEditing';

const EMPTY: UpsertLandingFeatureBody = {
  icon: 'timeline',
  titles: {},
  bodies: {},
  sortOrder: 0,
  isPublished: true,
};

/** The cards in the landing page's "features" section, on their own page. */
export function LandingFeaturesPage() {
  const { t } = useTranslation();
  const content = useAdminLandingContent();

  const canUpdate = adminSession.has(Permissions.LandingContentUpdate);
  const canCreate = adminSession.has(Permissions.LandingContentCreate);
  const canDelete = adminSession.has(Permissions.LandingContentDelete);

  const save = useSaveLandingFeature();
  const saveHeading = useUpdateLandingHeading();
  const remove = useDeleteLandingFeature();

  const [lang, setLang] = useEditingLanguage();
  const [notice, setNotice] = useState<string | null>(null);
  const [eyebrow, setEyebrow] = useState<Translations>({});
  const [sectionTitle, setSectionTitle] = useState<Translations>({});
  const [editing, setEditing] = useState<LandingFeatureDto | null>(null);
  const [isCreating, setIsCreating] = useState(false);
  const [form, setForm] = useState<UpsertLandingFeatureBody>(EMPTY);
  const [toDelete, setToDelete] = useState<LandingFeatureDto | null>(null);

  const featureCount = content.data?.features.length ?? 0;

  useEffect(() => {
    if (content.data) {
      setEyebrow(content.data.eyebrow ?? {});
      setSectionTitle(content.data.title ?? {});
    }
  }, [content.data]);

  useEffect(() => {
    if (editing) {
      setForm({
        id: editing.id,
        icon: editing.icon,
        titles: { ...editing.titles },
        bodies: { ...editing.bodies },
        sortOrder: editing.sortOrder,
        isPublished: editing.isPublished,
      });
    } else if (isCreating) {
      setForm({ ...EMPTY, sortOrder: featureCount });
    }
  }, [editing, isCreating, featureCount]);

  if (content.isPending) {
    return <LoadingState label={t('common.loading')} />;
  }

  if (content.isError || !content.data) {
    return <Alert variant="error">{t('errors.genericTitle')}</Alert>;
  }

  const features = [...content.data.features].sort((a, b) => a.sortOrder - b.sortOrder);
  const isDialogOpen = isCreating || editing !== null;
  const complete = Boolean(form.titles[DEFAULT_LANG]?.trim() && form.bodies[DEFAULT_LANG]?.trim());

  function closeDialog() {
    setEditing(null);
    setIsCreating(false);
    save.reset();
  }

  function submit() {
    save.mutate(form, {
      onSuccess: () => {
        closeDialog();
        setNotice(t('landingContent.cardSaved'));
      },
    });
  }

  function confirmDelete() {
    if (!toDelete) return;

    remove.mutate(toDelete.id, {
      onSuccess: () => {
        setToDelete(null);
        setNotice(t('landingContent.cardDeleted'));
      },
    });
  }

  return (
    <LandingPageShell
      title={t('landingContent.cardsTitle')}
      subtitle={t('landingContent.cardsHint')}
      lang={lang}
      onLangChange={setLang}
      filled={(code) =>
        Boolean(eyebrow[code]?.trim() || sectionTitle[code]?.trim())
        || features.some((feature) => feature.titles[code]?.trim() && feature.bodies[code]?.trim())
      }
    >
      <Notice message={notice} onDismiss={() => setNotice(null)} />

      {/* Only this section's heading is sent, so saving here cannot disturb the trust strip copy. */}
      <SectionHeadingPanel
        fields={[
          {
            id: 'eyebrow',
            label: t('landingContent.eyebrow'),
            value: eyebrow[lang] ?? '',
            onChange: (value) => setEyebrow(withLang(eyebrow, lang, value)),
          },
          {
            id: 'section-title',
            label: t('landingContent.sectionTitle'),
            value: sectionTitle[lang] ?? '',
            onChange: (value) => setSectionTitle(withLang(sectionTitle, lang, value)),
          },
        ]}
        canUpdate={canUpdate}
        complete={Boolean(eyebrow[DEFAULT_LANG]?.trim() && sectionTitle[DEFAULT_LANG]?.trim())}
        isPending={saveHeading.isPending}
        onSave={() =>
          saveHeading.mutate(
            { eyebrow, title: sectionTitle },
            { onSuccess: () => setNotice(t('landingContent.headingSaved')) },
          )
        }
      />

      <section className="space-y-4">
        {canCreate && (
          <div className="flex justify-end">
            <Button onClick={() => setIsCreating(true)} data-testid="new-card">
              <Plus className="size-4" />
              {t('landingContent.newCard')}
            </Button>
          </div>
        )}

        {features.length === 0 ? (
          <Alert variant="info">{t('landingContent.empty')}</Alert>
        ) : (
          <ul className="grid gap-3 sm:grid-cols-2" data-testid="card-list">
            {features.map((feature) => (
              <li
                key={feature.id}
                className="flex gap-4 rounded-2xl bg-white p-5 shadow-soft ring-1 ring-ink-100"
              >
                <GripVertical className="mt-0.5 size-4 shrink-0 text-ink-300" aria-hidden />
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-2">
                    <span className="rounded-md bg-ink-950 px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide text-brand-400">
                      {feature.icon}
                    </span>
                    <span
                      className={cn(
                        'rounded-md px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide',
                        feature.isPublished
                          ? 'bg-brand-50 text-brand-700'
                          : 'bg-ink-100 text-ink-400',
                      )}
                    >
                      {feature.isPublished
                        ? t('landingContent.published')
                        : t('landingContent.hidden')}
                    </span>
                  </div>
                  <h3 className="mt-2 truncate font-semibold text-ink-950" dir="auto">
                    {pick(feature.titles, lang)}
                  </h3>
                  <p className="mt-1 line-clamp-3 text-sm text-ink-500" dir="auto">
                    {pick(feature.bodies, lang)}
                  </p>
                </div>
                <div className="flex shrink-0 flex-col gap-1">
                  {canUpdate && (
                    <button
                      type="button"
                      onClick={() => setEditing(feature)}
                      className="flex size-8 items-center justify-center rounded-lg text-ink-400 hover:bg-ink-50 hover:text-ink-700"
                      aria-label={t('common.edit')}
                      data-testid={`edit-card-${feature.id}`}
                    >
                      <Pencil className="size-4" />
                    </button>
                  )}
                  {canDelete && (
                    <button
                      type="button"
                      onClick={() => setToDelete(feature)}
                      className="flex size-8 items-center justify-center rounded-lg text-ink-400 hover:bg-red-50 hover:text-red-600"
                      aria-label={t('common.delete')}
                      data-testid={`delete-card-${feature.id}`}
                    >
                      <Trash2 className="size-4" />
                    </button>
                  )}
                </div>
              </li>
            ))}
          </ul>
        )}
      </section>

      <CrudDialog
        open={isDialogOpen}
        title={editing ? t('landingContent.editCard') : t('landingContent.newCard')}
        onClose={closeDialog}
        onSubmit={submit}
        isPending={save.isPending}
        canSubmit={complete}
        error={save.error}
      >
        <div className="rounded-lg bg-ink-50 p-3">
          <LanguageTabs
            value={lang}
            onChange={setLang}
            filled={(l) => Boolean(form.titles[l]?.trim() || form.bodies[l]?.trim())}
          />
        </div>

        <Field label={t('landingContent.icon')} htmlFor="icon">
          <Select
            id="icon"
            value={form.icon}
            onChange={(event) => setForm({ ...form, icon: event.target.value })}
          >
            {LANDING_ICON_KEYS.map((key) => (
              <option key={key} value={key}>
                {t(`landingContent.icons.${key}`)}
              </option>
            ))}
          </Select>
        </Field>

        <Field label={t('landingContent.cardTitle')} htmlFor="card-title" required>
          <Input
            id="card-title"
            value={form.titles[lang] ?? ''}
            dir="auto"
            onChange={(event) =>
              setForm({ ...form, titles: withLang(form.titles, lang, event.target.value) })
            }
            data-testid="card-title"
          />
        </Field>

        <Field label={t('landingContent.cardBody')} htmlFor="card-body" required>
          <textarea
            id="card-body"
            rows={4}
            value={form.bodies[lang] ?? ''}
            dir="auto"
            onChange={(event) =>
              setForm({ ...form, bodies: withLang(form.bodies, lang, event.target.value) })
            }
            className="flex w-full rounded-xl border-0 bg-white px-3.5 py-2.5 text-sm text-ink-900 shadow-soft ring-1 ring-ink-200 transition-shadow placeholder:text-ink-300 focus:outline-none focus:ring-2 focus:ring-brand-500"
          />
        </Field>

        {!complete && <p className="text-xs text-amber-600">{t('landingContent.englishRequired')}</p>}

        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t('landingContent.sortOrder')} htmlFor="sort-order">
            <Input
              id="sort-order"
              type="number"
              min={0}
              value={form.sortOrder}
              onChange={(event) => setForm({ ...form, sortOrder: Number(event.target.value) || 0 })}
            />
          </Field>

          <label className="flex items-center gap-2 self-end pb-3 text-sm">
            <input
              type="checkbox"
              className="size-4 rounded border-ink-200"
              checked={form.isPublished}
              onChange={(event) => setForm({ ...form, isPublished: event.target.checked })}
            />
            {t('landingContent.isPublished')}
          </label>
        </div>
      </CrudDialog>

      <ConfirmDialog
        open={toDelete !== null}
        title={t('landingContent.deleteTitle')}
        body={t('landingContent.deleteBody')}
        confirmLabel={t('common.delete')}
        onClose={() => setToDelete(null)}
        onConfirm={confirmDelete}
        isPending={remove.isPending}
        destructive
      />
    </LandingPageShell>
  );
}
