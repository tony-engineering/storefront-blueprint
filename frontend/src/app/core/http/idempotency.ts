import { HttpErrorResponse } from '@angular/common/http';
import { MonoTypeOperatorFunction, retry, throwError, timer } from 'rxjs';
import { isTransient } from './problem';

/**
 * Idempotency key for one payment attempt (checkout, or "pay again" on an order).
 *
 * - Retrying the same request (same body) reuses the key, so the server replays the first
 *   result instead of creating a second order or charge.
 * - A different body gets a new key; reusing a key with another body would be a 422.
 * - Once the outcome is known (`complete()`), the next request is a new attempt with a new key.
 *   Otherwise a second "Pay" after a decline would just replay the decline.
 */
export class IdempotentAttempt {
  private current: { body: string; key: string } | null = null;

  keyFor(body: unknown): string {
    const json = JSON.stringify(body);
    if (this.current?.body !== json) {
      this.current = { body: json, key: crypto.randomUUID() };
    }
    return this.current.key;
  }

  complete(): void {
    this.current = null;
  }

  /** Call when a request fails: keep the key only if the outcome is still unknown. */
  failed(error: unknown): void {
    if (!isTransient(error)) {
      this.complete();
    }
  }
}

export const RETRY_DELAY_MS = 1000;

/**
 * Re-sends a request after transient errors (network, 409 in progress, 502–504).
 * Only safe on idempotent requests: GETs, or POSTs carrying an Idempotency-Key.
 */
export function retryTransient<T>(count = 3): MonoTypeOperatorFunction<T> {
  return retry({
    count,
    delay: (error: HttpErrorResponse) =>
      isTransient(error) ? timer(retryAfterMs(error)) : throwError(() => error),
  });
}

function retryAfterMs(error: HttpErrorResponse): number {
  const seconds = Number(error.headers?.get('Retry-After'));
  return Number.isFinite(seconds) && seconds > 0 ? Math.min(seconds, 10) * 1000 : RETRY_DELAY_MS;
}
