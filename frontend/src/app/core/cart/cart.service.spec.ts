import { TestBed } from '@angular/core/testing';
import { CART_STORAGE_KEY, CartService, MAX_QUANTITY } from './cart.service';

describe('CartService', () => {
  beforeEach(() => localStorage.clear());

  const create = () => TestBed.inject(CartService);

  it('merges repeated adds of the same product into one line', () => {
    const cart = create();
    cart.add('a');
    cart.add('a', 2);
    cart.add('b');
    expect(cart.items()).toEqual([
      { productId: 'a', quantity: 3 },
      { productId: 'b', quantity: 1 },
    ]);
    expect(cart.count()).toBe(4);
  });

  it('clamps quantities to 1..MAX_QUANTITY', () => {
    const cart = create();
    cart.add('a');
    cart.setQuantity('a', 0);
    expect(cart.items()[0].quantity).toBe(1);
    cart.setQuantity('a', 5000);
    expect(cart.items()[0].quantity).toBe(MAX_QUANTITY);
    cart.setQuantity('a', Number.NaN);
    expect(cart.items()[0].quantity).toBe(1);
  });

  it('saves to localStorage and restores on the next load', () => {
    create().add('a', 2);
    expect(JSON.parse(localStorage.getItem(CART_STORAGE_KEY)!)).toEqual([
      { productId: 'a', quantity: 2 },
    ]);

    TestBed.resetTestingModule();
    expect(create().items()).toEqual([{ productId: 'a', quantity: 2 }]);
  });

  it('ignores malformed stored data', () => {
    localStorage.setItem(
      CART_STORAGE_KEY,
      JSON.stringify([{ productId: 'ok', quantity: 2 }, { productId: 5 }, { quantity: 1 }, 'x']),
    );
    expect(create().items()).toEqual([{ productId: 'ok', quantity: 2 }]);

    TestBed.resetTestingModule();
    localStorage.setItem(CART_STORAGE_KEY, '{not json');
    expect(create().items()).toEqual([]);
  });

  it('drops products that left the catalog', () => {
    const cart = create();
    cart.add('a');
    cart.add('gone');
    expect(cart.retainOnly(new Set(['a']))).toBe(1);
    expect(cart.items().map((i) => i.productId)).toEqual(['a']);
  });

  it('removes and clears', () => {
    const cart = create();
    cart.add('a');
    cart.add('b');
    cart.remove('a');
    expect(cart.count()).toBe(1);
    cart.clear();
    expect(cart.items()).toEqual([]);
  });
});
