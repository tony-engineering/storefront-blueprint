import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api/api.service';
import { CartService } from '../../core/cart/cart.service';
import { retryTransient } from '../../core/http/idempotency';
import { problemMessage } from '../../core/http/problem';
import { Product } from '../../shared/models/api.models';
import { MoneyPipe } from '../../shared/pipes/money.pipe';

@Component({
  selector: 'app-product-list',
  imports: [RouterLink, MoneyPipe],
  templateUrl: './product-list.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProductList {
  private readonly api = inject(ApiService);
  private readonly cart = inject(CartService);

  protected readonly products = signal<Product[] | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly lastAdded = signal<Product | null>(null);

  constructor() {
    this.load();
  }

  protected load(): void {
    this.error.set(null);
    this.api
      .listProducts()
      .pipe(retryTransient())
      .subscribe({
        next: (products) => this.products.set(products),
        error: (err) => this.error.set(problemMessage(err)),
      });
  }

  protected add(product: Product): void {
    this.cart.add(product.id);
    this.lastAdded.set(product);
  }
}
