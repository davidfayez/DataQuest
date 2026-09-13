import { Alert, Button, Field, Input, LoadingState, Select, cn } from '@dv/ui';
import { Pencil, Plus, Trash2 } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { adminSession, Permissions } from '@/features/auth/session';
import {
  FOOTER_COLUMNS,
  useAdminFooterContent,
  useDeleteFooterLink,
  useSaveFooterLink,
  useUpdateFooterHeadings,
  type FooterColumn,
  type FooterLinkDto,
  type FooterLinkVisibility,
  type UpsertFooterLinkBody,
} from '@/features/content/footer';
import type { Translations } from '@/features/content/api';
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

const EMPTY: UpsertFooterLinkBody = {
  column: 'Explore',
  visibility: 'Everyone',
  labels: {},
  url: '',
  sortOrder: 0,
  isActive: true,
};

/**
 * The three link columns in the site footer, and their headings.
 *
 * Each row carries who it is shown to. The Account column is the reason: it shows Register and
 * Sign in to a stranger and their own pages to somebody signed in, and making it editable without
 * that would have shown everyone both halves.
 */
export function FooterLinksPage() {
  const { t } = useTranslation();
  const content = useAdminFooterContent();

  const canUpdate = adminSession.has(Permissions.LandingContentUpdate);
  const canCreate = adminSession.has(Permissions.LandingContentCreate);
  const canDelete = adminSession.has(Permissions.LandingContentDelete);

  const save = useSaveFooterLink();
  const saveHeadings = useUpdateFooterHeadings();
  const remove = useDeleteFooterLink();

  const [lang, setLang] = useEditingLanguage();
  const [notice, setNotice] = useState<string | null>(null);
  const [explore, setExplore] = useState<Translations>({});
  const [account, setAccount] = useState<Translations>({});
  const [organisation, setOrganisation] = useState<Translations>({});
  const [editing, setEditing] = useState<FooterLinkDto | null>(null);
  const [isCreating, setIsCreating] = useState(false);
  const [form, setForm] = useState<UpsertFooterLinkBody>(EMPTY);
  const [toDelete, setToDelete] = useState<FooterLinkDto | null>(null);

  const linkCount = content.data?.links.length ?? 0;

  useEffect(() => {
    if (content.data) {
      setExplore(content.data.exploreHeading ?? {});
      setAccount(content.data.accountHeading ?? {});
      setOrganisation(content.data.organisationHeading ?? {});
    }
  }, [content.data]);

  useEffect(() => {
    if (editing) {
      setForm({
        id: editing.id,
        column: editing.column,
        visibility: editing.visibility,
        labels: { ...editing.labels },
        url: editing.url,
        sortOrder: editing.sortOrder,
        isActive: editing.isActive,
      });
    } else if (isCreating) {
      setForm({ ...EMPTY, sortOrder: linkCount });
    }
  }, [editing, isCreating, linkCount]);

  if (content.isPending) {
    return <LoadingState label={t('common.loading')} />;
  }

  if (content.isError || !content.data) {
    return <Alert variant="error">{t('errors.genericTitle')}</Alert>;
  }

  const links = content.data.links;
  const isDialogOpen = isCreating || editing !== null;
  const complete = Boolean(form.labels[DEFAULT_LANG]?.trim() && form.url.trim());

  function closeDialog() {
    setEditing(null);
    setIsCreating(false);
    save.reset();
  }

  function submit() {
    save.mutate(
      { ...form, url: form.url.trim() },
      {
        onSuccess: () => {
          closeDialog();
          setNotice(t('footerContent.linkSaved'));
        },
      },
    );
  }

  function confirmDelete() {
    if (!toDelete) return;

    remove.mutate(toDelete.id, {
      onSuccess: () => {
        setToDelete(null);
        setNotice(t('footerContent.linkDeleted'));
      },
    });
  }

  return (
    <LandingPageShell
      title={t('footerContent.linksTitle')}
      subtitle={t('footerContent.linksHint')}
      lang={lang}
      onLangChange={setLang}
      filled={(code) =>
        Boolean(explore[code]?.trim() || account[code]?.trim() || organisation[code]?.trim())
        || links.some((link) => link.labels[code]?.trim())
      }
    >
      <Notice message={notice} onDismiss={() => setNotice(null)} />

      <SectionHeadingPanel
        fields={[
          {
            id: 'explore-heading',
            label: t('footerContent.exploreHeading'),
            value: explore[lang] ?? '',
            onChange: (value) => setExplore(withLang(explore, lang, value)),
          },
          {
            id: 'account-heading',
            label: t('footerContent.accountHeading'),
            value: account[lang] ?? '',
            onChange: (value) => setAccount(withLang(account, lang, value)),
          },
          {
            id: 'organisation-heading',
            label: t('footerContent.organisationHeading'),
            value: organisation[lang] ?? '',
            onChange: (value) => setOrganisation(withLang(organisation, lang, value)),
          },
        ]}
        canUpdate={canUpdate}
        complete={Boolean(
          explore[DEFAULT_LANG]?.trim()
          && account[DEFAULT_LANG]?.trim()
          && organisation[DEFAULT_LANG]?.trim(),
        )}
        isPending={saveHeadings.isPending}
        onSave={() =>
          saveHeadings.mutate(
            {
              exploreHeading: explore,
              accountHeading: account,
              organisationHeading: organisation,
            },
            { onSuccess: () => setNotice(t('landingContent.headingSaved')) },
          )
        }
      />

      {canCreate && (
        <div className="flex justify-end">
          <Button onClick={() => setIsCreating(true)} data-testid="new-link">
            <Plus className="size-4" />
            {t('footerContent.newLink')}
          </Button>
        </div>
      )}

      {/* One list per column, so the page reads the way the footer does. */}
      {FOOTER_COLUMNS.map((column) => {
        const rows = links
          .filter((link) => link.column === column)
          .sort((a, b) => a.sortOrder - b.sortOrder);

        return (
          <section key={column} className="space-y-3">
            <h2 className="font-display text-base font-semibold text-ink-950">
              {t(`footerContent.column.${column}`)}
            </h2>

            {rows.length === 0 ? (
              <Alert variant="info">{t('footerContent.columnEmpty')}</Alert>
            ) : (
              <ul className="grid gap-3 sm:grid-cols-2" data-testid={`links-${column}`}>
                {rows.map((link) => (
                  <li
                    key={link.id}
                    className="flex items-start gap-4 rounded-2xl bg-white p-4 shadow-soft ring-1 ring-ink-100"
                  >
                    <div className="min-w-0 flex-1">
                      <div className="flex flex-wrap items-center gap-2">
                        <span
                          className={cn(
                            'rounded-md px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide',
                            link.isActive ? 'bg-brand-50 text-brand-700' : 'bg-ink-100 text-ink-400',
                          )}
                        >
                          {link.isActive ? t('lookups.active') : t('lookups.inactive')}
                        </span>

                        {/* Worth saying on the row: a link only half the visitors ever see is
                            otherwise indistinguishable from one that is simply missing. */}
                        {link.visibility !== 'Everyone' && (
                          <span className="rounded-md bg-ink-100 px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide text-ink-500">
                            {t(`footerContent.visibility.${link.visibility}`)}
                          </span>
                        )}
                      </div>

                      <p className="mt-2 truncate font-medium text-ink-950" dir="auto">
                        {pick(link.labels, lang)}
                      </p>
                      <p className="truncate text-xs text-subtle" dir="ltr">
                        {link.url}
                      </p>
                    </div>

                    <div className="flex shrink-0 flex-col gap-1">
                      {canUpdate && (
                        <button
                          type="button"
                          onClick={() => setEditing(link)}
                          className="flex size-8 items-center justify-center rounded-lg text-ink-400 hover:bg-ink-50 hover:text-ink-700"
                          aria-label={t('common.edit')}
                          data-testid={`edit-link-${link.id}`}
                        >
                          <Pencil className="size-4" />
                        </button>
                      )}
                      {canDelete && (
                        <button
                          type="button"
                          onClick={() => setToDelete(link)}
                          className="flex size-8 items-center justify-center rounded-lg text-ink-400 hover:bg-red-50 hover:text-red-600"
                          aria-label={t('common.delete')}
                          data-testid={`delete-link-${link.id}`}
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
        );
      })}

      <CrudDialog
        open={isDialogOpen}
        title={editing ? t('footerContent.editLink') : t('footerContent.newLink')}
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
            filled={(code) => Boolean(form.labels[code]?.trim())}
          />

          <div className="grid gap-4 sm:grid-cols-2">
            <Field label={t('footerContent.columnLabel')} htmlFor="link-column" required>
              <Select
                id="link-column"
                value={form.column}
                onChange={(event) =>
                  setForm({ ...form, column: event.target.value as FooterColumn })
                }
                data-testid="link-column"
              >
                {FOOTER_COLUMNS.map((column) => (
                  <option key={column} value={column}>
                    {t(`footerContent.column.${column}`)}
                  </option>
                ))}
              </Select>
            </Field>

            <Field label={t('footerContent.visibilityLabel')} htmlFor="link-visibility">
              <Select
                id="link-visibility"
                value={form.visibility}
                onChange={(event) =>
                  setForm({ ...form, visibility: event.target.value as FooterLinkVisibility })
                }
                data-testid="link-visibility"
              >
                <option value="Everyone">{t('footerContent.visibility.Everyone')}</option>
                <option value="SignedOut">{t('footerContent.visibility.SignedOut')}</option>
                <option value="SignedIn">{t('footerContent.visibility.SignedIn')}</option>
              </Select>
            </Field>
          </div>

          <Field label={t('footerContent.linkLabel')} htmlFor="link-label" required>
            <Input
              id="link-label"
              dir="auto"
              maxLength={120}
              value={form.labels[lang] ?? ''}
              onChange={(event) =>
                setForm({ ...form, labels: withLang(form.labels, lang, event.target.value) })
              }
              data-testid="link-label"
            />
          </Field>

          <Field label={t('footerContent.linkUrl')} htmlFor="link-url" required>
            <Input
              id="link-url"
              dir="ltr"
              maxLength={500}
              placeholder="/tools"
              value={form.url}
              onChange={(event) => setForm({ ...form, url: event.target.value })}
              data-testid="link-url"
            />
          </Field>

          <p className="text-xs text-subtle">{t('footerContent.linkUrlHint')}</p>

          {!complete && <Alert variant="info">{t('footerContent.linkIncomplete')}</Alert>}

          <div className="grid gap-4 sm:grid-cols-2">
            <Field label={t('lookups.sortOrder')} htmlFor="link-sort">
              <Input
                id="link-sort"
                type="number"
                min={0}
                value={form.sortOrder}
                onChange={(event) =>
                  setForm({ ...form, sortOrder: Number(event.target.value) || 0 })
                }
              />
            </Field>

            <Field label={t('lookups.status')} htmlFor="link-active">
              <Select
                id="link-active"
                value={form.isActive ? '1' : '0'}
                onChange={(event) => setForm({ ...form, isActive: event.target.value === '1' })}
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
        title={t('footerContent.deleteLinkTitle')}
        body={t('footerContent.deleteLinkBody')}
        confirmLabel={t('common.delete')}
        onClose={() => setToDelete(null)}
        onConfirm={confirmDelete}
        isPending={remove.isPending}
        destructive
      />
    </LandingPageShell>
  );
}
