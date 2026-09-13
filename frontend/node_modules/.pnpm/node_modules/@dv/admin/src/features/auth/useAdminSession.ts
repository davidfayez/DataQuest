import { useSyncExternalStore } from 'react';
import { adminSession, type AdminSession } from './session';

export function useAdminSession(): AdminSession | null {
  return useSyncExternalStore(adminSession.subscribe, adminSession.getSnapshot, () => null);
}

/**
 * Permission check for rendering decisions. The server enforces the same permission on every
 * endpoint, so hiding a control here is a convenience, never the security boundary.
 */
export function usePermission(...permissions: string[]): boolean {
  const session = useAdminSession();
  if (!session) return false;
  return permissions.some((permission) => session.permissions.has(permission));
}
