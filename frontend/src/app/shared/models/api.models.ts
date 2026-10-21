// Wire contract of the Storefront API, written by hand from
// backend/tests/Storefront.IntegrationTests/Infrastructure/ApiContracts.cs.
// There is no client code generator (ARCHITECTURE.md §11): these interfaces are the typed
// layer, kept in step with the API contract by hand.

import type { OrderStatus } from '../../../ui-kit/storefront.js';

export type { OrderStatus };

/** Integer amount in minor units (pence) plus an ISO 4217 code. Never a float. */
export interface Money {
  amountMinor: number;
  currency: string;
}

export interface Product {
  id: string;
  name: string;
  description: string | null;
  price: Money;
}

export interface OrderLine {
  productId: string;
  productName: string;
  unitPrice: Money;
  quantity: number;
}

export interface PaymentAttempt {
  number: number;
  status: string;
  failureReason: string | null;
}

export interface Order {
  id: string;
  status: OrderStatus;
  /** True only when the provider confirmed a charge. */
  charged: boolean;
  failureReason: string | null;
  total: Money;
  lines: OrderLine[];
  payments: PaymentAttempt[];
}

export interface OrderLineRequest {
  productId: string;
  quantity: number;
}

/** Prices are never sent: the server computes them from the product IDs. */
export interface CreateOrderRequest {
  lines: OrderLineRequest[];
  paymentToken: string;
}

export interface RetryPaymentRequest {
  paymentToken: string;
}

export interface CreateProductRequest {
  name: string;
  /** Required by the API, but may be null. */
  description: string | null;
  priceMinor: number;
}

/** The outcome is still being resolved by the server; the order page keeps polling. */
export const IN_PROGRESS_STATUSES: readonly OrderStatus[] = [
  'Pending',
  'PaymentRetrying',
  'PaymentUnknown',
];

/** Nothing was charged, so the customer may start a new payment attempt on the same order. */
export const PAYABLE_AGAIN_STATUSES: readonly OrderStatus[] = [
  'PaymentFailed',
  'PaymentUnavailable',
];

/** Mock card tokens understood by the API's mock payment gateway. */
export const PAYMENT_TOKENS = [
  { value: 'tok_success', label: 'Approved' },
  { value: 'tok_decline', label: 'Declined (insufficient funds)' },
  { value: 'tok_timeout', label: 'Times out, then confirmed as paid' },
  { value: 'tok_error', label: 'Provider error, not charged' },
  { value: 'tok_unavailable', label: 'Provider down, not charged' },
  { value: 'tok_flaky', label: 'Slow to confirm, then paid' },
] as const;
