import { Alert, Button, Field, Input, LoadingState, Select, cn } from '@dv/ui';
import { Plus, Trash2 } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { adminSession, Permissions } from '@/features/auth/session';
import {
  COVERAGE_MARKERS,
  useAdminCoverageContent,
  useDeleteCoverageEntry,
  useSaveCoverageEntry,
  useUpdateCoverageHeading,
  type CoverageEntryDto,
  type CoverageMarker,
  type UpsertCoverageEntryBody,
} from '@/features/content/coverage';
import { useCountries } from '@/features/lookups/api';
import type { Translations } from '@/features/content/api';
import { ConfirmDialog, CrudDialog } from '@/shared/ui/CrudDialog';
import { Notice } from '@/pages/lookups/tabs/shared';
import {
  DEFAULT_LANG,
  LandingPageShell,
  SectionHeadingPanel,
  useEditingLanguage,
  withLang,
} from './landingEditing';

const EMPTY: UpsertCoverageEntryBody = {
  countryId: '',
  marker: 'Spot',
  sortOrder: 0,
  isPublished: true,
};

/** flagcdn, the same source the applicant site draws country flags from. */
const flagUrl = (code: string, width: 20 | 40 = 20) =>
  `https://flagcdn.com/w${width}/${code.toLowerCase()}.png`;

/**
 * "Where we verify": the section's heading, and the countries spotted on its map.
 *
 * A country is picked from the lookup rather than typed. The site draws the map from ISO codes, so
 * a free-typed name could never be placed on it — and the country's name is already translated
 * everywhere the site speaks, which is why this page has no name field of its own. That is also
 * why the language tabs above affect only the heading.
 */
