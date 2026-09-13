import { ApiError, type ProblemDetails } from './problem-details';

export interface HttpClientOptions {
  baseUrl: string;
  /** Returns the current access token, or null when the caller is anonymous. */
  getAccessToken?: () => string | null;
  /** Invoked once when the API rejects the token, so the app can clear session state. */
  onUnauthorized?: () => void;
  /** Active UI locale, forwarded as `Accept-Language` so the API resolves localized names. */
  getLanguage?: () => string;
  /**
   * Attempts to silently obtain a fresh access token (typically from the httpOnly refresh cookie),
   * installing it into the session. Resolves `true` when a new token is available. When set, a 401
   * triggers one refresh-and-retry before the request is allowed to fail.
   */
  refreshSession?: () => Promise<boolean>;
}

export type QueryValue =
  | string
  | number
  | boolean
  | undefined
  | null
  | ReadonlyArray<string | number | boolean>;

export interface RequestOptions {
  query?: Record<string, QueryValue>;
  signal?: AbortSignal;
  headers?: Record<string, string>;
  /**
   * Locale for this request, overriding the client-wide getter.
   *
   * A caller that caches a localized response must send the same language it files the result
   * under. The client-wide getter is read at fetch time while a cache key is captured at render
   * time, and the two can disagree during a language switch — which caches, say, an English
   * response under the Arabic key, where it then stays. Passing the language explicitly ties the
   * request and the key to one value.
   */
  language?: string;
  /**
   * Skips the automatic refresh-and-retry on a 401. The session-refresh request itself sets this
   * so a failed refresh cannot recurse into refreshing again.
   */
  skipAuthRefresh?: boolean;
}

/**
 * Thin typed wrapper over `fetch`. Every API call in both apps goes through this so auth
 * headers, locale negotiation and ProblemDetails parsing stay in exactly one place.
 */
export class HttpClient {
  /** A single in-flight refresh shared by every request that 401s at the same time. */
  private refreshInFlight: Promise<boolean> | null = null;

  /** How to obtain a fresh access token on a 401; settable after construction to avoid import cycles. */
  private refreshHandler?: () => Promise<boolean>;

  constructor(private readonly options: HttpClientOptions) {
    this.refreshHandler = options.refreshSession;
  }

  /**
   * Registers (or replaces) the session-refresh strategy. Apps call this once at startup so the
   * client can silently renew an expired access token without the auth module and the client
   * importing each other.
   */
  setRefreshSession(refresh: () => Promise<boolean>): void {
    this.refreshHandler = refresh;
  }

  private resolveBaseUrl(): string {
    return resolveBaseUrlFrom(this.options.baseUrl);
  }

  get<T>(path: string, options?: RequestOptions): Promise<T> {
    return this.send<T>('GET', path, undefined, options);
  }

  post<T>(path: string, body?: unknown, options?: RequestOptions): Promise<T> {
    return this.send<T>('POST', path, body, options);
  }

  put<T>(path: string, body?: unknown, options?: RequestOptions): Promise<T> {
    return this.send<T>('PUT', path, body, options);
  }

  patch<T>(path: string, body?: unknown, options?: RequestOptions): Promise<T> {
    return this.send<T>('PATCH', path, body, options);
  }

  delete<T>(path: string, options?: RequestOptions): Promise<T> {
    return this.send<T>('DELETE', path, undefined, options);
  }

  /** Multipart upload; the browser sets the boundary, so Content-Type is deliberately unset. */
  upload<T>(path: string, form: FormData, options?: RequestOptions): Promise<T> {
    return this.send<T>('POST', path, form, options);
  }

  /**
   * Fetches binary content (an image, a file) as a Blob, carrying the auth header.
   *
   * A plain `<img src>` cannot attach the bearer token, and the access token lives in memory
   * only — so protected images are fetched here and shown through an object URL instead.
   */
  async getBlob(path: string, options?: RequestOptions): Promise<Blob> {
    let response = await this.dispatch('GET', path, undefined, options, false);

    if (this.shouldRefresh(response, options)) {
      if (await this.refreshOnce()) {
        response = await this.dispatch('GET', path, undefined, options, false);
      }
    }

    if (response.status === 401) this.options.onUnauthorized?.();
    if (!response.ok) throw new ApiError(response.status, await readProblem(response));

    return response.blob();
  }

