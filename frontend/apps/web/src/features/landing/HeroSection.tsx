import { buttonVariants, cn } from '@dv/ui';
import { ArrowRight, MailCheck, Sparkles } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router-dom';
import { useLandingContent } from './api';
import { landingIcon } from './landingIcons';

export function HeroSection() {
  const { t, i18n } = useTranslation();
  const { lang = 'en' } = useParams<{ lang: string }>();
  const rtl = i18n.dir() === 'rtl';

  // Configured in the admin panel. While the request is in flight, and if it fails, the strip is
  // simply absent rather than briefly showing figures that are about to be replaced — a marketing
  // number that flickers from one value to another reads as a page that cannot be trusted.
  const content = useLandingContent();
  const stats = content.data?.stats ?? [];

  return (
    <section className="relative overflow-hidden">
      <div className="pointer-events-none absolute inset-0" aria-hidden>
        <div className="absolute -top-32 start-1/2 h-[540px] w-[900px] -translate-x-1/2 rounded-full bg-gradient-to-b from-brand-100/70 via-brand-50/40 to-transparent blur-3xl rtl:translate-x-1/2" />
        <div
          className="absolute inset-0 opacity-[0.35]"
          style={{
            backgroundImage:
              'linear-gradient(to right, rgb(28 35 45 / 0.045) 1px, transparent 1px), linear-gradient(to bottom, rgb(28 35 45 / 0.045) 1px, transparent 1px)',
            backgroundSize: '56px 56px',
            maskImage: 'radial-gradient(ellipse 80% 60% at 50% 0%, black 40%, transparent 100%)',
          }}
        />
      </div>

      <div className="relative mx-auto max-w-6xl px-4 pb-20 pt-16 sm:px-6 sm:pt-24">
        <div className="stagger mx-auto max-w-3xl text-center">
          <span className="inline-flex items-center gap-2 rounded-full bg-white px-4 py-1.5 text-xs font-semibold text-brand-800 shadow-soft ring-1 ring-brand-200">
            <Sparkles className="size-3.5 text-brand-600" aria-hidden />
            {t('landing.hero.eyebrow')}
          </span>
          <h1 className="mt-6 font-display text-4xl font-semibold leading-[1.08] tracking-tight text-ink-950 sm:text-6xl">
            {t('landing.hero.title1')}
            <br />
            <span className="bg-gradient-to-r from-brand-700 via-brand-600 to-teal-600 bg-clip-text italic text-transparent">
              {t('landing.hero.title2')}
            </span>
          </h1>
          <p className="mx-auto mt-6 max-w-2xl text-base leading-relaxed text-ink-500 sm:text-lg">
            {t('landing.subtitle')}
          </p>
          <div className="mt-9 flex flex-col items-center justify-center gap-3 sm:flex-row">
            <Link to={`/${lang}/register`} className={cn(buttonVariants({ size: 'lg' }), 'group gap-2')}>
              <MailCheck className="size-5" aria-hidden />
              {t('landing.primaryCta')}
              <ArrowRight
                className={cn(
                  'size-4 transition-transform group-hover:translate-x-0.5',
                  rtl && 'rotate-180 group-hover:-translate-x-0.5',
                )}
              />
            </Link>
            <Link to={`/${lang}/login`} className={buttonVariants({ variant: 'secondary', size: 'lg' })}>
              {t('landing.secondaryCta')}
            </Link>
          </div>
        </div>

        {/* The columns follow the count rather than assuming three, so an administrator who
            publishes two or four gets an even strip instead of a gap or a wrapped row. */}
        {stats.length > 0 && (
          <div
            className={cn(
              'mx-auto mt-16 grid max-w-3xl divide-x divide-ink-200/70 rounded-2xl bg-white/70 shadow-soft ring-1 ring-ink-100 backdrop-blur rtl:divide-x-reverse',
              stats.length === 1 && 'grid-cols-1',
              stats.length === 2 && 'grid-cols-2',
              stats.length === 3 && 'grid-cols-3',
              stats.length >= 4 && 'grid-cols-2 sm:grid-cols-4',
            )}
            data-testid="landing-stats"
          >
            {stats.map((stat) => {
              const Icon = landingIcon(stat.icon);

              return (
                <div key={stat.id} className="px-3 py-6 text-center sm:px-6">
                  {/* Optional, and it takes no space when absent — a strip of bare numbers stays
                      as tight as it was before icons existed. */}
                  {Icon && (
                    <span className="mx-auto mb-2 flex size-9 items-center justify-center rounded-full bg-brand-50 text-brand-600 ring-1 ring-brand-100">
                      <Icon className="size-4.5" />
                    </span>
                  )}
                  <div className="font-display text-2xl font-semibold text-ink-950 sm:text-3xl">
                    {stat.value}
                  </div>
                  <div className="mt-1 text-xs text-ink-400 sm:text-sm">{stat.label}</div>
                </div>
              );
            })}
          </div>
        )}
      </div>
    </section>
  );
}
