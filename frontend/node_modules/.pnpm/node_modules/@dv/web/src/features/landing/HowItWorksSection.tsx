import { cn } from '@dv/ui';
import { HelpCircle } from 'lucide-react';
import { useLandingContent, type LandingStep } from './api';
import { landingIcon } from './landingIcons';
import { SectionHeading } from './SectionHeading';
import { StepsCarousel } from './StepsCarousel';

/**
 * Static class names per column count, because Tailwind resolves classes at build time and cannot
 * see a string assembled at runtime. Four is the ceiling: past that the body text under each card
 * has no room left.
 */
const COLUMNS: Record<number, string> = {
  1: 'lg:grid-cols-1',
  2: 'lg:grid-cols-2',
  3: 'lg:grid-cols-3',
  4: 'lg:grid-cols-4',
};

/**
 * The "how it works" steps, configured in the admin panel.
 *
 * The number on each card comes from its position, so reordering the steps renumbers them and an
 * operator never has to keep a "3." written into the copy in step with where the card sits.
 *
 * The whole section — heading included — is absent when nothing is published, and nothing renders
 * until the content has loaded rather than a heading over cards that fill in a moment later.
 */
export function HowItWorksSection() {
  const content = useLandingContent();

  const steps = content.data?.steps ?? [];
  if (steps.length === 0) {
    return null;
  }

  const columns = COLUMNS[content.data?.howColumns ?? 3] ?? COLUMNS[3];
  const carousel = content.data?.howLayout === 'Carousel';

  const cards = steps.map((step, index) => ({
    id: step.id,
    node: <StepCard key={step.id} step={step} index={index} />,
  }));

  return (
    <section id="how" className="mx-auto max-w-6xl scroll-mt-24 px-4 py-24 sm:px-6">
      <SectionHeading
        eyebrow={content.data?.howEyebrow ?? ''}
        title={content.data?.howTitle ?? ''}
      />

      <div className="mt-14">
        {carousel ? (
          // Negative margin cancels the per-slide padding, so the first card lines up with the
          // heading above it rather than sitting half a gutter inside.
          <div className="-mx-2.5">
            <StepsCarousel
              items={cards}
              perView={content.data?.howColumns ?? 3}
              label={content.data?.howTitle ?? ''}
            />
          </div>
        ) : (
          <div className={cn('grid gap-5 sm:grid-cols-2', columns)} data-testid="steps-grid">
            {cards.map((card) => (
              <div key={card.id}>{card.node}</div>
            ))}
          </div>
        )}
      </div>
    </section>
  );
}

/** One step, drawn the same way in either layout so switching changes the arrangement only. */
function StepCard({ step, index }: { step: LandingStep; index: number }) {
  // An unrecognised key draws a neutral mark rather than nothing, so a card is never a number
  // floating above text with an empty tile beside it.
  const Icon = landingIcon(step.icon) ?? HelpCircle;

  return (
    <div className="group relative h-full rounded-2xl bg-white p-6 shadow-soft ring-1 ring-ink-100 transition-all duration-300 hover:-translate-y-1 hover:shadow-lift">
      <div className="flex items-center justify-between">
        <div className="flex h-11 w-11 items-center justify-center rounded-xl bg-brand-50 text-brand-700 ring-1 ring-brand-100 transition-colors group-hover:bg-brand-600 group-hover:text-white">
          <Icon className="size-[21px]" aria-hidden />
        </div>
        <span className="font-display text-4xl font-light text-ink-100 transition-colors group-hover:text-brand-100">
          {String(index + 1).padStart(2, '0')}
        </span>
      </div>
      <h3 className="mt-5 font-semibold text-ink-950">{step.title}</h3>
      <p className="mt-2 text-sm leading-relaxed text-ink-500">{step.body}</p>
    </div>
  );
}
