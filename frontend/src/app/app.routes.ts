import { Routes } from '@angular/router';

// One lazy-loaded route per feature folder. No auth guard on admin:
// the API has no authentication by design.
export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./features/products/product-list').then((m) => m.ProductList),
    title: 'Homes · Tiny Haven',
  },
  {
    path: 'cart',
    loadComponent: () => import('./features/cart/cart-page').then((m) => m.CartPage),
    title: 'Cart · Tiny Haven',
  },
  {
    path: 'orders',
    loadComponent: () => import('./features/orders/order-lookup').then((m) => m.OrderLookup),
    title: 'Track an order · Tiny Haven',
  },
  {
    path: 'orders/:id',
    loadComponent: () => import('./features/orders/order-details').then((m) => m.OrderDetails),
    title: 'Order · Tiny Haven',
  },
  {
    path: 'admin/products',
    loadComponent: () =>
      import('./features/admin/products/admin-products').then((m) => m.AdminProducts),
    title: 'Admin: products · Tiny Haven',
  },
  { path: '**', redirectTo: '' },
];
