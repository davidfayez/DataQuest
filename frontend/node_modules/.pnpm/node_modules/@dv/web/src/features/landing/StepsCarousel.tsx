import { cn } from '@dv/ui';
import { ChevronLeft, ChevronRight } from 'lucide-react';
import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';

/** How long a slide rests before the track advances on its own. */
const AUTOPLAY_MS = 5000;

/**
 * A sliding row of cards.
 *
 * Built on a transform rather than a scroll container so the movement can be eased and the active
 * page is a number this component owns — a scroll-snap carousel has to infer which page it is on
 * from scroll offsets, which drifts on trackpads and disagrees with the dots.
 *
 * What it respects:
 *
 * - `prefers-reduced-motion`, which turns off both the autoplay and the easing. Movement that a
 *   reader cannot stop is the part of a carousel that causes harm, so this is not decoration.
 * - Direction. The page is mirrored for Arabic, so the track translates the other way and the
 *   arrows swap; a "next" button that goes backwards in RTL is worse than none.
 * - Hover and focus, both of which pause the autoplay — a card being read should not slide away,
 *   and neither should one being tabbed through.
 */
export function StepsCarousel({
  items,
  perView,
  label,
}: {
  items: { id: string; node: ReactNode }[];
  /** Cards visible at once on a wide screen. Narrow screens always show one. */
  perView: number;
  /** Names the region for a screen reader, since the cards themselves do not. */
  label: string;
}) {
  const { t, i18n } = useTranslation();
  const rtl = i18n.dir() === 'rtl';

  const [visible, setVisible] = useState(1);
  const [page, setPage] = useState(0);
  const [paused, setPaused] = useState(false);
  const [reducedMotion, setReducedMotion] = useState(false);
  const trackRef = useRef<HTMLDivElement>(null);

  // Matches the grid's breakpoints so the carousel and the grid show the same number of cards at
  // the same widths, rather than each having its own idea of "wide".
  useEffect(() => {
    const sm = window.matchMedia('(min-width: 640px)');
    const lg = window.matchMedia('(min-width: 1024px)');

    const sync = () => setVisible(lg.matches ? perView : sm.matches ? Math.min(2, perView) : 1);

    sync();
    sm.addEventListener('change', sync);
    lg.addEventListener('change', sync);

    return () => {
      sm.removeEventListener('change', sync);
      lg.removeEventListener('change', sync);
    };
  }, [perView]);

  useEffect(() => {
    const query = window.matchMedia('(prefers-reduced-motion: reduce)');
    const sync = () => setReducedMotion(query.matches);

    sync();
    query.addEventListener('change', sync);
    return () => query.removeEventListener('change', sync);
  }, []);

  const pageCount = Math.max(1, Math.ceil(items.length / visible));

  // Shrinking the window can leave the track on a page that no longer exists.
  useEffect(() => {
    setPage((current) => Math.min(current, pageCount - 1));
  }, [pageCount]);

  const go = useCallback(
    (next: number) => setPage(((next % pageCount) + pageCount) % pageCount),
    [pageCount],
  );

  useEffect(() => {
    if (paused || reducedMotion || pageCount < 2) return;

    const timer = window.setInterval(() => setPage((c) => (c + 1) % pageCount), AUTOPLAY_MS);
    return () => window.clearInterval(timer);
  }, [paused, reducedMotion, pageCount]);

  // One page's worth of movement, mirrored for RTL.
  const offset = page * 100 * (rtl ? 1 : -1);

  return (
    <div
      className="relative"
      role="region"
      aria-roledescription="carousel"
      aria-label={label}
      onMouseEnter={() => setPaused(true)}
      onMouseLeave={() => setPaused(false)}
      onFocusCapture={() => setPaused(true)}
      onBlurCapture={() => setPaused(false)}
      data-testid="steps-carousel"
    >
      <div className="overflow-hidden">
        <div
          ref={trackRef}
          className={cn('flex', !reducedMotion && 'transition-transform duration-500 ease-out')}
          style={{ transform: `translateX(${offset}%)` }}
        >
          {items.map((item) => (
            <div
              key={item.id}
              // Padding rather than a gap: a gap would be counted in the 100% the track moves by,
              // so the pages would drift further out of alignment with every step.
              className="shrink-0 px-2.5"
              style={{ width: `${100 / visible}%` }}
            >
              {item.node}
            </div>
          ))}
        </div>
      </div>

      {pageCount > 1 && (
        <div className="mt-8 flex items-center justify-center gap-4">
          <button
            type="button"
            onClick={() => go(page - 1)}
            aria-label={t('common.previous')}
            className="flex size-10 items-center justify-center rounded-full bg-white text-ink-500 shadow-soft ring-1 ring-ink-100 transition-all hover:-translate-y-0.5 hover:text-ink-950 hover:shadow-lift focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-600"
            data-testid="carousel-prev"
          >
            {rtl ? <ChevronRight className="size-5" /> : <ChevronLeft className="size-5" />}
          </button>

          <div className="flex items-center gap-2">
            {Array.from({ length: pageCount }, (_, index) => (
              <button
                key={index}
                type="button"
                onClick={() => go(index)}
                aria-label={t('landing.goToSlide', { n: index + 1 })}
                aria-current={index === page}
                className={cn(
                  'h-2 rounded-full transition-all duration-300 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-600',
                  index === page ? 'w-6 bg-brand-600' : 'w-2 bg-ink-200 hover:bg-ink-300',
                )}
                data-testid={`carousel-dot-${index}`}
              />
            ))}
          </div>

          <button
            type="button"
            onClick={() => go(page + 1)}
            aria-label={t('common.next')}
            className="flex size-10 items-center justify-center rounded-full bg-white text-ink-500 shadow-soft ring-1 ring-ink-100 transition-all hover:-translate-y-0.5 hover:text-ink-950 hover:shadow-lift focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-600"
            data-testid="carousel-next"
          >
            {rtl ? <ChevronLeft className="size-5" /> : <ChevronRight className="size-5" />}
          </button>
        </div>
      )}
    </div>
  );
}
