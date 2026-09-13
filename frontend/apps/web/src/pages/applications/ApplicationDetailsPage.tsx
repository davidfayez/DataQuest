import {
  Alert,
  Button,
  Card,
  CardContent,
  cn,
  LoadingState,
  Spinner,
} from '@dv/ui';
import { ArrowLeft } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router-dom';
import { useApplication } from '@/entities/application/api';
import { StatusBadge } from '@/entities/application/StatusBadge';
import {
  useAddComment,
  useApplicationResults,
  useResubmitApplication,
  useTimeline,
} from '@/entities/application/timelineApi';
import { ApplicationStatus, NameLanguageType } from '@/entities/application/types';
import { ApplicationFileRow } from '@/features/applications/ApplicationFileRow';
import { ActivityTimeline } from '@/features/timeline/ActivityTimeline';
import { formatCalendarDate, formatCurrency, formatDateTime } from '@/shared/lib/format';
import { useApiErrorMessage } from '@/shared/lib/useApiError';

type Tab = 'details' | 'services' | 'files' | 'timeline';

const TABS: Tab[] = ['details', 'services', 'files', 'timeline'];

export function ApplicationDetailsPage() {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage ?? 'en';
  const { lang = 'en', id = '' } = useParams<{ lang: string; id: string }>();
  const toMessage = useApiErrorMessage();

  const application = useApplication(id);
  const timeline = useTimeline(id);
  const [tab, setTab] = useState<Tab>('details');
  const [reply, setReply] = useState('');

  const addComment = useAddComment(id);
  const resubmit = useResubmitApplication(id);

  const isSuccess = application.data?.status === ApplicationStatus.Success;
  const results = useApplicationResults(id, isSuccess);

  if (application.isPending) {
    return <LoadingState label={t('common.loading')} />;
  }

  if (application.isError || !application.data) {
    return (
      <div className="mx-auto max-w-3xl px-4 py-10">
        <Alert variant="error">{toMessage(application.error)}</Alert>
      </div>
    );
  }

  const details = application.data;
  const needsInfo = details.status === ApplicationStatus.MissedInfo;

  const nameFor = (type: NameLanguageType) => {
    const name = details.names.find((entry) => entry.languageType === type);
    return name ? [name.firstName, name.middleName, name.lastName].filter(Boolean).join(' ') : '—';
  };

  return (
    <div className="mx-auto max-w-4xl space-y-6 px-4 py-10 sm:px-6">
      <Link
        to={`/${lang}/applications`}
        className="inline-flex items-center gap-1.5 text-sm text-muted-foreground hover:text-foreground"
      >
        {/* rtl:rotate-180 flips the arrow so it still points "back" in a mirrored layout. */}
        <ArrowLeft className="size-4 rtl:rotate-180" aria-hidden="true" />
        {t('details.backToList')}
      </Link>

      <header className="flex flex-wrap items-start justify-between gap-4">
        <div className="space-y-1">
          <h1 className="font-mono text-xl font-semibold" dir="ltr">
            {details.applicationNumber}
          </h1>
          <p className="text-sm text-muted-foreground">{details.addressedTo}</p>
        </div>

        <div className="flex flex-col items-end gap-2">
          <StatusBadge status={details.status} data-testid="application-status" />
          <p className="text-lg font-semibold">
            {formatCurrency(details.totalCost, details.currencyCode, locale)}
          </p>
        </div>
      </header>

      {needsInfo && (
        <Alert variant="warning" title={t('timeline.missedInfoTitle')}>
          {t('timeline.missedInfoBody')}
        </Alert>
      )}

      {isSuccess && (
        <Card data-testid="results-section">
          <CardContent className="space-y-3 p-6">
            <div>
              <h2 className="font-medium">{t('details.resultsTitle')}</h2>
              <p className="text-sm text-muted-foreground">{t('details.resultsBody')}</p>
            </div>

            <ul className="space-y-2">
              {(results.data ?? []).map((file) => (
                <ApplicationFileRow key={file.id} applicationId={details.id} file={file} />
              ))}
            </ul>
          </CardContent>
        </Card>
      )}

      {/* Documents the review team shared, whatever the status — a request for more information
          can arrive with the paperwork that explains it, not only a successful verification. */}
      {details.documents.length > 0 && (
        <Card data-testid="shared-documents">
          <CardContent className="space-y-3 p-6">
            <div>
              <h2 className="font-medium">{t('details.documentsTitle')}</h2>
              <p className="text-sm text-muted-foreground">{t('details.documentsBody')}</p>
            </div>

            {details.documents.map((document) => (
              <div key={document.id} className="space-y-3 rounded-xl border border-border p-4">
                <div>
                  <p className="font-medium">{document.name}</p>
                  <p className="text-xs text-muted-foreground">
                    {formatDateTime(document.attachedAtUtc, locale)}
                  </p>
                </div>

                {document.fields.length > 0 && (
                  <dl className="grid gap-x-6 gap-y-1 sm:grid-cols-2">
                    {document.fields.map((field) => (
                      <div key={field.id} className="flex justify-between gap-3 text-sm">
                        <dt className="text-muted-foreground">{field.name}</dt>
                        <dd className="font-medium">{field.displayValue || '—'}</dd>
                      </div>
                    ))}
                  </dl>
                )}

                <ul className="space-y-2">
                  {document.files.map((file) => (
                    <ApplicationFileRow key={file.id} applicationId={details.id} file={file} />
                  ))}
                </ul>
              </div>
            ))}
          </CardContent>
        </Card>
      )}

      <div role="tablist" className="flex flex-wrap gap-1 border-b border-border">
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
            {t(`details.tabs.${name}`)}
          </button>
        ))}
      </div>

      {tab === 'details' && (
        <Card>
          <CardContent className="p-6">
            <dl className="space-y-2">
              {[
                // Labelled from `details` rather than the wizard's keys: this is a row in a record,
                // not a fieldset legend on a form, and it reads better shorter.
                [t('details.arabicName'), nameFor(NameLanguageType.Arabic)],
                [t('details.englishName'), nameFor(NameLanguageType.English)],
                // A step the applicant has not filled in yet shows a dash rather than a blank.
                [
                  t('wizard.review.birthDate'),
                  details.birthDate ? formatCalendarDate(details.birthDate, locale) : '—',
                ],
                [t('wizard.personal.email'), details.applicantEmail ?? '—'],
                // The left-to-right mark keeps '+20' from rendering as '20+' on the Arabic page.
                [
                  t('wizard.personal.phone'),
                  details.applicantPhoneNumber
                    ? `‎${details.applicantPhoneCode ?? ''}${details.applicantPhoneNumber}`
                    : '—',
                ],
                [t('wizard.details.transactionType'), details.transactionType?.name ?? '—'],
                [t('wizard.details.subTransactionType'), details.subTransactionType?.name ?? '—'],
                [t('wizard.details.authority'), details.verificationAuthority?.name ?? '—'],
                [t('details.createdOn'), formatDateTime(details.createdAtUtc, locale)],
                ...(details.paidAtUtc
                  ? [[t('details.paidOn'), formatDateTime(details.paidAtUtc, locale)]]
                  : []),
              ].map(([label, value]) => (
                <div key={label} className="flex flex-wrap justify-between gap-2 border-b border-border py-2 last:border-0">
                  <dt className="text-sm text-muted-foreground">{label}</dt>
                  <dd className="text-sm font-medium">{value}</dd>
                </div>
              ))}
            </dl>
          </CardContent>
        </Card>
      )}

      {tab === 'services' && (
        <div className="overflow-x-auto rounded-lg border border-border">
          <table className="w-full min-w-[36rem] text-sm">
            <thead className="bg-muted">
              <tr>
                <th scope="col" className="p-3 text-start font-medium">{t('wizard.summary.serviceName')}</th>
                <th scope="col" className="p-3 text-center font-medium">{t('wizard.summary.express')}</th>
                <th scope="col" className="p-3 text-end font-medium">{t('wizard.summary.quantity')}</th>
                <th scope="col" className="p-3 text-end font-medium">{t('wizard.summary.lineTotal')}</th>
              </tr>
            </thead>
            <tbody>
              {details.services.map((service) => (
                <tr key={service.id} className="border-t border-border">
                  <td className="p-3">{service.serviceName}</td>
                  <td className="p-3 text-center">{service.isExpress ? '✓' : '—'}</td>
                  <td className="p-3 text-end">{service.quantity}</td>
                  <td className="p-3 text-end whitespace-nowrap">
                    {formatCurrency(service.lineTotal, details.currencyCode, locale)}
                  </td>
                </tr>
              ))}
            </tbody>
            <tfoot>
              <tr className="border-t-2 border-border bg-muted/50">
                <td colSpan={3} className="p-3 text-end font-medium">{t('wizard.review.total')}</td>
                <td className="p-3 text-end font-semibold whitespace-nowrap">
                  {formatCurrency(details.totalCost, details.currencyCode, locale)}
                </td>
              </tr>
            </tfoot>
          </table>
        </div>
      )}

      {tab === 'files' && (
        <Card>
          <CardContent className="p-6">
            {details.files.length === 0 ? (
              <p className="text-center text-sm text-muted-foreground">{t('details.noFiles')}</p>
            ) : (
              <ul className="space-y-2">
                {details.files.map((file) => (
                  <ApplicationFileRow key={file.id} applicationId={details.id} file={file} />
                ))}
              </ul>
            )}
          </CardContent>
        </Card>
      )}

      {tab === 'timeline' && (
        <Card>
          <CardContent className="space-y-6 p-6">
            {timeline.isPending ? (
              <LoadingState label={t('common.loading')} />
            ) : (
              <ActivityTimeline entries={timeline.data ?? []} />
            )}

            {/* The composer and resubmit action appear only while the reviewer is waiting. */}
            {needsInfo && (
              <div className="space-y-3 border-t border-border pt-4">
                {addComment.isError ? (
                  <Alert variant="error">{toMessage(addComment.error)}</Alert>
                ) : null}

                <label htmlFor="reply" className="sr-only">
                  {t('timeline.replyPlaceholder')}
                </label>
                <textarea
                  id="reply"
                  rows={3}
                  value={reply}
                  onChange={(event) => setReply(event.target.value)}
                  placeholder={t('timeline.replyPlaceholder')}
                  data-testid="reply-box"
                  className="w-full rounded-lg border border-border bg-background p-3 text-sm focus-visible:border-ring focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                />

                <div className="flex flex-wrap justify-end gap-2">
                  <Button
                    variant="outline"
                    disabled={!reply.trim() || addComment.isPending}
                    data-testid="send-reply"
                    onClick={() =>
                      addComment.mutate(reply.trim(), { onSuccess: () => setReply('') })
                    }
                  >
                    {addComment.isPending && <Spinner />}
                    {t('timeline.send')}
                  </Button>

                  <Button
                    disabled={resubmit.isPending}
                    data-testid="resubmit"
                    onClick={() => resubmit.mutate()}
                  >
                    {resubmit.isPending && <Spinner />}
                    {t('timeline.resubmit')}
                  </Button>
                </div>
              </div>
            )}
          </CardContent>
        </Card>
      )}
    </div>
  );
}
