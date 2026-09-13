/** RFC 7807 payload as produced by the API's ProblemDetails pipeline. */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  traceId?: string;
  /** Per-field validation messages from FluentValidation / model binding. */
  errors?: Record<string, string[]>;
  /** Stable machine-readable code the UI switches on (e.g. `application.already_paid`). */
  code?: string;
  [key: string]: unknown;
}

/** Thrown for any non-2xx API response so callers can branch on status/code, not on strings. */
export class ApiError extends Error {
  readonly status: number;
  readonly problem: ProblemDetails;

  constructor(status: number, problem: ProblemDetails) {
    super(problem.detail ?? problem.title ?? `Request failed with status ${status}`);
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
  }

  get code(): string | undefined {
    return this.problem.code;
  }

  get fieldErrors(): Record<string, string[]> {
    return this.problem.errors ?? {};
  }

  get isUnauthorized(): boolean {
    return this.status === 401;
  }

  get isForbidden(): boolean {
    return this.status === 403;
  }
}
