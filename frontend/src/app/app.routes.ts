import { Routes } from '@angular/router';
import { HomeComponent } from './components/home/home.component';
import { CategoryUploadComponent } from './components/category-upload/category-upload.component';
import { CategoryCrudComponent } from './components/category-crud/category-crud.component';
import { LoginComponent } from './components/login/login.component';
import { RegisterComponent } from './components/register/register.component';
import { ForgotPasswordComponent } from './components/forgot-password/forgot-password.component';
import { ResetPasswordComponent } from './components/reset-password/reset-password.component';
import { ProfileComponent } from './components/profile/profile.component';
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
import { CashierComponent } from './components/cashier/cashier.component';
import { OnlineSaleTrackerComponent } from './components/online-sale-tracker/online-sale-tracker.component';
import { OnlineProfitTrackerComponent } from './components/online-profit-tracker/online-profit-tracker.component';
import { OperationalExpensesComponent } from './components/operational-expenses/operational-expenses.component';
import { CustomerReviewsComponent } from './components/customer-reviews/customer-reviews.component';
import { PriceForecastingComponent } from './components/price-forecasting/price-forecasting.component';
import { PriceCalculatorComponent } from './components/price-calculator/price-calculator.component';
import { DiscountMappingComponent } from './components/discount-mapping/discount-mapping.component';
import { InventoryManagementComponent } from './inventory-management/inventory-management.component';
import { KptAnalysisComponent } from './components/kpt-analysis/kpt-analysis.component';
import { authGuard, adminGuard } from './guards/auth.guard';

export const routes: Routes = [
  { path: '', component: HomeComponent },
  { path: 'home', component: HomeComponent },
  { path: 'login', component: LoginComponent },
  { path: 'register', component: RegisterComponent },
  { path: 'forgot-password', component: ForgotPasswordComponent },
  { path: 'reset-password', component: ResetPasswordComponent },
  { path: 'profile', component: ProfileComponent, canActivate: [authGuard] },
  { path: 'menu', component: MenuComponent },
  { path: 'cart', component: CartComponent },
  { path: 'checkout', component: CheckoutComponent, canActivate: [authGuard] },
  { path: 'orders', component: OrdersComponent, canActivate: [authGuard] },
  { path: 'reviews', component: CustomerReviewsComponent },
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
      { path: 'operational-expenses', component: OperationalExpensesComponent },
      { path: 'analytics', component: AdminAnalyticsComponent },
      { path: 'cashier', component: CashierComponent },
      { path: 'online-sale-tracker', component: OnlineSaleTrackerComponent },
      { path: 'online-profit-tracker', component: OnlineProfitTrackerComponent },
      { path: 'kpt-analysis', component: KptAnalysisComponent },
      { path: 'discount-mapping', component: DiscountMappingComponent },
      { path: 'price-forecasting', component: PriceForecastingComponent },
      { path: 'price-calculator', component: PriceCalculatorComponent },
      { path: 'inventory', component: InventoryManagementComponent },
      { path: '', redirectTo: 'dashboard', pathMatch: 'full' }
    ]
  },
  { path: '**', redirectTo: '' }
];
