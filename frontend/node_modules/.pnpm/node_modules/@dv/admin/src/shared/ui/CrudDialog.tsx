import { Alert, Button, Dialog, Spinner } from '@dv/ui';
import type { FormEvent, ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { useApiErrorMessage } from '../lib/useApiError';

interface Props {
  open: boolean;
  title: string;
  onClose: () => void;
  onSubmit: () => void;
  isPending: boolean;
  error: unknown;
  children: ReactNode;
  submitLabel?: string;
  /** When false, the submit button is disabled without implying a pending state. */
  canSubmit?: boolean;
  /** A roomier dialog for content like the permission grid that needs horizontal space. */
  wide?: boolean;
}

/**
 * The shared create/edit shell. Every lookup and RBAC screen uses it, so the submit position,
 * pending state and error placement are identical throughout the panel.
 */
export function CrudDialog({
  open,
  title,
  onClose,
  onSubmit,
  isPending,
  error,
  children,
  submitLabel,
  canSubmit = true,
  wide,
}: Props) {
  const { t } = useTranslation();
  const toMessage = useApiErrorMessage();

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    onSubmit();
  }

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title={title}
      className={wide ? 'w-[min(56rem,calc(100vw-2rem))]' : 'w-[min(42rem,calc(100vw-2rem))]'}
    >
      <form onSubmit={handleSubmit} noValidate className="space-y-4">
        {error ? (
          <Alert variant="error" title={t('errors.genericTitle')}>
            {toMessage(error)}
          </Alert>
        ) : null}

        {children}

        <div className="flex justify-end gap-3 pt-2">
          <Button type="button" variant="outline" onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button type="submit" disabled={isPending || !canSubmit} data-testid="dialog-submit">
            {isPending && <Spinner />}
            {submitLabel ?? t('common.save')}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}

interface ConfirmProps {
  open: boolean;
  title: string;
  body: string;
  confirmLabel: string;
  onClose: () => void;
  onConfirm: () => void;
  isPending: boolean;
  destructive?: boolean;
}

export function ConfirmDialog({
  open,
  title,
  body,
  confirmLabel,
  onClose,
  onConfirm,
  isPending,
  destructive,
}: ConfirmProps) {
  const { t } = useTranslation();

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title={title}
      footer={
        <>
          <Button variant="outline" onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button
            variant={destructive ? 'destructive' : 'primary'}
            disabled={isPending}
            onClick={onConfirm}
            data-testid="confirm-submit"
          >
            {isPending && <Spinner />}
            {confirmLabel}
          </Button>
        </>
      }
    >
      {body}
    </Dialog>
  );
}
