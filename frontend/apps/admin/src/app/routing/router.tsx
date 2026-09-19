import { createBrowserRouter, Navigate } from 'react-router-dom';
import { Permissions } from '@/features/auth/session';
import { AdminLoginPage } from '@/pages/login/AdminLoginPage';
import { ForgotPasswordPage } from '@/pages/auth/ForgotPasswordPage';
import { ResetPasswordPage } from '@/pages/auth/ResetPasswordPage';
import { ProfilePage } from '@/pages/profile/ProfilePage';
import { DashboardPage } from '@/pages/dashboard/DashboardPage';
import { LookupModulePage, type LookupKey } from '@/pages/lookups/LookupModulePage';
import { AuthorityFormPage } from '@/pages/lookups/AuthorityFormPage';
import { TransactionTypeFormPage } from '@/pages/lookups/TransactionTypeFormPage';
import { SubTransactionTypeFormPage } from '@/pages/lookups/SubTransactionTypeFormPage';
import { CurrencyFormPage } from '@/pages/lookups/CurrencyFormPage';
import { CountryFormPage } from '@/pages/lookups/CountryFormPage';
import { OrdersPage } from '@/pages/orders/OrdersPage';
import { PaymentMethodsPage } from '@/pages/payments/PaymentMethodsPage';
import { PaymentTypesPage } from '@/pages/payments/PaymentTypesPage';
import { PaymentTypeFormPage } from '@/pages/payments/PaymentTypeFormPage';
import { PaymentIntegrationsPage } from '@/pages/payments/PaymentIntegrationsPage';
import { PaymentIntegrationFormPage } from '@/pages/payments/PaymentIntegrationFormPage';
import { BanksPage } from '@/pages/payments/BanksPage';
import { ContactDirectoryPage } from '@/pages/content/ContactDirectoryPage';
import { FooterChannelsPage } from '@/pages/content/FooterChannelsPage';
import { FooterBrandPage } from '@/pages/content/FooterBrandPage';
import { FooterLinksPage } from '@/pages/content/FooterLinksPage';
import { TicketCategoriesPage } from '@/pages/tickets/TicketCategoriesPage';
import { TicketConversationPage } from '@/pages/tickets/TicketConversationPage';
import { TicketDetailsPage } from '@/pages/tickets/TicketDetailsPage';
import { TicketsPage } from '@/pages/tickets/TicketsPage';
import { BankFormPage } from '@/pages/payments/BankFormPage';
import { PaymentMethodFormPage } from '@/pages/payments/PaymentMethodFormPage';
import { ClientsPage } from '@/pages/clients/ClientsPage';
import { OrderDetailPage } from '@/pages/orders/OrderDetailPage';
import { WalletRequestHistoryPage } from '@/pages/wallet/WalletRequestHistoryPage';
import { WalletRequestsPage } from '@/pages/wallet/WalletRequestsPage';
import { ApplicationsQueuePage } from '@/pages/applications/ApplicationsQueuePage';
import { AdminApplicationDetailPage } from '@/pages/applications/AdminApplicationDetailPage';
import { RolesPage } from '@/pages/rbac/RolesPage';
import { AdminUsersPage } from '@/pages/rbac/AdminUsersPage';
import { AdminUserFormPage } from '@/pages/rbac/AdminUserFormPage';
import { LandingStatsPage } from '@/pages/content/LandingStatsPage';
import { LandingStepsPage } from '@/pages/content/LandingStepsPage';
import { LandingTrustPage } from '@/pages/content/LandingTrustPage';
import { CoveragePage } from '@/pages/content/CoveragePage';
import { LandingCtaPage } from '@/pages/content/LandingCtaPage';
import { LandingFeaturesPage } from '@/pages/content/LandingFeaturesPage';
import { ToolsPage } from '@/pages/content/ToolsPage';
import { WizardContentPage } from '@/pages/content/WizardContentPage';
import { HeaderLinksPage } from '@/pages/content/HeaderLinksPage';
import { AuditLogPage } from '@/pages/audit/AuditLogPage';
import { EmailConfigurationsPage } from '@/pages/settings/EmailConfigurationsPage';
import { SettingsPage } from '@/pages/settings/SettingsPage';
import { PasswordResetsPage } from '@/pages/settings/PasswordResetsPage';
import { AdminLayout } from '../layout/AdminLayout';
import { RequireAdmin, RequirePermission } from './guards';

