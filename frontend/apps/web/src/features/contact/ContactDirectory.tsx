import { cn } from '@dv/ui';
import { Building2, Mail, MapPin, Phone } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useContactDirectory, type ContactDirectoryEntryDto } from '@/entities/ticket/api';
import { flagUrl } from '@/shared/lib/phoneCountries';

/**
 * The organisation's own contact points, beside the form.
 *
 * Sits in a column of its own rather than under the form: someone who already knows their agent
 * should not have to scroll past a form they are not going to fill in. Everything here is
 * administrator-managed, so a section with nothing configured renders nothing at all — an empty
 * "Authorized agents" heading tells a visitor less than no heading.
 */
export function ContactDirectory() {
  const { t } = useTranslation();
  const directory = useContactDirectory();

  const agents = directory.data?.authorizedAgents ?? [];
  const administration = directory.data?.administration ?? [];

  if (directory.isPending || (agents.length === 0 && administration.length === 0)) {
    return null;
  }

  return (
    <div className="space-y-4">
      {administration.length > 0 && (
        <section className="rounded-2xl border border-border bg-card p-5 shadow-sm">
          <SectionHeading
            icon={<Building2 className="size-4" aria-hidden="true" />}
            label={t('contact.administration')}
          />

          <ul className="mt-4 space-y-5">
            {administration.map((entry) => (
              <li key={entry.id} className="space-y-2">
                {entry.title && <p className="text-sm font-semibold">{entry.title}</p>}
                <Details entry={entry} />
              </li>
            ))}
          </ul>
        </section>
      )}

      {agents.length > 0 && (
        <section className="rounded-2xl border border-border bg-card p-5 shadow-sm">
          <SectionHeading label={t('contact.authorizedAgents')} />

          <ul className="mt-3 divide-y divide-border/70">
            {agents.map((entry) => (
              <li key={entry.id} className="py-3 first:pt-1 last:pb-0">
                <div className="flex items-center gap-2.5">
                  {/* Decorative — the country name beside it carries the meaning. */}
                  {entry.countryCode && (
                    <img
                      src={flagUrl(entry.countryCode)}
                      srcSet={`${flagUrl(entry.countryCode, 40)} 2x`}
                      width={22}
                      height={16}
                      alt=""
                      aria-hidden
                      loading="lazy"
                      className="h-4 w-[22px] shrink-0 rounded-[2px] object-cover ring-1 ring-border"
                      onError={(event) => {
                        event.currentTarget.style.visibility = 'hidden';
                      }}
                    />
                  )}
                  <p className="min-w-0 flex-1 truncate text-sm font-semibold">
                    {entry.countryName ?? entry.title}
                  </p>
                </div>

                {entry.countryName && entry.title && (
                  <p className="mt-1 ps-[30px] text-xs text-muted-foreground">{entry.title}</p>
                )}

                {/* Indented to line up under the country name rather than under its flag. */}
                <div className="mt-1.5 ps-[30px]">
                  <Details entry={entry} compact />
                </div>
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  );
}

function SectionHeading({ icon, label }: { icon?: React.ReactNode; label: string }) {
  return (
    <h2 className="flex items-center gap-2 text-xs font-semibold tracking-wider text-muted-foreground uppercase">
      {icon && (
        <span className="flex size-7 items-center justify-center rounded-lg bg-primary/10 text-primary">
          {icon}
        </span>
      )}
      {label}
    </h2>
  );
}

/**
 * The reachable parts of an entry.
 *
 * Each is a link rather than text: on a phone, the difference between a number you can read and a
 * number you can call is the whole point of listing it.
 */
function Details({ entry, compact }: { entry: ContactDirectoryEntryDto; compact?: boolean }) {
  const row = 'flex items-start gap-2 text-sm';
  const iconClass = 'mt-0.5 size-3.5 shrink-0 text-muted-foreground';

  return (
    <div className={cn('space-y-1.5', compact && 'space-y-1')}>
      {entry.address && (
        <p className={cn(row, 'text-muted-foreground')}>
          <MapPin className={iconClass} aria-hidden="true" />
          <span className="leading-6">{entry.address}</span>
        </p>
      )}

      {entry.phone && (
        <p className={row}>
          <Phone className={iconClass} aria-hidden="true" />
          <a
            href={`tel:${entry.phone.replace(/[^\d+]/g, '')}`}
            className="font-mono text-[13px] tracking-tight transition-colors hover:text-primary hover:underline"
            dir="ltr"
          >
            {entry.phone}
          </a>
        </p>
      )}

      {entry.email && (
        <p className={row}>
          <Mail className={iconClass} aria-hidden="true" />
          <a
            href={`mailto:${entry.email}`}
            className="break-all transition-colors hover:text-primary hover:underline"
            dir="ltr"
          >
            {entry.email}
          </a>
        </p>
      )}
    </div>
  );
}
