import { HttpErrorResponse } from '@angular/common/http';
import { IdempotentAttempt } from './idempotency';
import { isTransient, problemMessage } from './problem';

describe('IdempotentAttempt', () => {
  const body = { lines: [{ productId: 'a', quantity: 1 }], paymentToken: 'tok_success' };

  it('reuses the key for the same body', () => {
    const attempt = new IdempotentAttempt();
    expect(attempt.keyFor(body)).toBe(attempt.keyFor({ ...body }));
  });

  it('uses a new key when the body changes', () => {
    const attempt = new IdempotentAttempt();
    expect(attempt.keyFor(body)).not.toBe(attempt.keyFor({ ...body, paymentToken: 'tok_decline' }));
  });

  it('keeps the key after a transient failure, drops it after a definite one', () => {
    const attempt = new IdempotentAttempt();
    const first = attempt.keyFor(body);

    attempt.failed(new HttpErrorResponse({ status: 0 }));
    expect(attempt.keyFor(body)).toBe(first);

    attempt.failed(new HttpErrorResponse({ status: 422 }));
    expect(attempt.keyFor(body)).not.toBe(first);
  });

  it('uses a new key once an attempt completed', () => {
    const attempt = new IdempotentAttempt();
    const first = attempt.keyFor(body);
    attempt.complete();
    expect(attempt.keyFor(body)).not.toBe(first);
  });
});

describe('problem helpers', () => {
  it('treats network errors, 409 and gateway errors as transient', () => {
    for (const status of [0, 409, 502, 503, 504]) {
      expect(isTransient(new HttpErrorResponse({ status }))).toBe(true);
    }
    for (const status of [400, 404, 422, 500]) {
      expect(isTransient(new HttpErrorResponse({ status }))).toBe(false);
    }
  });

  it('prefers field errors, then detail, then title', () => {
    expect(
      problemMessage(
        new HttpErrorResponse({ status: 400, error: { errors: { name: ['Name is required.'] } } }),
      ),
    ).toBe('Name is required.');
    expect(
      problemMessage(new HttpErrorResponse({ status: 422, error: { detail: 'Deleted.' } })),
    ).toBe('Deleted.');
    expect(
      problemMessage(new HttpErrorResponse({ status: 501, error: { title: 'Not implemented' } })),
    ).toBe('Not implemented');
  });
});
