import { AdminPanel, Alert, Button, LoadingState, Logo } from '@dv/ui';
import { Trash2, Upload } from 'lucide-react';
import { useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { adminSession, Permissions } from '@/features/auth/session';
import { useBranding, useDeleteLogo, useUploadLogo } from '@/features/branding/api';
import { useApiErrorMessage } from '@/shared/lib/useApiError';

const MAX_BYTES = 2 * 1024 * 1024;

/**
 * The platform logo.
 *
 * Shown at the sizes it is actually drawn at rather than as one large preview: the mark appears at
 * 24px in the sidebar and 32px in the site footer, and artwork that only works large is the usual
 * way a logo ends up unreadable everywhere it matters.
 */
export function BrandingCard() {
  const { t } = useTranslation();
  const branding = useBranding();
  const upload = useUploadLogo();
  const remove = useDeleteLogo();
  const toMessage = useApiErrorMessage();

  const inputRef = useRef<HTMLInputElement>(null);
  const [localError, setLocalError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const canUpdate = adminSession.has(Permissions.SettingsUpdate);

  if (branding.isPending) {
    return <LoadingState label={t('common.loading')} />;
  }

  // The previews below use <Logo>, which reads the same uploaded source from context, so this
  // panel shows whatever the rest of the app shows rather than a second opinion.
  const hasLogo = branding.data?.hasLogo ?? false;

  function choose(file: File | undefined) {
    setLocalError(null);
    setNotice(null);
    if (!file) return;

    // Checked here as well as on the server, so an obviously wrong file is refused before it is
    // carried across the network.
    if (!/\.(jpe?g|png)$/i.test(file.name)) {
      setLocalError(t('settings.branding.wrongType'));
      return;
    }

    if (file.size > MAX_BYTES) {
      setLocalError(t('settings.branding.tooLarge'));
      return;
    }

    upload.mutate(file, { onSuccess: () => setNotice(t('settings.branding.uploaded')) });
  }

  return (
    <AdminPanel title={t('settings.branding.title')} subtitle={t('settings.branding.description')}>
      <div className="space-y-4" data-testid="branding-panel">
        <div className="flex flex-wrap items-end gap-6 rounded-xl bg-ink-50/60 p-4">
          {/* Drawn from the same component both apps use, so this is the mark as it will appear. */}
          <div className="flex flex-col items-center gap-2">
            <Logo size={48} />
            <span className="text-[10px] uppercase tracking-wide text-subtle">48px</span>
          </div>
          <div className="flex flex-col items-center gap-2">
            <Logo size={32} />
            <span className="text-[10px] uppercase tracking-wide text-subtle">32px</span>
          </div>
          <div className="flex flex-col items-center gap-2">
            <Logo size={24} />
            <span className="text-[10px] uppercase tracking-wide text-subtle">24px</span>
          </div>

          <p className="ms-auto text-sm text-muted-foreground" data-testid="branding-source">
            {hasLogo
              ? t('settings.branding.usingUploaded', { name: branding.data?.fileName ?? '' })
              : t('settings.branding.usingBundled')}
          </p>
        </div>

        <p className="text-xs text-subtle">{t('settings.branding.hint')}</p>

        {localError && <Alert variant="error">{localError}</Alert>}
        {upload.isError && <Alert variant="error">{toMessage(upload.error)}</Alert>}
        {remove.isError && <Alert variant="error">{toMessage(remove.error)}</Alert>}
        {notice && <Alert variant="success" data-testid="branding-saved">{notice}</Alert>}

        {canUpdate && (
          <div className="flex flex-wrap justify-end gap-3">
            <input
              ref={inputRef}
              type="file"
              accept=".jpg,.jpeg,.png,image/jpeg,image/png"
              className="hidden"
              data-testid="logo-input"
              onChange={(event) => {
                const file = event.target.files?.[0];
                event.target.value = '';
                choose(file);
              }}
            />

            {hasLogo && (
              <Button
                variant="outline"
                disabled={remove.isPending || upload.isPending}
                onClick={() => {
                  setLocalError(null);
                  remove.mutate(undefined, {
                    onSuccess: () => setNotice(t('settings.branding.removed')),
                  });
                }}
                data-testid="remove-logo"
              >
                <Trash2 className="size-4" aria-hidden="true" />
                {t('settings.branding.remove')}
              </Button>
            )}

            <Button
              disabled={upload.isPending || remove.isPending}
              onClick={() => inputRef.current?.click()}
              data-testid="upload-logo"
            >
              <Upload className="size-4" aria-hidden="true" />
              {hasLogo ? t('settings.branding.replace') : t('settings.branding.upload')}
            </Button>
          </div>
        )}
      </div>
    </AdminPanel>
  );
}
