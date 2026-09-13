import { Alert, Logo, cn } from '@dv/ui';
import {
  ArrowLeftRight,
  AtSign,
  BadgeCheck,
  BarChart3,
  Bell,
  Building2,
  ChevronDown,
  ClipboardList,
  Contact,
  Coins,
  CreditCard,
  FileSearch,
  GitBranch,
  GraduationCap,
  Globe,
  Image,
  Landmark,
  LifeBuoy,
  Link2,
  ListOrdered,
  LayoutDashboard,
  KeyRound,
  LayoutTemplate,
  Megaphone,
  Menu,
  LogOut,
  PanelLeftClose,
  PanelLeftOpen,
  Settings,
  Send,
  Share2,
  SlidersHorizontal,
  Tags,
  UserCog,
  Users,
  WalletCards,
  Wrench,
} from 'lucide-react';
import { Fragment, useEffect, useState, type ComponentType } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom';
import { logoutAdmin } from '@/features/auth/api';
import { Avatar } from '@/features/auth/Avatar';
import { Permissions } from '@/features/auth/session';
import { useAdminSession } from '@/features/auth/useAdminSession';
import { LanguageSwitcher } from '@/features/language/LanguageSwitcher';
import { UtcClock } from '@/shared/ui/UtcClock';
import { formatDateTime, formatTime } from '@/shared/lib/format';
import { useLanguage } from '@/shared/lib/useLanguage';

interface NavItem {
  to: string;
  labelKey: string;
  icon: ComponentType<{ className?: string }>;
  permissions: string[];
  /** Stable hook for the sidebar link, e.g. "nav-countries". */
  testId: string;
}

interface NavGroup {
  /** Optional section header shown above the group (large screens only). */
  labelKey?: string;
  /** Starts folded away on first visit. Only meaningful for a group with a header to unfold it. */
  defaultCollapsed?: boolean;
  items: NavItem[];
}

