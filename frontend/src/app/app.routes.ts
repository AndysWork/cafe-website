import { Routes } from '@angular/router';
import { HomeComponent } from './components/home/home.component';
import { LoginComponent } from './components/login/login.component';
import { RegisterComponent } from './components/register/register.component';
import { MenuComponent } from './components/menu/menu.component';
import { authGuard, adminGuard } from './guards/auth.guard';

export const routes: Routes = [
  { path: '', component: HomeComponent },
  { path: 'home', component: HomeComponent },
  { path: 'login', component: LoginComponent },
  { path: 'register', component: RegisterComponent },
  { path: 'forgot-password', loadComponent: () => import('./components/forgot-password/forgot-password.component').then(m => m.ForgotPasswordComponent) },
  { path: 'reset-password', loadComponent: () => import('./components/reset-password/reset-password.component').then(m => m.ResetPasswordComponent) },
  { path: 'profile', loadComponent: () => import('./components/profile/profile.component').then(m => m.ProfileComponent), canActivate: [authGuard] },
  { path: 'menu', component: MenuComponent },
  { path: 'cart', loadComponent: () => import('./components/cart/cart.component').then(m => m.CartComponent) },
  { path: 'checkout', loadComponent: () => import('./components/checkout/checkout.component').then(m => m.CheckoutComponent), canActivate: [authGuard] },
  { path: 'orders', loadComponent: () => import('./components/orders/orders.component').then(m => m.OrdersComponent), canActivate: [authGuard] },
  { path: 'reviews', loadComponent: () => import('./components/customer-reviews/customer-reviews.component').then(m => m.CustomerReviewsComponent) },
  { path: 'offers', loadComponent: () => import('./components/offers/offers.component').then(m => m.OffersComponent) },
  { path: 'loyalty', loadComponent: () => import('./components/loyalty/loyalty.component').then(m => m.LoyaltyComponent), canActivate: [authGuard] },
  {
    path: 'admin',
    loadComponent: () => import('./components/admin-layout/admin-layout.component').then(m => m.AdminLayoutComponent),
    canActivate: [adminGuard],
    children: [
      { path: 'dashboard', loadComponent: () => import('./components/admin-dashboard/admin-dashboard.component').then(m => m.AdminDashboardComponent) },
      { path: 'profile', loadComponent: () => import('./components/profile/profile.component').then(m => m.ProfileComponent) },
      { path: 'category/upload', loadComponent: () => import('./components/category-upload/category-upload.component').then(m => m.CategoryUploadComponent) },
      { path: 'category/crud', loadComponent: () => import('./components/category-crud/category-crud.component').then(m => m.CategoryCrudComponent) },
      { path: 'menu/upload', loadComponent: () => import('./components/menu-upload/menu-upload.component').then(m => m.MenuUploadComponent) },
      { path: 'menu', loadComponent: () => import('./components/menu-management/menu-management.component').then(m => m.MenuManagementComponent) },
      { path: 'offers', loadComponent: () => import('./components/admin-offers/admin-offers.component').then(m => m.AdminOffersComponent) },
      { path: 'loyalty', loadComponent: () => import('./components/admin-loyalty/admin-loyalty.component').then(m => m.AdminLoyaltyComponent) },
      { path: 'sales', loadComponent: () => import('./components/admin-sales/admin-sales.component').then(m => m.AdminSalesComponent) },
      { path: 'expenses', loadComponent: () => import('./components/admin-expenses/admin-expenses.component').then(m => m.AdminExpensesComponent) },
      { path: 'operational-expenses', loadComponent: () => import('./components/operational-expenses/operational-expenses.component').then(m => m.OperationalExpensesComponent) },
      { path: 'analytics', loadComponent: () => import('./components/admin-analytics/admin-analytics.component').then(m => m.AdminAnalyticsComponent) },
      { path: 'cashier', loadComponent: () => import('./components/cashier/cashier.component').then(m => m.CashierComponent) },
      { path: 'online-sale-tracker', loadComponent: () => import('./components/online-sale-tracker/online-sale-tracker.component').then(m => m.OnlineSaleTrackerComponent) },
      { path: 'online-profit-tracker', loadComponent: () => import('./components/online-profit-tracker/online-profit-tracker.component').then(m => m.OnlineProfitTrackerComponent) },
      { path: 'kpt-analysis', loadComponent: () => import('./components/kpt-analysis/kpt-analysis.component').then(m => m.KptAnalysisComponent) },
      { path: 'discount-mapping', loadComponent: () => import('./components/discount-mapping/discount-mapping.component').then(m => m.DiscountMappingComponent) },
      { path: 'price-forecasting', loadComponent: () => import('./components/price-forecasting/price-forecasting.component').then(m => m.PriceForecastingComponent) },
      { path: 'price-calculator', loadComponent: () => import('./components/price-calculator/price-calculator.component').then(m => m.PriceCalculatorComponent) },
      { path: 'inventory', loadComponent: () => import('./inventory-management/inventory-management.component').then(m => m.InventoryManagementComponent) },
      { path: 'outlets', loadComponent: () => import('./components/outlet-management/outlet-management.component').then(m => m.OutletManagementComponent) },
      { path: 'staff', loadComponent: () => import('./components/staff-management/staff-management.component').then(m => m.StaffManagementComponent) },
      { path: 'bonus-calculation', loadComponent: () => import('./components/bonus-calculation/bonus-calculation.component').then(m => m.BonusCalculationComponent) },
      { path: 'bonus-configuration', loadComponent: () => import('./components/bonus-configuration/bonus-configuration.component').then(m => m.BonusConfigurationComponent) },
      { path: 'staff-performance', loadComponent: () => import('./components/staff-performance/staff-performance.component').then(m => m.StaffPerformanceComponent) },
      { path: 'daily-performance', loadComponent: () => import('./components/daily-performance-entry/daily-performance-entry.component').then(m => m.DailyPerformanceEntryComponent) },
      { path: 'user-analytics', loadComponent: () => import('./components/user-analytics/user-analytics.component').then(m => m.UserAnalyticsComponent) },
      { path: '', redirectTo: 'dashboard', pathMatch: 'full' }
    ]
  },
  { path: '**', redirectTo: '' }
];
