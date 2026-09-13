import { Logo, buttonVariants, cn } from '@dv/ui';
import { FileCheck2, LogOut, MessageSquare, Wallet } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Link, NavLink, Outlet, useNavigate, useParams } from 'react-router-dom';
import { SiteNav } from '@/features/header/SiteNav';
import { LanguageSwitcher } from '@/features/language/LanguageSwitcher';
import { SiteFooter } from './SiteFooter';
import { logout } from '@/features/auth/api';
import { useSession } from '@/features/auth/useSession';

const navPill = ({ isActive }: { isActive: boolean }) =>
  cn(
    'flex items-center gap-2 rounded-lg px-3 py-2 text-sm font-medium transition-colors',
    isActive ? 'bg-ink-950 text-white' : 'text-ink-600 hover:bg-ink-100 hover:text-ink-950',
  );

export function AppLayout() {
  const { t } = useTranslation();
  const session = useSession();
  const navigate = useNavigate();
  const { lang = 'en' } = useParams<{ lang: string }>();

  function signOut() {
    void logout();
    navigate(`/${lang}/login`);
  }

  return (
    <div className="flex min-h-screen flex-col">
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:absolute focus:z-50 focus:m-3 focus:rounded-lg focus:bg-brand-600 focus:px-4 focus:py-2 focus:text-white"
      >
        {t('common.skipToContent')}
      </a>

      <header className="sticky top-0 z-40 border-b border-ink-100/80 bg-paper/80 backdrop-blur-xl">
        <div className="mx-auto flex h-16 max-w-6xl items-center justify-between gap-4 px-4 sm:px-6">
          <div className="flex items-center gap-6">
            {/* The mark alone, with no wordmark beside it. The logo itself is decorative
                (alt="", aria-hidden), so the link carries the name for assistive technology. */}
            <Link
              to={session ? `/${lang}/applications` : `/${lang}`}
              aria-label={t('common.appName')}
              className="flex items-center"
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
            <SiteNav isSignedIn={Boolean(session)} />

            {session && (
              <nav className="flex items-center gap-1">
                <NavLink to={`/${lang}/applications`} end className={navPill}>
                  <FileCheck2 size={16} aria-hidden />
                  <span className="hidden sm:inline">{t('nav.dashboard')}</span>
                </NavLink>
                <NavLink to={`/${lang}/wallet`} className={navPill}>
                  <Wallet size={16} aria-hidden />
                  <span className="hidden sm:inline">{t('nav.wallet')}</span>
                </NavLink>
                <NavLink to={`/${lang}/tickets`} className={navPill}>
                  <MessageSquare size={16} aria-hidden />
                  <span className="hidden sm:inline">{t('nav.myTickets')}</span>
                </NavLink>
              </nav>
            )}
          </div>

          <div className="flex items-center gap-3">
            <LanguageSwitcher />
            {session ? (
              <>
                <span className="reference hidden text-xs font-semibold tracking-wider text-ink-800 sm:block">
                  {session.orderNumber}
                </span>
                <button
                  onClick={signOut}
                  className="cursor-pointer rounded-lg p-2 text-ink-400 transition-colors hover:bg-ink-100 hover:text-ink-800"
                  title={t('common.signOut')}
                >
                  <LogOut size={17} aria-hidden />
                  <span className="sr-only">{t('common.signOut')}</span>
                </button>
              </>
            ) : (
              <>
                <Link
                  to={`/${lang}/login`}
                  className="hidden text-sm font-medium text-ink-600 transition-colors hover:text-ink-950 sm:block"
                >
                  {t('nav.login')}
                </Link>
                <Link to={`/${lang}/register`} className={buttonVariants({ size: 'sm' })}>
                  {t('nav.start')}
                </Link>
              </>
            )}
          </div>
        </div>
      </header>

      <main id="main" className="flex-1">
        <Outlet />
      </main>

      <SiteFooter />
    </div>
  );
}
