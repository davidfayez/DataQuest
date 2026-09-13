import { Alert, Button, Card, CardContent, LoadingState, cn } from '@dv/ui';
import { ArrowLeft, ChevronLeft, ChevronRight } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router-dom';
import { useApplication } from '@/entities/application/api';
import {
  useApplicationChangeLog,
  useApplicationStatusLog,
  type ApplicationChangeLogEntryDto,
} from '@/entities/application/logApi';
import { StatusBadge } from '@/entities/application/StatusBadge';
import { statusLabelKey } from '@/entities/application/statusLabels';
import { ActorType } from '@/entities/application/timelineApi';
import { formatDateTime } from '@/shared/lib/format';
import { useApiErrorMessage } from '@/shared/lib/useApiError';

type Tab = 'status' | 'changes';

const TABS: Tab[] = ['status', 'changes'];

/**
 * Turns an audit action code into something the applicant can read.
 *
 * The API records actions as stable identifiers (`Application.StatusChanged`) because they are
 * filtered on and must not drift with wording, so they are translated at the edge. i18next treats
 * a dot as a nesting separator, hence the underscore. An action with no translation yet falls back
 * to a spaced-out form — a new action should look unpolished, never broken.
 */
function actionLabel(action: string, t: (key: string) => string): string {
  const key = `log.action.${action.replace(/\./g, '_')}`;
  const translated = t(key);
  if (translated !== key) return translated;

  const event = action.split('.')[1];
  if (!event) return action;

  const spaced = event.replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase();
  return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}

/** Who did it, in the applicant's own terms rather than the enum's. */
function actorLabel(
  type: ActorType,
  name: string | null,
  t: (key: string) => string,
): string {
  if (type === ActorType.Applicant) return t('log.you');
  if (type === ActorType.System) return t('log.system');
  return name ?? t('timeline.reviewer');
}

/** `applicationNumber` → `Application number`, for a payload key with no dedicated translation. */
function detailLabel(key: string, t: (key: string) => string): string {
  const translated = t(`log.detail.${key}`);
  if (translated !== `log.detail.${key}`) return translated;

  const spaced = key.replace(/([a-z])([A-Z])/g, '$1 $2');
  return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}

function ChangeLogRow({ entry }: { entry: ApplicationChangeLogEntryDto }) {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage ?? 'en';
  const details = Object.entries(entry.details);

  return (
    <li className="border-t border-border p-4 first:border-t-0" data-testid={`change-${entry.id}`}>
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <p className="font-medium">{actionLabel(entry.action, t)}</p>
        <p className="text-xs text-muted-foreground whitespace-nowrap">
          {formatDateTime(entry.createdAtUtc, locale)}
        </p>
      </div>

      <p className="mt-0.5 text-sm text-muted-foreground">
        {t('log.by', { actor: actorLabel(entry.actorType, entry.actorName, t) })}
      </p>

      {details.length > 0 && (
        <dl className="mt-2 flex flex-wrap gap-x-6 gap-y-1 text-xs">
          {details.map(([key, value]) => (
            <div key={key} className="flex gap-1.5">
              <dt className="text-muted-foreground">{detailLabel(key, t)}:</dt>
              <dd className="font-medium">{value}</dd>
            </div>
          ))}
        </dl>
      )}
    </li>
  );
}

/**
 * The two histories an applicant may want, side by side but deliberately separate: the status log
 * is the lifecycle, the change log is everything anyone touched. An edit that rewrites nine fields
 * without moving the status appears only in the second.
 */
