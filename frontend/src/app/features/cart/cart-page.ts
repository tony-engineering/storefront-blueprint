import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api/api.service';
import { CartService, MAX_QUANTITY } from '../../core/cart/cart.service';
import { retryTransient } from '../../core/http/idempotency';
import { problemMessage } from '../../core/http/problem';
import { Product } from '../../shared/models/api.models';
import { sumMoney } from '../../shared/models/money';
import { MoneyPipe } from '../../shared/pipes/money.pipe';
import { Checkout } from '../checkout/checkout';

interface CartLine {
  product: Product;
  quantity: number;
}

/** Cart table plus the checkout panel. Prices shown here come from the live catalog. */
@Component({
  selector: 'app-cart-page',
  imports: [RouterLink, MoneyPipe, Checkout],
  templateUrl: './cart-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CartPage {
  private readonly api = inject(ApiService);
  protected readonly cart = inject(CartService);
  protected readonly maxQuantity = MAX_QUANTITY;

  private readonly catalog = signal<ReadonlyMap<string, Product> | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly removedCount = signal(0);

  protected readonly loaded = computed(() => this.catalog() !== null);
  protected readonly lines = computed<CartLine[]>(() => {
    const catalog = this.catalog();
    return catalog
      ? this.cart.items().flatMap((item) => {
          const product = catalog.get(item.productId);
          return product ? [{ product, quantity: item.quantity }] : [];
        })
      : [];
  });
  protected readonly total = computed(() =>
    sumMoney(this.lines().map((l) => ({ price: l.product.price, quantity: l.quantity }))),
  );
  protected readonly orderLines = computed(() =>
    this.lines().map((l) => ({ productId: l.product.id, quantity: l.quantity })),
  );

  constructor() {
    this.loadCatalog();
  }

  /** Also called when checkout is rejected (422), e.g. a product was deleted meanwhile. */
  protected loadCatalog(): void {
    this.error.set(null);
    this.api
      .listProducts()
      .pipe(retryTransient())
      .subscribe({
        next: (products) => {
          this.catalog.set(new Map(products.map((p) => [p.id, p])));
          this.removedCount.set(this.cart.retainOnly(new Set(products.map((p) => p.id))));
        },
        error: (err) => this.error.set(problemMessage(err)),
      });
  }

  protected step(line: CartLine, delta: number): void {
    this.cart.setQuantity(line.product.id, line.quantity + delta);
  }

  protected typed(line: CartLine, input: HTMLInputElement): void {
    this.cart.setQuantity(line.product.id, Number(input.value));
    // Show the clamped value even when it equals the previous one (typing 0 when it was 1).
    input.value = String(this.cart.items().find((i) => i.productId === line.product.id)?.quantity);
  }
}
