import { SUPPORTED_LANGUAGES } from '@dv/i18n';
import {
  AdminPageHeader,
  Alert,
  Button,
  Field,
  Input,
  LoadingState,
  Select,
  Spinner,
  cn,
} from '@dv/ui';
import { Image as ImageIcon, Pencil, Plus, Trash2, Upload, Video } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { adminSession, Permissions } from '@/features/auth/session';
import {
  useAdminTools,
  useDeleteTool,
  useSaveTool,
  useUploadToolImage,
  type AdminToolDto,
  type ToolKind,
  type Translations,
  type UpsertToolBody,
} from '@/features/tools/api';
import { ConfirmDialog, CrudDialog } from '@/shared/ui/CrudDialog';
import { Notice } from '@/pages/lookups/tabs/shared';

const DEFAULT_LANG = 'en';

const EMPTY: UpsertToolBody = {
  kind: 'Video',
  names: {},
  descriptions: {},
  videoUrl: '',
  sortOrder: 0,
  isPublished: true,
};

function withLang(map: Translations, lang: string, value: string): Translations {
  return { ...map, [lang]: value };
}

/** Requested language, then English, then whatever exists — matches the server's fallback. */
function pick(map: Translations | undefined, lang: string): string {
  if (!map) return '';
  return map[lang] || map[DEFAULT_LANG] || Object.values(map).find(Boolean) || '';
}

/** A wrapping row of language chips; a dot marks languages that already have content. */
function LanguageTabs({
  value,
  onChange,
  filled,
}: {
  value: string;
  onChange: (lang: string) => void;
  filled: (lang: string) => boolean;
}) {
  return (
    <div className="flex flex-wrap gap-1.5" role="tablist" aria-label="Content language">
      {SUPPORTED_LANGUAGES.map((language) => {
        const active = value === language.code;
        return (
          <button
            key={language.code}
            type="button"
            role="tab"
            aria-selected={active}
            onClick={() => onChange(language.code)}
            className={cn(
              'inline-flex items-center gap-1.5 rounded-lg px-2.5 py-1 text-xs font-medium transition-colors',
              active ? 'bg-ink-950 text-white' : 'bg-ink-100 text-ink-600 hover:bg-ink-200',
            )}
          >
            <span>{language.name}</span>
            {language.code === DEFAULT_LANG && <span title="Required">*</span>}
            {!active && filled(language.code) && (
              <span className="size-1.5 rounded-full bg-brand-500" aria-hidden />
            )}
          </button>
        );
      })}
    </div>
  );
}

