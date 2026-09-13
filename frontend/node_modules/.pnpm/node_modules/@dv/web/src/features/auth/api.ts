import { apiClient } from '@/shared/api/client';
import { sessionStore } from './session';

export interface RegisterOrderResponse {
  email: string;
  /** Present only when the API is running with credential echo enabled for local development. */
  orderNumber: string | null;
  password: string | null;
}

export interface LoginOrderResponse {
  accessToken: string;
  expiresAtUtc: string;
  orderId: string;
  orderNumber: string;
  isSetupComplete: boolean;
  languageCode: string;
}

export interface ForgotOrderPasswordResponse {
  /** The mailbox the new password went to, masked. Null when no such order exists. */
  maskedEmail: string | null;
  /** How long the emailed password stays good for; the page says the same as the email. */
  validityMinutes: number;
}

/**
 * Asks for a fresh password on an order. Always resolves — an unknown order number comes back with
 * no masked address rather than an error, so the screen cannot be used to discover order numbers.
 */
export function forgotOrderPassword(orderNumber: string) {
  return apiClient.post<ForgotOrderPasswordResponse>('orders/forgot-password', {
    orderNumber: orderNumber.trim().toUpperCase(),
  });
}

export function registerOrder(email: string, languageCode: string) {
  return apiClient.post<RegisterOrderResponse>('orders/register', { email, languageCode });
}

/** Installs a login/refresh payload as the active session. */
function installSession(result: LoginOrderResponse) {
  sessionStore.set({
    accessToken: result.accessToken,
    expiresAtUtc: result.expiresAtUtc,
    orderId: result.orderId,
    orderNumber: result.orderNumber,
    isSetupComplete: result.isSetupComplete,
  });
}

/** Signs in and installs the session, so callers never handle the raw token. */
export async function loginOrder(orderNumber: string, password: string) {
  const result = await apiClient.post<LoginOrderResponse>('orders/login', {
    orderNumber: orderNumber.trim().toUpperCase(),
    password,
  });

  installSession(result);
  return result;
}

/**
 * Attempts to restore a session from the httpOnly refresh cookie. Called on app start (so a reload
 * keeps the applicant signed in) and by the client on a 401 (so an expired access token is renewed
 * silently). Resolves `true` when a new access token was installed.
 */
export async function restoreSession(): Promise<boolean> {
  try {
    const result = await apiClient.post<LoginOrderResponse>('orders/refresh', undefined, {
      skipAuthRefresh: true,
    });
    installSession(result);
    return true;
  } catch {
    return false;
  }
}

/** Signs out: clears the server's refresh cookie, then drops the in-memory session. */
export async function logout(): Promise<void> {
  try {
    await apiClient.post('orders/logout', undefined, { skipAuthRefresh: true });
  } catch {
    // The session is being torn down regardless of whether the network call succeeds.
  }
  sessionStore.clear();
}

// Let the client renew an expired access token from the refresh cookie without importing this
// module (which would create a cycle with the client).
apiClient.setRefreshSession(restoreSession);

export interface SetupOrderResponse {
  verificationCountryId: string;
  countryName: string;
  currencyId: string;
  currencyCode: string;
  walletBalance: number;
  contactPersonName: string;
  /** Dial code and national number joined, e.g. `+201001234567`. */
  contactPersonPhone: string;
}

export interface SetupOrderPayload {
  verificationCountryId: string;
  currencyId: string;
  contactPersonName: string;
  /** ISO 3166-1 alpha-2 of the dial code chosen, e.g. `EG`. */
  contactPersonPhoneCountry: string;
  /** The dial prefix itself, e.g. `+20`. */
  contactPersonPhoneCode: string;
  /** The national number, digits only. */
  contactPersonPhoneNumber: string;
}

export async function setupOrder(payload: SetupOrderPayload) {
  const result = await apiClient.put<SetupOrderResponse>('orders/setup', payload);

  sessionStore.markSetupComplete();
  return result;
}
