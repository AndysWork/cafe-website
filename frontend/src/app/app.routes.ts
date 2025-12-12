import { Routes } from '@angular/router';
import { HomeComponent } from './components/home/home.component';
import { CategoryUploadComponent } from './components/category-upload/category-upload.component';
import { CategoryCrudComponent } from './components/category-crud/category-crud.component';
import { LoginComponent } from './components/login/login.component';
import { OrdersComponent } from './components/orders/orders.component';
import { OffersComponent } from './components/offers/offers.component';
import { LoyaltyComponent } from './components/loyalty/loyalty.component';
import { AdminDashboardComponent } from './components/admin-dashboard/admin-dashboard.component';
import { MenuUploadComponent } from './components/menu-upload/menu-upload.component';
import { MenuManagementComponent } from './components/menu-management/menu-management.component';

export const routes: Routes = [
  { path: '', component: HomeComponent },
  { path: 'home', component: HomeComponent },
  { path: 'login', component: LoginComponent },
  { path: 'orders', component: OrdersComponent },
  { path: 'offers', component: OffersComponent },
  { path: 'loyalty', component: LoyaltyComponent },
  { path: 'admin/dashboard', component: AdminDashboardComponent },
  { path: 'admin/category/upload', component: CategoryUploadComponent },
  { path: 'admin/category/crud', component: CategoryCrudComponent },
  { path: 'admin/menu/upload', component: MenuUploadComponent },
  { path: 'admin/menu', component: MenuManagementComponent },
  { path: '**', redirectTo: '' }
];