export function ToolsPage() {
  const { t } = useTranslation();
  const tools = useAdminTools();

  const canCreate = adminSession.has(Permissions.ToolsCreate);
  const canUpdate = adminSession.has(Permissions.ToolsUpdate);
  const canDelete = adminSession.has(Permissions.ToolsDelete);

  const save = useSaveTool();
  const remove = useDeleteTool();
  const uploadImage = useUploadToolImage();

  const [editing, setEditing] = useState<AdminToolDto | null>(null);
  const [isCreating, setIsCreating] = useState(false);
  const [form, setForm] = useState<UpsertToolBody>(EMPTY);
  const [lang, setLang] = useState(DEFAULT_LANG);
  const [toDelete, setToDelete] = useState<AdminToolDto | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [pendingImage, setPendingImage] = useState<File | null>(null);
  const fileInput = useRef<HTMLInputElement>(null);

  const isDialogOpen = isCreating || editing !== null;

  useEffect(() => {
    setForm(
      editing
        ? {
            id: editing.id,
            kind: editing.kind,
            names: { ...editing.names },
            descriptions: { ...editing.descriptions },
            videoUrl: editing.videoUrl ?? '',
            sortOrder: editing.sortOrder,
            isPublished: editing.isPublished,
          }
        : EMPTY,
    );
    setPendingImage(null);
    setLang(DEFAULT_LANG);
  }, [editing, isCreating]);

  function closeDialog() {
    setEditing(null);
    setIsCreating(false);
    save.reset();
    uploadImage.reset();
  }

  // Save the entry first: a new image entry needs an id before its picture can be attached.
  async function submit() {
    const saved = await save.mutateAsync({
      ...form,
      videoUrl: form.kind === 'Video' ? (form.videoUrl ?? '') : null,
    });

    if (form.kind === 'Image' && pendingImage) {
      await uploadImage.mutateAsync({ id: saved.id, file: pendingImage });
    }

    closeDialog();
    setNotice(t('tools.saved'));
  }

  if (tools.isPending) return <LoadingState label={t('common.loading')} />;
  if (tools.isError || !tools.data) return <Alert variant="error">{t('errors.genericTitle')}</Alert>;

  const entries = tools.data;

  return (
    <div className="animate-fade-in space-y-6">
      <AdminPageHeader title={t('tools.title')} subtitle={t('tools.subtitle')} />

      <Notice message={notice} onDismiss={() => setNotice(null)} />

      <Alert variant="info">{t('tools.intro')}</Alert>

      {canCreate && (
        <Button onClick={() => setIsCreating(true)}>
          <Plus className="size-4" />
          {t('tools.create')}
        </Button>
      )}

      {entries.length === 0 ? (
        <Alert variant="info">{t('tools.empty')}</Alert>
      ) : (
        <ul className="space-y-3">
          {entries.map((entry) => (
            <li
              key={entry.id}
              className="flex items-start gap-4 rounded-xl bg-white p-4 ring-1 ring-ink-200"
            >
              <div className="mt-0.5 flex size-9 shrink-0 items-center justify-center rounded-lg bg-ink-100 text-ink-600">
                {entry.kind === 'Video' ? (
                  <Video className="size-4" />
                ) : (
                  <ImageIcon className="size-4" />
                )}
              </div>

              <div className="min-w-0 flex-1">
                <p className="truncate font-medium text-ink-950">
                  {pick(entry.names, DEFAULT_LANG) || t('tools.untitled')}
                </p>
                <p className="mt-0.5 line-clamp-2 text-sm text-ink-500">
                  {pick(entry.descriptions, DEFAULT_LANG)}
                </p>

                <div className="mt-2 flex flex-wrap items-center gap-2 text-xs">
                  <span className="rounded bg-ink-100 px-1.5 py-0.5 text-ink-600">
                    {t('tools.order')}: {entry.sortOrder}
                  </span>
                  <span
                    className={cn(
                      'rounded px-1.5 py-0.5',
                      entry.isPublished
                        ? 'bg-success-muted text-ink-700'
                        : 'bg-ink-100 text-ink-500',
                    )}
                  >
                    {entry.isPublished ? t('tools.published') : t('tools.hidden')}
                  </span>
                  {entry.kind === 'Video' ? (
                    <span dir="ltr" className="truncate font-mono text-ink-400">
                      {entry.videoUrl}
                    </span>
                  ) : entry.hasImage ? (
                    <span className="truncate text-ink-400">{entry.imageFileName}</span>
                  ) : (
                    <span className="rounded bg-warning-muted px-1.5 py-0.5 text-ink-700">
                      {t('tools.imageMissing')}
                    </span>
                  )}
                </div>
              </div>

              <div className="flex shrink-0 gap-1">
                {canUpdate && (
                  <Button
                    variant="ghost"
                    size="sm"
                    aria-label={t('common.edit')}
                    onClick={() => setEditing(entry)}
                  >
                    <Pencil className="size-4" />
                  </Button>
                )}
                {canDelete && (
                  <Button
                    variant="ghost"
                    size="sm"
                    aria-label={t('common.delete')}
                    onClick={() => setToDelete(entry)}
                  >
                    <Trash2 className="size-4" />
                  </Button>
                )}
              </div>
            </li>
          ))}
        </ul>
      )}

      <CrudDialog
        open={isDialogOpen}
        title={editing ? t('tools.editTitle') : t('tools.createTitle')}
        onClose={closeDialog}
        onSubmit={() => void submit()}
        isPending={save.isPending || uploadImage.isPending}
        error={save.error ?? uploadImage.error}
      >
        <Field label={t('tools.kind')} htmlFor="kind" required>
          <Select
            id="kind"
            value={form.kind}
            onChange={(event) => setForm({ ...form, kind: event.target.value as ToolKind })}
          >
            <option value="Video">{t('tools.kindVideo')}</option>
            <option value="Image">{t('tools.kindImage')}</option>
          </Select>
        </Field>

        {form.kind === 'Video' ? (
          <Field
            label={t('tools.videoUrl')}
            htmlFor="videoUrl"
            required
            hint={t('tools.videoUrlHint')}
          >
            <Input
              id="videoUrl"
              dir="ltr"
              placeholder="https://www.youtube.com/watch?v=..."
              value={form.videoUrl ?? ''}
              onChange={(event) => setForm({ ...form, videoUrl: event.target.value })}
            />
          </Field>
        ) : (
          <Field label={t('tools.image')} htmlFor="image" hint={t('tools.imageHint')}>
            <div className="space-y-2">
              <input
                ref={fileInput}
                id="image"
                type="file"
                accept="image/jpeg,image/png"
                className="hidden"
                onChange={(event) => setPendingImage(event.target.files?.[0] ?? null)}
              />
              <Button type="button" variant="secondary" onClick={() => fileInput.current?.click()}>
                <Upload className="size-4" />
                {t('tools.chooseImage')}
              </Button>
              <p className="text-sm text-ink-500">
                {pendingImage?.name ?? editing?.imageFileName ?? t('tools.noImageChosen')}
              </p>
            </div>
          </Field>
        )}

        <LanguageTabs
          value={lang}
          onChange={setLang}
          filled={(code) => Boolean(form.names[code] || form.descriptions[code])}
        />

        <Field label={t('tools.name')} htmlFor="name" required={lang === DEFAULT_LANG}>
          <Input
            id="name"
            value={form.names[lang] ?? ''}
            onChange={(event) =>
              setForm({ ...form, names: withLang(form.names, lang, event.target.value) })
            }
          />
        </Field>

        <Field
          label={t('tools.description')}
          htmlFor="description"
          required={lang === DEFAULT_LANG}
        >
          <textarea
            id="description"
            rows={4}
            className="w-full rounded-xl border-0 bg-white px-3.5 py-2.5 text-sm text-ink-900 ring-1 ring-ink-200 shadow-soft focus:outline-none focus:ring-2 focus:ring-brand-500"
            value={form.descriptions[lang] ?? ''}
            onChange={(event) =>
              setForm({
                ...form,
                descriptions: withLang(form.descriptions, lang, event.target.value),
              })
            }
          />
        </Field>

        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t('tools.order')} htmlFor="sortOrder">
            <Input
              id="sortOrder"
              type="number"
              min={0}
              value={form.sortOrder}
              onChange={(event) =>
                setForm({ ...form, sortOrder: Number(event.target.value) || 0 })
              }
            />
          </Field>

          <Field label={t('tools.visibility')} htmlFor="isPublished">
            <Select
              id="isPublished"
              value={form.isPublished ? 'yes' : 'no'}
              onChange={(event) => setForm({ ...form, isPublished: event.target.value === 'yes' })}
            >
              <option value="yes">{t('tools.published')}</option>
              <option value="no">{t('tools.hidden')}</option>
            </Select>
          </Field>
        </div>
      </CrudDialog>

      <ConfirmDialog
        open={toDelete !== null}
        title={t('tools.deleteTitle')}
        body={t('tools.deleteBody', { name: pick(toDelete?.names, DEFAULT_LANG) })}
        confirmLabel={t('common.delete')}
        destructive
        isPending={remove.isPending}
        onClose={() => setToDelete(null)}
        onConfirm={async () => {
          if (toDelete) await remove.mutateAsync(toDelete.id);
          setToDelete(null);
          setNotice(t('tools.deleted'));
        }}
      />

      {(save.isPending || uploadImage.isPending) && (
        <span className="sr-only">
          <Spinner />
        </span>
      )}
    </div>
  );
}