  private async send<T>(
    method: string,
    path: string,
    body?: unknown,
    options?: RequestOptions,
  ): Promise<T> {
    let response = await this.dispatch(method, path, body, options, true);

    // A 401 usually means the access token expired mid-session. One silent refresh (from the
    // httpOnly cookie) and a single retry keeps the user working without a trip to the sign-in page.
    if (this.shouldRefresh(response, options)) {
      if (await this.refreshOnce()) {
        response = await this.dispatch(method, path, body, options, true);
      }
    }

    if (response.status === 401) this.options.onUnauthorized?.();

    if (!response.ok) throw new ApiError(response.status, await readProblem(response));

    if (response.status === 204 || response.headers.get('content-length') === '0') {
      return undefined as T;
    }

    return (await response.json()) as T;
  }

  /** Builds and sends one request. Reads the access token fresh each call so a retry uses the new one. */
  private dispatch(
    method: string,
    path: string,
    body: unknown,
    options: RequestOptions | undefined,
    json: boolean,
  ): Promise<Response> {
    const url = new URL(path.replace(/^\//, ''), this.resolveBaseUrl());
    appendQuery(url, options?.query);

    const isForm = body instanceof FormData;
    const headers: Record<string, string> = {
      ...(json ? { Accept: 'application/json' } : {}),
      ...(json && !isForm && body !== undefined ? { 'Content-Type': 'application/json' } : {}),
      ...options?.headers,
    };

    const token = this.options.getAccessToken?.();
    if (token) headers.Authorization = `Bearer ${token}`;

    // An explicit per-request locale wins over the client-wide getter, and over any
    // Accept-Language that arrived in options.headers.
    const language = options?.language ?? this.options.getLanguage?.();
    if (language) headers['Accept-Language'] = language;

    return fetch(url, {
      method,
      headers,
      // Send the httpOnly refresh cookie with every request so the session endpoints can read it.
      credentials: 'include',
      body: isForm ? (body as FormData) : body !== undefined ? JSON.stringify(body) : undefined,
      signal: options?.signal,
    });
  }

  private shouldRefresh(response: Response, options?: RequestOptions): boolean {
    return (
      response.status === 401 && !options?.skipAuthRefresh && this.refreshHandler !== undefined
    );
  }

  /** Runs at most one refresh at a time; concurrent 401s all await the same attempt. */
  private refreshOnce(): Promise<boolean> {
    const refresh = this.refreshHandler;
    if (!refresh) return Promise.resolve(false);

    this.refreshInFlight ??= refresh()
      .catch(() => false)
      .finally(() => {
        this.refreshInFlight = null;
      });

    return this.refreshInFlight;
  }
}

/**
 * Resolves the configured base against the current origin.
 *
 * The default base is the relative path `/api/v1/`, which the dev server proxies to the backend.
 * `new URL()` rejects a relative base outright, so a same-origin base has to be made absolute
 * before it is used.
 */
function resolveBaseUrlFrom(baseUrl: string): string {
  const withSlash = ensureTrailingSlash(baseUrl);

  if (/^https?:\/\//i.test(withSlash)) {
    return withSlash;
  }

  const origin = typeof window === 'undefined' ? 'http://localhost' : window.location.origin;
  return new URL(withSlash, origin).toString();
}

/** Appends scalar and array query values. Arrays become repeated keys (`?id=a&id=b`). */
function appendQuery(url: URL, query: RequestOptions['query']): void {
  for (const [key, value] of Object.entries(query ?? {})) {
    if (value === undefined || value === null) continue;
    if (Array.isArray(value)) {
      for (const item of value) url.searchParams.append(key, String(item));
      continue;
    }
    url.searchParams.set(key, String(value));
  }
}

async function readProblem(response: Response): Promise<ProblemDetails> {
  try {
    const parsed: unknown = await response.json();
    if (parsed && typeof parsed === 'object') return parsed as ProblemDetails;
  } catch {
    // Fall through to a synthetic problem below — the body was empty or not JSON.
  }
  return { status: response.status, title: response.statusText };
}

function ensureTrailingSlash(value: string): string {
  return value.endsWith('/') ? value : `${value}/`;
}
