import { Alert, Button, Field, Input, LoadingState, Select, cn } from '@dv/ui';
import { ArrowDown, ArrowUp, Plus, Trash2 } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { adminSession, Permissions } from '@/features/auth/session';
import {
  HEADER_VISIBILITIES,
  useAdminHeaderContent,
  useDeleteHeaderLink,
  useSaveHeaderLink,
  type HeaderLinkDto,
  type HeaderLinkVisibility,
  type UpsertHeaderLinkBody,
} from '@/features/content/header';
import { ConfirmDialog, CrudDialog } from '@/shared/ui/CrudDialog';
import { Notice } from '@/pages/lookups/tabs/shared';
import {
  DEFAULT_LANG,
  LandingPageShell,
  LanguageTabs,
  useEditingLanguage,
  withLang,
} from './landingEditing';

const EMPTY: UpsertHeaderLinkBody = {
  key: null,
  labels: {},
  url: '',
  visibility: 'Everyone',
  sortOrder: 0,
  isActive: true,
};

/**
 * The site header: which entries it carries, in what order, and who sees each one.
 *
 * The entries the site ships with carry a key rather than a label, because the web app already has
 * their wording in all ten languages — so reordering the header costs no translation. Writing a
 * label here overrides the app's for that language, and clearing it hands the entry back.
 *
 * Position is a number, matching the footer's links. The arrows are that same number moved by one,
 * which is the operation an operator actually wants.
 */
