import { Logo } from '@dv/ui';
import {
  ArrowUp,
  ChevronRight,
  Facebook,
  Instagram,
  Linkedin,
  Mail,
  MessageCircle,
  MessageSquare,
  Send,
  Youtube,
} from 'lucide-react';
import type { CSSProperties } from 'react';
import type { LucideIcon } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router-dom';
import { useSession } from '@/features/auth/useSession';
import {
  CONTACT_EMAIL,
  COPYRIGHT_FROM,
  ORGANISATION_NAME,
  ORGANISATION_URL,
} from './footerLinks';
import { useFooterChannels, type SocialPlatform } from '@/features/footer/api';
import {
  footerLogoSrc,
  hrefFor,
  useFooterContent,
  type FooterLinkDto,
} from '@/features/footer/content';

/**
 * How each platform is drawn. Keyed on the name the API sends, and owned here rather than stored
 * beside the link, because artwork is not something an operator should have to supply: configuring
 * a channel is choosing a platform and pasting an address.
 *
 * Lucide ships no brand glyphs for Telegram, VK, X, TikTok, WhatsApp or Messenger, so those reuse
 * the nearest generic mark, and the ones with no sensible stand-in fall back to their initials —
 * which reads better than a wrong icon.
 */
const PLATFORMS: Record<SocialPlatform, { label: string; color: string; icon?: LucideIcon }> = {
  Facebook: { label: 'Facebook', color: '#1877f2', icon: Facebook },
  Instagram: { label: 'Instagram', color: '#e1306c', icon: Instagram },
  LinkedIn: { label: 'LinkedIn', color: '#0a66c2', icon: Linkedin },
  YouTube: { label: 'YouTube', color: '#ff0000', icon: Youtube },
  Telegram: { label: 'Telegram', color: '#229ed9', icon: Send },
  Vk: { label: 'VK', color: '#0077ff' },
  X: { label: 'X', color: '#000000' },
  TikTok: { label: 'TikTok', color: '#ff0050' },
  WhatsApp: { label: 'WhatsApp', color: '#25d366', icon: MessageCircle },
  Messenger: { label: 'Messenger', color: '#0084ff', icon: MessageSquare },
};

/** Carries a link's own brand colour into the hover rules below. */
type BrandStyle = CSSProperties & { '--brand': string };

function SocialLink({ href, platform }: { href: string; platform: SocialPlatform }) {
  const { label, color, icon: Icon } = PLATFORMS[platform];

  return (
    <li>
      <a
        href={href}
        target="_blank"
        rel="noopener noreferrer"
        title={label}
        style={{ '--brand': color } as BrandStyle}
        className="flex size-8 items-center justify-center rounded-full bg-white/5 text-ink-300 ring-1 ring-white/10 transition-all duration-200 hover:-translate-y-0.5 hover:bg-[var(--brand)] hover:text-white hover:ring-[var(--brand)]"
      >
        {Icon ? (
          <Icon className="size-4" aria-hidden />
        ) : (
          <span className="text-[10px] font-bold" aria-hidden>
            {label}
          </span>
        )}
        <span className="sr-only">{label}</span>
      </a>
    </li>
  );
}

/** The messaging shortcuts read as labelled pills — an icon alone says little. */
function MessagingPill({ href, platform }: { href: string; platform: SocialPlatform }) {
  const { label, color, icon: Icon } = PLATFORMS[platform];

  return (
    <li>
      <a
        href={href}
        target="_blank"
        rel="noopener noreferrer"
        title={label}
        style={{ '--brand': color } as BrandStyle}
        className="flex items-center gap-1.5 rounded-full bg-white/5 px-3 py-1.5 text-xs font-medium text-ink-300 ring-1 ring-white/10 transition-all duration-200 hover:-translate-y-0.5 hover:bg-[var(--brand)] hover:text-white hover:ring-[var(--brand)]"
      >
        {Icon ? <Icon className="size-4" aria-hidden /> : null}
        {label}
      </a>
    </li>
  );
}

/**
 * A footer link. The chevron sits ahead of the label and slides on hover, echoing the caret
 * bullets the reference footer puts in front of every list item.
 */
