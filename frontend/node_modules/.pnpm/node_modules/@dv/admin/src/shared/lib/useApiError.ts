import { ApiError } from '@dv/api-client';
import { useTranslation } from 'react-i18next';

/** Turns an unknown thrown value into a localized message, keyed off the API's stable codes. */
export function useApiErrorMessage() {
  const { t } = useTranslation();

  return (error: unknown): string => {
    if (!(error instanceof ApiError)) return t('errors.genericTitle');

    // A rejected sign-in and an expired session are both 401, and telling someone their session
    // ended when they simply mistyped a password sends them looking for the wrong problem. The
    // API separates the two with a stable code, so key off that rather than the status alone.
    if (error.code === 'auth.invalid_credentials') {
      return t('login.invalid');
    }

    switch (error.status) {
      case 401:
        return t('errors.sessionExpired');
      case 403:
        return t('errors.forbidden');
      case 404:
        return t('errors.notFound');
      case 429:
        return t('errors.tooManyRequests');
      default:
        break;
    }

    if (error.status === 400) {
      const first = Object.values(error.fieldErrors)[0]?.[0];
      if (first) return first;
    }

    // Domain rule violations already carry a readable explanation from the server.
    if (error.status === 409 || error.status === 422) {
      return error.problem.detail ?? t('errors.genericTitle');
    }

    return t('errors.genericBody', { traceId: error.problem.traceId ?? '—' });
  };
}