export function ApplicationLogPage() {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage ?? 'en';
  const { lang = 'en', id = '' } = useParams<{ lang: string; id: string }>();
  const toMessage = useApiErrorMessage();

  const [tab, setTab] = useState<Tab>('status');
  const [page, setPage] = useState(1);

  const application = useApplication(id);
  const statusLog = useApplicationStatusLog(id);
  const changeLog = useApplicationChangeLog(id, page);

  if (application.isPending) {
    return <LoadingState label={t('common.loading')} />;
  }

  if (application.isError) {
    return (
      <div className="mx-auto max-w-4xl px-4 py-10 sm:px-6">
        <Alert variant="error">{toMessage(application.error)}</Alert>
      </div>
    );
  }

  const changes = changeLog.data;

  return (
    <div className="mx-auto max-w-4xl space-y-6 px-4 py-10 sm:px-6">
      <Link
        to={`/${lang}/applications`}
        className="inline-flex items-center gap-1.5 text-sm text-muted-foreground hover:text-foreground"
        data-testid="back-to-applications"
      >
        <ArrowLeft className="size-4 rtl:rotate-180" aria-hidden="true" />
        {t('dashboard.title')}
      </Link>

      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold">{t('log.title')}</h1>
          <p className="mt-0.5 font-mono text-sm text-muted-foreground" dir="ltr">
            {application.data?.applicationNumber}
          </p>
        </div>

        {application.data && <StatusBadge status={application.data.status} />}
      </div>

      <div className="flex border-b border-border" role="tablist">
        {TABS.map((name) => (
          <button
            key={name}
            role="tab"
            type="button"
            aria-selected={tab === name}
            onClick={() => setTab(name)}
            data-testid={`tab-${name}`}
            className={cn(
              '-mb-px border-b-2 px-4 py-2 text-sm transition-colors',
              tab === name
                ? 'border-primary font-medium text-primary'
                : 'border-transparent text-muted-foreground hover:text-foreground',
            )}
          >
            {t(`log.tabs.${name}`)}
          </button>
        ))}
      </div>

      {tab === 'status' && (
        <section aria-label={t('log.tabs.status')}>
          {statusLog.isPending ? (
            <LoadingState label={t('common.loading')} />
          ) : statusLog.isError ? (
            <Alert variant="error">{toMessage(statusLog.error)}</Alert>
          ) : (statusLog.data ?? []).length === 0 ? (
            <Card>
              <CardContent className="p-10 text-center text-sm text-muted-foreground">
                {t('log.statusEmpty')}
              </CardContent>
            </Card>
          ) : (
            <ol className="overflow-hidden rounded-lg border border-border">
              {(statusLog.data ?? []).map((entry) => (
                <li
                  key={entry.id}
                  className="border-t border-border p-4 first:border-t-0"
                  data-testid={`status-${entry.id}`}
                >
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <p className="flex flex-wrap items-center gap-2 text-sm">
                      <span className="text-muted-foreground">
                        {t(statusLabelKey(entry.fromStatus))}
                      </span>
                      {/* A logical arrow: it mirrors with the page under RTL. */}
                      <ChevronRight
                        className="size-4 text-muted-foreground rtl:rotate-180"
                        aria-hidden="true"
                      />
                      <StatusBadge status={entry.toStatus} />
                    </p>

                    <p className="text-xs text-muted-foreground whitespace-nowrap">
                      {formatDateTime(entry.createdAtUtc, locale)}
                    </p>
                  </div>

                  <p className="mt-1 text-sm text-muted-foreground">
                    {t('log.by', {
                      actor: actorLabel(entry.changedByType, entry.changedByName, t),
                    })}
                  </p>

                  {entry.note && <p className="mt-1.5 text-sm">{entry.note}</p>}
                </li>
              ))}
            </ol>
          )}
        </section>
      )}

      {tab === 'changes' && (
        <section aria-label={t('log.tabs.changes')} className="space-y-4">
          <p className="text-sm text-muted-foreground">{t('log.changesIntro')}</p>

          {changeLog.isPending ? (
            <LoadingState label={t('common.loading')} />
          ) : changeLog.isError ? (
            <Alert variant="error">{toMessage(changeLog.error)}</Alert>
          ) : (changes?.items ?? []).length === 0 ? (
            <Card>
              <CardContent className="p-10 text-center text-sm text-muted-foreground">
                {t('log.changesEmpty')}
              </CardContent>
            </Card>
          ) : (
            <>
              <ol className="overflow-hidden rounded-lg border border-border">
                {(changes?.items ?? []).map((entry) => (
                  <ChangeLogRow key={entry.id} entry={entry} />
                ))}
              </ol>

              {changes && changes.totalPages > 1 && (
                <div className="flex items-center justify-between gap-3">
                  <p className="text-sm text-muted-foreground">
                    {t('wallet.pageOf', { page: changes.page, total: changes.totalPages })}
                  </p>

                  <div className="flex gap-2">
                    <Button
                      variant="outline"
                      size="sm"
                      disabled={!changes.hasPrevious || changeLog.isFetching}
                      onClick={() => setPage((current) => Math.max(1, current - 1))}
                      data-testid="change-log-previous"
                    >
                      <ChevronLeft className="size-4 rtl:rotate-180" aria-hidden="true" />
                      {t('common.previous')}
                    </Button>

                    <Button
                      variant="outline"
                      size="sm"
                      disabled={!changes.hasNext || changeLog.isFetching}
                      onClick={() => setPage((current) => current + 1)}
                      data-testid="change-log-next"
                    >
                      {t('common.next')}
                      <ChevronRight className="size-4 rtl:rotate-180" aria-hidden="true" />
                    </Button>
                  </div>
                </div>
              )}
            </>
          )}
        </section>
      )}
    </div>
  );
}
