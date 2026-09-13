/**
 * Runtime configuration. The dev server proxies `/api` to the backend, so the default base URL is
 * relative and no CORS setup is needed locally.
 */
export const env = {
  apiBaseUrl: import.meta.env.VITE_API_BASE_URL ?? '/api/v1/',
  isDevelopment: import.meta.env.DEV,
} as const;
