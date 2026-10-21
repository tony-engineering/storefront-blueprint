import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { ApiService } from '../../core/api/api.service';
import { CartService } from '../../core/cart/cart.service';
import { IdempotentAttempt, retryTransient } from '../../core/http/idempotency';
import { isTransient, problemMessage } from '../../core/http/problem';
import { Money, OrderLineRequest, PAYMENT_TOKENS } from '../../shared/models/api.models';
import { MoneyPipe } from '../../shared/pipes/money.pipe';

/**
 * Order summary and "Place order". Repeated requests are safe: one Idempotency-Key per attempt,
 * reused on retries, and the button is disabled while a request is in flight.
 */
@Component({
  selector: 'app-checkout',
  imports: [FormsModule, MoneyPipe],
  templateUrl: './checkout.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Checkout {
  private readonly api = inject(ApiService);
  private readonly cart = inject(CartService);
  private readonly router = inject(Router);
  private readonly attempt = new IdempotentAttempt();

  readonly lines = input.required<OrderLineRequest[]>();
  readonly total = input.required<Money | null>();
  /** The server refused the order as invalid (422), e.g. a product was deleted meanwhile. */
  readonly rejected = output();

  protected readonly tokens = PAYMENT_TOKENS;
  protected readonly paymentToken = signal<string>(PAYMENT_TOKENS[0].value);
  protected readonly placing = signal(false);
  protected readonly error = signal<{ title: string; message: string } | null>(null);

  protected placeOrder(): void {
    if (this.placing() || this.lines().length === 0) {
      return;
    }
    this.placing.set(true);
    this.error.set(null);

    const body = { lines: this.lines(), paymentToken: this.paymentToken() };
    this.api
      .createOrder(body, this.attempt.keyFor(body))
      .pipe(retryTransient())
      .subscribe({
        // 201 (paid or declined) and 202 (still resolving) both mean the order exists:
        // the order page shows the outcome and offers to pay again if nothing was charged.
        next: (order) => {
          this.attempt.complete();
          this.cart.clear();
          void this.router.navigate(['/orders', order.id]);
        },
        error: (err: unknown) => {
          this.attempt.failed(err);
          this.placing.set(false);
          this.error.set(
            isTransient(err)
              ? {
                  // The order may or may not exist. The key is kept, so pressing again is safe.
                  title: 'Order not confirmed',
                  message:
                    'We could not confirm your order. Press "Place order" again: ' +
                    'you will not be charged twice.',
                }
              : { title: 'Order not placed', message: problemMessage(err) },
          );
          if (err instanceof HttpErrorResponse && err.status === 422) {
            this.rejected.emit();
          }
        },
      });
  }
}
