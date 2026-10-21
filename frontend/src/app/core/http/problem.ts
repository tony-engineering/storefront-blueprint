import { HttpErrorResponse } from '@angular/common/http';

/** RFC 9457 ProblemDetails, as returned by the API for every error. */
interface ProblemDetails {
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
}

/**
 * True when the outcome of the request is unknown or temporary, so sending the same request
 * again (with the same Idempotency-Key) is safe: no response (network), 409 (same key still
 * in progress) or a gateway error from the proxy.
 */
export function isTransient(error: unknown): boolean {
  return error instanceof HttpErrorResponse && [0, 409, 502, 503, 504].includes(error.status);
}

/** Turns any HTTP error into one sentence to show the customer. */
export function problemMessage(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) {
    return 'Something went wrong. Please try again.';
  }
  if (error.status === 0) {
    return 'Cannot reach the server. Check your connection and try again.';
  }

  const problem = (typeof error.error === 'object' ? error.error : null) as ProblemDetails | null;
  const fieldErrors = Object.values(problem?.errors ?? {}).flat();
  if (fieldErrors.length > 0) {
    return fieldErrors.join(' ');
  }
  return (
    problem?.detail ?? problem?.title ?? `The server answered ${error.status}. Please try again.`
  );
}
