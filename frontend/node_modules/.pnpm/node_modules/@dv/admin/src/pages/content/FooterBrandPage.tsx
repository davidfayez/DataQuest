import { Alert, Button, Field, Input, LoadingState, Logo, Select, cn } from '@dv/ui';
import { ImageOff, Pencil, Plus, Trash2, Upload } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import type { ChangeEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { adminSession, Permissions } from '@/features/auth/session';
import {
  footerLogoSrc,
  useAdminFooterContent,
  useDeleteFooterLogo,
  useSaveFooterLogoWithImage,
  useUpdateFooterHeadings,
  type FooterLogoDto,
  type UpsertFooterLogoBody,
} from '@/features/content/footer';
import type { Translations } from '@/features/content/api';
import { ConfirmDialog, CrudDialog } from '@/shared/ui/CrudDialog';
import { Notice } from '@/pages/lookups/tabs/shared';
import {
  DEFAULT_LANG,
  LandingPageShell,
  SectionHeadingPanel,
  pick,
  useEditingLanguage,
  withLang,
} from './landingEditing';

const MAX_BYTES = 2 * 1024 * 1024;

const EMPTY: UpsertFooterLogoBody = {
  sortOrder: 0,
  isActive: true,
};

/**
 * The block on the left of the footer: the platform mark, the line under it, and the row of
 * additional marks beside them.
 *
 * The platform logo itself is not edited here — it is the same mark the header, the admin sidebar
 * and the sign-in screens draw, so it lives once under Settings. This page shows it so the block
 * can be seen whole, and links there.
 */
export function FooterBrandPage() {
  const { t } = useTranslation();
  const content = useAdminFooterContent();

  const canUpdate = adminSession.has(Permissions.LandingContentUpdate);
  const canCreate = adminSession.has(Permissions.LandingContentCreate);
  const canDelete = adminSession.has(Permissions.LandingContentDelete);

  const saveHeadings = useUpdateFooterHeadings();
  const save = useSaveFooterLogoWithImage();
  const remove = useDeleteFooterLogo();

  const [lang, setLang] = useEditingLanguage();
  const [notice, setNotice] = useState<string | null>(null);
  const [localError, setLocalError] = useState<string | null>(null);
  const [subtitle, setSubtitle] = useState<Translations>({});
  const [editing, setEditing] = useState<FooterLogoDto | null>(null);
  const [isCreating, setIsCreating] = useState(false);
  const [form, setForm] = useState<UpsertFooterLogoBody>(EMPTY);
  const [toDelete, setToDelete] = useState<FooterLogoDto | null>(null);

  // The file chosen in the dialog, and the blob URL that previews it. The upload only happens on
  // save, so a dialog closed without saving leaves nothing behind on the server.
  const [file, setFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<string | null>(null);

  const inputRef = useRef<HTMLInputElement>(null);
  const logoCount = content.data?.logos.length ?? 0;

  useEffect(() => {
    if (content.data) setSubtitle(content.data.subtitle ?? {});
  }, [content.data]);

  useEffect(() => {
    if (editing) {
      // Alternative text and the link are no longer edited here, but a row saved before that may
      // still carry them, so they are sent back untouched rather than blanked.
      setForm({
        id: editing.id,
        alts: { ...editing.alts },
        url: editing.url,
        sortOrder: editing.sortOrder,
        isActive: editing.isActive,
      });
    } else if (isCreating) {
      setForm({ ...EMPTY, sortOrder: logoCount });
    }
  }, [editing, isCreating, logoCount]);

  // A blob URL is a live handle into the browser's memory; dropping it without revoking leaks the
  // file for as long as the tab is open.
  useEffect(() => () => { if (preview) URL.revokeObjectURL(preview); }, [preview]);

  if (content.isPending) {
    return <LoadingState label={t('common.loading')} />;
  }

  if (content.isError || !content.data) {
    return <Alert variant="error">{t('errors.genericTitle')}</Alert>;
  }

  const logos = [...content.data.logos].sort((a, b) => a.sortOrder - b.sortOrder);
  const isDialogOpen = isCreating || editing !== null;

  // A new mark is its image, so there is nothing to save without one. An existing mark can be
  // reordered or switched off without touching the file it already has.
  const complete = Boolean(file) || Boolean(editing?.hasImage);

  function closeDialog() {
    setEditing(null);
    setIsCreating(false);
    clearFile();
    setLocalError(null);
    save.reset();
  }

  function clearFile() {
    setFile(null);
    setPreview((current) => {
      if (current) URL.revokeObjectURL(current);
      return null;
    });
  }

  function receiveFile(event: ChangeEvent<HTMLInputElement>) {
    const chosen = event.target.files?.[0];
    event.target.value = '';
    setLocalError(null);
    if (!chosen) return;

    // Checked here as well as on the server, so an obviously wrong file is refused before it is
    // carried across the network.
    if (!/\.(jpe?g|png)$/i.test(chosen.name)) {
      setLocalError(t('settings.branding.wrongType'));
      return;
    }

    if (chosen.size > MAX_BYTES) {
      setLocalError(t('settings.branding.tooLarge'));
      return;
    }

    clearFile();
    setFile(chosen);
    setPreview(URL.createObjectURL(chosen));
  }

  async function submit() {
    try {
      await save.mutateAsync({ body: form, file });
      closeDialog();
      setNotice(t('footerContent.logoSaved'));
    } catch {
      // Reported by the dialog's own error line; the mark is saved or it is not, never half.
    }
  }

  return (
    <LandingPageShell
      title={t('footerContent.brandTitle')}
      subtitle={t('footerContent.brandHint')}
      lang={lang}
      onLangChange={setLang}
      // The subtitle is the only per-language copy left on this page; a mark carries none.
      filled={(code) => Boolean(subtitle[code]?.trim())}
    >
      <Notice message={notice} onDismiss={() => setNotice(null)} />

      {/* The platform mark, shown but not edited here — it is the same one every realm draws. */}
      <section className="flex flex-wrap items-center gap-4 rounded-2xl bg-white p-6 shadow-soft ring-1 ring-ink-100">
        <Logo size={40} />
        <div className="min-w-0 flex-1">
          <p className="font-medium text-ink-950">{t('footerContent.platformLogo')}</p>
          <p className="mt-0.5 text-sm text-ink-500">{t('footerContent.platformLogoHint')}</p>
        </div>
        <Button variant="outline" onClick={() => window.location.assign('/settings')}>
          {t('footerContent.openSettings')}
        </Button>
      </section>

      <SectionHeadingPanel
        fields={[
          {
            id: 'footer-subtitle',
            label: t('footerContent.subtitle'),
            value: subtitle[lang] ?? '',
            onChange: (value) => setSubtitle(withLang(subtitle, lang, value)),
          },
        ]}
        canUpdate={canUpdate}
        complete={Boolean(subtitle[DEFAULT_LANG]?.trim())}
        isPending={saveHeadings.isPending}
        onSave={() =>
          saveHeadings.mutate(
            { subtitle },
            { onSuccess: () => setNotice(t('landingContent.headingSaved')) },
          )
        }
      />

      <section className="space-y-4">
        <div className="flex items-center justify-between gap-3">
          <div>
            <h2 className="font-display text-base font-semibold text-ink-950">
              {t('footerContent.logosTitle')}
            </h2>
            <p className="mt-1 text-sm text-ink-500">{t('footerContent.logosHint')}</p>
          </div>
          {canCreate && (
            <Button onClick={() => setIsCreating(true)} data-testid="new-logo">
              <Plus className="size-4" />
              {t('footerContent.newLogo')}
            </Button>
          )}
        </div>

        {logos.length === 0 ? (
          <Alert variant="info">{t('footerContent.logosEmpty')}</Alert>
        ) : (
          <ul className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3" data-testid="logo-list">
            {logos.map((logo) => (
              <li
                key={logo.id}
                className="flex items-start gap-4 rounded-2xl bg-white p-4 shadow-soft ring-1 ring-ink-100"
              >
                {/* The mark on the dark ground the footer actually uses, so an image with a
                    transparent background is judged where it will live rather than on white. */}
                <div className="flex size-16 shrink-0 items-center justify-center rounded-xl bg-ink-950 p-2">
                  {logo.hasImage ? (
                    <img
                      src={footerLogoSrc(logo.id)}
                      alt={pick(logo.alts, lang)}
                      className="max-h-full max-w-full object-contain"
                    />
                  ) : (
                    <ImageOff className="size-5 text-ink-500" aria-hidden />
                  )}
                </div>

                <div className="min-w-0 flex-1">
                  <span
                    className={cn(
                      'rounded-md px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide',
                      logo.isShowable
                        ? 'bg-brand-50 text-brand-700'
                        : logo.isActive
                          ? 'bg-amber-50 text-amber-700'
                          : 'bg-ink-100 text-ink-400',
                    )}
                  >
                    {logo.isShowable
                      ? t('lookups.active')
                      : logo.isActive
                        ? t('footerContent.needsImage')
                        : t('lookups.inactive')}
                  </span>

                  {/* The file name identifies the row now that there is no description to show. */}
                  <p className="mt-2 truncate font-medium text-ink-950" dir="auto">
                    {logo.fileName || pick(logo.alts, lang) || t('footerContent.noImageYet')}
                  </p>
                </div>

                <div className="flex shrink-0 flex-col gap-1">
                  {canUpdate && (
                    <button
                      type="button"
                      onClick={() => setEditing(logo)}
                      className="flex size-8 items-center justify-center rounded-lg text-ink-400 hover:bg-ink-50 hover:text-ink-700"
                      aria-label={t('common.edit')}
                      data-testid={`edit-logo-${logo.id}`}
                    >
                      <Pencil className="size-4" />
                    </button>
                  )}
                  {canDelete && (
                    <button
                      type="button"
                      onClick={() => setToDelete(logo)}
                      className="flex size-8 items-center justify-center rounded-lg text-ink-400 hover:bg-red-50 hover:text-red-600"
                      aria-label={t('common.delete')}
                      data-testid={`delete-logo-${logo.id}`}
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
        title={editing ? t('footerContent.editLogo') : t('footerContent.newLogo')}
        onClose={closeDialog}
        onSubmit={submit}
        isPending={save.isPending}
        canSubmit={complete}
        error={save.error}
      >
        <div className="space-y-4">
          <input
            ref={inputRef}
            type="file"
            accept=".jpg,.jpeg,.png,image/jpeg,image/png"
            className="hidden"
            data-testid="logo-file"
            onChange={receiveFile}
          />

          {/* The mark is previewed on the dark ground the footer actually uses, so a transparent
              background is judged where it will live rather than on white. */}
          <div className="flex items-center gap-4 rounded-2xl bg-ink-950 p-4">
            <div className="flex size-24 shrink-0 items-center justify-center rounded-xl bg-white/5 p-3">
              {preview ? (
                <img
                  src={preview}
                  alt=""
                  className="max-h-full max-w-full object-contain"
                  data-testid="logo-preview"
                />
              ) : editing?.hasImage ? (
                <img
                  src={footerLogoSrc(editing.id)}
                  alt=""
                  className="max-h-full max-w-full object-contain"
                  data-testid="logo-preview"
                />
              ) : (
                <ImageOff className="size-6 text-ink-500" aria-hidden />
              )}
            </div>

            <div className="min-w-0 flex-1">
              <p className="truncate text-sm font-medium text-white" dir="auto">
                {file?.name ?? editing?.fileName ?? t('footerContent.noImageYet')}
              </p>
              <p className="mt-0.5 text-xs text-ink-400">{t('footerContent.imageRules')}</p>
              {/* Explicitly not a submit button. This sits inside the dialog's form, where a
                  button with no type is a submit button, and clicking it saved the mark before a
                  file had even been chosen. */}
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="mt-3"
                onClick={() => inputRef.current?.click()}
                data-testid="choose-logo"
              >
                <Upload className="size-4" aria-hidden="true" />
                {file || editing?.hasImage
                  ? t('footerContent.replaceImage')
                  : t('footerContent.addImage')}
              </Button>
            </div>
          </div>

          {localError && <Alert variant="error">{localError}</Alert>}

          <div className="grid gap-4 sm:grid-cols-2">
            <Field label={t('lookups.sortOrder')} htmlFor="logo-sort">
              <Input
                id="logo-sort"
                type="number"
                min={0}
                value={form.sortOrder}
                onChange={(event) =>
                  setForm({ ...form, sortOrder: Number(event.target.value) || 0 })
                }
              />
            </Field>

            <Field label={t('lookups.status')} htmlFor="logo-active">
              <Select
                id="logo-active"
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
        title={t('footerContent.deleteLogoTitle')}
        body={t('footerContent.deleteLogoBody')}
        confirmLabel={t('common.delete')}
        onClose={() => setToDelete(null)}
        onConfirm={() => {
          if (!toDelete) return;
          remove.mutate(toDelete.id, {
            onSuccess: () => {
              setToDelete(null);
              setNotice(t('footerContent.logoDeleted'));
            },
          });
        }}
        isPending={remove.isPending}
        destructive
      />
    </LandingPageShell>
  );
}