export function CoveragePage() {
  const { t } = useTranslation();
  const content = useAdminCoverageContent();

  // Every country, so the picker is the whole lookup rather than the first page of it.
  const countries = useCountries({ page: 1, pageSize: 300, isActive: true });

  const canUpdate = adminSession.has(Permissions.LandingContentUpdate);
  const canCreate = adminSession.has(Permissions.LandingContentCreate);
  const canDelete = adminSession.has(Permissions.LandingContentDelete);

  const saveHeading = useUpdateCoverageHeading();
  const save = useSaveCoverageEntry();
  const remove = useDeleteCoverageEntry();

  const [lang, setLang] = useEditingLanguage();
  const [notice, setNotice] = useState<string | null>(null);
  const [title, setTitle] = useState<Translations>({});
  const [subtitle, setSubtitle] = useState<Translations>({});
  const [editing, setEditing] = useState<CoverageEntryDto | null>(null);
  const [isCreating, setIsCreating] = useState(false);
  const [form, setForm] = useState<UpsertCoverageEntryBody>(EMPTY);
  const [toDelete, setToDelete] = useState<CoverageEntryDto | null>(null);

  const entries = useMemo(
    () => [...(content.data?.countries ?? [])].sort((a, b) => a.sortOrder - b.sortOrder),
    [content.data],
  );

  useEffect(() => {
    if (content.data) {
      setTitle(content.data.title ?? {});
      setSubtitle(content.data.subtitle ?? {});
    }
  }, [content.data]);

  useEffect(() => {
    if (editing) {
      setForm({
        id: editing.id,
        countryId: editing.countryId,
        marker: editing.marker,
        sortOrder: editing.sortOrder,
        isPublished: editing.isPublished,
      });
    } else if (isCreating) {
      setForm({ ...EMPTY, sortOrder: entries.length });
    }
  }, [editing, isCreating, entries.length]);

  if (content.isPending) {
    return <LoadingState label={t('common.loading')} />;
  }

  if (content.isError || !content.data) {
    return <Alert variant="error">{t('errors.genericTitle')}</Alert>;
  }

  const isDialogOpen = isCreating || editing !== null;

  // A country already on the map is not offered again — the server refuses it, and an option that
  // can only fail is worse than one that is not there.
  const taken = new Set(entries.filter((e) => e.id !== editing?.id).map((e) => e.countryId));
  const options = (countries.data?.items ?? []).filter((c) => !taken.has(c.id));

  function closeDialog() {
    setEditing(null);
    setIsCreating(false);
    save.reset();
  }

  function submit() {
    save.mutate(form, {
      onSuccess: () => {
        closeDialog();
        setNotice(t('coverage.countrySaved'));
      },
    });
  }

  return (
    <LandingPageShell
      title={t('coverage.pageTitle')}
      subtitle={t('coverage.pageHint')}
      lang={lang}
      onLangChange={setLang}
      // A country carries no copy of its own here, so the heading is all there is to translate.
      filled={(code) => Boolean(title[code]?.trim() || subtitle[code]?.trim())}
    >
      <Notice message={notice} onDismiss={() => setNotice(null)} />

      <SectionHeadingPanel
        fields={[
          {
            id: 'coverage-title',
            label: t('coverage.title'),
            value: title[lang] ?? '',
            onChange: (value) => setTitle(withLang(title, lang, value)),
          },
          {
            id: 'coverage-subtitle',
            label: t('coverage.subtitle'),
            value: subtitle[lang] ?? '',
            onChange: (value) => setSubtitle(withLang(subtitle, lang, value)),
          },
        ]}
        canUpdate={canUpdate}
        complete={Boolean(title[DEFAULT_LANG]?.trim())}
        isPending={saveHeading.isPending}
        onSave={() =>
          saveHeading.mutate(
            { title, subtitle },
            { onSuccess: () => setNotice(t('landingContent.headingSaved')) },
          )
        }
      />

      <section className="space-y-4">
        <div className="flex items-center justify-between gap-3">
          <div>
            <h2 className="font-display text-base font-semibold text-ink-950">
              {t('coverage.countriesTitle')}
            </h2>
            <p className="mt-1 text-sm text-ink-500">{t('coverage.countriesHint')}</p>
          </div>
          {canCreate && (
            <Button onClick={() => setIsCreating(true)} data-testid="new-coverage-country">
              <Plus className="size-4" />
              {t('coverage.newCountry')}
            </Button>
          )}
        </div>

        {entries.length === 0 ? (
          <Alert variant="info">{t('coverage.countriesEmpty')}</Alert>
        ) : (
          <ul
            className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3"
            data-testid="coverage-country-list"
          >
            {entries.map((entry) => (
              <li
                key={entry.id}
                className="flex items-center gap-3 rounded-2xl bg-white p-4 shadow-soft ring-1 ring-ink-100"
              >
                <img
                  src={flagUrl(entry.code)}
                  srcSet={`${flagUrl(entry.code, 40)} 2x`}
                  alt=""
                  width={24}
                  height={18}
                  className="h-[18px] w-6 shrink-0 rounded-[2px] object-cover ring-1 ring-ink-100"
                />

                <div className="min-w-0 flex-1">
                  <p className="truncate font-medium text-ink-950" dir="auto">
                    {entry.name}
                  </p>
                  <div className="mt-1 flex flex-wrap items-center gap-1.5">
                    <span
                      className={cn(
                        'inline-block rounded-md px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide',
                        entry.isPublished
                          ? 'bg-brand-50 text-brand-700'
                          : 'bg-ink-100 text-ink-400',
                      )}
                    >
                      {entry.isPublished ? t('lookups.active') : t('lookups.inactive')}
                    </span>
                    {/* Which of the two marks the map draws here, without opening the row. */}
                    <span className="inline-block rounded-md bg-ink-100 px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide text-ink-500">
                      {t(`coverage.marker${entry.marker}`)}
                    </span>
                  </div>
                </div>

                {canUpdate && (
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={() => setEditing(entry)}
                    data-testid={`edit-coverage-${entry.code}`}
                  >
                    {t('common.edit')}
                  </Button>
                )}
                {canDelete && (
                  <button
                    type="button"
                    onClick={() => setToDelete(entry)}
                    className="flex size-8 shrink-0 items-center justify-center rounded-lg text-ink-400 hover:bg-red-50 hover:text-red-600"
                    aria-label={t('common.delete')}
                    data-testid={`delete-coverage-${entry.code}`}
                  >
                    <Trash2 className="size-4" />
                  </button>
                )}
              </li>
            ))}
          </ul>
        )}
      </section>

      <CrudDialog
        open={isDialogOpen}
        title={editing ? t('coverage.editCountry') : t('coverage.newCountry')}
        onClose={closeDialog}
        onSubmit={submit}
        isPending={save.isPending}
        canSubmit={Boolean(form.countryId)}
        error={save.error}
      >
        <div className="space-y-4">
          <Field label={t('coverage.country')} htmlFor="coverage-country" required>
            <Select
              id="coverage-country"
              value={form.countryId}
              onChange={(event) => setForm({ ...form, countryId: event.target.value })}
              data-testid="coverage-country"
            >
              <option value="">{t('coverage.chooseCountry')}</option>
              {options.map((country) => (
                <option key={country.id} value={country.id}>
                  {country.name} ({country.code})
                </option>
              ))}
            </Select>
          </Field>

          <Field label={t('coverage.marker')} htmlFor="coverage-marker" required>
            <Select
              id="coverage-marker"
              value={form.marker}
              onChange={(event) =>
                setForm({ ...form, marker: event.target.value as CoverageMarker })
              }
              data-testid="coverage-marker"
            >
              {COVERAGE_MARKERS.map((marker) => (
                <option key={marker} value={marker}>
                  {t(`coverage.marker${marker}`)}
                </option>
              ))}
            </Select>
          </Field>

          <p className="text-xs text-subtle">{t('coverage.markerHint')}</p>

          <div className="grid gap-4 sm:grid-cols-2">
            <Field label={t('lookups.sortOrder')} htmlFor="coverage-sort">
              <Input
                id="coverage-sort"
                type="number"
                min={0}
                value={form.sortOrder}
                onChange={(event) =>
                  setForm({ ...form, sortOrder: Number(event.target.value) || 0 })
                }
              />
            </Field>

            <Field label={t('lookups.status')} htmlFor="coverage-published">
              <Select
                id="coverage-published"
                value={form.isPublished ? '1' : '0'}
                onChange={(event) =>
                  setForm({ ...form, isPublished: event.target.value === '1' })
                }
              >
                <option value="1">{t('lookups.active')}</option>
                <option value="0">{t('lookups.inactive')}</option>
              </Select>
            </Field>
          </div>
        </div>
      </CrudDialog>

      <ConfirmDialog
        open={toDelete !== null}
        title={t('coverage.deleteTitle')}
        body={t('coverage.deleteBody')}
        confirmLabel={t('common.delete')}
        onClose={() => setToDelete(null)}
        onConfirm={() => {
          if (!toDelete) return;
          remove.mutate(toDelete.id, {
            onSuccess: () => {
              setToDelete(null);
              setNotice(t('coverage.countryDeleted'));
            },
          });
        }}
        isPending={remove.isPending}
        destructive
      />
    </LandingPageShell>
  );
}
