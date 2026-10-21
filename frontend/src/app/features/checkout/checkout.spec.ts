import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { CartService } from '../../core/cart/cart.service';
import { RETRY_DELAY_MS } from '../../core/http/idempotency';
import { Order } from '../../shared/models/api.models';
import { Checkout } from './checkout';

const ORDER: Order = {
  id: '11111111-1111-1111-1111-111111111111',
  status: 'Paid',
  charged: true,
  failureReason: null,
  total: { amountMinor: 100, currency: 'GBP' },
  lines: [],
  payments: [],
};

describe('Checkout', () => {
  let fixture: ComponentFixture<Checkout>;
  let http: HttpTestingController;
  let root: HTMLElement;
  let navigate: ReturnType<typeof vi.spyOn>;

  beforeEach(async () => {
    vi.useFakeTimers();
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture = TestBed.createComponent(Checkout);
    fixture.componentRef.setInput('lines', [{ productId: 'a', quantity: 2 }]);
    fixture.componentRef.setInput('total', { amountMinor: 100, currency: 'GBP' });
    fixture.detectChanges();
    root = fixture.nativeElement;
  });

  afterEach(() => {
    http.verify();
    vi.useRealTimers();
  });

  const button = () => root.querySelector<HTMLButtonElement>('button.sf-btn-primary')!;

  it('sends the lines and token, never a price, with an Idempotency-Key', () => {
    button().click();
    const req = http.expectOne('/api/orders');
    expect(req.request.body).toEqual({
      lines: [{ productId: 'a', quantity: 2 }],
      paymentToken: 'tok_success',
    });
    expect(req.request.headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
    req.flush(ORDER);
  });

  it('disables the button while the request is in flight, so a double click sends one request', () => {
    button().click();
    fixture.detectChanges();
    expect(button().disabled).toBe(true);

    button().click(); // ignored
    http.expectOne('/api/orders').flush(ORDER);
    http.expectNone('/api/orders');
  });

  it('retries a network failure with the same Idempotency-Key', async () => {
    button().click();
    const first = http.expectOne('/api/orders');
    const key = first.request.headers.get('Idempotency-Key');
    first.error(new ProgressEvent('error'));

    await vi.advanceTimersByTimeAsync(RETRY_DELAY_MS);
    const second = http.expectOne('/api/orders');
    expect(second.request.headers.get('Idempotency-Key')).toBe(key);
    second.flush(ORDER);
  });

  it('clears the cart and opens the order once it is accepted (201 or 202)', () => {
    const cart = TestBed.inject(CartService);
    cart.add('a', 2);

    button().click();
    http
      .expectOne('/api/orders')
      .flush(
        { ...ORDER, status: 'PaymentUnknown', charged: false },
        { status: 202, statusText: 'Accepted' },
      );

    expect(cart.items()).toEqual([]);
    expect(navigate).toHaveBeenCalledWith(['/orders', ORDER.id]);
  });

  it('shows the server message on a 422, re-enables the button and asks for a fresh catalog', () => {
    const rejected = vi.fn();
    fixture.componentInstance.rejected.subscribe(rejected);

    button().click();
    http
      .expectOne('/api/orders')
      .flush(
        { detail: 'Product is no longer available.' },
        { status: 422, statusText: 'Unprocessable' },
      );
    fixture.detectChanges();

    expect(root.textContent).toContain('Product is no longer available.');
    expect(button().disabled).toBe(false);
    expect(rejected).toHaveBeenCalled();
  });
});
