import { Routes } from '@angular/router';

// Feature pages (products, cart, checkout, orders, admin) are added as
// lazy-loaded routes here, one folder per feature under features/.
export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./features/home/home').then((m) => m.Home),
    title: 'Storefront',
  },
  { path: '**', redirectTo: '' },
];
