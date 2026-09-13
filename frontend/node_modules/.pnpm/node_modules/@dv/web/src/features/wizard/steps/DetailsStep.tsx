import { zodResolver } from '@hookform/resolvers/zod';
import { SUPPORTED_LANGUAGES } from '@dv/i18n';
import { Button, Card, CardContent, Field, Input, SearchableSelect } from '@dv/ui';
import { Plus, Trash2, Zap } from 'lucide-react';
import { useFieldArray, useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import {
  useAuthorities,
  useServiceTypes,
  useSubTransactionTypes,
  useTransactionTypes,
} from '@/entities/application/api';
import { formatCurrency } from '@/shared/lib/format';
import { detailsSchema, type DetailsValues } from '../schemas';
import { useStepHeading } from '../api';

interface Props {
  /** Persists whatever has been entered so far and leaves the wizard. */
  onSaveDraft?: (values: DetailsValues) => void;
  isSavingDraft?: boolean;
  defaultValues: DetailsValues | null;
  currencyCode: string;
  onNext: (values: DetailsValues) => void;
  onBack: () => void;
}

const EMPTY_SERVICE = { serviceTypeId: '', quantity: 1, languageCode: 'en', isExpress: false };

/** Which dependent fields a given parent owns, so a change clears exactly the right things. */
type CascadeLevel = 'transactionTypeId' | 'subTransactionTypeId';

/**
 * The note an administrator authored for this service, with any `{{cost}}` placeholder replaced by
 * the express surcharge in the order's currency. Returns null when no note is configured, which is
 * the caller's signal to fall back to the app's own wording.
 */
function applicantNoteFor(service: { expressNote?: string | null }, cost: string) {
  const note = service.expressNote?.trim();
  return note ? note.replaceAll('{{cost}}', cost) : null;
}

/**
 * The output languages a service is configured for, in the platform's own order and labelled with
 * each locale's native name. An unconfigured service falls back to every language, matching what
 * the server resolves.
 */
function languageOptionsFor(service?: { outputLanguages?: string[] }) {
  const offered = new Set(service?.outputLanguages ?? []);

  return SUPPORTED_LANGUAGES.filter(
    (language) => offered.size === 0 || offered.has(language.code),
  ).map((language) => ({ value: language.code, label: language.name }));
}

export function DetailsStep({
  onSaveDraft,
  isSavingDraft,
  defaultValues,
  currencyCode,
  onNext,
  onBack,
}: Props) {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage ?? 'en';

  const form = useForm<DetailsValues>({
    resolver: zodResolver(detailsSchema(t)),
    defaultValues: defaultValues ?? {
      transactionTypeId: '',
      subTransactionTypeId: '',
      verificationAuthorityId: '',
      services: [EMPTY_SERVICE],
    },
  });

  const transactionTypeId = form.watch('transactionTypeId');
  const subTransactionTypeId = form.watch('subTransactionTypeId');
  const authorityId = form.watch('verificationAuthorityId');
  const services = form.watch('services');

  const transactionTypes = useTransactionTypes();
  const subTypes = useSubTransactionTypes(transactionTypeId || undefined);
  const authorities = useAuthorities(subTransactionTypeId || undefined);
  const serviceTypes = useServiceTypes(authorityId || undefined, subTransactionTypeId || undefined);

  const serviceFields = useFieldArray({ control: form.control, name: 'services' });
  const errors = form.formState.errors;

  /**
   * Applies a cascade change immediately, clearing whatever depended on it. Deliberately
   * unconfirmed: re-picking a parent is a routine correction, and interrupting it with a
   * dialog every time cost more than the discarded selections were worth.
   */
  function applyChange(level: CascadeLevel, value: string) {
    form.setValue(level, value, { shouldValidate: false });

    if (level === 'transactionTypeId') {
      form.setValue('subTransactionTypeId', '');
    }

    form.setValue('verificationAuthorityId', '');
    form.setValue('services', [EMPTY_SERVICE]);
  }

  /** Changing the authority only invalidates the services, which it owns directly. */
  function handleAuthorityChange(value: string) {
    form.setValue('verificationAuthorityId', value, { shouldValidate: false });
    form.setValue('services', [EMPTY_SERVICE]);
  }

  const heading = useStepHeading('details');

  return (
    <form noValidate className="space-y-6" onSubmit={form.handleSubmit(onNext)}>
      <header className="space-y-1">
        <h2 className="text-lg font-semibold">{heading.title}</h2>
        <p className="text-sm text-muted-foreground">{heading.subtitle}</p>
      </header>

      <div className="grid gap-4 sm:grid-cols-3">
        <Field
          label={t('wizard.details.transactionType')}
          htmlFor="transactionTypeId"
          required
          error={errors.transactionTypeId?.message}
        >
          <SearchableSelect
            id="transactionTypeId"
            value={transactionTypeId}
            disabled={transactionTypes.isPending}
            invalid={Boolean(errors.transactionTypeId)}
            onChange={(value) => applyChange('transactionTypeId', value)}
            placeholder={t('wizard.details.transactionTypePlaceholder')}
            searchPlaceholder={t('common.search')}
            emptyMessage={t('common.noResults')}
            options={(transactionTypes.data ?? []).map((type) => ({
              value: type.id,
              label: type.name,
            }))}
          />
        </Field>

        <Field
          label={t('wizard.details.subTransactionType')}
          htmlFor="subTransactionTypeId"
          required
          error={errors.subTransactionTypeId?.message}
        >
          <SearchableSelect
            id="subTransactionTypeId"
            value={subTransactionTypeId}
            disabled={!transactionTypeId || subTypes.isPending}
            invalid={Boolean(errors.subTransactionTypeId)}
            onChange={(value) => applyChange('subTransactionTypeId', value)}
            placeholder={
              !transactionTypeId
                ? t('wizard.details.selectParentFirst')
                : t('wizard.details.subTransactionTypePlaceholder')
            }
            searchPlaceholder={t('common.search')}
            emptyMessage={t('common.noResults')}
            options={(subTypes.data ?? []).map((type) => ({
              value: type.id,
              label: type.name,
            }))}
          />
        </Field>

        <Field
          label={t('wizard.details.authority')}
          htmlFor="verificationAuthorityId"
          required
          error={errors.verificationAuthorityId?.message}
        >
          <SearchableSelect
            id="verificationAuthorityId"
            value={authorityId}
            disabled={!subTransactionTypeId || authorities.isPending}
            invalid={Boolean(errors.verificationAuthorityId)}
            onChange={handleAuthorityChange}
            placeholder={
              !subTransactionTypeId
                ? t('wizard.details.selectParentFirst')
                : authorities.data?.length === 0
                  ? t('wizard.details.noOptions')
                  : t('wizard.details.authorityPlaceholder')
            }
            searchPlaceholder={t('common.search')}
            emptyMessage={t('common.noResults')}
            options={(authorities.data ?? []).map((authority) => ({
              value: authority.id,
              label: authority.name,
            }))}
          />
        </Field>
      </div>

      <section className="space-y-4">
        <header className="space-y-1">
          <h3 className="font-medium">{t('wizard.details.servicesTitle')}</h3>
          <p className="text-sm text-muted-foreground">{t('wizard.details.servicesSubtitle')}</p>
        </header>

        {errors.services?.root && (
          <p role="alert" className="text-sm text-destructive">
            {errors.services.root.message}
          </p>
        )}

        {serviceFields.fields.map((field, index) => {
          const selectedId = services[index]?.serviceTypeId;
          const selected = serviceTypes.data?.find((service) => service.id === selectedId);

          return (
            <Card key={field.id}>
              <CardContent className="space-y-4 p-4">
                <div className="flex items-center justify-between">
                  <h4 className="text-sm font-medium">
                    {t('wizard.details.serviceNumber', { index: index + 1 })}
                  </h4>
                  {serviceFields.fields.length > 1 && (
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      onClick={() => serviceFields.remove(index)}
                      aria-label={t('wizard.details.removeService')}
                    >
                      <Trash2 className="size-4" aria-hidden="true" />
                    </Button>
                  )}
                </div>

                <div className="grid gap-4 sm:grid-cols-3">
                  <Field
                    label={t('wizard.details.serviceType')}
                    htmlFor={`service-type-${index}`}
                    required
                    error={errors.services?.[index]?.serviceTypeId?.message}
                    className="sm:col-span-3"
                  >
                    <SearchableSelect
                      id={`service-type-${index}`}
                      disabled={!authorityId || serviceTypes.isPending}
                      invalid={Boolean(errors.services?.[index]?.serviceTypeId)}
                      value={selectedId ?? ''}
                      onChange={(value) => {
                        form.setValue(`services.${index}.serviceTypeId`, value, {
                          shouldValidate: true,
                        });
                        const next = serviceTypes.data?.find((s) => s.id === value);
                        if (!next?.enableExpress) {
                          form.setValue(`services.${index}.isExpress`, false);
                        }

                        // Each service is issued in its own set of languages, so a code carried
                        // over from the previous choice may not be on offer here.
                        const offered = next?.outputLanguages ?? [];
                        const current = services[index]?.languageCode;
                        if (offered.length > 0 && (!current || !offered.includes(current))) {
                          form.setValue(`services.${index}.languageCode`, offered[0]!, {
                            shouldValidate: true,
                          });
                        }
                      }}
                      placeholder={
                        !authorityId
                          ? t('wizard.details.selectParentFirst')
                          : t('wizard.details.serviceTypePlaceholder')
                      }
                      searchPlaceholder={t('common.search')}
                      emptyMessage={t('common.noResults')}
                      options={(serviceTypes.data ?? []).map((service) => ({
                        value: service.id,
                        label: service.name,
                      }))}
                    />
                  </Field>

                  <Field
                    label={t('wizard.details.quantity')}
                    htmlFor={`service-qty-${index}`}
                    required
                    error={errors.services?.[index]?.quantity?.message}
                  >
                    <Input
                      id={`service-qty-${index}`}
                      type="number"
                      min={1}
                      invalid={Boolean(errors.services?.[index]?.quantity)}
                      {...form.register(`services.${index}.quantity`)}
                    />
                  </Field>

                  <Field label={t('wizard.details.language')} htmlFor={`service-lang-${index}`}>
                    <SearchableSelect
                      id={`service-lang-${index}`}
                      disabled={!selectedId}
                      value={services[index]?.languageCode ?? ''}
                      onChange={(value) =>
                        form.setValue(`services.${index}.languageCode`, value, {
                          shouldValidate: true,
                        })
                      }
                      placeholder={
                        selectedId
                          ? t('wizard.details.languagePlaceholder')
                          : t('wizard.details.selectServiceFirst')
                      }
                      searchPlaceholder={t('common.search')}
                      emptyMessage={t('common.noResults')}
                      options={languageOptionsFor(selected)}
                    />
                  </Field>

                  {/* The express toggle exists only for services that actually offer it — the
                      server rejects the flag otherwise, so showing it would be a dead end. */}
                  <div className="flex items-end">
                    {selected?.enableExpress ? (
                      <label
                        htmlFor={`service-express-${index}`}
                        className="flex h-11 w-full items-center gap-2 rounded-lg border border-border px-3"
                      >
                        <input
                          id={`service-express-${index}`}
                          type="checkbox"
                          className="size-4 rounded border-border"
                          {...form.register(`services.${index}.isExpress`)}
                        />
                        <span className="text-sm">{t('wizard.details.express')}</span>
                      </label>
                    ) : (
                      selectedId && (
                        <p className="pb-3 text-xs text-muted-foreground">
                          {t('wizard.details.expressUnavailable')}
                        </p>
                      )
                    )}
                  </div>
                </div>

                {/* An administrator's note is shown whenever one is set, express or not. The
                    fallback wording describes the surcharge itself, so it appears only while
                    express is actually ticked — with the box clear there is no surcharge to
                    explain. */}
                {selected &&
                  (() => {
                    const cost = formatCurrency(selected.expressCost, currencyCode, locale);
                    const note = applicantNoteFor(selected, cost);
                    if (note) {
                      return <p className="text-xs text-muted-foreground">{note}</p>;
                    }

                    const isExpress = Boolean(services[index]?.isExpress);
                    if (!selected.enableExpress || !isExpress) return null;

                    // Given its own tinted panel rather than grey fine print: it appears the
                    // moment express is ticked and tells the applicant what they have just
                    // agreed to pay, so it has to be read rather than skimmed past.
                    return (
                      <p
                        className="flex items-start gap-2 rounded-lg border border-primary-border bg-primary-muted px-3 py-2 text-sm leading-relaxed text-foreground"
                        data-testid={`express-hint-${index}`}
                      >
                        <Zap className="mt-0.5 size-4 shrink-0 text-primary" aria-hidden="true" />
                        <span>{t('wizard.details.expressHint', { cost })}</span>
                      </p>
                    );
                  })()}
              </CardContent>
            </Card>
          );
        })}

        <Button
          type="button"
          variant="outline"
          disabled={!authorityId}
          onClick={() => serviceFields.append(EMPTY_SERVICE)}
        >
          <Plus className="size-4" aria-hidden="true" />
          {t('wizard.details.addService')}
        </Button>
      </section>

      <div className="flex flex-wrap justify-end gap-3">
        <Button type="button" variant="outline" onClick={onBack}>
          {t('wizard.previous')}
        </Button>
        {onSaveDraft && (
          <Button type="button" variant="outline" onClick={() => onSaveDraft(form.getValues())} disabled={isSavingDraft}>
            {t('wizard.saveDraft')}
          </Button>
        )}
        <Button type="submit">{t('wizard.next')}</Button>
      </div>
    </form>
  );
}