const NAV_GROUPS: NavGroup[] = [
  {
    items: [
      { to: '/', labelKey: 'nav.dashboard', icon: LayoutDashboard, permissions: [Permissions.DashboardView], testId: 'nav-dashboard' },
      { to: '/applications', labelKey: 'nav.applications', icon: ClipboardList, permissions: [Permissions.ApplicationsView], testId: 'nav-applications' },
      { to: '/orders', labelKey: 'nav.orders', icon: Users, permissions: [Permissions.OrdersView], testId: 'nav-orders' },
      { to: '/wallet-requests', labelKey: 'nav.walletRequests', icon: WalletCards, permissions: [Permissions.OrdersView], testId: 'nav-wallet-requests' },
      { to: '/payments/methods', labelKey: 'nav.paymentMethods', icon: CreditCard, permissions: [Permissions.PaymentMethodsView], testId: 'nav-payment-methods' },
      { to: '/payments/types', labelKey: 'nav.paymentTypes', icon: SlidersHorizontal, permissions: [Permissions.PaymentMethodsView], testId: 'nav-payment-types' },
      { to: '/banks', labelKey: 'nav.banks', icon: Landmark, permissions: [Permissions.PaymentMethodsView], testId: 'nav-banks' },
      { to: '/clients', labelKey: 'nav.clients', icon: Building2, permissions: [Permissions.ClientsView], testId: 'nav-clients' },
    ],
  },
  // Support's own corner: the queue, the categories that feed its dropdown, and the directory the
  // public contact page is built from. They were scattered across three groups — the categories
  // sitting under Lookups, which is where nobody thought to look for them.
  {
    labelKey: 'nav.support',
    defaultCollapsed: true,
    items: [
      { to: '/tickets', labelKey: 'nav.tickets', icon: LifeBuoy, permissions: [Permissions.TicketsView], testId: 'nav-tickets' },
      { to: '/ticket-categories', labelKey: 'nav.ticketCategories', icon: Tags, permissions: [Permissions.TicketCategoriesView], testId: 'nav-ticket-categories' },
      { to: '/contact-directory', labelKey: 'nav.contactDirectory', icon: Contact, permissions: [Permissions.ContactDirectoryView], testId: 'nav-contact-directory' },
    ],
  },
  // The landing page is edited one section at a time. They were a single long page whose four
  // parts had nothing to do with each other beyond appearing on the same screen — an editor
  // changing a statistic scrolled past the feature cards to reach it.
  {
    labelKey: 'nav.landingContent',
    defaultCollapsed: true,
    items: [
      { to: '/landing-content/stats', labelKey: 'nav.landingStats', icon: BarChart3, permissions: [Permissions.LandingContentView], testId: 'nav-landing-stats' },
      { to: '/landing-content/steps', labelKey: 'nav.landingSteps', icon: ListOrdered, permissions: [Permissions.LandingContentView], testId: 'nav-landing-steps' },
      { to: '/landing-content/trusted-by', labelKey: 'nav.landingTrust', icon: BadgeCheck, permissions: [Permissions.LandingContentView], testId: 'nav-landing-trust' },
      { to: '/landing-content/coverage', labelKey: 'nav.landingCoverage', icon: Globe, permissions: [Permissions.LandingContentView], testId: 'nav-landing-coverage' },
      { to: '/landing-content/features', labelKey: 'nav.landingFeatures', icon: LayoutTemplate, permissions: [Permissions.LandingContentView], testId: 'nav-landing-features' },
      { to: '/landing-content/cta', labelKey: 'nav.landingCta', icon: Megaphone, permissions: [Permissions.LandingContentView], testId: 'nav-landing-cta' },
    ],
  },
  // The footer is edited in three pieces, matching how it is drawn: the brand block, the link
  // columns, and the social channels.
  {
    labelKey: 'nav.footer',
    defaultCollapsed: true,
    items: [
      { to: '/footer-brand', labelKey: 'nav.footerBrand', icon: Image, permissions: [Permissions.LandingContentView], testId: 'nav-footer-brand' },
      { to: '/footer-links', labelKey: 'nav.footerLinks', icon: Link2, permissions: [Permissions.LandingContentView], testId: 'nav-footer-links' },
      { to: '/footer-channels', labelKey: 'nav.footerChannels', icon: Share2, permissions: [Permissions.SocialLinksView], testId: 'nav-footer-channels' },
    ],
  },
  {
    labelKey: 'nav.lookups',
    defaultCollapsed: true,
    items: [
      { to: '/lookups/countries', labelKey: 'lookups.countries', icon: Globe, permissions: [Permissions.CountriesView], testId: 'nav-countries' },
      { to: '/lookups/currencies', labelKey: 'lookups.currencies', icon: Coins, permissions: [Permissions.CurrenciesView], testId: 'nav-currencies' },
      { to: '/lookups/addressees', labelKey: 'lookups.addressees', icon: Send, permissions: [Permissions.AddresseesView], testId: 'nav-addressees' },
      { to: '/lookups/transactionTypes', labelKey: 'lookups.transactionTypes', icon: ArrowLeftRight, permissions: [Permissions.TransactionTypesView], testId: 'nav-transactionTypes' },
      { to: '/lookups/subTransactionTypes', labelKey: 'lookups.subTransactionTypes', icon: GitBranch, permissions: [Permissions.SubTransactionTypesView], testId: 'nav-subTransactionTypes' },
      { to: '/lookups/authorities', labelKey: 'lookups.authorities', icon: Landmark, permissions: [Permissions.AuthoritiesView], testId: 'nav-authorities' },
      { to: '/lookups/serviceTypes', labelKey: 'lookups.serviceTypes', icon: Wrench, permissions: [Permissions.ServiceTypesView], testId: 'nav-serviceTypes' },
    ],
  },
  {
    items: [
      // Roles templates stay available at /roles for direct URL access, but permissions are
      // assigned on each user — so the Roles sidebar entry is intentionally hidden.
      { to: '/users', labelKey: 'nav.users', icon: UserCog, permissions: [Permissions.AdminUsersView], testId: 'nav-users' },
      { to: '/tools', labelKey: 'nav.tools', icon: GraduationCap, permissions: [Permissions.ToolsView], testId: 'nav-tools' },
      // The header sits above every page, marketing or not, so it belongs beside the other
      // site-wide entries rather than inside the collapsed Footer group.
      { to: '/header-navigation', labelKey: 'nav.headerNavigation', icon: Menu, permissions: [Permissions.LandingContentView], testId: 'nav-header-navigation' },
      { to: '/wizard-content', labelKey: 'nav.wizardContent', icon: ListOrdered, permissions: [Permissions.LandingContentView], testId: 'nav-wizard-content' },
      { to: '/audit', labelKey: 'nav.audit', icon: FileSearch, permissions: [Permissions.AuditLogView], testId: 'nav-audit' },
      { to: '/email-configurations', labelKey: 'nav.emailConfigurations', icon: AtSign, permissions: [Permissions.SettingsView], testId: 'nav-email-configurations' },
      { to: '/password-resets', labelKey: 'nav.passwordResets', icon: KeyRound, permissions: [Permissions.SettingsView], testId: 'nav-password-resets' },
      { to: '/settings', labelKey: 'nav.settings', icon: Settings, permissions: [Permissions.SettingsView], testId: 'nav-settings' },
    ],
  },
];