function FooterLinkRow({
  children,
  ...anchor
}: { children: React.ReactNode } & (
  { href: string; to?: never; external?: boolean } | { to: string; href?: never; external?: never }
)) {
  const inner = (
    <>
      <ChevronRight
        className="size-3.5 shrink-0 text-ink-600 transition-all duration-200 group-hover:translate-x-0.5 group-hover:text-brand-400 rtl:rotate-180 rtl:group-hover:-translate-x-0.5"
        aria-hidden
      />
      {children}
    </>
  );
  const className =
    'group inline-flex items-center gap-1.5 py-0.5 text-[13px] text-ink-400 transition-colors hover:text-white';

  return (
    <li>
      {anchor.to !== undefined ? (
        <Link to={anchor.to} className={className}>
          {inner}
        </Link>
      ) : (
        <a
          href={anchor.href}
          className={className}
          {...(anchor.external ? { target: '_blank', rel: 'noopener noreferrer' } : {})}
        >
          {inner}
        </a>
      )}
    </li>
  );
}

/**
 * One column of the footer.
 *
 * Renders nothing at all when an administrator has emptied it — a heading over no links is worse
 * than a narrower footer.
 */
function FooterColumn({
  id,
  className,
  heading,
  links,
  lang,
}: {
  id: string;
  className: string;
  heading: string;
  links: FooterLinkDto[];
  lang: string;
}) {
  if (links.length === 0) return null;

  return (
    <nav className={className} aria-labelledby={id}>
      <ColumnHeading>
        <span id={id}>{heading}</span>
      </ColumnHeading>
      <ul className="mt-3">
        {links.map((link) => {
          const target = hrefFor(link.url, lang);

          // An in-app path routes without a page load; an anchor and an external address are
          // plain anchors, the latter opening away from the site.
          return target.to ? (
            <FooterLinkRow key={link.id} to={target.to}>
              {link.label}
            </FooterLinkRow>
          ) : (
            <FooterLinkRow key={link.id} href={target.href} external={target.external}>
              {link.label}
            </FooterLinkRow>
          );
        })}
      </ul>
    </nav>
  );
}

function ColumnHeading({ children }: { children: React.ReactNode }) {
  return (
    <h2 className="text-xs font-semibold tracking-[0.14em] text-white uppercase">
      {children}
      <span className="mt-2 block h-px w-6 bg-brand-500" aria-hidden />
    </h2>
  );
}

/**
 * Mirrors the footer on itep.nen-global.org — the same organisation, so the same contact points,
 * and the same shape: a dark ground, columns of links, the social row, and a bottom bar carrying
 * NEN's copyright line beside the contact details.
 */