export function HeaderLinksPage() {
  const { t } = useTranslation();
  const content = useAdminHeaderContent();
  const save = useSaveHeaderLink();
  const remove = useDeleteHeaderLink();

  const canUpdate = adminSession.has(Permissions.LandingContentUpdate);
  const canCreate = adminSession.has(Permissions.LandingContentCreate);
  const canDelete = adminSession.has(Permissions.LandingContentDelete);

  const [lang, setLang] = useEditingLanguage();
  const [notice, setNotice] = useState<string | null>(null);
  const [editing, setEditing] = useState<HeaderLinkDto | null>(null);
  const [isCreating, setIsCreating] = useState(false);
  const [form, setForm] = useState<UpsertHeaderLinkBody>(EMPTY);
  const [toDelete, setToDelete] = useState<HeaderLinkDto | null>(null);

  const links = useMemo(
    () => [...(content.data?.links ?? [])].sort((a, b) => a.sortOrder - b.sortOrder),
    [content.data],
  );

  useEffect(() => {
    if (editing) {
      setForm({
        id: editing.id,
        key: editing.key,
        labels: editing.labels ?? {},
        url: editing.url,
        visibility: editing.visibility,
        sortOrder: editing.sortOrder,
        isActive: editing.isActive,
      });
    } else if (isCreating) {
      setForm({ ...EMPTY, sortOrder: links.length });
    }
  }, [editing, isCreating, links.length]);

  if (content.isPending) {
    return <LoadingState label={t('common.loading')} />;
  }

  if (content.isError || !content.data) {
    return <Alert variant="error">{t('errors.genericTitle')}</Alert>;
  }

  /** What the header shows for a row: the override being edited, else the app's own name. */
  function nameOf(link: HeaderLinkDto): string {
    const override = link.labels?.[lang] || link.labels?.[DEFAULT_LANG];
    if (override) return override;

    return link.key ? t(`headerContent.key.${link.key}`, link.key) : t('headerContent.custom');
  }

  function closeDialog() {
    setEditing(null);
    setIsCreating(false);
    save.reset();
  }

  function submit() {
    save.mutate(form, {
      onSuccess: () => {
        closeDialog();
        setNotice(t('headerContent.saved'));
      },
    });
  }

  /** Swaps a row with its neighbour by writing both positions. */
  function move(index: number, direction: -1 | 1) {
    const link = links[index];
    const neighbour = links[index + direction];
    if (!link || !neighbour) return;

    const body = (row: HeaderLinkDto, sortOrder: number): UpsertHeaderLinkBody => ({
      id: row.id,
      key: row.key,
      labels: row.labels ?? {},
      url: row.url,
      visibility: row.visibility,
      sortOrder,
      isActive: row.isActive,
    });

    save.mutate(body(link, neighbour.sortOrder), {
      onSuccess: () =>
        save.mutate(body(neighbour, link.sortOrder), {
          onSuccess: () => setNotice(t('headerContent.saved')),
        }),
    });
  }

  const isDialogOpen = isCreating || editing !== null;

  return (
    <LandingPageShell
      title={t('headerContent.pageTitle')}
      subtitle={t('headerContent.pageHint')}
      lang={lang}
      onLangChange={setLang}
      filled={(code) => links.some((link) => Boolean(link.labels?.[code]?.trim()))}
    >
      <Notice message={notice} onDismiss={() => setNotice(null)} />

      <Alert variant="info">{t('headerContent.labelHint')}</Alert>

      <section className="space-y-4">
        <div className="flex items-center justify-between gap-3">
          <div>
            <h2 className="font-display text-base font-semibold text-ink-950">
              {t('headerContent.linksTitle')}
            </h2>
            <p className="mt-1 text-sm text-ink-500">{t('headerContent.linksHint')}</p>
          </div>
          {canCreate && (
            <Button onClick={() => setIsCreating(true)} data-testid="new-header-link">
              <Plus className="size-4" />
              {t('headerContent.newLink')}
            </Button>
          )}
        </div>

        {links.length === 0 ? (
          <Alert variant="info">{t('headerContent.empty')}</Alert>
        ) : (
          <ul className="space-y-2" data-testid="header-link-list">
            {links.map((link, index) => (
              <li
                key={link.id}
                className="flex items-center gap-3 rounded-2xl bg-white p-4 shadow-soft ring-1 ring-ink-100"
                data-testid={`header-link-${link.key ?? link.id}`}
              >
                <span className="w-6 shrink-0 text-center text-xs font-bold text-ink-300">
                  {index + 1}
                </span>

                <div className="min-w-0 flex-1">
                  <p className="truncate font-medium text-ink-950" dir="auto">
                    {nameOf(link)}
                  </p>
                  <div className="mt-1 flex flex-wrap items-center gap-1.5">
                    <span className="inline-block rounded-md bg-ink-100 px-2 py-0.5 text-[10px] font-medium text-ink-500">
                      {link.url}
                    </span>
                    <span
                      className={cn(
                        'inline-block rounded-md px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide',
                        link.isActive ? 'bg-brand-50 text-brand-700' : 'bg-ink-100 text-ink-400',
                      )}
                    >
                      {link.isActive ? t('lookups.active') : t('lookups.inactive')}
                    </span>
                    {link.visibility !== 'Everyone' && (
                      <span className="inline-block rounded-md bg-ink-100 px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide text-ink-500">
                        {t(`footerContent.visibility.${link.visibility}`)}
                      </span>
                    )}
                  </div>
                </div>

                {canUpdate && (
                  <div className="flex shrink-0 items-center gap-1">
                    <button
                      type="button"
                      onClick={() => move(index, -1)}
                      disabled={index === 0 || save.isPending}
                      className="flex size-8 items-center justify-center rounded-lg text-ink-400 hover:bg-ink-100 hover:text-ink-800 disabled:opacity-30"
                      aria-label={t('headerContent.moveUp')}
                      data-testid={`move-up-${link.key ?? link.id}`}
                    >
                      <ArrowUp className="size-4" />
                    </button>
                    <button
                      type="button"
                      onClick={() => move(index, 1)}
                      disabled={index === links.length - 1 || save.isPending}
                      className="flex size-8 items-center justify-center rounded-lg text-ink-400 hover:bg-ink-100 hover:text-ink-800 disabled:opacity-30"
                      aria-label={t('headerContent.moveDown')}
                      data-testid={`move-down-${link.key ?? link.id}`}
                    >
                      <ArrowDown className="size-4" />
                    </button>
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => setEditing(link)}
                      data-testid={`edit-header-${link.key ?? link.id}`}
                    >
                      {t('common.edit')}
                    </Button>
                  </div>
                )}

                {canDelete && (
                  <button
                    type="button"
                    onClick={() => setToDelete(link)}
                    className="flex size-8 shrink-0 items-center justify-center rounded-lg text-ink-400 hover:bg-red-50 hover:text-red-600"
                    aria-label={t('common.delete')}
                    data-testid={`delete-header-${link.key ?? link.id}`}
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
        title={editing ? t('headerContent.editLink') : t('headerContent.newLink')}
        onClose={closeDialog}
        onSubmit={submit}
        isPending={save.isPending}
        // A row of the operator's own has nothing to fall back to, so it needs English wording.
        canSubmit={Boolean(form.url.trim()) && (form.key !== null || Boolean(form.labels?.en?.trim()))}
        error={save.error}
      >
        <div className="space-y-4">
          <LanguageTabs
            value={lang}
            onChange={setLang}
            filled={(code) => Boolean(form.labels?.[code]?.trim())}
          />

          <Field label={t('headerContent.label')} htmlFor="header-label">
            <Input
              id="header-label"
              value={form.labels?.[lang] ?? ''}
              dir="auto"
              maxLength={120}
              placeholder={form.key ? t(`headerContent.key.${form.key}`, form.key) : undefined}
              onChange={(event) =>
                setForm({ ...form, labels: withLang(form.labels ?? {}, lang, event.target.value) })
              }
              data-testid="header-label"
            />
          </Field>

          <Field label={t('headerContent.url')} htmlFor="header-url" required>
            <Input
              id="header-url"
              value={form.url}
              dir="ltr"
              maxLength={500}
              onChange={(event) => setForm({ ...form, url: event.target.value })}
              data-testid="header-url"
            />
          </Field>

          <p className="text-xs text-subtle">{t('headerContent.urlHint')}</p>

          <div className="grid gap-4 sm:grid-cols-3">
            <Field label={t('headerContent.visibility')} htmlFor="header-visibility">
              <Select
                id="header-visibility"
                value={form.visibility}
                onChange={(event) =>
                  setForm({ ...form, visibility: event.target.value as HeaderLinkVisibility })
                }
                data-testid="header-visibility"
              >
                {HEADER_VISIBILITIES.map((visibility) => (
                  <option key={visibility} value={visibility}>
                    {t(`footerContent.visibility.${visibility}`)}
                  </option>
                ))}
              </Select>
            </Field>

            <Field label={t('headerContent.sortOrder')} htmlFor="header-sort">
              <Input
                id="header-sort"
                type="number"
                min={0}
                value={form.sortOrder}
                onChange={(event) =>
                  setForm({ ...form, sortOrder: Number(event.target.value) || 0 })
                }
                data-testid="header-sort"
              />
            </Field>

            <Field label={t('lookups.status')} htmlFor="header-active">
              <Select
                id="header-active"
                value={form.isActive ? '1' : '0'}
                onChange={(event) => setForm({ ...form, isActive: event.target.value === '1' })}
                data-testid="header-active"
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
        title={t('headerContent.deleteTitle')}
        body={t('headerContent.deleteBody')}
        confirmLabel={t('common.delete')}
        onClose={() => setToDelete(null)}
        onConfirm={() => {
          if (!toDelete) return;
          remove.mutate(toDelete.id, {
            onSuccess: () => {
              setToDelete(null);
              setNotice(t('headerContent.deleted'));
            },
          });
        }}
        isPending={remove.isPending}
        destructive
      />
    </LandingPageShell>
  );
}
