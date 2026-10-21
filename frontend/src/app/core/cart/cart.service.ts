import { Injectable, computed, signal } from '@angular/core';

/** What the cart stores: IDs and quantities only. Prices always come from the catalog. */
export interface CartItem {
  productId: string;
  quantity: number;
}

export const CART_STORAGE_KEY = 'storefront.cart.v1';
export const MAX_QUANTITY = 100; // same limit as the API

/** Signals-based cart, saved to localStorage so it survives a reload. */
@Injectable({ providedIn: 'root' })
export class CartService {
  private readonly _items = signal<CartItem[]>(readStorage());

  readonly items = this._items.asReadonly();
  readonly count = computed(() => this._items().reduce((sum, i) => sum + i.quantity, 0));

  add(productId: string, quantity = 1): void {
    this.update((items) => {
      const existing = items.find((i) => i.productId === productId);
      return existing
        ? items.map((i) => (i === existing ? { ...i, quantity: clamp(i.quantity + quantity) } : i))
        : [...items, { productId, quantity: clamp(quantity) }];
    });
  }

  setQuantity(productId: string, quantity: number): void {
    this.update((items) =>
      items.map((i) => (i.productId === productId ? { ...i, quantity: clamp(quantity) } : i)),
    );
  }

  remove(productId: string): void {
    this.update((items) => items.filter((i) => i.productId !== productId));
  }

  /** Drops items whose product left the catalog (deleted by an admin). Returns how many were dropped. */
  retainOnly(productIds: ReadonlySet<string>): number {
    const before = this._items().length;
    this.update((items) => items.filter((i) => productIds.has(i.productId)));
    return before - this._items().length;
  }

  clear(): void {
    this.update(() => []);
  }

  private update(change: (items: CartItem[]) => CartItem[]): void {
    this._items.update(change);
    try {
      localStorage.setItem(CART_STORAGE_KEY, JSON.stringify(this._items()));
    } catch {
      // Storage full or blocked (private mode): the cart still works for this page view.
    }
  }
}

function clamp(quantity: number): number {
  return Number.isFinite(quantity) ? Math.min(MAX_QUANTITY, Math.max(1, Math.round(quantity))) : 1;
}

/** Reads the saved cart, ignoring anything malformed (old format, hand-edited storage). */
function readStorage(): CartItem[] {
  try {
    const parsed: unknown = JSON.parse(localStorage.getItem(CART_STORAGE_KEY) ?? '[]');
    return Array.isArray(parsed)
      ? parsed
          .filter(
            (i): i is CartItem =>
              typeof i?.productId === 'string' && Number.isInteger(i?.quantity) && i.quantity > 0,
          )
          .map((i) => ({ productId: i.productId, quantity: clamp(i.quantity) }))
      : [];
  } catch {
    return [];
  }
}
