import { QueryClientProvider } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { I18nextProvider } from 'react-i18next';
import type { i18n as I18nInstance } from 'i18next';
import { RouterProvider } from 'react-router-dom';
import { LoadingState, LogoSourceProvider } from '@dv/ui';
import { restoreSession } from '@/features/auth/api';
import { logoSrc, useBranding } from '@/features/branding/api';
import { queryClient } from '@/shared/api/client';
import { setupI18n } from '@/shared/config/i18n';
import { router } from './routing/router';
import { ErrorBoundary } from './ErrorBoundary';

/**
 * Supplies the administrator's logo to every <Logo> in the tree.
 *
 * Inside the query provider because it fetches, and above the router so the sign-in and error
 * screens carry the brand too. While the request is in flight the bundled mark shows, which is
 * the same thing the site drew before the logo was configurable.
 */
function BrandedRouter() {
  const branding = useBranding();

  return (
    <LogoSourceProvider src={logoSrc(branding.data)}>
      <RouterProvider router={router} />
    </LogoSourceProvider>
  );
}

/**
 * Application root. i18next resolves asynchronously because locale bundles are lazy-loaded, so
 * the tree renders only once the active language is in place — this avoids a flash of untranslated
 * keys and, for ar/ur, a flash of the wrong text direction. In parallel, a session is restored from
 * the refresh cookie so a page reload does not sign the applicant out.
 */
export function App() {
  const [i18n, setI18n] = useState<I18nInstance | null>(null);
  const [sessionRestored, setSessionRestored] = useState(false);

  useEffect(() => {
    let cancelled = false;

    void setupI18n().then((instance) => {
      if (!cancelled) setI18n(instance);
    });

    void restoreSession().finally(() => {
      if (!cancelled) setSessionRestored(true);
    });

    return () => {
      cancelled = true;
    };
  }, []);

  if (!i18n || !sessionRestored) {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <LoadingState label="Loading…" />
      </div>
    );
  }

  return (
    <ErrorBoundary
      fallback={
        <div className="flex min-h-screen items-center justify-center p-6 text-center">
          <p className="text-sm text-muted-foreground">
            Something went wrong. Please reload the page.
          </p>
        </div>
      }
    >
      <I18nextProvider i18n={i18n}>
        <QueryClientProvider client={queryClient}>
          <BrandedRouter />
        </QueryClientProvider>
      </I18nextProvider>
    </ErrorBoundary>
  );
}