const ROUTE_TITLES: Record<string, string> = {
  '/': 'nav.dashboard',
  '/applications': 'nav.applications',
  '/orders': 'nav.orders',
  '/wallet-requests': 'nav.walletRequests',
  '/clients': 'nav.clients',
  '/roles': 'nav.roles',
  '/users': 'nav.users',
  '/landing-content/stats': 'nav.landingStats',
  '/landing-content/steps': 'nav.landingSteps',
  '/landing-content/trusted-by': 'nav.landingTrust',
  '/landing-content/coverage': 'nav.landingCoverage',
  '/landing-content/cta': 'nav.landingCta',
  '/password-resets': 'nav.passwordResets',
  '/landing-content/features': 'nav.landingFeatures',
  '/tools': 'nav.tools',
  '/wizard-content': 'nav.wizardContent',
  '/header-navigation': 'nav.headerNavigation',
  '/footer-brand': 'nav.footerBrand',
  '/footer-links': 'nav.footerLinks',
  '/footer-channels': 'nav.footerChannels',
  '/audit': 'nav.audit',
  '/email-configurations': 'nav.emailConfigurations',
  '/contact-directory': 'nav.contactDirectory',
  '/settings': 'nav.settings',
  '/profile': 'account.title',
};

const SIDEBAR_STORAGE_KEY = 'dv.admin.sidebarOpen';
// Versioned: a preference saved before a group existed cannot say whether that group should
// start folded, and unioning the defaults into it would re-fold a group the reader had opened on
// purpose. Bumping the key hands everyone the current defaults once, after which their own choices
// stick again.
const COLLAPSED_GROUPS_STORAGE_KEY = 'dv.admin.collapsedNavGroups.v3';

/** The sidebar is shown unless it was explicitly hidden on a previous visit. */
function readSidebarOpen(): boolean {
  try {
    return localStorage.getItem(SIDEBAR_STORAGE_KEY) !== '0';
  } catch {
    // Private browsing can refuse storage entirely; the default is a visible sidebar.
    return true;
  }
}

function readCollapsedGroups(): Set<string> {
  try {
    const stored = localStorage.getItem(COLLAPSED_GROUPS_STORAGE_KEY);
    if (stored) return new Set(JSON.parse(stored) as string[]);
  } catch {
    // Unreadable or malformed: fall back to the defaults below.
  }

  return new Set(
    NAV_GROUPS.filter((group) => group.defaultCollapsed && group.labelKey).map(
      (group) => group.labelKey!,
    ),
  );
}

function resolvePageTitle(pathname: string): string {
  if (pathname.startsWith('/applications/')) return 'nav.applications';
  if (pathname.startsWith('/orders/')) return 'nav.orders';
  if (pathname.startsWith('/ticket-categories')) return 'nav.ticketCategories';
  if (pathname.startsWith('/tickets')) return 'nav.tickets';
  if (pathname.startsWith('/banks')) return 'nav.banks';
  if (pathname.startsWith('/payments/types')) return 'nav.paymentTypes';
  if (pathname.startsWith('/payments')) return 'nav.paymentMethods';
  if (pathname.startsWith('/lookups/')) {
    const key = pathname.split('/')[2];
    return key ? `lookups.${key}` : 'nav.lookups';
  }
  const base = Object.keys(ROUTE_TITLES).find((p) => (p === '/' ? pathname === '/' : pathname.startsWith(p)));
  return base ? ROUTE_TITLES[base]! : 'common.adminPanel';
}