export function SiteFooter() {
  const { t } = useTranslation();
  const { lang = 'en' } = useParams<{ lang: string }>();
  const session = useSession();

  // While the request is in flight both rows are empty, so the block renders nothing rather than
  // two headings that fill in a moment later and shift the page under the reader.
  const channels = useFooterChannels();
  const followUs = channels.data?.followUs ?? [];
  const messageUs = channels.data?.messageUs ?? [];

  const footer = useFooterContent();
  const logos = footer.data?.logos ?? [];

  /**
   * Drops the rows this visitor should not see.
   *
   * The filter runs here rather than on the server because the footer is fetched anonymously and
   * cached: a per-session response would be uncacheable, and would put "is this reader signed in"
   * into a cache key shared by everyone.
   */
  const visibleIn = (links: FooterLinkDto[] | undefined) =>
    (links ?? []).filter(
      (link) =>
        link.visibility === 'Everyone'
        || (link.visibility === 'SignedIn' ? Boolean(session) : !session),
    );

  return (
    <footer className="mt-auto bg-ink-950 text-ink-300">
      {/* The reference sets a coloured rule above its dark footer; here it is the brand accent. */}
      <div
        className="h-px w-full bg-gradient-to-r from-transparent via-brand-500/70 to-transparent"
        aria-hidden
      />

      {/* Narrower than the page's max-w-6xl: the columns hold together instead of drifting apart. */}
      <div className="mx-auto max-w-5xl px-4 py-8 sm:px-6">
        <div className="grid gap-8 sm:grid-cols-2 lg:grid-cols-12 lg:gap-6">
          {/* Brand, promise, and the marks. The email and phone that used to sit here are in the
              bottom bar and in the Explore column's Contact us, so repeating them was noise. */}
          <div className="sm:col-span-2 lg:col-span-4">
            <Link
              to={`/${lang}`}
              className="inline-flex items-center gap-3"
              onClick={() => window.scrollTo({ top: 0, behavior: 'smooth' })}
            >
              <Logo size={32} />
              <span className="font-display text-base font-semibold text-white">
                {t('common.appName')}
              </span>
            </Link>

            <p className="mt-3 max-w-sm text-[13px] leading-relaxed text-ink-400">
              {footer.data?.subtitle ?? ''}
            </p>

            {/* The marks an administrator uploaded — accreditations, partners. Absent entirely
                when none are published: the block then ends at the subtitle, which reads better
                than an empty row. */}
            {logos.length > 0 && (
              <ul className="mt-4 flex flex-wrap items-center gap-3">
                {logos.map((logo) => {
                  const image = (
                    <img
                      src={footerLogoSrc(logo.imageUrl)}
                      alt={logo.alt}
                      // Height-constrained rather than boxed: uploaded marks are all different
                      // shapes, and fixing both dimensions would squash half of them.
                      className="h-8 w-auto max-w-[120px] object-contain opacity-80 transition-opacity hover:opacity-100"
                      loading="lazy"
                    />
                  );

                  return (
                    <li key={logo.id}>
                      {logo.url ? (
                        <a href={logo.url} target="_blank" rel="noopener noreferrer" title={logo.alt}>
                          {image}
                        </a>
                      ) : (
                        image
                      )}
                    </li>
                  );
                })}
              </ul>
            )}
          </div>

          {/* Explore holds the shortest labels, so it gives its width to Account, which holds
              the longest ones. */}
          <FooterColumn
            id="footer-explore"
            className="lg:col-span-2"
            heading={footer.data?.exploreHeading ?? ''}
            links={visibleIn(footer.data?.explore)}
            lang={lang}
          />

          <FooterColumn
            id="footer-account"
            className="lg:col-span-3"
            heading={footer.data?.accountHeading ?? ''}
            links={visibleIn(footer.data?.account)}
            lang={lang}
          />

          <FooterColumn
            id="footer-organisation"
            className="lg:col-span-3"
            heading={footer.data?.organisationHeading ?? ''}
            links={visibleIn(footer.data?.organisation)}
            lang={lang}
          />
        </div>

        {/* Social accounts on one side, the messaging shortcuts on the other. Both are configured
            in the admin panel, and a row an administrator has emptied renders nothing at all rather
            than a heading over a blank space. */}
        {(followUs.length > 0 || messageUs.length > 0) && (
          <div className="mt-8 flex flex-col gap-5 border-t border-white/10 pt-6 md:flex-row md:items-start md:justify-between">
            {followUs.length > 0 && (
              <div>
                <p className="text-xs font-semibold tracking-[0.14em] text-ink-400 uppercase">
                  {t('footer.followUs')}
                </p>
                <ul className="mt-3 flex flex-wrap items-center gap-2">
                  {followUs.map((link) => (
                    <SocialLink key={link.id} href={link.url} platform={link.platform} />
                  ))}
                </ul>
              </div>
            )}

            {messageUs.length > 0 && (
              <div>
                <p className="text-xs font-semibold tracking-[0.14em] text-ink-400 uppercase">
                  {t('footer.messageUs')}
                </p>
                <ul className="mt-3 flex flex-wrap items-center gap-2">
                  {messageUs.map((link) => (
                    <MessagingPill key={link.id} href={link.url} platform={link.platform} />
                  ))}
                </ul>
              </div>
            )}
          </div>
        )}
      </div>

      {/* The bottom bar: the reference's navy strip, copyright on one side, contact on the other. */}
      <div className="border-t border-white/10 bg-black/25">
        <div className="mx-auto flex max-w-5xl flex-col items-center justify-between gap-2 px-4 py-3 text-[11px] text-ink-400 sm:flex-row sm:px-6">
          <p>
            Copyright © {COPYRIGHT_FROM} - {new Date().getFullYear()}{' '}
            <a
              href={ORGANISATION_URL}
              target="_blank"
              rel="noopener noreferrer"
              className="underline-offset-4 transition-colors hover:text-white hover:underline"
            >
              {ORGANISATION_NAME}
            </a>
          </p>

          <div className="flex items-center gap-3">
            <a
              href={`mailto:${CONTACT_EMAIL}`}
              className="inline-flex items-center gap-1.5 transition-colors hover:text-white"
            >
              <Mail className="size-3" aria-hidden />
              <span>{t('footer.contact')}</span>
            </a>
            <button
              type="button"
              onClick={() => window.scrollTo({ top: 0, behavior: 'smooth' })}
              className="inline-flex cursor-pointer items-center gap-1.5 rounded-full bg-white/5 px-2.5 py-1 ring-1 ring-white/10 transition-colors hover:bg-brand-600 hover:text-white hover:ring-brand-600"
            >
              <ArrowUp className="size-3" aria-hidden />
              <span>{t('footer.backToTop')}</span>
            </button>
          </div>
        </div>
      </div>
    </footer>
  );
}
