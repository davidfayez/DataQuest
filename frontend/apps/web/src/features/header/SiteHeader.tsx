import { Logo, buttonVariants, cn } from '@dv/ui';
import { FileCheck2, LogIn, Menu, MessageSquare, Wallet, X, type LucideIcon } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, NavLink, useLocation, useNavigate, useParams } from 'react-router-dom';
import { logout } from '@/features/auth/api';
import { useSession } from '@/features/auth/useSession';
import { LanguageSwitcher } from '@/features/language/LanguageSwitcher';
import { AccountMenu } from './AccountMenu';
import { navItemClass, type NavVariant } from './navStyles';
import { SiteNavLinks } from './SiteNav';

/** The signed-in applicant's own areas, which follow the arranged entries. */
const ACCOUNT_LINKS: { path: string; labelKey: string; icon: LucideIcon; end?: boolean }[] = [
  { path: 'applications', labelKey: 'nav.dashboard', icon: FileCheck2, end: true },
  { path: 'wallet', labelKey: 'nav.wallet', icon: Wallet },
  { path: 'tickets', labelKey: 'nav.myTickets', icon: MessageSquare },
];

const MENU_ID = 'site-menu';

/**
 * The sticky bar across the top of every applicant page.
 *
 * On wide screens every entry sits in one row. Below `lg` there is not room for them — least of all
 * once a session adds its three — so the entries move into a panel behind a menu button, and the bar
 * keeps only what is used from anywhere: the mark, the language, and the account.
 */
export function SiteHeader() {
  const { t } = useTranslation();
  const session = useSession();
  const navigate = useNavigate();
  const location = useLocation();
  const { lang = 'en' } = useParams<{ lang: string }>();
  const [menuOpen, setMenuOpen] = useState(false);

  // Any navigation — a link in the panel, the back button, an anchor jump — puts the panel away.
  useEffect(() => {
    setMenuOpen(false);
  }, [location.pathname, location.hash]);

  useEffect(() => {
    if (!menuOpen) return;

    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') setMenuOpen(false);
    }

    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [menuOpen]);

  function signOut() {
    setMenuOpen(false);
    void logout();
    navigate(`/${lang}/login`);
  }

  function accountLinks(variant: NavVariant) {
    return ACCOUNT_LINKS.map(({ path, labelKey, icon: Icon, end }) => (
      <NavLink
        key={path}
        to={`/${lang}/${path}`}
        end={end}
        className={({ isActive }) => navItemClass(variant, isActive)}
      >
        <Icon size={16} aria-hidden className="shrink-0" />
        <span>{t(labelKey)}</span>
      </NavLink>
    ));
  }

  return (
    <header className="sticky top-0 z-40 border-b border-ink-100/80 bg-paper/80 backdrop-blur-xl">
      <div className="mx-auto flex h-16 max-w-6xl items-center gap-3 px-4 sm:px-6">
        {/* The mark alone, with no wordmark beside it. The logo itself is decorative
            (alt="", aria-hidden), so the link carries the name for assistive technology. */}
        <Link
          to={session ? `/${lang}/applications` : `/${lang}`}
          aria-label={t('common.appName')}
          className="flex shrink-0 items-center"
          // The landing page is long and its header links jump down it, so the mark has to get
          // you back to the top — including when it links to the page you are already on,
          // where the router changes nothing and the scroll position would simply stay put.
          onClick={() => window.scrollTo({ top: 0, behavior: 'smooth' })}
        >
          <Logo size={30} />
        </Link>

        {/* Arranged in the admin panel: which entries the header carries, in what order, and
            who sees each one. The labels and glyphs stay in the app, so rearranging the bar
            never costs a translation. */}
        <nav className="ms-3 hidden min-w-0 items-center gap-0.5 lg:flex">
          <SiteNavLinks isSignedIn={Boolean(session)} variant="bar" />
          {session && (
            <>
              <span aria-hidden className="mx-1.5 h-5 w-px shrink-0 bg-ink-200" />
              {accountLinks('bar')}
            </>
          )}
        </nav>

        <div className="ms-auto flex shrink-0 items-center gap-2">
          <LanguageSwitcher />

          {session ? (
            <AccountMenu orderNumber={session.orderNumber} onSignOut={signOut} />
          ) : (
            <>
              <Link
                to={`/${lang}/login`}
                className="hidden whitespace-nowrap rounded-lg px-3 py-2 text-sm font-medium text-ink-600 transition-colors hover:bg-ink-100 hover:text-ink-950 sm:block"
              >
                {t('nav.login')}
              </Link>
              <Link
                to={`/${lang}/register`}
                className={cn(buttonVariants({ size: 'sm' }), 'whitespace-nowrap')}
              >
                {t('nav.start')}
              </Link>
            </>
          )}

          <button
            type="button"
            onClick={() => setMenuOpen((previous) => !previous)}
            aria-expanded={menuOpen}
            aria-controls={MENU_ID}
            className="flex size-9 cursor-pointer items-center justify-center rounded-lg text-ink-700 transition-colors hover:bg-ink-100 hover:text-ink-950 lg:hidden"
          >
            {menuOpen ? <X size={20} aria-hidden /> : <Menu size={20} aria-hidden />}
            <span className="sr-only">{menuOpen ? t('common.close') : t('common.menu')}</span>
          </button>
        </div>
      </div>

      {menuOpen && (
        <div
          id={MENU_ID}
          className="absolute inset-x-0 top-full border-b border-ink-100 bg-paper shadow-lift lg:hidden"
        >
          <nav className="mx-auto flex max-h-[calc(100dvh-4rem)] max-w-6xl flex-col gap-0.5 overflow-y-auto px-4 py-3 sm:px-6">
            <SiteNavLinks isSignedIn={Boolean(session)} variant="panel" />
            <div aria-hidden className="mx-3 my-2 h-px bg-ink-100" />
            {session ? (
              accountLinks('panel')
            ) : (
              <Link to={`/${lang}/login`} className={navItemClass('panel', false)}>
                <LogIn size={16} aria-hidden className="shrink-0 rtl:-scale-x-100" />
                <span>{t('nav.login')}</span>
              </Link>
            )}
          </nav>
        </div>
      )}
    </header>
  );
}
