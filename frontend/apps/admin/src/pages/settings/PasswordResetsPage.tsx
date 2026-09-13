import {
  AdminPageHeader,
  Alert,
  Button,
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
  Dialog,
  Field,
  Input,
  Spinner,
  cn,
} from '@dv/ui';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { adminSession, Permissions } from '@/features/auth/session';
import {
  usePasswordResetLog,
  usePasswordResetValidity,
  useSavePasswordResetValidity,
  type PasswordResetListParams,
  type PasswordResetLogEntry,
} from '@/features/settings/api';
import { useApiErrorMessage } from '@/shared/lib/useApiError';
import { DataTable, type Column } from '@/shared/ui/DataTable';

/** How each outcome is coloured. Anything unexpected falls back to neutral rather than crashing. */
const OUTCOME_TONE: Record<string, string> = {
  Used: 'bg-brand-50 text-brand-700',
  Pending: 'bg-amber-50 text-amber-700',
  Expired: 'bg-ink-100 text-ink-500',
  Superseded: 'bg-ink-100 text-ink-500',
  UnknownOrder: 'bg-red-50 text-red-700',
};

/**
 * Who asked for a new password, from where, and what became of it.
 *
 * Every request is here, including those against order numbers that do not exist — a run of those
 * is the pattern most worth seeing, and it is invisible in a log of successes.
 *
 * The table stays readable at a glance; everything the lookup returned lives one click away in the
 * details dialog, because thirty columns is not a table anybody reads.
 */
