import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', loadComponent: () => import('./features/products/product-manager.page').then(m => m.ProductManagerPage) },
  { path: 'barcode-scanner', loadComponent: () => import('./features/barcode-scanner/barcode-scanner.page').then(m => m.BarcodeScannerPage) },
  { path: 'label-maker', loadComponent: () => import('./features/label-maker/label-maker.page').then(m => m.LabelMakerPage) },
  { path: 'admin', loadComponent: () => import('./features/admin/admin.page').then(m => m.AdminPage) },
  { path: 'social-accounts', loadComponent: () => import('./features/social-accounts/social-accounts.page').then(m => m.SocialAccountsPage) },
  { path: 'test', loadComponent: () => import('./features/test/test.page').then(m => m.TestPage) },
  { path: '**', redirectTo: '' },
];
