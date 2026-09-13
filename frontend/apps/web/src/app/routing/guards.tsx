import type { ReactNode } from 'react';
import { Navigate, useLocation, useParams } from 'react-router-dom';
import { useSession } from '@/features/auth/useSession';

/**
 * Route guards. These improve the experience — the server is what actually enforces access, and
 * every applicant endpoint is scoped to the order on the token regardless of what the UI allows.
 */

export function RequireAuth({ children }: { children: ReactNode }) {
  const session = useSession();
  const location = useLocation();
  const { lang = 'en' } = useParams<{ lang: string }>();

  if (!session) {
    // Remember where they were headed so sign-in can return them there.
    return <Navigate to={`/${lang}/login`} state={{ from: location.pathname }} replace />;
  }

  return <>{children}</>;
}

/** Blocks the app until the order's country and currency have been chosen. */
export function RequireSetup({ children }: { children: ReactNode }) {
  const session = useSession();
  const { lang = 'en' } = useParams<{ lang: string }>();

  if (session && !session.isSetupComplete) {
    return <Navigate to={`/${lang}/setup`} replace />;
  }

  return <>{children}</>;
}

/** Keeps a signed-in applicant away from the sign-in and registration screens. */
export function RedirectIfAuthenticated({ children }: { children: ReactNode }) {
  const session = useSession();
  const { lang = 'en' } = useParams<{ lang: string }>();

  if (session) {
    return <Navigate to={session.isSetupComplete ? `/${lang}/applications` : `/${lang}/setup`} replace />;
  }

  return <>{children}</>;
}
