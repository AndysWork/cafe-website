import { Routes } from '@angular/router';
import { HomeComponent } from './components/home/home.component';
import { CategoryUploadComponent } from './components/category-upload/category-upload.component';
import { CategoryCrudComponent } from './components/category-crud/category-crud.component';
import { LoginComponent } from './components/login/login.component';
import { RegisterComponent } from './components/register/register.component';
import { OrdersComponent } from './components/orders/orders.component';
import { OffersComponent } from './components/offers/offers.component';
import { LoyaltyComponent } from './components/loyalty/loyalty.component';
import { AdminLayoutComponent } from './components/admin-layout/admin-layout.component';
import { AdminDashboardComponent } from './components/admin-dashboard/admin-dashboard.component';
import { AdminOffersComponent } from './components/admin-offers/admin-offers.component';
import { AdminLoyaltyComponent } from './components/admin-loyalty/admin-loyalty.component';
import { AdminSalesComponent } from './components/admin-sales/admin-sales.component';
import { AdminExpensesComponent } from './components/admin-expenses/admin-expenses.component';
import { AdminAnalyticsComponent } from './components/admin-analytics/admin-analytics.component';
import { MenuUploadComponent } from './components/menu-upload/menu-upload.component';
import { MenuManagementComponent } from './components/menu-management/menu-management.component';
import { MenuComponent } from './components/menu/menu.component';
import { CartComponent } from './components/cart/cart.component';
import { CheckoutComponent } from './components/checkout/checkout.component';
import { authGuard, adminGuard } from './guards/auth.guard';

export const routes: Routes = [
  { path: '', component: HomeComponent },
  { path: 'home', component: HomeComponent },
  { path: 'login', component: LoginComponent },
  { path: 'register', component: RegisterComponent },
  { path: 'menu', component: MenuComponent },
  { path: 'cart', component: CartComponent },
  { path: 'checkout', component: CheckoutComponent, canActivate: [authGuard] },
  { path: 'orders', component: OrdersComponent, canActivate: [authGuard] },
  { path: 'offers', component: OffersComponent },
  { path: 'loyalty', component: LoyaltyComponent, canActivate: [authGuard] },
  {
    path: 'admin',
    component: AdminLayoutComponent,
    canActivate: [adminGuard],
    children: [
      { path: 'dashboard', component: AdminDashboardComponent },
      { path: 'category/upload', component: CategoryUploadComponent },
      { path: 'category/crud', component: CategoryCrudComponent },
      { path: 'menu/upload', component: MenuUploadComponent },
      { path: 'menu', component: MenuManagementComponent },
      { path: 'offers', component: AdminOffersComponent },
      { path: 'loyalty', component: AdminLoyaltyComponent },
      { path: 'sales', component: AdminSalesComponent },
      { path: 'expenses', component: AdminExpensesComponent },
      { path: 'analytics', component: AdminAnalyticsComponent },
      { path: '', redirectTo: 'dashboard', pathMatch: 'full' }
    ]
  },
  { path: '**', redirectTo: '' }
];