/** Each lookup is its own route (and sidebar entry), guarded by its own View permission. */
export const LOOKUP_ROUTES: { key: LookupKey; permission: string }[] = [
  { key: 'countries', permission: Permissions.CountriesView },
  { key: 'currencies', permission: Permissions.CurrenciesView },
  { key: 'addressees', permission: Permissions.AddresseesView },
  { key: 'transactionTypes', permission: Permissions.TransactionTypesView },
  { key: 'subTransactionTypes', permission: Permissions.SubTransactionTypesView },
  { key: 'authorities', permission: Permissions.AuthoritiesView },
  { key: 'serviceTypes', permission: Permissions.ServiceTypesView },
];

/**
 * Every module is wrapped in the permission it needs. The sidebar hides what the admin cannot
 * use; these guards cover the case of navigating straight to a URL. Neither is the real boundary —
 * the API enforces the same permission on every request.
 */
export const router = createBrowserRouter([
  { path: '/login', element: <AdminLoginPage /> },
  { path: '/forgot-password', element: <ForgotPasswordPage /> },
  { path: '/reset-password', element: <ResetPasswordPage /> },
  {
    path: '/',
    element: (
      <RequireAdmin>
        <AdminLayout />
      </RequireAdmin>
    ),
    children: [
      {
        index: true,
        element: (
          <RequirePermission permissions={[Permissions.DashboardView]}>
            <DashboardPage />
          </RequirePermission>
        ),
      },
      {
        path: 'applications',
        element: (
          <RequirePermission permissions={[Permissions.ApplicationsView]}>
            <ApplicationsQueuePage />
          </RequirePermission>
        ),
      },
      {
        path: 'applications/:id',
        element: (
          <RequirePermission permissions={[Permissions.ApplicationsView]}>
            <AdminApplicationDetailPage />
          </RequirePermission>
        ),
      },
      {
        path: 'orders',
        element: (
          <RequirePermission permissions={[Permissions.OrdersView]}>
            <OrdersPage />
          </RequirePermission>
        ),
      },
      {
        path: 'orders/:id',
        element: (
          <RequirePermission permissions={[Permissions.OrdersView]}>
            <OrderDetailPage />
          </RequirePermission>
        ),
      },
      // Seeing the queue needs only Orders.View; the decision buttons are gated per row on
      // Orders.Credit / Orders.Withdraw, and the API enforces the same split.
      {
        path: 'wallet-requests',
        element: (
          <RequirePermission permissions={[Permissions.OrdersView]}>
            <WalletRequestsPage />
          </RequirePermission>
        ),
      },
      {
        path: 'wallet-requests/:id',
        element: (
          <RequirePermission permissions={[Permissions.OrdersView]}>
            <WalletRequestHistoryPage />
          </RequirePermission>
        ),
      },
      {
        path: 'clients',
        element: (
          <RequirePermission permissions={[Permissions.ClientsView]}>
            <ClientsPage />
          </RequirePermission>
        ),
      },
      // Payment methods are configuration, but heavy enough for their own module: a method spans
      // a type, a country/currency mapping and a list of receiving accounts. The type catalogue
      // behind them is its own page, since it is set up once rather than worked through daily.
      { path: 'payments', element: <Navigate to="/payments/methods" replace /> },
      {
        path: 'payments/types',
        element: (
          <RequirePermission permissions={[Permissions.PaymentMethodsView]}>
            <PaymentTypesPage />
          </RequirePermission>
        ),
      },
      // Support tickets raised through the public contact form.
      {
        path: 'tickets',
        element: (
          <RequirePermission permissions={[Permissions.TicketsView]}>
            <TicketsPage />
          </RequirePermission>
        ),
      },
      {
        path: 'tickets/:id',
        element: (
          <RequirePermission permissions={[Permissions.TicketsView]}>
            <TicketDetailsPage />
          </RequirePermission>
        ),
      },
      {
        path: 'contact-directory',
        element: (
          <RequirePermission permissions={[Permissions.ContactDirectoryView]}>
            <ContactDirectoryPage />
          </RequirePermission>
        ),
      },
      {
        path: 'footer-brand',
        element: (
          <RequirePermission permissions={[Permissions.LandingContentView]}>
            <FooterBrandPage />
          </RequirePermission>
        ),
      },
      {
        path: 'footer-links',
        element: (
          <RequirePermission permissions={[Permissions.LandingContentView]}>
            <FooterLinksPage />
          </RequirePermission>
        ),
      },
      {
        path: 'footer-channels',
        element: (
          <RequirePermission permissions={[Permissions.SocialLinksView]}>
            <FooterChannelsPage />
          </RequirePermission>
        ),
      },
      {
        path: 'tickets/:id/conversation',
        element: (
          <RequirePermission permissions={[Permissions.TicketsView]}>
            <TicketConversationPage />
          </RequirePermission>
        ),
      },
      {
        path: 'ticket-categories',
        element: (
          <RequirePermission permissions={[Permissions.TicketCategoriesView]}>
            <TicketCategoriesPage />
          </RequirePermission>
        ),
      },
      // The bank catalogue a bank-transfer method picks from.
      {
        path: 'banks',
        element: (
          <RequirePermission permissions={[Permissions.PaymentMethodsView]}>
            <BanksPage />
          </RequirePermission>
        ),
      },
      {
        path: 'banks/new',
        element: (
          <RequirePermission permissions={[Permissions.PaymentMethodsCreate]}>
            <BankFormPage />
          </RequirePermission>
        ),
      },
      {
        path: 'banks/:id/edit',
        element: (
          <RequirePermission permissions={[Permissions.PaymentMethodsUpdate]}>
            <BankFormPage />
          </RequirePermission>
        ),
      },
      {
        path: 'payments/types/new',
        element: (
          <RequirePermission permissions={[Permissions.PaymentMethodsCreate]}>
            <PaymentTypeFormPage />
          </RequirePermission>
        ),
      },
      {
        path: 'payments/types/:id/edit',
        element: (
          <RequirePermission permissions={[Permissions.PaymentMethodsUpdate]}>
            <PaymentTypeFormPage />
          </RequirePermission>
        ),
      },
      {
        path: 'payments/integrations',
        element: (
          <RequirePermission permissions={[Permissions.PaymentMethodsView]}>
            <PaymentIntegrationsPage />
          </RequirePermission>
        ),
      },
      {
        path: 'payments/integrations/new',
        element: (
          <RequirePermission permissions={[Permissions.PaymentMethodsCreate]}>
            <PaymentIntegrationFormPage />
          </RequirePermission>
        ),
      },
      {
        path: 'payments/integrations/:id/edit',
        element: (
          <RequirePermission permissions={[Permissions.PaymentMethodsUpdate]}>
            <PaymentIntegrationFormPage />
          </RequirePermission>
        ),
      },
      {
        path: 'payments/methods',
        element: (
          <RequirePermission permissions={[Permissions.PaymentMethodsView]}>
            <PaymentMethodsPage />
          </RequirePermission>
        ),
      },
      {
        path: 'payments/methods/new',
        element: (
          <RequirePermission permissions={[Permissions.PaymentMethodsCreate]}>
            <PaymentMethodFormPage />
          </RequirePermission>
        ),
      },
      {
        path: 'payments/methods/:id/edit',
        element: (
          <RequirePermission permissions={[Permissions.PaymentMethodsUpdate]}>
            <PaymentMethodFormPage />
          </RequirePermission>
        ),
      },
      // Landing on the bare /lookups path sends the admin to the first lookup they can view.
      { path: 'lookups', element: <Navigate to="/lookups/countries" replace /> },
      // Authorities create/edit are full pages, not a dialog: the form spans a country, both
      // names and a mapping across the whole transaction cascade.
      {
        path: 'lookups/authorities/new',
        element: (
          <RequirePermission permissions={[Permissions.AuthoritiesCreate]}>
            <AuthorityFormPage />
          </RequirePermission>
        ),
      },
      {
        path: 'lookups/authorities/:id/edit',
        element: (
          <RequirePermission permissions={[Permissions.AuthoritiesUpdate]}>
            <AuthorityFormPage />
          </RequirePermission>
        ),
      },
      {
        path: 'lookups/transactionTypes/new',
        element: (
          <RequirePermission permissions={[Permissions.TransactionTypesCreate]}>
            <TransactionTypeFormPage />
          </RequirePermission>
        ),
      },
      {
        path: 'lookups/transactionTypes/:id/edit',
        element: (
          <RequirePermission permissions={[Permissions.TransactionTypesUpdate]}>
            <TransactionTypeFormPage />
          </RequirePermission>
        ),
      },
      {
        path: 'lookups/subTransactionTypes/new',
        element: (
          <RequirePermission permissions={[Permissions.SubTransactionTypesCreate]}>
            <SubTransactionTypeFormPage />
          </RequirePermission>
        ),
      },
      {
        path: 'lookups/subTransactionTypes/:id/edit',
        element: (
          <RequirePermission permissions={[Permissions.SubTransactionTypesUpdate]}>
            <SubTransactionTypeFormPage />
          </RequirePermission>
        ),
      },
      {
        path: 'lookups/currencies/new',
        element: (
          <RequirePermission permissions={[Permissions.CurrenciesCreate]}>
            <CurrencyFormPage />
          </RequirePermission>
        ),
      },
      {
        path: 'lookups/currencies/:id/edit',
        element: (
          <RequirePermission permissions={[Permissions.CurrenciesUpdate]}>
            <CurrencyFormPage />
          </RequirePermission>
        ),
      },
      {
        path: 'lookups/countries/new',
        element: (
          <RequirePermission permissions={[Permissions.CountriesCreate]}>
            <CountryFormPage />
          </RequirePermission>
        ),
      },
      {
        path: 'lookups/countries/:id/edit',
        element: (
          <RequirePermission permissions={[Permissions.CountriesUpdate]}>
            <CountryFormPage />
          </RequirePermission>
        ),
      },
      ...LOOKUP_ROUTES.map(({ key, permission }) => ({
        path: `lookups/${key}`,
        element: (
          <RequirePermission permissions={[permission]}>
            <LookupModulePage module={key} />
          </RequirePermission>
        ),
      })),
      {
        path: 'roles',
        element: (
          <RequirePermission permissions={[Permissions.RolesView]}>
            <RolesPage />
          </RequirePermission>
        ),
      },
      {
        path: 'users',
        element: (
          <RequirePermission permissions={[Permissions.AdminUsersView]}>
            <AdminUsersPage />
          </RequirePermission>
        ),
      },
      // Creating and editing an administrator are full pages: the form carries the account plus
      // the whole permission matrix.
      {
        path: 'users/new',
        element: (
          <RequirePermission permissions={[Permissions.AdminUsersCreate]}>
            <AdminUserFormPage />
          </RequirePermission>
        ),
      },
      {
        path: 'users/:id/edit',
        element: (
          <RequirePermission permissions={[Permissions.AdminUsersUpdate]}>
            <AdminUserFormPage />
          </RequirePermission>
        ),
      },
      // The landing editor is one page per section now. The bare path still resolves, so a
      // bookmark or an old link lands on the first of them rather than on nothing.
      { path: 'landing-content', element: <Navigate to="/landing-content/stats" replace /> },
      // The headings moved onto the pages of the sections they label; an old bookmark still lands
      // somewhere sensible rather than on nothing.
      { path: 'landing-content/headings', element: <Navigate to="/landing-content/features" replace /> },
      {
        path: 'landing-content/stats',
        element: (
          <RequirePermission permissions={[Permissions.LandingContentView]}>
            <LandingStatsPage />
          </RequirePermission>
        ),
      },
      {
        path: 'landing-content/steps',
        element: (
          <RequirePermission permissions={[Permissions.LandingContentView]}>
            <LandingStepsPage />
          </RequirePermission>
        ),
      },
      {
        path: 'landing-content/trusted-by',
        element: (
          <RequirePermission permissions={[Permissions.LandingContentView]}>
            <LandingTrustPage />
          </RequirePermission>
        ),
      },
      {
        path: 'landing-content/cta',
        element: (
          <RequirePermission permissions={[Permissions.LandingContentView]}>
            <LandingCtaPage />
          </RequirePermission>
        ),
      },
      {
        path: 'landing-content/coverage',
        element: (
          <RequirePermission permissions={[Permissions.LandingContentView]}>
            <CoveragePage />
          </RequirePermission>
        ),
      },
      {
        path: 'landing-content/features',
        element: (
          <RequirePermission permissions={[Permissions.LandingContentView]}>
            <LandingFeaturesPage />
          </RequirePermission>
        ),
      },
      {
        // The site header sits above every page, marketing or not, so it is edited on its own.
        path: 'header-navigation',
        element: (
          <RequirePermission permissions={[Permissions.LandingContentView]}>
            <HeaderLinksPage />
          </RequirePermission>
        ),
      },
      {
        // Not under landing-content: this is the applicant's application form, not the marketing
        // page, and it is reached from its own sidebar entry.
        path: 'wizard-content',
        element: (
          <RequirePermission permissions={[Permissions.LandingContentView]}>
            <WizardContentPage />
          </RequirePermission>
        ),
      },
      {
        path: 'audit',
        element: (
          <RequirePermission permissions={[Permissions.AuditLogView]}>
            <AuditLogPage />
          </RequirePermission>
        ),
      },
      {
        path: 'tools',
        element: (
          <RequirePermission permissions={[Permissions.ToolsView]}>
            <ToolsPage />
          </RequirePermission>
        ),
      },
      {
        path: 'email-configurations',
        element: (
          <RequirePermission permissions={[Permissions.SettingsView]}>
            <EmailConfigurationsPage />
          </RequirePermission>
        ),
      },
      {
        path: 'settings',
        element: (
          <RequirePermission permissions={[Permissions.SettingsView]}>
            <SettingsPage />
          </RequirePermission>
        ),
      },
      {
        path: 'password-resets',
        element: (
          <RequirePermission permissions={[Permissions.SettingsView]}>
            <PasswordResetsPage />
          </RequirePermission>
        ),
      },
      // Every administrator has an account, so the profile needs no particular permission.
      { path: 'profile', element: <ProfilePage /> },
      { path: '*', element: <Navigate to="/" replace /> },
    ],
  },
]);
