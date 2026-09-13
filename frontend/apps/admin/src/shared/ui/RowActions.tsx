import { cn } from '@dv/ui';
import { Eye, Pencil, Trash2 } from 'lucide-react';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';

const iconButtonClass =
  'inline-flex size-8 items-center justify-center rounded-lg text-ink-400 transition-colors hover:bg-ink-50 hover:text-ink-800 disabled:pointer-events-none disabled:opacity-40';

interface IconActionProps {
  label: string;
  onClick?: () => void;
  to?: string;
  destructive?: boolean;
  testId?: string;
  children: ReactNode;
}

function IconAction({ label, onClick, to, destructive, testId, children }: IconActionProps) {
  const className = cn(iconButtonClass, destructive && 'hover:bg-red-50 hover:text-red-600');

  if (to) {
    return (
      <Link to={to} aria-label={label} title={label} data-testid={testId} className={className}>
        {children}
      </Link>
    );
  }

  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      data-testid={testId}
      onClick={onClick}
      className={className}
    >
      {children}
    </button>
  );
}

/** Compact icon-only edit / delete / view controls used in every admin list. */
export function RowActions({
  onEdit,
  onDelete,
  viewTo,
  onView,
  editTestId,
  deleteTestId,
  viewTestId,
  className,
}: {
  onEdit?: () => void;
  onDelete?: () => void;
  viewTo?: string;
  onView?: () => void;
  editTestId?: string;
  deleteTestId?: string;
  viewTestId?: string;
  className?: string;
}) {
  const { t } = useTranslation();

  if (!onEdit && !onDelete && !viewTo && !onView) return null;

  return (
    <div className={cn('flex justify-end gap-0.5', className)}>
      {(viewTo || onView) && (
        <IconAction
          label={t('common.view')}
          to={viewTo}
          onClick={onView}
          testId={viewTestId}
        >
          <Eye className="size-4" aria-hidden />
        </IconAction>
      )}
      {onEdit && (
        <IconAction label={t('common.edit')} onClick={onEdit} testId={editTestId}>
          <Pencil className="size-4" aria-hidden />
        </IconAction>
      )}
      {onDelete && (
        <IconAction
          label={t('common.delete')}
          onClick={onDelete}
          destructive
          testId={deleteTestId}
        >
          <Trash2 className="size-4" aria-hidden />
        </IconAction>
      )}
    </div>
  );
}