export function AdminLayout() {
  const { t } = useTranslation();
  const session = useAdminSession();
  const navigate = useNavigate();
  const locale = useLanguage();
  const { pathname } = useLocation();

  const groups = NAV_GROUPS.map((group) => ({
    ...group,
    items: group.items.filter((item) =>
      item.permissions.some((permission) => session?.permissions.has(permission)),
    ),
  })).filter((group) => group.items.length > 0);

  const totalVisible = groups.reduce((count, group) => count + group.items.length, 0);

  const [isSidebarOpen, setIsSidebarOpen] = useState(readSidebarOpen);
  const [collapsedGroups, setCollapsedGroups] = useState(readCollapsedGroups);

  // Both preferences outlive the session, so the panel reopens the way it was left.
  useEffect(() => {
    try {
      localStorage.setItem(SIDEBAR_STORAGE_KEY, isSidebarOpen ? '1' : '0');
    } catch {
      // A rejected write only costs the preference, never the render.
    }
  }, [isSidebarOpen]);

  useEffect(() => {
    try {
      localStorage.setItem(COLLAPSED_GROUPS_STORAGE_KEY, JSON.stringify([...collapsedGroups]));
    } catch {
      // As above.
    }
  }, [collapsedGroups]);

  function toggleGroup(labelKey: string) {
    setCollapsedGroups((previous) => {
      const next = new Set(previous);
      if (next.has(labelKey)) next.delete(labelKey);
      else next.add(labelKey);
      return next;
    });
  }

  function signOut() {
    void logoutAdmin();
    navigate('/login', { replace: true });
  }

  return (
    <div className="flex min-h-screen bg-[#eef1f5]">
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:absolute focus:z-50 focus:m-3 focus:rounded-md focus:bg-brand-600 focus:px-4 focus:py-2 focus:text-white"
      >
        {t('common.skipToContent')}
      </a>

      <nav
        id="admin-sidebar"
        aria-label={t('common.adminPanel')}
        className={cn(
          'sticky top-0 h-screen w-[76px] shrink-0 flex-col border-e border-white/5 bg-ink-950 text-ink-200 lg:w-[280px]',
          isSidebarOpen ? 'flex' : 'hidden',
        )}
      >
        <div
          className="pointer-events-none absolute inset-0 opacity-30"
          style={{
            backgroundImage: 'radial-gradient(circle at 20% 0%, rgb(16 185 129 / 0.35) 0%, transparent 55%)',
          }}
          aria-hidden
        />

        <div className="relative flex items-center gap-3 px-4 py-6 lg:px-6">
          <div className="flex size-10 shrink-0 items-center justify-center rounded-xl bg-brand-500/20 ring-1 ring-brand-400/30">
            <Logo size={24} />
          </div>
          <div className="hidden min-w-0 lg:block">
            <p className="truncate font-display text-base font-bold text-white">{t('common.adminPanel')}</p>
            <p className="text-[11px] font-bold uppercase tracking-[0.2em] text-brand-400">NEN</p>
          </div>
        </div>

        <ul className="relative flex flex-1 flex-col gap-1 overflow-y-auto px-3 py-1 lg:px-4">
          {groups.map((group, index) => {
            // Collapsing only applies from `lg` up, because that is the only breakpoint where the
            // header — and therefore the control that unfolds the group again — is rendered. On the
            // narrow icon rail every item stays reachable.
            const isCollapsed = group.labelKey ? collapsedGroups.has(group.labelKey) : false;

            return (
              <Fragment key={group.labelKey ?? `group-${index}`}>
                {group.labelKey && (
                  <li className="hidden pb-1 pt-4 lg:block">
                    <button
                      type="button"
                      onClick={() => toggleGroup(group.labelKey!)}
                      aria-expanded={!isCollapsed}
                      data-testid={`nav-group-${group.labelKey.split('.').pop()}`}
                      // Larger and at full weight, so the section header reads as a heading rather
                      // than as faint decoration above the links.
                      className="flex w-full items-center justify-between gap-2 rounded-lg px-3 py-1.5 text-sm font-bold uppercase tracking-[0.14em] text-white/75 transition-colors hover:bg-white/5 hover:text-white"
                    >
                      <span className="truncate">{t(group.labelKey)}</span>
                      <ChevronDown
                        className={cn(
                          'size-4 shrink-0 transition-transform duration-200',
                          isCollapsed && '-rotate-90 rtl:rotate-90',
                        )}
                        aria-hidden
                      />
                    </button>
                  </li>
                )}
                {group.items.map((item) => (
                  <li key={item.to} className={cn(isCollapsed && 'lg:hidden')}>
                    <NavLink
                      to={item.to}
                      end={item.to === '/'}
                      data-testid={item.testId}
                      className={({ isActive }) =>
                        cn(
                          'group flex items-center gap-3 rounded-xl px-3 py-3 text-base font-bold transition-all duration-200',
                          isActive
                            ? 'bg-gradient-to-r from-brand-600/90 to-brand-500/80 text-white shadow-[0_8px_24px_-8px_rgb(16_185_129/0.6)]'
                            : 'text-white/55 hover:bg-white/5 hover:text-white',
                        )
                      }
                    >
                      <item.icon className="size-5 shrink-0" />
                      <span className="hidden truncate lg:inline">{t(item.labelKey)}</span>
                    </NavLink>
                  </li>
                ))}
              </Fragment>
            );
          })}
        </ul>

        <div className="relative border-t border-white/5 px-3 py-5 lg:px-4">
          <div className="hidden rounded-xl bg-white/[0.06] px-4 py-3 ring-1 ring-white/10 lg:block">
            <p className="truncate text-base font-bold text-white">{session?.fullName}</p>
            {session?.loginAtUtc && (
              <p className="mt-0.5 text-xs font-semibold text-brand-400">
                {t('account.signedInAt', { time: formatTime(session.loginAtUtc, locale) })}
              </p>
            )}
          </div>
        </div>
      </nav>

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="sticky top-0 z-30 flex h-16 shrink-0 items-center justify-between gap-4 border-b border-ink-200/60 bg-white/80 px-5 backdrop-blur-xl sm:px-8 lg:px-10">
          <div className="flex min-w-0 items-center gap-3">
            <button
              type="button"
              onClick={() => setIsSidebarOpen((open) => !open)}
              data-testid="sidebar-toggle"
              aria-controls="admin-sidebar"
              aria-expanded={isSidebarOpen}
              aria-label={t(isSidebarOpen ? 'common.hideSidebar' : 'common.showSidebar')}
              title={t(isSidebarOpen ? 'common.hideSidebar' : 'common.showSidebar')}
              className="flex size-10 shrink-0 items-center justify-center rounded-xl bg-ink-50 text-ink-500 ring-1 ring-ink-100 transition-colors hover:text-ink-900"
            >
              {isSidebarOpen ? (
                <PanelLeftClose className="size-[18px] rtl:rotate-180" aria-hidden />
              ) : (
                <PanelLeftOpen className="size-[18px] rtl:rotate-180" aria-hidden />
              )}
            </button>
            <div className="min-w-0">
              <p className="text-[11px] font-bold uppercase tracking-[0.18em] text-brand-600">{t('common.adminPanel')}</p>
              <p className="truncate font-display text-lg font-semibold text-ink-950">{t(resolvePageTitle(pathname))}</p>
            </div>
          </div>
          <div className="flex items-center gap-3">
            <button
              type="button"
              className="flex size-10 items-center justify-center rounded-xl bg-ink-50 text-ink-400 ring-1 ring-ink-100 transition-colors hover:text-ink-700"
              aria-label={t('dashboard.recentActivity')}
            >
              <Bell className="size-[18px]" />
            </button>
            <LanguageSwitcher />
            <UtcClock />
            <Link
              to="/profile"
              data-testid="account-link"
              className="hidden items-center gap-3 rounded-xl bg-white px-3 py-2 text-ink-950 ring-1 ring-ink-200/70 transition-colors hover:bg-ink-50 sm:flex"
            >
              <Avatar size={32} />
              <span className="pe-1 text-start leading-tight">
                <span className="block text-xs font-semibold">{session?.fullName}</span>
                {session?.loginAtUtc && (
                  <span className="mt-0.5 block text-[11px] font-medium text-ink-500">
                    {t('account.lastSignInAt', { time: formatDateTime(session.loginAtUtc, locale) })}
                  </span>
                )}
              </span>
            </Link>
            <button
              type="button"
              onClick={signOut}
              data-testid="sign-out"
              title={t('common.signOut')}
              className="flex items-center gap-2 rounded-xl bg-ink-50 px-3 py-2 text-sm font-semibold text-ink-600 ring-1 ring-ink-200/70 transition-colors hover:bg-red-50 hover:text-red-700 hover:ring-red-200"
            >
              <LogOut className="size-4 shrink-0 rtl:rotate-180" aria-hidden />
              <span className="hidden sm:inline">{t('common.signOut')}</span>
            </button>
          </div>
        </header>

        <main id="main" className="relative flex-1 px-5 py-6 sm:px-8 sm:py-8 lg:px-10">
          <div
            className="pointer-events-none absolute inset-0 opacity-[0.45]"
            style={{
              backgroundImage:
                'linear-gradient(to right, rgb(28 35 45 / 0.03) 1px, transparent 1px), linear-gradient(to bottom, rgb(28 35 45 / 0.03) 1px, transparent 1px)',
              backgroundSize: '48px 48px',
            }}
            aria-hidden
          />
          <div className="relative w-full">
            {totalVisible === 0 ? (
              <Alert variant="warning">{t('errors.noAccess')}</Alert>
            ) : (
              <Outlet />
            )}
          </div>
        </main>
      </div>
    </div>
  );
}
