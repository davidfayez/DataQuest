import { Alert } from '@dv/ui';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Navigate } from 'react-router-dom';
import { useAdminSession, usePermission } from '@/features/auth/useAdminSession';

export function RequireAdmin({ children }: { children: ReactNode }) {
  const session = useAdminSession();
  return session ? <>{children}</> : <Navigate to="/login" replace />;
}

/**
 * Renders a module only when the admin holds one of its permissions. Reaching the route directly
 * without permission shows a refusal rather than a blank screen — and the API would refuse the
 * underlying request anyway.
 */
export function RequirePermission({
  permissions,
  children,
}: {
  permissions: string[];
  children: ReactNode;
}) {
  const { t } = useTranslation();
  const allowed = usePermission(...permissions);

  return allowed ? <>{children}</> : <Alert variant="error">{t('errors.forbidden')}</Alert>;
}
