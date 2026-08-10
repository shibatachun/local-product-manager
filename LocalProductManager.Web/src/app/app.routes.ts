import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', loadComponent: () => import('./features/product-management/product-manager.page').then(m => m.ProductManagerPage) },
  { path: 'products/:productId', loadComponent: () => import('./features/product-details/product-details.page').then(m => m.ProductDetailsPage) },
  { path: 'barcode-scanner', redirectTo: '/?view=barcodeScanner', pathMatch: 'full' },
  { path: 'label-maker', redirectTo: '/?view=labelMaker', pathMatch: 'full' },
  { path: 'admin', loadComponent: () => import('./features/admin/admin.page').then(m => m.AdminPage) },
  { path: 'social-accounts', loadComponent: () => import('./features/social-accounts/social-accounts.page').then(m => m.SocialAccountsPage) },
  { path: 'test', loadComponent: () => import('./features/test/test.page').then(m => m.TestPage) },
  { path: '**', redirectTo: '' },
];
