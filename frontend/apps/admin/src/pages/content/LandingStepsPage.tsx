import { Alert, Button, Field, Input, LoadingState, SearchableSelect, Select, Spinner, cn } from '@dv/ui';
import { Pencil, Plus, Trash2 } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { adminSession, Permissions } from '@/features/auth/session';
import {
  LANDING_ICON_KEYS,
  useAdminLandingContent,
  useDeleteLandingStep,
  useSaveLandingStep,
  useUpdateLandingHeading,
  useUpdateStepsLayout,
  type LandingSectionLayout,
  type LandingStepDto,
  type Translations,
  type UpsertLandingStepBody,
} from '@/features/content/api';
import { ConfirmDialog, CrudDialog } from '@/shared/ui/CrudDialog';
import { Notice } from '@/pages/lookups/tabs/shared';
import {
  DEFAULT_LANG,
  LandingIcon,
  LandingPageShell,
  LanguageTabs,
  SectionHeadingPanel,
  pick,
  useEditingLanguage,
  withLang,
} from './landingEditing';

const EMPTY: UpsertLandingStepBody = {
  icon: 'mail',
  titles: {},
  bodies: {},
  sortOrder: 0,
  isPublished: true,
};

/**
 * The "how it works" steps, and the heading above them.
 *
 * The number on each card is not edited: it comes from the step's place in the order, so moving a
 * step renumbers the set and nobody has to keep a "3." written into the copy in step with where
 * the card actually sits.
 */
