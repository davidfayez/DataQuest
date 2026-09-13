import { cn } from '@dv/ui';
import { GraduationCap, LifeBuoy, type LucideIcon } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { NavLink, useParams } from 'react-router-dom';
import { hrefFor } from '@/features/footer/content';
import { useHeaderLinks } from './content';

/**
 * The built-in entries' wording, which stays in the app.
 *
 * An administrator arranges the header; the words come from the same bundles as the rest of the
 * site, so reordering the bar never costs the Japanese or Russian translation. ("knowledge" reads
 * from the older `nav.tools` key, which is where that label has always lived.)
 */
const LABEL_KEYS: Record<string, string> = {
  home: 'nav.home',
  how: 'nav.how',
  services: 'nav.services',
  coverage: 'nav.coverage',
  knowledge: 'nav.tools',
  contact: 'nav.contact',
};

/** Only the two entries that carried a glyph before; the rest read as plain words. */
const ICONS: Record<string, LucideIcon> = {
  knowledge: GraduationCap,
  contact: LifeBuoy,
};

const ITEM =
  'flex items-center gap-2 rounded-lg px-3 py-2 text-sm font-medium transition-colors';

const RESTING = 'text-ink-600 hover:bg-ink-100 hover:text-ink-950';

/**
 * The site header's own navigation, in the order an administrator arranged it.
 *
 * Three kinds of destination, told apart by the stored string exactly as the footer does it: an
 * anchor is a plain link so the jump works from any page, an in-app path is a router link that can
 * show as current, and an external address opens in its own tab.
 */
export function SiteNav({ isSignedIn }: { isSignedIn: boolean }) {
  const { t } = useTranslation();
  const { lang = 'en' } = useParams<{ lang: string }>();
  const links = useHeaderLinks(isSignedIn);

  if (links.length === 0) return null;

  return (
    <nav className="flex items-center gap-1">
      {links.map((link) => {
        const Icon = link.key ? ICONS[link.key] : undefined;
        const labelKey = link.key ? LABEL_KEYS[link.key] : undefined;
        const label = link.label?.trim() || (labelKey ? t(labelKey) : '');

        // A built-in entry this version of the app does not know would otherwise draw a blank.
        if (!label) return null;

        const target = hrefFor(link.url, lang);
        // Without an icon there is nothing to show once the label is hidden, so those entries wait
        // for a wider screen — which is what the landing anchors did before.
        const shell = cn(ITEM, !Icon && 'hidden md:flex');
        const text = <span className={cn(Icon && 'hidden sm:inline')}>{label}</span>;

        if (target.to) {
          return (
            <NavLink
              key={link.id}
              to={target.to}
              // "Home" points at the landing page, which is the prefix of every other route.
              end={target.to === `/${lang}` || target.to === `/${lang}/`}
              className={({ isActive }) =>
                cn(shell, isActive ? 'bg-ink-950 text-white' : RESTING)
              }
            >
              {Icon && <Icon size={16} aria-hidden />}
              {text}
            </NavLink>
          );
        }

        return (
          <a
            key={link.id}
            href={target.href}
            className={cn(shell, RESTING)}
            {...(target.external ? { target: '_blank', rel: 'noopener noreferrer' } : {})}
          >
            {Icon && <Icon size={16} aria-hidden />}
            {text}
          </a>
        );
      })}
    </nav>
  );
}
