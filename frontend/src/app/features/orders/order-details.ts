import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import {
  Observable,
  Subject,
  catchError,
  combineLatest,
  exhaustMap,
  map,
  of,
  startWith,
  switchMap,
  takeWhile,
  tap,
  timer,
} from 'rxjs';
import { orderStatus } from '../../../ui-kit/storefront.js';
import { ApiService } from '../../core/api/api.service';
import { IdempotentAttempt, retryTransient } from '../../core/http/idempotency';
import { isTransient, problemMessage } from '../../core/http/problem';
import {
  IN_PROGRESS_STATUSES,
  Money,
  Order,
  OrderLine,
  PAYABLE_AGAIN_STATUSES,
  PAYMENT_TOKENS,
} from '../../shared/models/api.models';
import { StatusBadge } from '../../shared/components/status-badge/status-badge';
import { MoneyPipe } from '../../shared/pipes/money.pipe';

export const POLL_INTERVAL_MS = 2000;

/**
 * Order status, lines and payment attempts. Polls while the payment outcome is still being
 * resolved (Pending, PaymentRetrying, PaymentUnknown) and stops at a final status or on leave.
 * Offers a new payment attempt when nothing was charged (PaymentFailed, PaymentUnavailable).
 */
@Component({
  selector: 'app-order-details',
  imports: [FormsModule, MoneyPipe, StatusBadge],
  templateUrl: './order-details.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OrderDetails {
  private readonly api = inject(ApiService);
  private readonly reload$ = new Subject<void>();
  private readonly attempt = new IdempotentAttempt();

  /** Route parameter `:id` (router input binding). */
  readonly id = input.required<string>();

  protected readonly order = signal<Order | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly connectionLost = signal(false);

  protected readonly tokens = PAYMENT_TOKENS;
  protected readonly paymentToken = signal<string>(PAYMENT_TOKENS[0].value);
  protected readonly paying = signal(false);
  protected readonly payError = signal<string | null>(null);

  protected readonly status = computed(() => {
    const order = this.order();
    return order ? orderStatus(order.status) : null;
  });
  protected readonly inProgress = computed(() =>
    IN_PROGRESS_STATUSES.includes(this.order()?.status ?? 'Paid'),
  );
  protected readonly canPayAgain = computed(() =>
    PAYABLE_AGAIN_STATUSES.includes(this.order()?.status ?? 'Paid'),
  );

  constructor() {
    const id$ = toObservable(this.id).pipe(tap(() => this.reset()));
    combineLatest([id$, this.reload$.pipe(startWith(undefined))])
      .pipe(
        switchMap(([id]) => this.poll(id)),
        takeUntilDestroyed(),
      )
      .subscribe();
  }

  protected lineTotal(line: OrderLine): Money {
    return {
      amountMinor: line.unitPrice.amountMinor * line.quantity,
      currency: line.unitPrice.currency,
    };
  }

  /** "insufficient_funds" → "insufficient funds". */
  protected reason(code: string | null): string {
    return code ? code.replaceAll('_', ' ') : '';
  }

  protected payAgain(): void {
    const order = this.order();
    if (!order || this.paying()) {
      return;
    }
    this.paying.set(true);
    this.payError.set(null);

    const body = { paymentToken: this.paymentToken() };
    this.api
      .retryPayment(order.id, body, this.attempt.keyFor({ orderId: order.id, ...body }))
      .pipe(retryTransient())
      .subscribe({
        next: (updated) => {
          this.attempt.complete();
          this.paying.set(false);
          this.order.set(updated);
          this.reload$.next(); // poll again if the new attempt is still resolving
        },
        error: (err: unknown) => {
          this.attempt.failed(err);
          this.paying.set(false);
          this.payError.set(problemMessage(err));
          this.reload$.next(); // e.g. 409: the order changed state, show the current one
        },
      });
  }

  /** GETs now, then every POLL_INTERVAL_MS while the outcome is open. Transient errors keep polling. */
  private poll(id: string): Observable<boolean> {
    return timer(0, POLL_INTERVAL_MS).pipe(
      exhaustMap(() =>
        this.api.getOrder(id).pipe(
          tap((order) => {
            this.order.set(order);
            this.loadError.set(null);
            this.connectionLost.set(false);
          }),
          map((order) => IN_PROGRESS_STATUSES.includes(order.status)),
          catchError((err: unknown) => {
            if (isTransient(err)) {
              this.connectionLost.set(true);
              return of(true);
            }
            this.loadError.set(
              err instanceof HttpErrorResponse && err.status === 404
                ? 'We could not find an order with this ID.'
                : problemMessage(err),
            );
            return of(false);
          }),
        ),
      ),
      takeWhile((keepPolling) => keepPolling),
    );
  }

  private reset(): void {
    this.order.set(null);
    this.loadError.set(null);
    this.connectionLost.set(false);
    this.payError.set(null);
    this.attempt.complete();
  }
}