export function LandingStepsPage() {
  const { t } = useTranslation();
  const content = useAdminLandingContent();

  const canUpdate = adminSession.has(Permissions.LandingContentUpdate);
  const canCreate = adminSession.has(Permissions.LandingContentCreate);
  const canDelete = adminSession.has(Permissions.LandingContentDelete);

  const save = useSaveLandingStep();
  const saveHeading = useUpdateLandingHeading();
  const remove = useDeleteLandingStep();
  const saveLayout = useUpdateStepsLayout();

  const [lang, setLang] = useEditingLanguage();
  const [notice, setNotice] = useState<string | null>(null);
  const [howEyebrow, setHowEyebrow] = useState<Translations>({});
  const [howTitle, setHowTitle] = useState<Translations>({});
  const [layout, setLayout] = useState<LandingSectionLayout>('Grid');
  const [columns, setColumns] = useState(3);
  const [editing, setEditing] = useState<LandingStepDto | null>(null);
  const [isCreating, setIsCreating] = useState(false);
  const [form, setForm] = useState<UpsertLandingStepBody>(EMPTY);
  const [toDelete, setToDelete] = useState<LandingStepDto | null>(null);

  const stepCount = content.data?.steps.length ?? 0;

  useEffect(() => {
    if (content.data) {
      setHowEyebrow(content.data.howEyebrow ?? {});
      setHowTitle(content.data.howTitle ?? {});
      setLayout(content.data.howLayout ?? 'Grid');
      setColumns(content.data.howColumns ?? 3);
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
      setForm({ ...EMPTY, sortOrder: stepCount });
    }
  }, [editing, isCreating, stepCount]);

  if (content.isPending) {
    return <LoadingState label={t('common.loading')} />;
  }

  if (content.isError || !content.data) {
    return <Alert variant="error">{t('errors.genericTitle')}</Alert>;
  }

  const steps = [...content.data.steps].sort((a, b) => a.sortOrder - b.sortOrder);
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
        setNotice(t('landingContent.stepSaved'));
      },
    });
  }

  function confirmDelete() {
    if (!toDelete) return;

    remove.mutate(toDelete.id, {
      onSuccess: () => {
        setToDelete(null);
        setNotice(t('landingContent.stepDeleted'));
      },
    });
  }

  return (
    <LandingPageShell
      title={t('landingContent.stepsTitle')}
      subtitle={t('landingContent.stepsHint')}
      lang={lang}
      onLangChange={setLang}
      filled={(code) =>
        Boolean(howEyebrow[code]?.trim() || howTitle[code]?.trim())
        || steps.some((step) => step.titles[code]?.trim() && step.bodies[code]?.trim())
      }
    >
      <Notice message={notice} onDismiss={() => setNotice(null)} />

      {/* Only this section's heading is sent, so saving here cannot disturb the other sections. */}
      <SectionHeadingPanel
        fields={[
          {
            id: 'how-eyebrow',
            label: t('landingContent.eyebrow'),
            value: howEyebrow[lang] ?? '',
            onChange: (value) => setHowEyebrow(withLang(howEyebrow, lang, value)),
          },
          {
            id: 'how-title',
            label: t('landingContent.sectionTitle'),
            value: howTitle[lang] ?? '',
            onChange: (value) => setHowTitle(withLang(howTitle, lang, value)),
          },
        ]}
        canUpdate={canUpdate}
        complete={Boolean(howEyebrow[DEFAULT_LANG]?.trim() && howTitle[DEFAULT_LANG]?.trim())}
        isPending={saveHeading.isPending}
        onSave={() =>
          saveHeading.mutate(
            { howEyebrow, howTitle },
            { onSuccess: () => setNotice(t('landingContent.headingSaved')) },
          )
        }
      />

      {/* Arrangement, not copy — so it is saved on its own and is not repeated per language. */}
      <section className="rounded-2xl bg-white p-6 shadow-soft ring-1 ring-ink-100">
        <h2 className="font-display text-base font-semibold text-ink-950">
          {t('landingContent.layoutTitle')}
        </h2>
        <p className="mt-1 text-sm text-ink-500">{t('landingContent.layoutHint')}</p>

        <div className="mt-5 grid gap-4 sm:grid-cols-2">
          <Field label={t('landingContent.layout')} htmlFor="steps-layout">
            <Select
              id="steps-layout"
              value={layout}
              disabled={!canUpdate}
              onChange={(event) => setLayout(event.target.value as LandingSectionLayout)}
              data-testid="steps-layout"
            >
              <option value="Grid">{t('landingContent.layoutGrid')}</option>
              <option value="Carousel">{t('landingContent.layoutCarousel')}</option>
            </Select>
          </Field>

          <Field label={t('landingContent.columns')} htmlFor="steps-columns">
            <Select
              id="steps-columns"
              value={String(columns)}
              disabled={!canUpdate}
              onChange={(event) => setColumns(Number(event.target.value))}
              data-testid="steps-columns"
            >
              {[1, 2, 3, 4].map((n) => (
                <option key={n} value={n}>
                  {t('landingContent.columnsOption', { count: n })}
                </option>
              ))}
            </Select>
          </Field>
        </div>

        <p className="mt-3 text-xs text-subtle">
          {layout === 'Carousel'
            ? t('landingContent.carouselHint')
            : t('landingContent.gridHint')}
        </p>

        {canUpdate && (
          <div className="mt-4 flex justify-end">
            <Button
              onClick={() =>
                saveLayout.mutate(
                  { layout, columns },
                  { onSuccess: () => setNotice(t('landingContent.layoutSaved')) },
                )
              }
              disabled={saveLayout.isPending}
              data-testid="save-layout"
            >
              {saveLayout.isPending && <Spinner />}
              {t('common.save')}
            </Button>
          </div>
        )}
      </section>

      <section className="space-y-4">
        {canCreate && (
          <div className="flex justify-end">
            <Button onClick={() => setIsCreating(true)} data-testid="new-step">
              <Plus className="size-4" />
              {t('landingContent.newStep')}
            </Button>
          </div>
        )}

        {steps.length === 0 ? (
          <Alert variant="info">{t('landingContent.stepsEmpty')}</Alert>
        ) : (
          <ul className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3" data-testid="step-list">
            {steps.map((step, index) => (
              <li
                key={step.id}
                className="flex gap-4 rounded-2xl bg-white p-5 shadow-soft ring-1 ring-ink-100"
              >
                <div className="min-w-0 flex-1">
                  <div className="flex items-center justify-between gap-2">
                    <LandingIcon icon={step.icon} />
                    {/* Shown the way the site draws it, so the order in this list is the order a
                        visitor sees rather than something to work out. */}
                    <span className="font-display text-2xl font-light text-ink-200">
                      {String(index + 1).padStart(2, '0')}
                    </span>
                  </div>

                  <span
                    className={cn(
                      'mt-3 inline-block rounded-md px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide',
                      step.isPublished ? 'bg-brand-50 text-brand-700' : 'bg-ink-100 text-ink-400',
                    )}
                  >
                    {step.isPublished ? t('landingContent.published') : t('landingContent.hidden')}
                  </span>

                  <h3 className="mt-2 truncate font-semibold text-ink-950" dir="auto">
                    {pick(step.titles, lang)}
                  </h3>
                  <p className="mt-1 line-clamp-3 text-sm text-ink-500" dir="auto">
                    {pick(step.bodies, lang)}
                  </p>
                </div>

                <div className="flex shrink-0 flex-col gap-1">
                  {canUpdate && (
                    <button
                      type="button"
                      onClick={() => setEditing(step)}
                      className="flex size-8 items-center justify-center rounded-lg text-ink-400 hover:bg-ink-50 hover:text-ink-700"
                      aria-label={t('common.edit')}
                      data-testid={`edit-step-${step.id}`}
                    >
                      <Pencil className="size-4" />
                    </button>
                  )}
                  {canDelete && (
                    <button
                      type="button"
                      onClick={() => setToDelete(step)}
                      className="flex size-8 items-center justify-center rounded-lg text-ink-400 hover:bg-red-50 hover:text-red-600"
                      aria-label={t('common.delete')}
                      data-testid={`delete-step-${step.id}`}
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
        title={editing ? t('landingContent.editStep') : t('landingContent.newStep')}
        onClose={closeDialog}
        onSubmit={submit}
        isPending={save.isPending}
        canSubmit={complete}
        error={save.error}
      >
        <div className="space-y-4">
          <LanguageTabs
            value={lang}
            onChange={setLang}
            filled={(code) => Boolean(form.titles[code]?.trim() || form.bodies[code]?.trim())}
          />

          <Field label={t('landingContent.icon')} htmlFor="step-icon">
            <SearchableSelect
              id="step-icon"
              value={form.icon}
              onChange={(value) => setForm({ ...form, icon: value || 'mail' })}
              searchPlaceholder={t('landingContent.searchIcons')}
              emptyMessage={t('landingContent.noIconMatch')}
              options={LANDING_ICON_KEYS.map((key) => ({
                value: key,
                label: t(`landingContent.icons.${key}`),
                icon: <LandingIcon icon={key} className="size-7" />,
              }))}
            />
          </Field>

          <Field label={t('landingContent.cardTitle')} htmlFor="step-title" required>
            <Input
              id="step-title"
              dir="auto"
              maxLength={200}
              value={form.titles[lang] ?? ''}
              onChange={(event) =>
                setForm({ ...form, titles: withLang(form.titles, lang, event.target.value) })
              }
              data-testid="step-title"
            />
          </Field>

          <Field label={t('landingContent.cardBody')} htmlFor="step-body" required>
            <textarea
              id="step-body"
              rows={4}
              dir="auto"
              maxLength={1000}
              value={form.bodies[lang] ?? ''}
              onChange={(event) =>
                setForm({ ...form, bodies: withLang(form.bodies, lang, event.target.value) })
              }
              className="flex w-full rounded-xl border-0 bg-white px-3.5 py-2.5 text-sm text-ink-900 shadow-soft ring-1 ring-ink-200 transition-shadow placeholder:text-ink-300 focus:outline-none focus:ring-2 focus:ring-brand-500"
              data-testid="step-body"
            />
          </Field>

          {!complete && <Alert variant="info">{t('landingContent.englishRequired')}</Alert>}

          <div className="grid gap-4 sm:grid-cols-2">
            <Field label={t('lookups.sortOrder')} htmlFor="step-sort">
              <Input
                id="step-sort"
                type="number"
                min={0}
                value={form.sortOrder}
                onChange={(event) =>
                  setForm({ ...form, sortOrder: Number(event.target.value) || 0 })
                }
              />
            </Field>

            <Field label={t('lookups.status')} htmlFor="step-published">
              <Select
                id="step-published"
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
        title={t('landingContent.deleteStepTitle')}
        body={t('landingContent.deleteStepBody')}
        confirmLabel={t('common.delete')}
        onClose={() => setToDelete(null)}
        onConfirm={confirmDelete}
        isPending={remove.isPending}
        destructive
      />
    </LandingPageShell>
  );
}
