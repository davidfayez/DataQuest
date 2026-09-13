import { useSyncExternalStore } from 'react';
import { sessionStore, type Session } from './session';

/** Subscribes a component to the in-memory session. */
export function useSession(): Session | null {
  return useSyncExternalStore(sessionStore.subscribe, sessionStore.getSnapshot, () => null);
}

export function useIsAuthenticated(): boolean {
  return useSession() !== null;
}
