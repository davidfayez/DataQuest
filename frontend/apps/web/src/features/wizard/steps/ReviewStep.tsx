import { Alert, Button, Card, CardContent, LoadingState, Spinner } from '@dv/ui';
import { useTranslation } from 'react-i18next';
import { useApplication } from '@/entities/application/api';
import { NameLanguageType } from '@/entities/application/types';
import { DocumentPreviewCard } from '../DocumentPreviewCard';
import { formatCalendarDate, formatCurrency } from '@/shared/lib/format';
import { useApiErrorMessage } from '@/shared/lib/useApiError';

interface Props {
  applicationId: string;
  isSubmitting: boolean;
  submitError: unknown;
  onSubmit: () => void;
  onSaveDraft: () => void;
  onBack: () => void;
}

function DetailRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-wrap justify-between gap-2 border-b border-border py-2 last:border-0">
      <dt className="text-sm text-muted-foreground">{label}</dt>
      <dd className="text-sm font-medium">{value}</dd>
    </div>
  );
}

/**
 * Final review. Everything shown here is read back from the server rather than from wizard state,
 * so the applicant confirms exactly what was persisted — including the authoritative totals.
 */
export function ReviewStep({
  applicationId,
  isSubmitting,
  submitError,
  onSubmit,
  onSaveDraft,
  onBack,
}: Props) {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage ?? 'en';
  const toMessage = useApiErrorMessage();
  const application = useApplication(applicationId);

  if (application.isPending) {
    return <LoadingState label={t('common.loading')} />;
  }

  if (application.isError || !application.data) {
    return <Alert variant="error">{toMessage(application.error)}</Alert>;
  }

  const details = application.data;
  const arabic = details.names.find((name) => name.languageType === NameLanguageType.Arabic);
  const english = details.names.find((name) => name.languageType === NameLanguageType.English);

  const fullName = (name: typeof arabic) =>
    name ? [name.firstName, name.middleName, name.lastName].filter(Boolean).join(' ') : '—';

  return (
    <div className="space-y-6">
      <header className="space-y-1">
        <h2 className="text-lg font-semibold">{t('wizard.review.title')}</h2>
        <p className="text-sm text-muted-foreground">{t('wizard.review.subtitle')}</p>
      </header>

      {/* Ternary rather than `&&`: submitError is `unknown`, so the short-circuit value would
          not be a renderable ReactNode. */}
      {submitError ? (
        <Alert variant="error" title={t('errors.genericTitle')}>
          {toMessage(submitError)}
        </Alert>
      ) : null}

      <Card>
        <CardContent className="p-4">
          <h3 className="mb-2 font-medium">{t('wizard.review.applicant')}</h3>
          <dl>
            <DetailRow label={t('wizard.review.addressee')} value={details.addressedTo} />
            <DetailRow label={t('wizard.personal.arabicName')} value={fullName(arabic)} />
            <DetailRow label={t('wizard.personal.englishName')} value={fullName(english)} />
            <DetailRow
              label={t('wizard.review.birthDate')}
              value={details.birthDate ? formatCalendarDate(details.birthDate, locale) : '—'}
            />
            <DetailRow label={t('wizard.personal.email')} value={details.applicantEmail ?? '—'} />
            <DetailRow
              label={t('wizard.personal.phone')}
              value={
                details.applicantPhoneNumber
                  ? `‎${details.applicantPhoneCode ?? ''}${details.applicantPhoneNumber}`
                  : '—'
              }
            />
          </dl>
        </CardContent>
      </Card>

      <Card>
        <CardContent className="p-4">
          <h3 className="mb-2 font-medium">{t('wizard.review.verification')}</h3>
          <dl>
            <DetailRow
              label={t('wizard.details.transactionType')}
              value={details.transactionType?.name ?? '—'}
            />
            <DetailRow
              label={t('wizard.details.subTransactionType')}
              value={details.subTransactionType?.name ?? '—'}
            />
            <DetailRow
              label={t('wizard.details.authority')}
              value={details.verificationAuthority?.name ?? '—'}
            />
          </dl>
        </CardContent>
      </Card>

      <Card>
        <CardContent className="p-4">
          <h3 className="mb-2 font-medium">{t('wizard.review.services')}</h3>
          <dl>
            {details.services.map((service) => (
              <DetailRow
                key={service.id}
                label={`${service.serviceName} × ${service.quantity}${
                  service.isExpress ? ` (${t('wizard.summary.express')})` : ''
                }`}
                value={formatCurrency(service.lineTotal, details.currencyCode, locale)}
              />
            ))}
          </dl>

          <div className="mt-3 flex justify-between border-t-2 border-border pt-3">
            <span className="font-medium">{t('wizard.review.total')}</span>
            <span className="text-lg font-semibold" data-testid="review-total">
              {formatCurrency(details.totalCost, details.currencyCode, locale)}
            </span>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardContent className="p-4">
          <h3 className="mb-2 font-medium">{t('wizard.review.documents')}</h3>

          {details.files.length === 0 ? (
            <p className="text-sm text-muted-foreground">{t('wizard.review.noDocuments')}</p>
          ) : (
            <>
              {/* Shown rather than listed: this is the last moment to notice the wrong page
                  went up, and a filename cannot show that. */}
              <ul className="grid gap-2 sm:grid-cols-2" data-testid="review-documents">
                {details.files.map((file) => (
                  <DocumentPreviewCard
                    key={file.id}
                    applicationId={applicationId}
                    fileId={file.id}
                    fileName={file.fileName}
                    contentType={file.contentType}
                    sizeBytes={file.sizeBytes}
                    requirementName={
                      details.requiredFiles.find(
                        (requirement) => requirement.requiredFileId === file.requiredFileId,
                      )?.name
                    }
                  />
                ))}
              </ul>

              <p className="mt-3 text-xs text-muted-foreground">
                {t('wizard.review.documentsHint')}
              </p>
            </>
          )}
        </CardContent>
      </Card>

      <Alert variant="info">{t('wizard.review.submitHint')}</Alert>

      <div className="flex flex-wrap justify-end gap-3">
        <Button type="button" variant="outline" onClick={onBack}>
          {t('wizard.previous')}
        </Button>
        <Button type="button" variant="outline" onClick={onSaveDraft}>
          {t('wizard.saveDraft')}
        </Button>
        <Button
          type="button"
          onClick={onSubmit}
          disabled={isSubmitting}
          data-testid="submit-application"
        >
          {isSubmitting && <Spinner />}
          {t('wizard.submit')}
        </Button>
      </div>
    </div>
  );
}
