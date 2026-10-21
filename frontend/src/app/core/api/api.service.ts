import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import {
  CreateOrderRequest,
  CreateProductRequest,
  Order,
  Product,
  RetryPaymentRequest,
} from '../../shared/models/api.models';

/** Thin typed wrapper over the Storefront API. `/api` is proxied by nginx (Docker) or the dev server. */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  listProducts() {
    return this.http.get<Product[]>('/api/products');
  }

  /** 201 when the payment outcome is known, 202 while it is still being resolved. Both return the order. */
  createOrder(body: CreateOrderRequest, idempotencyKey: string) {
    return this.http.post<Order>('/api/orders', body, {
      headers: { 'Idempotency-Key': idempotencyKey },
    });
  }

  getOrder(id: string) {
    return this.http.get<Order>(`/api/orders/${encodeURIComponent(id)}`);
  }

  /** New payment attempt on an order that is PaymentFailed or PaymentUnavailable. */
  retryPayment(orderId: string, body: RetryPaymentRequest, idempotencyKey: string) {
    return this.http.post<Order>(`/api/orders/${encodeURIComponent(orderId)}/payments`, body, {
      headers: { 'Idempotency-Key': idempotencyKey },
    });
  }

  createProduct(body: CreateProductRequest) {
    return this.http.post<Product>('/api/admin/products', body);
  }

  /** Soft delete; idempotent (204 again for an already deleted product). */
  deleteProduct(id: string) {
    return this.http.delete<void>(`/api/admin/products/${encodeURIComponent(id)}`);
  }
}
