import { createBrowserRouter } from 'react-router-dom';
import { AppLayout } from '../layout/AppLayout';
import { LandingPage } from '@/pages/landing/LandingPage';
import { RegisterPage } from '@/pages/register/RegisterPage';
import { LoginPage } from '@/pages/login/LoginPage';
import { ForgotPasswordPage } from '@/pages/login/ForgotPasswordPage';
import { OrderSetupPage } from '@/pages/setup/OrderSetupPage';
import { ApplicationsPage } from '@/pages/applications/ApplicationsPage';
import { NewApplicationPage } from '@/pages/wizard/NewApplicationPage';
import { ApplicationDetailsPage } from '@/pages/applications/ApplicationDetailsPage';
import { ApplicationLogPage } from '@/pages/applications/ApplicationLogPage';
import { WalletPage } from '@/pages/wallet/WalletPage';
import { WalletRequestDetailsPage } from '@/pages/wallet/WalletRequestDetailsPage';
import { AddFundsPage } from '@/pages/wallet/AddFundsPage';
import { WithdrawPage } from '@/pages/wallet/WithdrawPage';
import { ToolsPage } from '@/pages/tools/ToolsPage';
import { NotFoundPage } from '@/pages/errors/NotFoundPage';
import { LanguageBoundary } from './LanguageBoundary';
import { LocaleRedirect } from './LocaleRedirect';
import { ContactPage } from '@/pages/contact/ContactPage';
import { MyTicketDetailsPage } from '@/pages/tickets/MyTicketDetailsPage';
import { MyTicketsPage } from '@/pages/tickets/MyTicketsPage';
import { RedirectIfAuthenticated, RequireAuth, RequireSetup } from './guards';

/**
 * Every route is namespaced under `/{lang}/`, so a URL carries its language and can be shared or
 * bookmarked in that language. A bare path redirects to the detected locale.
 */
export const router = createBrowserRouter([
  { path: '/', element: <LocaleRedirect /> },
  {
    path: '/:lang',
    // Keeps the i18next language in step with the URL segment on every navigation.
    element: <LanguageBoundary />,
    children: [
      {
        element: <AppLayout />,
        children: [
          { index: true, element: <LandingPage /> },
          // Public: the guides are useful before signing up, not only after.
          { path: 'tools', element: <ToolsPage /> },
          // Public and unguarded, including for a signed-in applicant: someone who cannot get
          // into their order is exactly who needs to reach support.
          { path: 'contact', element: <ContactPage /> },
          {
            path: 'register',
            element: (
              <RedirectIfAuthenticated>
                <RegisterPage />
              </RedirectIfAuthenticated>
            ),
          },
          {
            path: 'login',
            element: (
              <RedirectIfAuthenticated>
                <LoginPage />
              </RedirectIfAuthenticated>
            ),
          },
          {
            path: 'forgot-password',
            element: (
              <RedirectIfAuthenticated>
                <ForgotPasswordPage />
              </RedirectIfAuthenticated>
            ),
          },
          {
            path: 'setup',
            element: (
              <RequireAuth>
                <OrderSetupPage />
              </RequireAuth>
            ),
          },
          {
            path: 'applications',
            element: (
              <RequireAuth>
                <RequireSetup>
                  <ApplicationsPage />
                </RequireSetup>
              </RequireAuth>
            ),
          },
          {
            path: 'applications/new',
            element: (
              <RequireAuth>
                <RequireSetup>
                  <NewApplicationPage />
                </RequireSetup>
              </RequireAuth>
            ),
          },
          {
            // The wizard in edit mode: the same six steps, hydrated from the saved draft.
            path: 'applications/:id/edit',
            element: (
              <RequireAuth>
                <RequireSetup>
                  <NewApplicationPage />
                </RequireSetup>
              </RequireAuth>
            ),
          },
          {
            // Declared before the catch-all details route so the segment is not read as an id.
            path: 'applications/:id/log',
            element: (
              <RequireAuth>
                <RequireSetup>
                  <ApplicationLogPage />
                </RequireSetup>
              </RequireAuth>
            ),
          },
          {
            path: 'applications/:id',
            element: (
              <RequireAuth>
                <RequireSetup>
                  <ApplicationDetailsPage />
                </RequireSetup>
              </RequireAuth>
            ),
          },
          // Support history. Behind RequireAuth but deliberately not RequireSetup: an applicant
          // whose order is half-configured is exactly the person who has written to support.
          {
            path: 'tickets',
            element: (
              <RequireAuth>
                <MyTicketsPage />
              </RequireAuth>
            ),
          },
          {
            path: 'tickets/:id',
            element: (
              <RequireAuth>
                <MyTicketDetailsPage />
              </RequireAuth>
            ),
          },
          {
            path: 'wallet/requests/:id',
            element: (
              <RequireAuth>
                <RequireSetup>
                  <WalletRequestDetailsPage />
                </RequireSetup>
              </RequireAuth>
            ),
          },
          // Adding funds and withdrawing are pages of their own, reached from the wallet.
          {
            path: 'wallet/add-funds',
            element: (
              <RequireAuth>
                <RequireSetup>
                  <AddFundsPage />
                </RequireSetup>
              </RequireAuth>
            ),
          },
          {
            path: 'wallet/withdraw',
            element: (
              <RequireAuth>
                <RequireSetup>
                  <WithdrawPage />
                </RequireSetup>
              </RequireAuth>
            ),
          },
          {
            path: 'wallet',
            element: (
              <RequireAuth>
                <RequireSetup>
                  <WalletPage />
                </RequireSetup>
              </RequireAuth>
            ),
          },
          { path: '*', element: <NotFoundPage /> },
        ],
      },
    ],
  },
  { path: '*', element: <LocaleRedirect /> },
]);