export function PasswordResetsPage() {
  const { t } = useTranslation();
  const [params, setParams] = useState<PasswordResetListParams>({ page: 1, pageSize: 25 });
  const [selected, setSelected] = useState<PasswordResetLogEntry | null>(null);
  const log = usePasswordResetLog(params);

  const columns: Column<PasswordResetLogEntry>[] = [
    {
      key: 'requestedAtUtc',
      header: t('passwordResets.when'),
      getValue: (row) => row.requestedAtUtc,
      render: (row) => (
        <span className="whitespace-nowrap text-xs" dir="ltr">
          {new Date(row.requestedAtUtc).toLocaleString()}
        </span>
      ),
    },
    {
      key: 'orderNumber',
      header: t('passwordResets.order'),
      getValue: (row) => row.orderNumber,
      render: (row) => (
        <div>
          <span className="font-mono text-xs font-semibold">{row.orderNumber}</span>
          {row.maskedEmail && (
            <span className="block text-xs text-ink-400" dir="ltr">
              {row.maskedEmail}
            </span>
          )}
        </div>
      ),
    },
    {
      key: 'where',
      header: t('passwordResets.where'),
      sortable: false,
      getValue: (row) => row.ipAddress ?? '',
      render: (row) => (
        <div className="text-xs">
          <span className="block font-mono" dir="ltr">
            {row.ipAddress ?? '—'}
          </span>
          <span className="block text-ink-400">
            {[row.city, row.regionName, row.country].filter(Boolean).join(', ')
              || t('passwordResets.unknownPlace')}
          </span>
        </div>
      ),
    },
    {
      key: 'network',
      header: t('passwordResets.network'),
      sortable: false,
      getValue: (row) => row.isp ?? '',
      render: (row) => (
        <div className="text-xs" title={row.reverseDns ?? undefined}>
          <span className="block">{row.isp ?? '—'}</span>
          <span className="flex flex-wrap gap-1 pt-0.5">
            {/* The flags an operator scans for first: a reset from a VPN or a data centre is not
                the same event as one from a home line. */}
            {row.isProxy && <Flag tone="red" label={t('passwordResets.flags.proxy')} />}
            {row.isHosting && <Flag tone="amber" label={t('passwordResets.flags.hosting')} />}
            {row.isMobileNetwork && <Flag tone="ink" label={t('passwordResets.flags.mobile')} />}
          </span>
        </div>
      ),
    },
    {
      key: 'device',
      header: t('passwordResets.device'),
      sortable: false,
      getValue: (row) => row.browser ?? '',
      render: (row) => (
        // The full user agent is the title, so the summary stays short and nothing is lost.
        <div className="text-xs" title={row.userAgent ?? undefined}>
          <span className="block">{row.browser ?? '—'}</span>
          <span className="block text-ink-400">
            {[row.operatingSystem, row.deviceType].filter(Boolean).join(' · ') || '—'}
          </span>
        </div>
      ),
    },
    {
      key: 'outcome',
      header: t('passwordResets.outcome'),
      align: 'center',
      getValue: (row) => row.outcome,
      render: (row) => (
        <span
          className={cn(
            'inline-block rounded-md px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide',
            OUTCOME_TONE[row.outcome] ?? 'bg-ink-100 text-ink-500',
          )}
          data-testid={`outcome-${row.orderNumber}`}
        >
          {t(`passwordResets.outcomes.${row.outcome}`, { defaultValue: row.outcome })}
        </span>
      ),
    },
    {
      key: 'validity',
      header: t('passwordResets.validity'),
      align: 'center',
      getValue: (row) => row.validityMinutes,
      render: (row) => (
        <span className="whitespace-nowrap text-xs text-ink-500">
          {t('passwordResets.minutes', { count: row.validityMinutes })}
        </span>
      ),
    },
    {
      key: 'details',
      header: '',
      sortable: false,
      align: 'end',
      render: (row) => (
        <Button
          variant="ghost"
          size="sm"
          onClick={() => setSelected(row)}
          data-testid={`details-${row.orderNumber}`}
        >
          {t('passwordResets.details')}
        </Button>
      ),
    },
  ];

  return (
    <div className="animate-fade-in space-y-6">
      <AdminPageHeader
        title={t('passwordResets.title')}
        subtitle={t('passwordResets.subtitle')}
      />

      <ValidityCard />

      <DataTable
        data={log.data}
        isPending={log.isPending}
        rowKey={(row) => row.id}
        onSearch={(search) => setParams((p) => ({ ...p, search, page: 1 }))}
        onPageChange={(page) => setParams((p) => ({ ...p, page }))}
        onPageSizeChange={(pageSize) => setParams((p) => ({ ...p, pageSize, page: 1 }))}
        // Back to the first page: the row that sorts first belongs on page one.
        onSortChange={(sort) =>
          setParams((p) => ({
            ...p,
            sortBy: sort?.key,
            sortDescending: sort?.descending,
            page: 1,
          }))
        }
        columns={columns}
        toolbar={
          <label className="flex items-center gap-2 text-sm">
            <input
              type="checkbox"
              className="size-4 rounded border-border"
              checked={params.onlyUnknownOrders ?? false}
              onChange={(event) =>
                setParams((p) => ({
                  ...p,
                  onlyUnknownOrders: event.target.checked || undefined,
                  page: 1,
                }))
              }
              data-testid="only-unknown"
            />
            {t('passwordResets.onlyUnknown')}
          </label>
        }
      />

      <DetailsDialog entry={selected} onClose={() => setSelected(null)} />
    </div>
  );
}

/** A small coloured chip for the network flags. */
function Flag({ label, tone }: { label: string; tone: 'red' | 'amber' | 'ink' }) {
  const tones = {
    red: 'bg-red-50 text-red-700',
    amber: 'bg-amber-50 text-amber-700',
    ink: 'bg-ink-100 text-ink-500',
  } as const;

  return (
    <span className={cn('rounded px-1.5 py-0.5 text-[10px] font-semibold', tones[tone])}>
      {label}
    </span>
  );
}

