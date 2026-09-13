import { Alert, Button, Card, CardContent } from '@dv/ui';
import { FileText } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useServiceTypes } from '@/entities/application/api';
import { formatCurrency } from '@/shared/lib/format';
import type { DetailsValues } from '../schemas';
import { useStepHeading } from '../api';

interface Props {
  /** Persists whatever has been entered so far and leaves the wizard. */
  onSaveDraft?: () => void;
  isSavingDraft?: boolean;
  details: DetailsValues;
  currencyCode: string;
  onNext: () => void;
  onBack: () => void;
}

/**
 * The services summary table. Line totals are computed here for display using the same rule the
 * server applies — (cost + express) × quantity — but the authoritative figure is the one returned
 * when the draft is saved, and that is what the applicant is charged.
 */
export function SummaryStep({
  onSaveDraft,
  isSavingDraft,
  details,
  currencyCode,
  onNext,
  onBack,
}: Props) {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage ?? 'en';
  const serviceTypes = useServiceTypes(
    details.verificationAuthorityId,
    details.subTransactionTypeId,
  );

  const rows = details.services
    .map((line) => {
      const serviceType = serviceTypes.data?.find((service) => service.id === line.serviceTypeId);
      if (!serviceType) return null;

      const quantity = Number(line.quantity);
      const expressCost = line.isExpress ? serviceType.expressCost : 0;

      return {
        line,
        serviceType,
        quantity,
        expressCost,
        lineTotal: (serviceType.cost + expressCost) * quantity,
      };
    })
    .filter((row): row is NonNullable<typeof row> => row !== null);

  const grandTotal = rows.reduce((sum, row) => sum + row.lineTotal, 0);

  const heading = useStepHeading('summary');

  return (
    <div className="space-y-6">
      <header className="space-y-1">
        <h2 className="text-lg font-semibold">{heading.title}</h2>
        <p className="text-sm text-muted-foreground">{heading.subtitle}</p>
      </header>

      {rows.length === 0 ? (
        <Alert variant="warning">{t('wizard.summary.noServices')}</Alert>
      ) : (
        <>
          {/* The table scrolls inside its own container so a narrow viewport never forces the
              whole page to scroll sideways. */}
          <div className="overflow-x-auto rounded-lg border border-border">
            <table className="w-full min-w-[48rem] text-sm">
              <thead className="bg-muted text-start">
                <tr>
                  <th scope="col" className="p-3 text-start font-medium">
                    {t('wizard.summary.serviceName')}
                  </th>
                  <th scope="col" className="p-3 text-start font-medium">
                    {t('wizard.summary.description')}
                  </th>
                  <th scope="col" className="p-3 text-start font-medium">
                    {t('wizard.summary.executionTime')}
                  </th>
                  <th scope="col" className="p-3 text-end font-medium">
                    {t('wizard.summary.cost')}
                  </th>
                  <th scope="col" className="p-3 text-center font-medium">
                    {t('wizard.summary.express')}
                  </th>
                  <th scope="col" className="p-3 text-end font-medium">
                    {t('wizard.summary.expressCost')}
                  </th>
                  <th scope="col" className="p-3 text-end font-medium">
                    {t('wizard.summary.quantity')}
                  </th>
                  <th scope="col" className="p-3 text-end font-medium">
                    {t('wizard.summary.lineTotal')}
                  </th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row, index) => (
                  <tr key={`${row.serviceType.id}-${index}`} className="border-t border-border">
                    <td className="p-3 font-medium">{row.serviceType.name}</td>
                    <td className="max-w-xs p-3 text-muted-foreground">
                      {row.serviceType.description}
                    </td>
                    <td className="p-3 whitespace-nowrap">
                      {t('wizard.summary.days', { count: row.serviceType.executionTimeDays })}
                    </td>
                    <td className="p-3 text-end whitespace-nowrap">
                      {formatCurrency(row.serviceType.cost, currencyCode, locale)}
                    </td>
                    <td className="p-3 text-center">{row.line.isExpress ? '✓' : '—'}</td>
                    <td className="p-3 text-end whitespace-nowrap">
                      {row.expressCost > 0
                        ? formatCurrency(row.expressCost, currencyCode, locale)
                        : '—'}
                    </td>
                    <td className="p-3 text-end">{row.quantity}</td>
                    <td className="p-3 text-end font-medium whitespace-nowrap">
                      {formatCurrency(row.lineTotal, currencyCode, locale)}
                    </td>
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr className="border-t-2 border-border bg-muted/50">
                  <td colSpan={7} className="p-3 text-end font-medium">
                    {t('wizard.summary.grandTotal')}
                  </td>
                  <td
                    className="p-3 text-end text-base font-semibold whitespace-nowrap"
                    data-testid="grand-total"
                  >
                    {formatCurrency(grandTotal, currencyCode, locale)}
                  </td>
                </tr>
              </tfoot>
            </table>
          </div>

          <section className="space-y-3">
            <h3 className="font-medium">{t('wizard.summary.requiredFilesTitle')}</h3>
            <div className="grid gap-3 sm:grid-cols-2">
              {rows.map((row, index) => (
                <Card key={`files-${row.serviceType.id}-${index}`}>
                  <CardContent className="space-y-2 p-4">
                    <p className="text-sm font-medium">{row.serviceType.name}</p>
                    <ul className="space-y-1.5">
                      {row.serviceType.requiredFiles.map((file) => (
                        <li key={file.id} className="flex items-center gap-2 text-sm">
                          <FileText
                            className="size-4 shrink-0 text-muted-foreground"
                            aria-hidden="true"
                          />
                          <span>{file.name}</span>
                          <span
                            className={
                              file.isMandatory
                                ? 'ms-auto rounded-full bg-destructive/10 px-2 py-0.5 text-xs text-destructive'
                                : 'ms-auto rounded-full bg-muted px-2 py-0.5 text-xs text-muted-foreground'
                            }
                          >
                            {file.isMandatory
                              ? t('wizard.summary.mandatory')
                              : t('wizard.summary.optionalFile')}
                          </span>
                        </li>
                      ))}
                    </ul>
                  </CardContent>
                </Card>
              ))}
            </div>
          </section>
        </>
      )}

      <div className="flex flex-wrap justify-end gap-3">
        <Button type="button" variant="outline" onClick={onBack}>
          {t('wizard.previous')}
        </Button>
        {onSaveDraft && (
          <Button type="button" variant="outline" onClick={onSaveDraft} disabled={isSavingDraft}>
            {t('wizard.saveDraft')}
          </Button>
        )}
        <Button type="button" onClick={onNext} disabled={rows.length === 0}>
          {t('wizard.next')}
        </Button>
      </div>
    </div>
  );
}
