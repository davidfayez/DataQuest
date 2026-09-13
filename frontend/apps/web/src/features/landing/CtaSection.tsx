import { buttonVariants, cn } from '@dv/ui';
import { ArrowRight } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router-dom';
import { useLandingContent } from './api';

/**
 * The closing call to action, configured in the admin panel.
 *
 * Falls back to the built-in translated copy while the request is in flight or if it fails, so a
 * slow or unavailable API leaves the page with a working button rather than an empty green box.
 */
export function CtaSection() {
  const { t, i18n } = useTranslation();
  const { lang = 'en' } = useParams<{ lang: string }>();
  const rtl = i18n.dir() === 'rtl';
  const content = useLandingContent();

  const title = content.data?.ctaTitle || t('landing.cta.title');
  const body = content.data?.ctaBody || t('landing.cta.body');
  const label = content.data?.ctaButton || t('landing.cta.button');

  const target = destination(content.data?.ctaLink ?? '/register', lang);

  const buttonClass = cn(
    buttonVariants({ variant: 'dark', size: 'lg' }),
    'group mt-8 inline-flex gap-2 bg-white !text-ink-950 hover:bg-brand-50',
  );

  const inner = (
    <>
      {label}
      <ArrowRight
        className={cn(
          'size-4 transition-transform group-hover:translate-x-0.5',
          rtl && 'rotate-180 group-hover:-translate-x-0.5',
        )}
      />
    </>
  );

  return (
    <section className="px-4 pb-24 sm:px-6">
      <div className="relative mx-auto max-w-6xl overflow-hidden rounded-3xl bg-gradient-to-br from-brand-700 via-brand-600 to-teal-700 px-6 py-16 text-center shadow-lift sm:py-20">
        <div
          className="pointer-events-none absolute inset-0 opacity-20"
          style={{
            backgroundImage: 'radial-gradient(circle at 20% 20%, white 1px, transparent 1px)',
            backgroundSize: '28px 28px',
          }}
          aria-hidden
        />
        <div className="relative">
          <h2 className="mx-auto max-w-2xl font-display text-3xl font-semibold tracking-tight text-white sm:text-4xl">
            {title}
          </h2>
          <p className="mx-auto mt-4 max-w-xl text-brand-50/90">{body}</p>

          {target.to ? (
            <Link to={target.to} className={buttonClass}>
              {inner}
            </Link>
          ) : (
            <a
              href={target.href}
              className={buttonClass}
              {...(target.external ? { target: '_blank', rel: 'noopener noreferrer' } : {})}
            >
              {inner}
            </a>
          )}
        </div>
      </div>
    </section>
  );
}

/**
 * Reads the stored destination the way the footer's links are read: an in-app path routes without
 * a page load, an anchor scrolls, and a full address opens away from the site.
 */
function destination(
  url: string,
  lang: string,
): { href: string; external: boolean; to?: string } {
  const value = url.trim();

  if (/^https?:\/\//i.test(value)) return { href: value, external: true };
  if (value.startsWith('#')) return { href: `/${lang}${value}`, external: false };

  return { href: `/${lang}${value}`, external: false, to: `/${lang}${value}` };
}
