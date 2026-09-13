import { cn } from '@dv/ui';
import { Building2 } from 'lucide-react';
import { useLandingContent } from './api';
import { landingIcon } from './landingIcons';

/**
 * The "trusted for verification with" band, configured in the admin panel.
 *
 * Built as a proper section rather than the caption-sized row it was: this is the page's social
 * proof, and at 12px grey text between two full sections it read as a legal footnote. It now
 * carries the same heading scale and card treatment as the sections either side of it, so the
 * names are legible at a glance instead of squinted at.
 *
 * The heading and the names are both editable, and the whole section — heading included — is
 * absent when nothing is published: a lone heading above empty space says less than nothing.
 *
 * Nothing renders until the content has loaded, rather than a heading over a row that fills in a
 * moment later and pushes the page down.
 */
export function TrustStrip() {
  const content = useLandingContent();

  const title = content.data?.trustTitle ?? '';
  const entries = content.data?.trustedBy ?? [];

  if (entries.length === 0) {
    return null;
  }

  return (
    <section className="border-y border-ink-100 bg-cream/50 py-16 sm:py-20">
      <div className="mx-auto max-w-6xl px-4 sm:px-6">
        {title && (
          <h2 className="mx-auto max-w-3xl text-center font-display text-2xl font-semibold tracking-tight text-ink-950 sm:text-3xl">
            {title}
          </h2>
        )}

        {/* Columns follow the count, but never past three: a name like "Ministry of Higher
            Education" wraps to three lines in a fifth of this container, and a row of five
            cramped cards reads worse than two tidy rows of three. */}
        <ul
          className={cn(
            'mt-10 grid gap-4 sm:grid-cols-2 lg:gap-5',
            entries.length === 4 ? 'lg:grid-cols-4' : entries.length >= 3 && 'lg:grid-cols-3',
          )}
          data-testid="trust-strip"
        >
          {entries.map((entry) => {
            // The building mark is what every entry drew before icons were editable, so it stays
            // the default rather than leaving an operator's untouched row without one.
            const Icon = landingIcon(entry.icon) ?? Building2;

            return (
              <li
                key={entry.id}
                className="flex items-center gap-4 rounded-2xl bg-white p-5 shadow-soft ring-1 ring-ink-100 transition-all duration-300 hover:shadow-lift"
              >
                <span className="flex size-11 shrink-0 items-center justify-center rounded-xl bg-ink-950 text-brand-400">
                  <Icon className="size-5" aria-hidden />
                </span>
                <span className="min-w-0 font-semibold leading-snug text-ink-950 sm:text-lg">
                  {entry.name}
                </span>
              </li>
            );
          })}
        </ul>
      </div>
    </section>
  );
}
