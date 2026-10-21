import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Order } from '../../shared/models/api.models';
import { OrderDetails, POLL_INTERVAL_MS } from './order-details';

const ID = '22222222-2222-2222-2222-222222222222';
const url = `/api/orders/${ID}`;

const order = (status: Order['status'], extra: Partial<Order> = {}): Order => ({
  id: ID,
  status,
  charged: status === 'Paid',
  failureReason: null,
  total: { amountMinor: 4999900, currency: 'GBP' },
  lines: [],
  payments: [],
  ...extra,
});

describe('OrderDetails', () => {
  let fixture: ComponentFixture<OrderDetails>;
  let http: HttpTestingController;

  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(OrderDetails);
    fixture.componentRef.setInput('id', ID);
    fixture.detectChanges();
  });

  afterEach(() => {
    http.verify();
    vi.useRealTimers();
  });

  const tick = async () => {
    await vi.advanceTimersByTimeAsync(POLL_INTERVAL_MS);
    fixture.detectChanges();
  };
  const text = () => (fixture.nativeElement as HTMLElement).textContent ?? '';

  it('keeps polling while the payment is unresolved and stops at a final status', async () => {
    await vi.advanceTimersByTimeAsync(0);
    http.expectOne(url).flush(order('PaymentUnknown'));
    fixture.detectChanges();
    expect(text()).toContain('Confirming payment');

    await tick();
    http.expectOne(url).flush(order('PaymentRetrying'));

    await tick();
    http.expectOne(url).flush(order('Paid'));
    fixture.detectChanges();
    expect(text()).toContain('Payment received');

    await tick();
    await tick();
    http.expectNone(url); // polling has stopped
  });

  it('stops polling when the page is left', async () => {
    await vi.advanceTimersByTimeAsync(0);
    http.expectOne(url).flush(order('PaymentUnknown'));

    fixture.destroy();
    await vi.advanceTimersByTimeAsync(POLL_INTERVAL_MS * 3);
    http.expectNone(url);
  });

  it('keeps polling through a transient error', async () => {
    await vi.advanceTimersByTimeAsync(0);
    http.expectOne(url).flush(order('Pending'));

    await tick();
    http.expectOne(url).error(new ProgressEvent('error'));
    fixture.detectChanges();
    expect(text()).toContain('Connection lost');

    await tick();
    http.expectOne(url).flush(order('Paid'));
    fixture.detectChanges();
    expect(text()).not.toContain('Connection lost');
  });

  it('shows a clear message and stops for an unknown order', async () => {
    await vi.advanceTimersByTimeAsync(0);
    http.expectOne(url).flush({}, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();
    expect(text()).toContain('could not find an order');

    await tick();
    http.expectNone(url);
  });

  it('offers another card only when nothing was charged, with a new key per attempt', async () => {
    await vi.advanceTimersByTimeAsync(0);
    http.expectOne(url).flush(order('PaymentFailed', { failureReason: 'insufficient_funds' }));
    fixture.detectChanges();
    expect(text()).toContain('Pay with another card');
    expect(text()).toContain('insufficient funds');

    const pay = () =>
      (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
        'button.sf-btn-primary',
      )!;

    pay().click();
    const first = http.expectOne(`${url}/payments`);
    expect(first.request.body).toEqual({ paymentToken: 'tok_success' });
    const firstKey = first.request.headers.get('Idempotency-Key');
    first.flush(order('PaymentFailed', { failureReason: 'insufficient_funds' }));
    await vi.advanceTimersByTimeAsync(0);
    http.match(url).forEach((r) => r.flush(order('PaymentFailed')));
    fixture.detectChanges();

    pay().click();
    const second = http.expectOne(`${url}/payments`);
    expect(second.request.headers.get('Idempotency-Key')).not.toBe(firstKey);
    second.flush(order('Paid'));
    await vi.advanceTimersByTimeAsync(0);
    http.match(url).forEach((r) => r.flush(order('Paid')));
    fixture.detectChanges();
    expect(text()).not.toContain('Pay with another card');
  });

  it('does not offer another card while the payment is still resolving or after success', async () => {
    await vi.advanceTimersByTimeAsync(0);
    http.expectOne(url).flush(order('PaymentUnknown'));
    fixture.detectChanges();
    expect(text()).not.toContain('Pay with another card');
  });
});
