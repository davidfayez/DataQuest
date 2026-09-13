/**
 * Session state for the applicant realm.
 *
 * The access token is held in memory only — never in localStorage — so an XSS payload cannot read
 * it back out of persistent storage. Order number / setup status are persisted for UI convenience;
 * after a reload the access token is renewed from the httpOnly refresh cookie via `orders/refresh`.
 */

export interface Session {
  accessToken: string;
  expiresAtUtc: string;
  orderId: string;
  orderNumber: string;
  isSetupComplete: boolean;
}

/** The slice that survives a page reload. Deliberately excludes the token. */
interface PersistedSession {
  orderId: string;
  orderNumber: string;
  isSetupComplete: boolean;
}

const STORAGE_KEY = 'dv.session';

let current: Session | null = null;
const listeners = new Set<() => void>();

function notify() {
  for (const listener of listeners) listener();
}

export const sessionStore = {
  subscribe(listener: () => void): () => void {
    listeners.add(listener);
    return () => listeners.delete(listener);
  },

  getSnapshot(): Session | null {
    return current;
  },

  set(session: Session): void {
    current = session;

    const persisted: PersistedSession = {
      orderId: session.orderId,
      orderNumber: session.orderNumber,
      isSetupComplete: session.isSetupComplete,
    };
    localStorage.setItem(STORAGE_KEY, JSON.stringify(persisted));

    notify();
  },

  /** Records that setup finished without forcing a re-login. */
  markSetupComplete(): void {
    if (!current) return;
    current = { ...current, isSetupComplete: true };

    const persisted = readPersisted();
    if (persisted) {
      localStorage.setItem(STORAGE_KEY, JSON.stringify({ ...persisted, isSetupComplete: true }));
    }

    notify();
  },

  clear(): void {
    current = null;
    localStorage.removeItem(STORAGE_KEY);
    notify();
  },

  getAccessToken(): string | null {
    if (!current) return null;

    // Treat an expired token as absent so the UI redirects to sign-in rather than firing a
    // request that is certain to come back 401.
    if (new Date(current.expiresAtUtc).getTime() <= Date.now()) {
      return null;
    }

    return current.accessToken;
  },

  /** The order number from a previous visit, used to prefill the sign-in form. */
  getRememberedOrderNumber(): string | null {
    return readPersisted()?.orderNumber ?? null;
  },
};

function readPersisted(): PersistedSession | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    return raw ? (JSON.parse(raw) as PersistedSession) : null;
  } catch {
    return null;
  }
}
