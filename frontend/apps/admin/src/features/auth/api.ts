import { apiClient } from '@/shared/api/client';
import { adminSession } from './session';

interface LoginResponse {
  accessToken: string;
  expiresAtUtc: string;
  adminUserId: string;
  fullName: string;
  email: string;
  username: string;
  permissions: string[];
  roles: string[];
  hasAvatar: boolean;
  loginAtUtc: string;
}

/** Installs a login/refresh payload as the active session. */
function installSession(result: LoginResponse) {
  adminSession.set({
    accessToken: result.accessToken,
    expiresAtUtc: result.expiresAtUtc,
    adminUserId: result.adminUserId,
    fullName: result.fullName,
    email: result.email,
    username: result.username,
    permissions: new Set(result.permissions),
    roles: result.roles,
    hasAvatar: result.hasAvatar,
    avatarVersion: 0,
    loginAtUtc: result.loginAtUtc,
  });
}

/**
 * Signs in and installs the session, so callers never handle the raw token.
 * `usernameOrEmail` accepts either handle — the server matches both.
 */
export async function loginAdmin(usernameOrEmail: string, password: string) {
  const result = await apiClient.post<LoginResponse>('admin/auth/login', {
    usernameOrEmail: usernameOrEmail.trim(),
    password,
  });

  installSession(result);
  return result;
}

/**
 * Attempts to restore a session from the httpOnly refresh cookie. Called on app start (so a reload
 * keeps the admin signed in) and by the client on a 401 (so an expired access token is renewed
 * silently). Resolves `true` when a new access token was installed. `skipAuthRefresh` stops a failed
 * refresh from recursing back into another refresh.
 */
export async function restoreAdminSession(): Promise<boolean> {
  try {
    const result = await apiClient.post<LoginResponse>('admin/auth/refresh', undefined, {
      skipAuthRefresh: true,
    });
    installSession(result);
    return true;
  } catch {
    return false;
  }
}

/** Signs out: clears the server's refresh cookie, then drops the in-memory session. */
export async function logoutAdmin(): Promise<void> {
  try {
    await apiClient.post('admin/auth/logout', undefined, { skipAuthRefresh: true });
  } catch {
    // The session is being torn down regardless of whether the network call succeeds.
  }
  adminSession.clear();
}

// Let the client renew an expired access token from the refresh cookie without importing this
// module (which would create a cycle with the client).
apiClient.setRefreshSession(restoreAdminSession);

/** Starts a self-service reset. Always resolves — the server never reveals whether the email exists. */
export function requestPasswordReset(email: string) {
  return apiClient.post('admin/auth/forgot-password', { email: email.trim() });
}

/** Completes a reset with the token from the email link. */
export function resetPassword(token: string, newPassword: string) {
  return apiClient.post('admin/auth/reset-password', { token, newPassword });
}

/** Changes the signed-in administrator's own password. */
export function changePassword(currentPassword: string, newPassword: string) {
  return apiClient.post('admin/profile/change-password', { currentPassword, newPassword });
}

/** Uploads a new profile photo, then flags the session so the header refreshes. */
export async function uploadAvatar(file: File) {
  const form = new FormData();
  form.append('file', file);
  const result = await apiClient.upload<{ hasAvatar: boolean }>('admin/profile/avatar', form);
  adminSession.markAvatarUpdated();
  return result;
}