/** Everything the request and the lookup revealed, grouped the way it would be read. */
function DetailsDialog({
  entry,
  onClose,
}: {
  entry: PasswordResetLogEntry | null;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const [copied, setCopied] = useState(false);

  useEffect(() => setCopied(false), [entry]);

  if (!entry) return null;

  const f = (key: string) => t(`passwordResets.fields.${key}`);
  const time = (value: string | null) => (value ? new Date(value).toLocaleString() : null);
  const bool = (value: boolean | null) =>
    value === null ? null : value ? t('passwordResets.yes') : t('passwordResets.no');

  const hasLocation = entry.latitude !== null && entry.longitude !== null;
  const coordinates = hasLocation ? `${entry.latitude}, ${entry.longitude}` : null;

  const sections: { title: string; rows: [string, string | null][] }[] = [
    {
      title: t('passwordResets.sections.request'),
      rows: [
        [f('requestedAt'), time(entry.requestedAtUtc)],
        [f('orderNumber'), entry.orderNumber],
        [f('maskedEmail'), entry.maskedEmail],
        [f('acceptLanguage'), entry.acceptLanguage],
        [f('languageCode'), entry.languageCode],
      ],
    },
    {
      title: t('passwordResets.sections.network'),
      rows: [
        [f('ipAddress'), entry.ipAddress],
        [f('reverseDns'), entry.reverseDns],
        [f('isp'), entry.isp],
        [f('organisation'), entry.organisation],
        [f('autonomousSystem'), entry.autonomousSystem],
        [f('proxy'), bool(entry.isProxy)],
        [f('hosting'), bool(entry.isHosting)],
        [f('mobileNetwork'), bool(entry.isMobileNetwork)],
      ],
    },
    {
      title: t('passwordResets.sections.location'),
      rows: [
        [f('country'), entry.country],
        [f('countryCode'), entry.countryCode],
        [f('region'), [entry.regionName, entry.region].filter(Boolean).join(' · ') || null],
        [f('city'), entry.city],
        [f('district'), entry.district],
        [f('postalCode'), entry.postalCode],
        [f('continent'), [entry.continent, entry.continentCode].filter(Boolean).join(' · ') || null],
        [f('coordinates'), coordinates],
        [f('timeZone'), entry.timeZone],
        [
          f('utcOffset'),
          entry.utcOffsetSeconds === null ? null : formatOffset(entry.utcOffsetSeconds),
        ],
        [f('currency'), entry.currency],
      ],
    },
    {
      title: t('passwordResets.sections.device'),
      rows: [
        [f('browser'), entry.browser],
        [f('operatingSystem'), entry.operatingSystem],
        [f('deviceType'), entry.deviceType],
        [f('userAgent'), entry.userAgent],
      ],
    },
    {
      title: t('passwordResets.sections.outcome'),
      rows: [
        [
          f('outcome'),
          t(`passwordResets.outcomes.${entry.outcome}`, { defaultValue: entry.outcome }),
        ],
        [f('validity'), t('passwordResets.minutes', { count: entry.validityMinutes })],
        [f('expiresAt'), time(entry.expiresAtUtc)],
        [f('usedAt'), time(entry.usedAtUtc)],
      ],
    },
  ];

  // A plain-text copy is what actually gets pasted into a ticket or an email to an ISP.
  const copyAll = async () => {
    const text = sections
      .map(
        (section) =>
          `${section.title}\n`
          + section.rows.map(([label, value]) => `  ${label}: ${value ?? '—'}`).join('\n'),
      )
      .join('\n\n');

    try {
      await navigator.clipboard.writeText(text);
      setCopied(true);
    } catch {
      // A browser that refuses clipboard access is not worth an error dialog; the values are all
      // on screen to be selected by hand.
      setCopied(false);
    }
  };

  return (
    <Dialog
      open={entry !== null}
      onClose={onClose}
      title={t('passwordResets.detailsTitle')}
      className="max-w-3xl"
      footer={
        <div className="flex items-center justify-end gap-2">
          <Button variant="ghost" onClick={copyAll} data-testid="copy-details">
            {copied ? t('passwordResets.copied') : t('passwordResets.copyAll')}
          </Button>
          <Button onClick={onClose}>{t('passwordResets.close')}</Button>
        </div>
      }
    >
      <div className="space-y-5" data-testid="reset-details">
        {!hasLocation && !entry.country && (
          <Alert variant="info">{t('passwordResets.noLocation')}</Alert>
        )}

        {sections.map((section) => (
          <section key={section.title}>
            <h3 className="pb-1 text-xs font-bold uppercase tracking-wide text-ink-400">
              {section.title}
            </h3>
            <dl className="divide-y divide-border/60 border-t border-border/60">
              {section.rows.map(([label, value]) => (
                <div key={label} className="grid grid-cols-3 gap-3 py-1.5">
                  <dt className="text-xs text-ink-500">{label}</dt>
                  <dd
                    className={cn(
                      'col-span-2 break-words text-xs',
                      value ? 'text-ink-800' : 'text-ink-300',
                    )}
                    dir={value && /^[\x20-\x7E]*$/.test(value) ? 'ltr' : undefined}
                  >
                    {value ?? '—'}
                  </dd>
                </div>
              ))}
            </dl>
          </section>
        ))}

        {hasLocation && (
          <a
            className="inline-block text-xs font-semibold text-brand-600 hover:underline"
            href={`https://www.openstreetmap.org/?mlat=${entry.latitude}&mlon=${entry.longitude}#map=10/${entry.latitude}/${entry.longitude}`}
            target="_blank"
            rel="noreferrer noopener"
          >
            {t('passwordResets.viewOnMap')}
          </a>
        )}
      </div>
    </Dialog>
  );
}

/** Seconds from UTC as `UTC+05:00`, which is how anyone reading a log expects to see it. */
function formatOffset(seconds: number): string {
  const sign = seconds < 0 ? '-' : '+';
  const total = Math.abs(seconds);
  const hours = String(Math.floor(total / 3600)).padStart(2, '0');
  const minutes = String(Math.floor((total % 3600) / 60)).padStart(2, '0');

  return `UTC${sign}${hours}:${minutes}`;
}

/** The window an emailed password is good for. */
function ValidityCard() {
  const { t } = useTranslation();
  const settings = usePasswordResetValidity();
  const save = useSavePasswordResetValidity();
  const toMessage = useApiErrorMessage();

  const canUpdate = adminSession.has(Permissions.SettingsUpdate);

  const [minutes, setMinutes] = useState('');
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    if (settings.data) setMinutes(String(settings.data.validityMinutes));
  }, [settings.data]);

  if (settings.isPending) return null;

  if (settings.isError || !settings.data) {
    return <Alert variant="error">{t('errors.genericTitle')}</Alert>;
  }

  const value = Number(minutes);
  const valid =
    Number.isInteger(value)
    && value >= settings.data.minMinutes
    && value <= settings.data.maxMinutes;

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t('passwordResets.validityTitle')}</CardTitle>
        <CardDescription>{t('passwordResets.validityHint')}</CardDescription>
      </CardHeader>

      <CardContent className="space-y-4">
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t('passwordResets.validityLabel')} htmlFor="reset-validity" required>
            <Input
              id="reset-validity"
              type="number"
              dir="ltr"
              min={settings.data.minMinutes}
              max={settings.data.maxMinutes}
              value={minutes}
              onChange={(event) => {
                setMinutes(event.target.value);
                setSaved(false);
              }}
              disabled={!canUpdate}
              data-testid="reset-validity"
            />
          </Field>
        </div>

        {!valid && (
          <Alert variant="warning">
            {t('passwordResets.validityRange', {
              min: settings.data.minMinutes,
              max: settings.data.maxMinutes,
            })}
          </Alert>
        )}

        {save.isError && <Alert variant="error">{toMessage(save.error)}</Alert>}
        {saved && <Alert variant="success">{t('passwordResets.validitySaved')}</Alert>}

        {canUpdate && (
          <div className="flex justify-end">
            <Button
              onClick={() =>
                save.mutate(value, { onSuccess: () => setSaved(true) })
              }
              disabled={!valid || save.isPending}
              data-testid="save-validity"
            >
              {save.isPending && <Spinner />}
              {t('common.save')}
            </Button>
          </div>
        )}
      </CardContent>
    </Card>
  );
}
