import { lazy, Suspense } from 'react';
import { useCoverageContent } from './api';
import { flagUrl } from '@/shared/lib/phoneCountries';

/**
 * Leaflet and its stylesheet are worth their weight for a map you can pan, but not in the chunk
 * that renders the hero. They arrive while the reader is still above this section.
 */
const CoverageMap = lazy(() =>
  import('./CoverageMap').then((module) => ({ default: module.CoverageMap })),
);

/**
 * "Where we verify": the heading, the map, and the countries marked on it.
 *
 * Heading and countries are both admin-managed, and the whole section — heading included — is
 * absent when no country is published. A map of the world with nothing marked on it is a claim
 * about coverage that reads as "none".
 *
 * The country list is not a caption for the map, it is the accessible form of it: the map itself
 * is `aria-hidden` decoration, and this is what carries the same information as text.
 */
export function CoverageSection() {
  const content = useCoverageContent();

  const countries = content.data?.countries ?? [];
  const title = content.data?.title ?? '';
  const subtitle = content.data?.subtitle ?? '';

  if (countries.length === 0) {
    return null;
  }

  return (
    <section id="coverage" className="scroll-mt-24 bg-white py-16 sm:py-20">
      <div className="mx-auto max-w-6xl px-4 sm:px-6">
        <div className="mx-auto max-w-3xl text-center">
          {title && (
            <h2 className="font-display text-2xl font-semibold tracking-tight text-ink-950 sm:text-3xl">
              {title}
            </h2>
          )}
          {subtitle && <p className="mt-3 text-base leading-relaxed text-ink-600">{subtitle}</p>}
        </div>

        {/* Full width: the countries this platform covers sit close together, and a narrower map
            crowds their markers whatever the zoom. */}
        <div className="mt-10 overflow-hidden rounded-3xl ring-1 ring-ink-100">
          {/* The reserved box keeps the list below from jumping when the map lands. */}
          <Suspense fallback={<div className="h-[380px] w-full bg-cream/60 sm:h-[460px]" />}>
            <CoverageMap countries={countries} />
          </Suspense>
        </div>

        {/* Not a caption for the map: the map is something you drive, and this is the same
            information as plain text, in an order a reader can follow without one. */}
        <ul className="mt-8 flex flex-wrap justify-center gap-2.5">
          {countries.map((country) => (
            <li
              key={country.id}
              className="flex items-center gap-2.5 rounded-full bg-white py-2 ps-2.5 pe-4 shadow-soft ring-1 ring-ink-100"
            >
              <img
                src={flagUrl(country.code)}
                srcSet={`${flagUrl(country.code, 40)} 2x`}
                alt=""
                width={20}
                height={15}
                loading="lazy"
                className="h-[15px] w-5 shrink-0 rounded-[2px] object-cover ring-1 ring-ink-100"
              />
              <span className="text-sm font-medium text-ink-800">{country.name}</span>
            </li>
          ))}
        </ul>
      </div>
    </section>
  );
}
