import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** Find an order by its ID (orders are anonymous in this demo; the ID is the secret). */
@Component({
  selector: 'app-order-lookup',
  imports: [FormsModule],
  template: `
    <section class="sf-section">
      <div class="sf-container" style="max-width: 640px">
        <div class="sf-section-head">
          <div>
            <span class="sf-eyebrow">Orders</span>
            <h1>Track an order</h1>
          </div>
        </div>
        <form class="sf-panel" (ngSubmit)="open()">
          <div class="sf-panel-body sf-stack">
            <div class="sf-form-group">
              <label class="sf-label" for="order-id">Order ID</label>
              <input
                class="sf-input"
                id="order-id"
                name="orderId"
                autocomplete="off"
                placeholder="e.g. 3f2a8c1e-…"
                [attr.aria-invalid]="invalid() || null"
                aria-describedby="order-id-error"
                [ngModel]="orderId()"
                (ngModelChange)="orderId.set($event); invalid.set(false)"
              />
              @if (invalid()) {
                <span class="sf-field-error" id="order-id-error"
                  >Enter the order ID from your confirmation.</span
                >
              }
            </div>
            <button type="submit" class="sf-btn sf-btn-primary">Find order</button>
          </div>
        </form>
      </div>
    </section>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OrderLookup {
  private readonly router = inject(Router);
  protected readonly orderId = signal('');
  protected readonly invalid = signal(false);

  protected open(): void {
    const id = this.orderId().trim();
    if (!GUID.test(id)) {
      this.invalid.set(true);
      return;
    }
    void this.router.navigate(['/orders', id.toLowerCase()]);
  }
}
