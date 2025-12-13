import { Routes } from '@angular/router';
import { HomeComponent } from './components/home/home.component';
import { CategoryUploadComponent } from './components/category-upload/category-upload.component';
import { CategoryCrudComponent } from './components/category-crud/category-crud.component';
import { LoginComponent } from './components/login/login.component';
import { RegisterComponent } from './components/register/register.component';
import { OrdersComponent } from './components/orders/orders.component';
import { OffersComponent } from './components/offers/offers.component';
import { LoyaltyComponent } from './components/loyalty/loyalty.component';
import { AdminDashboardComponent } from './components/admin-dashboard/admin-dashboard.component';
import { MenuUploadComponent } from './components/menu-upload/menu-upload.component';
import { MenuManagementComponent } from './components/menu-management/menu-management.component';
import { authGuard, adminGuard } from './guards/auth.guard';

export const routes: Routes = [
  { path: '', component: HomeComponent },
  { path: 'home', component: HomeComponent },
  { path: 'login', component: LoginComponent },
  { path: 'register', component: RegisterComponent },
  { path: 'orders', component: OrdersComponent, canActivate: [authGuard] },
  { path: 'offers', component: OffersComponent },
  { path: 'loyalty', component: LoyaltyComponent, canActivate: [authGuard] },
  { path: 'admin/dashboard', component: AdminDashboardComponent, canActivate: [adminGuard] },
  { path: 'admin/category/upload', component: CategoryUploadComponent, canActivate: [adminGuard] },
  { path: 'admin/category/crud', component: CategoryCrudComponent, canActivate: [adminGuard] },
  { path: 'admin/menu/upload', component: MenuUploadComponent, canActivate: [adminGuard] },
  { path: 'admin/menu', component: MenuManagementComponent, canActivate: [adminGuard] },
  { path: '**', redirectTo: '' }
];
