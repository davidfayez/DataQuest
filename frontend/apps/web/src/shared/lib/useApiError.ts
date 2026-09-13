import { ApiError } from '@dv/api-client';
import { useTranslation } from 'react-i18next';

/**
 * Turns an unknown thrown value into a message the user can act on.
 *
 * The API supplies a stable `code` on every problem, so the UI localizes from that rather than
 * displaying the server's English `detail`. Codes without a dedicated translation fall back to a
 * generic message carrying the trace id for support.
 */
export function useApiErrorMessage() {
  const { t } = useTranslation();

  return (error: unknown): string => {
    if (!(error instanceof ApiError)) {
      // Only a request that never got an answer is a connection problem. Everything else that
      // lands here is a fault in the app itself, and telling somebody to check their connection
      // sends them to restart a router over a bug that would still be there afterwards.
      return isNetworkFailure(error)
        ? t('errors.networkBody')
        : t('errors.genericBody', { traceId: '—' });
    }

    switch (error.status) {
      case 401:
        return t('login.invalidCredentials');
      case 403:
        return t('errors.forbidden');
      case 404:
        return t('errors.notFound');
      case 429:
        return t('errors.tooManyRequests');
      default:
        break;
    }

    // Field-level validation messages are rendered next to their inputs; the banner summarises.
    if (error.status === 400) {
      const first = Object.values(error.fieldErrors)[0]?.[0];
      if (first) return first;
    }

    // A domain rule violation already carries a human-readable explanation from the server, and
    // so does a rejected upload — 413 and 415 come from the upload security gate and say exactly
    // why the file was refused, which is far more useful than the generic fallback.
    if (
      error.status === 409 ||
      error.status === 422 ||
      error.status === 413 ||
      error.status === 415
    ) {
      return error.problem.detail ?? t('errors.genericTitle');
    }

    return t('errors.genericBody', { traceId: error.problem.traceId ?? '—' });
  };
}

/**
 * Whether the request failed before any response existed.
 *
 * `fetch` rejects with a TypeError when the network is down, DNS fails, the server refuses the
 * connection, or CORS blocks the response outright — and it rejects with an AbortError when the
 * request was cancelled. Anything else reaching the error path is an exception thrown by our own
 * code, which is not something the reader can fix by reconnecting.
 */
function isNetworkFailure(error: unknown): boolean {
  if (error instanceof DOMException && error.name === 'AbortError') {
    return true;
  }

  return error instanceof TypeError;
}
