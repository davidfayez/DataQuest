import { cn } from '@dv/ui';
import { FileCheck2, FileUp } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { statusLabelKey } from '@/entities/application/statusLabels';
import {
  ActorType,
  TimelineEntryKind,
  type TimelineEntryDto,
} from '@/entities/application/timelineApi';
import type { ApplicationStatus } from '@/entities/application/types';
import { formatDateTime } from '@/shared/lib/format';

/**
 * The merged activity feed, rendered as a two-sided chat.
 *
 * The applicant's own messages sit on the trailing edge and the reviewer's on the leading edge,
 * expressed with `self-start`/`self-end` and logical padding rather than left/right, so the whole
 * conversation mirrors correctly under `dir="rtl"` without a second set of styles.
 */
export function ActivityTimeline({ entries }: { entries: TimelineEntryDto[] }) {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage ?? 'en';

  if (entries.length === 0) {
    return <p className="py-10 text-center text-sm text-muted-foreground">{t('timeline.empty')}</p>;
  }

  return (
    <ol className="flex flex-col gap-4">
      {entries.map((entry) => {
        if (entry.kind === TimelineEntryKind.Comment) {
          const isMine = entry.authorType === ActorType.Applicant;

          return (
            <li
              key={entry.id}
              className={cn('flex max-w-[85%] flex-col gap-1', isMine ? 'self-end' : 'self-start')}
              data-testid={isMine ? 'comment-mine' : 'comment-theirs'}
            >
              <div
                className={cn(
                  'rounded-2xl px-4 py-2.5 text-sm',
                  isMine
                    ? 'rounded-ee-sm bg-primary text-primary-foreground'
                    : 'rounded-es-sm bg-muted text-foreground',
                )}
              >
                <p className="whitespace-pre-wrap break-words">{entry.body}</p>
              </div>

              <p
                className={cn(
                  'px-1 text-xs text-muted-foreground',
                  isMine ? 'text-end' : 'text-start',
                )}
              >
                {isMine ? t('timeline.you') : (entry.authorName ?? t('timeline.reviewer'))} ·{' '}
                {formatDateTime(entry.createdAtUtc, locale)}
              </p>
            </li>
          );
        }

        if (entry.kind === TimelineEntryKind.StatusChange) {
          return (
            <li key={entry.id} className="self-center" data-testid="timeline-status">
              <p className="rounded-full bg-muted px-3 py-1 text-center text-xs text-muted-foreground">
                {t('timeline.statusChanged', {
                  from: t(statusLabelKey(entry.fromStatus as ApplicationStatus)),
                  to: t(statusLabelKey(entry.toStatus as ApplicationStatus)),
                })}
                {' · '}
                {formatDateTime(entry.createdAtUtc, locale)}
              </p>
            </li>
          );
        }

        const isResult = entry.kind === TimelineEntryKind.ResultAttached;
        const Icon = isResult ? FileCheck2 : FileUp;

        return (
          <li key={entry.id} className="self-center" data-testid="timeline-file">
            <p className="flex items-center gap-2 rounded-full bg-muted px-3 py-1 text-xs text-muted-foreground">
              <Icon className="size-3.5 shrink-0" aria-hidden="true" />
              {t(isResult ? 'timeline.resultAttached' : 'timeline.fileUploaded', {
                name: entry.fileName ?? '',
              })}
              {' · '}
              {formatDateTime(entry.createdAtUtc, locale)}
            </p>
          </li>
        );
      })}
    </ol>
  );
}
