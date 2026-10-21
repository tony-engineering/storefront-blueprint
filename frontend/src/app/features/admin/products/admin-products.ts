import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService } from '../../../core/api/api.service';
import { retryTransient } from '../../../core/http/idempotency';
import { problemMessage } from '../../../core/http/problem';
import { Product } from '../../../shared/models/api.models';
import { parseMinorUnits } from '../../../shared/models/money';
import { MoneyPipe } from '../../../shared/pipes/money.pipe';

const PRICE_PATTERN = /^\s*\d{1,9}(\.\d{1,2})?\s*$/;

/** Create and delete products. Not guarded yet: the API has no auth endpoint (ARCHITECTURE.md §9). */
@Component({
  selector: 'app-admin-products',
  imports: [ReactiveFormsModule, MoneyPipe],
  templateUrl: './admin-products.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminProducts {
  private readonly api = inject(ApiService);
  private readonly confirmDialog =
    viewChild.required<ElementRef<HTMLDialogElement>>('confirmDelete');

  protected readonly products = signal<Product[] | null>(null);
  protected readonly listError = signal<string | null>(null);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    description: ['', Validators.maxLength(2000)],
    price: ['', [Validators.required, Validators.pattern(PRICE_PATTERN)]],
  });
  protected readonly saving = signal(false);
  protected readonly saveError = signal<string | null>(null);
  protected readonly saved = signal<string | null>(null);

  protected readonly pendingDelete = signal<Product | null>(null);
  protected readonly deletingId = signal<string | null>(null);
  protected readonly deleteError = signal<string | null>(null);

  constructor() {
    this.load();
  }

  protected load(): void {
    this.listError.set(null);
    this.api
      .listProducts()
      .pipe(retryTransient())
      .subscribe({
        next: (products) => this.products.set(products),
        error: (err) => this.listError.set(problemMessage(err)),
      });
  }

  protected create(): void {
    this.form.markAllAsTouched();
    const priceMinor = parseMinorUnits(this.form.controls.price.value);
    if (this.form.invalid || this.saving() || priceMinor === null || priceMinor <= 0) {
      return;
    }
    const { name, description } = this.form.getRawValue();
    this.saving.set(true);
    this.saveError.set(null);
    this.saved.set(null);

    // Not retried automatically: product creation has no idempotency key, so a retry could duplicate it.
    this.api
      .createProduct({ name: name.trim(), description: description.trim() || null, priceMinor })
      .subscribe({
        next: (product) => {
          this.saving.set(false);
          this.saved.set(product.name);
          this.form.reset();
          this.load();
        },
        error: (err) => {
          this.saving.set(false);
          this.saveError.set(problemMessage(err));
        },
      });
  }

  protected askDelete(product: Product): void {
    this.deleteError.set(null);
    this.pendingDelete.set(product);
    this.confirmDialog().nativeElement.showModal();
  }

  /** Native <dialog> closed: Esc or Cancel give "" / "cancel", the Delete button gives "confirm". */
  protected onDialogClose(): void {
    const product = this.pendingDelete();
    this.pendingDelete.set(null);
    if (product && this.confirmDialog().nativeElement.returnValue === 'confirm') {
      this.delete(product);
    }
  }

  private delete(product: Product): void {
    this.deletingId.set(product.id);
    // Safe to retry: DELETE is idempotent (204 again for an already deleted product).
    this.api
      .deleteProduct(product.id)
      .pipe(retryTransient())
      .subscribe({
        next: () => {
          this.deletingId.set(null);
          this.load();
        },
        error: (err) => {
          this.deletingId.set(null);
          this.deleteError.set(`${product.name}: ${problemMessage(err)}`);
        },
      });
  }
}
