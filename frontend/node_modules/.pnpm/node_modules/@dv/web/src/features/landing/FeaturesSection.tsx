import { Languages, MessagesSquare, Receipt, ShieldCheck } from 'lucide-react';
import type { ComponentType } from 'react';
import { useTranslation } from 'react-i18next';
import { useLandingContent } from './api';
import { LANDING_ICONS } from './landingIcons';
import { SectionHeading } from './SectionHeading';

// The key-to-glyph map is shared with the hero's statistics strip, so both draw an
// administrator's choice the same way.

interface FeatureItem {
  icon: ComponentType<{ className?: string }>;
  title: string;
  body: string;
}

export function FeaturesSection() {
  const { t } = useTranslation();
  const content = useLandingContent();

  // Built-in translated copy, used until the admin publishes cards (or if the API is unreachable).
  const fallbackEyebrow = t('landing.features.eyebrow');
  const fallbackTitle = t('landing.features.title');
  const fallbackItems: FeatureItem[] = [
    { icon: MessagesSquare, title: t('landing.features.timelineTitle'), body: t('landing.features.timelineBody') },
    { icon: Receipt, title: t('landing.features.walletTitle'), body: t('landing.features.walletBody') },
    { icon: Languages, title: t('landing.features.i18nTitle'), body: t('landing.features.i18nBody') },
    { icon: ShieldCheck, title: t('landing.features.securityTitle'), body: t('landing.features.securityBody') },
  ];

  const managed = content.data;
  const hasManaged = Boolean(managed && managed.features.length > 0);

  const eyebrow = hasManaged ? managed!.eyebrow : fallbackEyebrow;
  const title = hasManaged ? managed!.title : fallbackTitle;
  const items: FeatureItem[] = hasManaged
    ? managed!.features.map((feature) => ({
        icon: LANDING_ICONS[feature.icon] ?? ShieldCheck,
        title: feature.title,
        body: feature.body,
      }))
    : fallbackItems;

  return (
    <section id="features" className="mx-auto max-w-6xl scroll-mt-24 px-4 py-24 sm:px-6">
      <SectionHeading eyebrow={eyebrow} title={title} />
      <div className="mt-14 grid gap-5 sm:grid-cols-2">
        {items.map((item, index) => (
          <div
            key={`${item.title}-${index}`}
            className="flex gap-5 rounded-2xl bg-white p-6 shadow-soft ring-1 ring-ink-100 transition-all duration-300 hover:shadow-lift"
          >
            <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-ink-950 text-brand-400">
              <item.icon className="size-5" aria-hidden />
            </div>
            <div>
              <h3 className="font-semibold text-ink-950">{item.title}</h3>
              <p className="mt-1.5 text-sm leading-relaxed text-ink-500">{item.body}</p>
            </div>
          </div>
        ))}
      </div>
    </section>
  );
}
