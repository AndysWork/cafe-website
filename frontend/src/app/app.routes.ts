import { Routes } from '@angular/router';
import { HomeComponent } from './components/home/home.component';
import { CategoryUploadComponent } from './components/category-upload/category-upload.component';
import { CategoryCrudComponent } from './components/category-crud/category-crud.component';
import { LoginComponent } from './components/login/login.component';
import { OrdersComponent } from './components/orders/orders.component';
import { OffersComponent } from './components/offers/offers.component';
import { LoyaltyComponent } from './components/loyalty/loyalty.component';

export const routes: Routes = [
  { path: '', component: HomeComponent },
  { path: 'home', component: HomeComponent },
  { path: 'login', component: LoginComponent },
  { path: 'orders', component: OrdersComponent },
  { path: 'offers', component: OffersComponent },
  { path: 'loyalty', component: LoyaltyComponent },
  { path: 'category/upload', component: CategoryUploadComponent },
  { path: 'category/crud', component: CategoryCrudComponent },
  { path: 'menu', component: HomeComponent }, // Placeholder for menu component
  { path: '**', redirectTo: '' }
];
