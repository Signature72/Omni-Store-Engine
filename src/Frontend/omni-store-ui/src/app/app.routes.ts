import { Routes } from '@angular/router';
import { CatalogComponent } from './features/catalog/catalog.component';
import { CheckoutComponent } from './features/checkout/checkout.component';
import { LoginComponent } from './features/auth/login.component';

export const routes: Routes = [
  { path: '', component: CatalogComponent },
  { path: 'checkout', component: CheckoutComponent },
  { path: 'login', component: LoginComponent },
  { path: '**', redirectTo: '' }
];
