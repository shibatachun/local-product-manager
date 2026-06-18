import { Routes } from '@angular/router';
import { ProductManagerPage } from './features/products/product-manager.page';
import { AdminPage } from './features/admin/admin.page';
import { SocialAccountsPage } from './features/social-accounts/social-accounts.page';
import { TestPage } from './features/test/test.page';

export const routes: Routes = [
  { path: '', component: ProductManagerPage },
  { path: 'admin', component: AdminPage },
  { path: 'social-accounts', component: SocialAccountsPage },
  { path: 'test', component: TestPage },
  { path: '**', redirectTo: '' },
];
