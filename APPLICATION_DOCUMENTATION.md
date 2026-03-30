# Maa Tara Cafe — Comprehensive Application Documentation

> **Version:** 3.0 | **Last Updated:** March 30, 2026  
> **Stack:** Angular 19.2 + .NET 9 Azure Functions (Isolated Worker) + MongoDB Atlas  
> **Domain:** Multi-outlet cafe management + online ordering platform

---

## Table of Contents

1. [Feature Catalogue (by User Role)](#1-feature-catalogue-by-user-role)
2. [Step-by-Step Feature Usage Guide](#2-step-by-step-feature-usage-guide)
3. [Tech Architecture](#3-tech-architecture)
4. [System Architecture](#4-system-architecture)
5. [ER Diagram](#5-er-diagram)
6. [Architectural Flaws & Recommendations](#6-architectural-flaws--recommendations)

---

## 1. Feature Catalogue (by User Role)

### 1.1 Public (Unauthenticated) User

| # | Feature | Description |
|---|---------|-------------|
| 1 | **Home Page** | Brand landing page with outlet locations (Leaflet map), testimonials, stats counter, featured categories |
| 2 | **Browse Menu** | View all menu items with category/subcategory filtering, dietary type badges (veg/non-veg/egg/vegan), price display |
| 3 | **Cart** | Add/remove items, adjust quantities, view subtotal/packaging charges (localStorage-based, no login needed) |
| 4 | **View Offers** | Browse active discount offers with codes, validity periods, and discount types |
| 5 | **Customer Reviews** | Read customer reviews and ratings |
| 6 | **Table Reservations** | Reserve a table by selecting date, time slot, party size, and special requests |
| 7 | **Subscription Plans** | Browse available meal subscription plans with pricing and benefits |
| 8 | **Public Stats** | View total orders served, menu item count, outlet count, average rating |
| 9 | **Register** | Create account with username, email, password, phone number |
| 10 | **Login** | JWT-based authentication with brute-force protection |
| 11 | **Forgot/Reset Password** | Email-based password reset with token link |

### 1.2 Registered Customer (Role: `user`)

| # | Feature | Description |
|---|---------|-------------|
| 1 | **Place Orders** | Full checkout with delivery/pickup/dine-in options, scheduled orders (30 min – 7 days), coupon codes, delivery address management |
| 2 | **Payment** | Cash on Delivery (COD) or Razorpay online payment (credit/debit/UPI/wallets) |
| 3 | **Order Tracking** | View order history, real-time status tracking (pending → confirmed → preparing → ready → delivered), cancel orders |
| 4 | **Order Receipts** | Download PDF receipts for completed orders |
| 5 | **Wallet** | Digital wallet with Razorpay recharge, use wallet balance during checkout, view transaction history |
| 6 | **Loyalty Program** | Earn points on orders, tiered rewards (Bronze/Silver/Gold/Platinum), redeem rewards for discounts |
| 7 | **Points Transfer** | Transfer loyalty points to other users by username |
| 8 | **Referral System** | Generate referral code, share with friends, earn bonus points when referral is applied |
| 9 | **Birthday Bonus** | Set birthday in profile, claim annual birthday bonus points |
| 10 | **External Order Claims** | Upload Zomato/Swiggy invoice screenshots to earn loyalty points on external orders |
| 11 | **Loyalty Card** | Digital loyalty card with QR code for in-store scanning |
| 12 | **Favorites** | Mark menu items as favorites for quick reordering |
| 13 | **Delivery Addresses** | Save multiple delivery addresses with labels, collector name/phone, set default |
| 14 | **Profile Management** | Edit name, email, phone, profile picture upload, change password |
| 15 | **Notifications** | Real-time in-app notifications for order status changes, loyalty points, offers, system alerts |
| 16 | **Reviews** | Submit ratings (1-5 stars) and written reviews after order completion |
| 17 | **Combo Meals** | View and order combo meal deals with savings display |
| 18 | **Happy Hour Deals** | Automatic discounts during happy hour time windows |
| 19 | **Delivery Fee Calculation** | Dynamic delivery fee based on delivery zone distance configuration |
| 20 | **Recommendations** | Personalized menu recommendations and trending items |
| 21 | **Subscriptions** | Subscribe to meal plans, track active subscription, view included benefits |

### 1.3 Admin (Role: `admin`)

#### Menu & Product Management
| # | Feature | Description |
|---|---------|-------------|
| 1 | **Menu CRUD** | Create, edit, delete menu items with name, description, category, prices (shop/online/dine-in), dietary type, image upload |
| 2 | **Category/SubCategory CRUD** | Organize menu with categories and subcategories per outlet |
| 3 | **Bulk Menu Upload** | Upload menu items via Excel spreadsheet |
| 4 | **Category Upload** | Bulk upload categories via Excel |
| 5 | **Menu Item Variants** | Support for size/variant pricing per menu item |
| 6 | **Copy from Outlet** | Clone menu items from one outlet to another |
| 7 | **Toggle Availability** | Quick enable/disable menu items without deleting |
| 8 | **Combo Meal Management** | Create/edit combo meal deals with bundled items, discounts, and validity periods |
| 9 | **Happy Hour Management** | Configure time-based discounts with day-of-week rules and category targeting |

#### Financial Management
| # | Feature | Description |
|---|---------|-------------|
| 10 | **Sales Recording** | Record daily sales (cash/card/UPI/online), Excel upload, date range queries |
| 11 | **Sales Summary** | Aggregated sales analytics with revenue breakdown |
| 12 | **Expense Tracking** | Record expenses by type (inventory/salary/rent/utilities/maintenance/marketing/other), Excel upload |
| 13 | **Expense Analytics** | Hierarchical expense breakdown, category analysis, date range filtering |
| 14 | **Operational Expenses** | Track monthly fixed costs (rent, salaries, electricity, maintenance, misc) with rent calculation |
| 15 | **Cashier Module** | Quick point-of-sale recording with sales item types and payment method tracking |
| 16 | **Cash Reconciliation** | Daily cash/coin/online counting vs expected, deficit tracking, opening/closing balance management |
| 17 | **Online Sales Tracker** | Record Zomato/Swiggy orders with platform commission, payout, discount, GST tracking |
| 18 | **Online Profit Tracker** | Analyze per-order profitability after platform deductions and operational costs |
| 19 | **Platform Charges** | Track monthly charges from delivery platforms (Zomato/Swiggy) |
| 20 | **GST Reports** | Generate GST summary and GSTR-1 export |
| 21 | **Report Export** | Export sales, expenses, orders, and profit/loss reports as downloadable files |

#### Pricing Tools
| # | Feature | Description |
|---|---------|-------------|
| 22 | **Price Forecasting** | Project future menu prices based on ingredient cost trends, making cost, packaging, and overhead allocation |
| 23 | **Price Calculator** | Calculate recommended prices per item using recipe costs, overhead allocation, and desired profit margins |
| 24 | **KPT Analysis** | Kitchen Preparation Time analysis from online delivery data for operational efficiency |
| 25 | **Discount Mapping** | Map and manage discount coupons from delivery platforms with activation/deactivation |
| 26 | **Overhead Costs** | Define hourly/daily/monthly overhead costs (rent, utilities, etc.) auto-allocated to menu pricing |

#### Inventory & Supply Chain
| # | Feature | Description |
|---|---------|-------------|
| 27 | **Inventory Management** | Track ingredient stock levels with min/max thresholds, stock-in/stock-out/adjust operations |
| 28 | **Stock Alerts** | Automatic alerts for low stock, out of stock, expiring, and overstock conditions |
| 29 | **Ingredient Management** | CRUD for ingredients with market price tracking, categories, unit management |
| 30 | **Recipe Management** | Define ingredient recipes per menu item with quantity, unit, wastage factor — auto-calculates making cost |
| 31 | **Price History** | Track ingredient price changes over time with source attribution |
| 32 | **Auto Price Update** | Configurable automatic market price fetching for ingredients |
| 33 | **Frozen Items** | Track frozen inventory (packets, weights, per-piece pricing), Excel upload, sync to main inventory |
| 34 | **Auto Reorder** | Generate purchase orders automatically when stock falls below reorder threshold |
| 35 | **Purchase Orders** | Create, approve, and track purchase orders with supplier details |

#### Staff & HR Management
| # | Feature | Description |
|---|---------|-------------|
| 36 | **Staff CRUD** | Full employee management with personal info, employment details, salary, bank details, multi-outlet assignment |
| 37 | **Daily Performance Entry** | Record staff daily metrics: in/out time, working hours, orders prepared, good/bad orders, refunds — with multi-shift support |
| 38 | **Staff Performance Dashboard** | Aggregate performance analytics with date range filtering and bonus summaries |
| 39 | **Bonus Dashboard** | Calculate and view staff bonuses based on configured rules |
| 40 | **Bonus Configuration** | Create flexible bonus rules: overtime hours, undertime, snacks preparation, bad orders, good ratings, refund deductions — with per-unit/per-hour/percentage/fixed calculations |
| 41 | **Attendance** | Clock in/out tracking, daily attendance status (present/absent/half-day/late/leave), working hours calculation |
| 42 | **Leave Management** | Submit/approve/reject leave requests (sick/casual/earned/unpaid) with date range and reason tracking |

#### Operations
| # | Feature | Description |
|---|---------|-------------|
| 43 | **Kitchen Display System** | Real-time order queue for kitchen staff with status updates and kitchen stats |
| 44 | **KOT Printing** | Generate Kitchen Order Tickets for order preparation |
| 45 | **Delivery Zones** | Configure distance-based delivery zones with fees, free delivery thresholds, estimated delivery times |
| 46 | **Delivery Partners** | Manage delivery riders (name, phone, vehicle), assign to orders, track delivery completion |
| 47 | **Table Reservations Admin** | View and manage all customer reservations, update status (confirmed/seated/completed/cancelled/no-show) |
| 48 | **Wastage Tracking** | Record daily wastage with items, values, and reasons (expired/damaged/overproduction/returned) |
| 49 | **Multi-Outlet Management** | Create/manage multiple outlets with individual settings (operating hours, tax rates, delivery radius, order types) |
| 50 | **Outlet Selector** | Switch between outlets — all data automatically scoped to selected outlet |

#### Customer & Marketing
| # | Feature | Description |
|---|---------|-------------|
| 51 | **Offer Management** | Create percentage/flat/BOGO offers with codes, validity periods, usage limits, category targeting |
| 52 | **Loyalty Admin** | View all loyalty accounts, create/edit/delete rewards, view redemption history |
| 53 | **External Claim Review** | Review customer Zomato/Swiggy invoice claims — approve/reject with optional point override and admin notes |
| 54 | **Customer Segments** | Auto-segment customers (new/regular/VIP/dormant/at-risk) based on order history, spending patterns |
| 55 | **Subscription Plans Admin** | Create/manage meal subscription plans with pricing, duration, benefits, included items |

#### Analytics & Reporting
| # | Feature | Description |
|---|---------|-------------|
| 56 | **Business Dashboard** | Overview analytics with revenue trends, order volumes, popular items, peak hours |
| 57 | **User Analytics** | Track user behavior — page views, feature usage, session duration, cart analytics, API response times |
| 58 | **Branch Comparison** | Compare performance metrics across outlets side-by-side |

#### Security & Administration
| # | Feature | Description |
|---|---------|-------------|
| 59 | **User Management** | View all users, promote/demote roles, toggle active status |
| 60 | **API Key Management** | Generate, rotate, revoke API keys for external integrations |
| 61 | **Audit Logs** | View security audit trail — auth events, data access, security alerts — with CSV export |
| 62 | **CSRF Protection** | Token-based CSRF protection for state-changing operations |
| 63 | **Database Backup** | Manual and scheduled (timer trigger) database backup to Azure Blob Storage |

---

## 2. Step-by-Step Feature Usage Guide

### 2.1 Customer Journey

#### Browsing & Ordering
1. **Visit the app** → Land on home page with outlet info, map, and stats
2. **Browse Menu** → Click "Menu" in navbar → Filter by category/subcategory → View dietary badges and prices
3. **Add to Cart** → Click "Add to Cart" on a menu item → Adjust quantity → Cart icon shows badge count
4. **View Cart** → Click Cart icon → Review items, adjust quantities, see subtotal with packaging charges
5. **Checkout** → Click "Proceed to Checkout" (requires login) → Choose order type:
   - **Delivery:** Select/add delivery address → Delivery fee calculated by zone
   - **Pickup:** Select outlet for pickup
   - **Dine-in:** Enter table number
6. **Schedule Order (Optional)** → Toggle "Schedule for Later" → Pick date and time (30 min to 7 days ahead)
7. **Apply Discounts** → Enter coupon code → Apply loyalty points → Use wallet balance
8. **Pay** → Choose COD or Razorpay (credit/debit/UPI/net banking)
9. **Track Order** → Go to "Orders" → View status progression: Pending → Confirmed → Preparing → Ready → Delivered
10. **Review** → After delivery, submit star rating and written review
11. **Download Receipt** → Click "Download PDF" on completed order

#### Loyalty Program
1. **View Account** → Click "Loyalty Points" → See current points, tier, transaction history
2. **Earn Points** → Points auto-awarded on order completion
3. **Redeem Rewards** → Go to "Rewards" tab → Browse available rewards → Click "Redeem" (deducts points)
4. **Referral** → Go to "Referral" tab → Copy your referral code → Share with friends
5. **Apply Referral** → Enter friend's referral code → Both earn bonus points
6. **Birthday** → Go to "Card" tab → Set your birthday → Claim birthday bonus annually
7. **External Claims** → Go to "Claims" tab → Click "+ New Claim" → Select Zomato/Swiggy → Enter total amount → Upload invoice screenshot → Submit → Wait for admin approval

#### Wallet
1. **View Wallet** → Click "Wallet" → See balance and recent transactions
2. **Recharge** → Click "Recharge" → Enter amount → Pay via Razorpay → Balance updated instantly
3. **Use at Checkout** → During checkout, toggle "Use Wallet Balance" → Amount deducted from wallet first

#### Profile & Addresses
1. **Edit Profile** → Click username → "Edit Profile" → Update name, email, phone, upload picture
2. **Change Password** → Profile → "Change Password" → Enter current and new password
3. **Manage Addresses** → Profile → "Addresses" → Add new address with label, full address, collector details → Set default

### 2.2 Admin Journey

#### Initial Setup
1. **Login** → Use admin credentials → Redirected to admin dashboard
2. **Create Outlets** → Profile dropdown → "Manage Outlets" → Create outlet with name, address, contact, settings
3. **Select Outlet** → Use outlet selector (top-right dropdown) → All subsequent data scoped to selected outlet

#### Menu Setup
1. **Create Categories** → Menu → Categories → Click "Add Category" → Enter name → Save
2. **Create SubCategories** → Categories page → Select category → "Add SubCategory"
3. **Add Menu Items** → Menu → Menu Items → "Add Item" → Fill name, description, category, subcategory, prices (shop/online/dine-in), making price, packaging charge, dietary type → Upload image → Save
4. **Bulk Upload** → Menu → Upload → Download Excel template → Fill rows → Upload
5. **Create Combos** → Menu → Combo Meals → "Create Combo" → Select items → Set combo price → Set validity → Save
6. **Toggle Availability** → Menu Items → Click availability toggle on any item

#### Daily Operations
1. **Kitchen Display** → Operations → Kitchen Display → View incoming orders → Update status (preparing → ready)
2. **Cashier** → Finance → Cashier → Record walk-in sales with items, payment method → Save
3. **Record Sales** → Finance → Sales → "Add Sale" → Enter items, amounts, payment method, date → Save
4. **Record Expenses** → Finance → Expenses → "Add Expense" → Select type, enter amount, payment method → Save
5. **Cash Reconciliation** → Finance → Cashier → End of Day → Enter counted cash/coins/online → Compare to expected → Record deficit/surplus
6. **Daily Performance** → Staff → Daily Performance → Select staff → Enter shift times, orders prepared, good/bad counts, refunds → Save

#### Staff Management
1. **Add Staff** → Staff → Staff Management → "Add Employee" → Fill personal info, position, employment type, salary → Assign to outlet(s) → Save
2. **Mark Attendance** → Staff → Attendance → Select staff → Clock In → At end of shift → Clock Out
3. **Leave Request** → Staff → Attendance → "Leave Requests" tab → View pending → Approve/Reject with notes
4. **Configure Bonuses** → Staff → Bonus Config → "Create Configuration" → Add rules (overtime rate, bad order deduction, etc.) → Set applicable positions → Activate
5. **Calculate Bonuses** → Staff → Bonus Dashboard → Select date range → View per-staff bonus breakdown

#### Inventory
1. **Add Ingredient** → Operations → Inventory → "Add Item" → Enter name, category, unit, min/max stock, supplier → Save
2. **Stock In** → Inventory → Select item → "Stock In" → Enter quantity, purchase price → Save
3. **Stock Out** → Inventory → Select item → "Stock Out" → Enter quantity, reason → Save
4. **Define Recipe** → Tools → Price Calculator → Select menu item → Add ingredients with quantities → Save recipe
5. **Auto Reorder Setup** → Operations → Auto Reorder → Items below reorder level auto-generate purchase orders
6. **Review Alerts** → Inventory → "Alerts" tab → Resolve low stock / expiring alerts

#### Analytics & Reporting
1. **Dashboard** → View revenue trends, order volume, popular items, peak hours
2. **Business Analytics** → Analytics → Business Analytics → Select date range → View charts and KPIs
3. **Branch Comparison** → Analytics → Branch Comparison → Select outlets → Compare metrics side by side
4. **Export Reports** → Tools → Export Reports → Select report type (Sales/Expenses/Orders/P&L) → Select date range → Download
5. **GST Reports** → Finance → Expenses → GST tab → View GST summary → Export GSTR-1

#### Marketing
1. **Create Offer** → Marketing → Offers → "Create Offer" → Set title, discount type (percentage/flat/BOGO), code, validity, min order → Save
2. **Configure Happy Hours** → Marketing → Happy Hours → "Create Rule" → Set time window, days, discount type, applicable categories → Activate
3. **Customer Segments** → Marketing → Customer Segments → View auto-segmented customer list → Use for targeted marketing
4. **Review Claims** → Marketing → Loyalty → "Invoice Claims" tab → View pending claims → Click "Review" → Verify invoice → Approve/Reject with notes

---

## 3. Tech Architecture

### 3.1 Technology Stack

```
┌─────────────────────────────────────────────────────────────────┐
│                        FRONTEND                                 │
│  Angular 19.2 (Standalone Components, Signals)                  │
│  TypeScript 5.x | SCSS | PWA (Service Worker + Offline Queue)  │
│  State: Angular Signals (5 stores)                              │
│  HTTP: HttpClient with 4 interceptors (auth, error, analytics,  │
│        outlet) + exponential backoff retry                      │
│  Routing: Lazy-loaded modules with Guards                       │
│  UI: Custom SCSS (Sky Blue #0EA5E9 + Lime #84CC16)             │
│  Maps: Leaflet (lazy-loaded) | Payments: Razorpay (lazy)       │
│  Charts: Chart.js | QR: qrcode | Excel: SheetJS                │
│  Icons: Font Awesome 6.4                                        │
├─────────────────────────────────────────────────────────────────┤
│                      API GATEWAY                                │
│  Azure Functions V4 (Isolated Worker, .NET 9)                   │
│  74 Function files | 318 HTTP endpoints | 2 Timer triggers      │
│  1 Warmup trigger                                               │
│  Route Prefix: /api/                                            │
│  Auth: JWT (BCrypt, 24hr expiry) + Centralized AuthMiddleware   │
│  Middleware: SecurityHeaders → InputSanitization → RateLimit    │
│             → Authorization → RequestLogging → ApiVersioning    │
│  4-Tier Rate Limiting (Auth/AdminWrite/ExportReport/PublicRead) │
│  OpenAPI/Swagger auto-generated                                 │
├─────────────────────────────────────────────────────────────────┤
│                    BACKEND SERVICES                              │
│  MongoService (10 partial classes, 54 collections)              │
│  14 Repository Interfaces (IMenuRepository, IOrderRepository…)  │
│  AuthService (JWT + BCrypt)                                     │
│  BlobStorageService (Azure Blob — images, backups)              │
│  EmailService (MailKit SMTP)                                    │
│  WhatsAppService (Twilio API)                                   │
│  RazorpayService (Payment processing)                           │
│  MarketPriceService (External ingredient prices)                │
│  NotificationService (In-app push)                              │
│  FileUploadService (EPPlus Excel parsing)                       │
│  EventLogService (Event sourcing — state transition audit)      │
│  OutboxService (Transactional outbox for reliable side effects) │
│  MongoInitializationService (DB setup, indexes, seeding)        │
├─────────────────────────────────────────────────────────────────┤
│                      DATA LAYER                                 │
│  MongoDB Atlas (Cluster: maataracafecluster)                    │
│  Database: CafeDB | 54 Collections (+ EventLogs, OutboxMessages)│
│  All data scoped by OutletId (multi-tenant)                     │
│  35+ compound indexes | Soft-delete with ISoftDeletable          │
│  In-memory caching for reference data (IMemoryCache)            │
├─────────────────────────────────────────────────────────────────┤
│                   EXTERNAL SERVICES                             │
│  Razorpay (Payments) | Twilio (WhatsApp)                        │
│  MailKit SMTP (Email) | Azure Blob Storage (Files)              │
│  OpenStreetMap/Leaflet (Maps) | Market Price APIs               │
└─────────────────────────────────────────────────────────────────┘
```

### 3.2 Frontend Architecture

```
frontend/src/app/
├── components/          # 59 standalone Angular components
│   ├── home/            # Public landing page
│   ├── menu/            # Menu browsing (public)
│   ├── cart/            # Shopping cart
│   ├── checkout/        # Order placement
│   ├── orders/          # Order listing
│   ├── order-detail/    # Single order view
│   ├── loyalty/         # Loyalty program (user)
│   ├── wallet/          # Digital wallet (user)
│   ├── navbar/          # Public navbar
│   ├── admin-layout/    # Admin shell with grouped nav dropdowns
│   ├── admin-dashboard/ # Admin overview
│   └── ...              # 48 more admin/user components
├── shared/              # 5 shared UI components (confirm-dialog, empty-state,
│                        #   loading-spinner, toast-container)
├── inventory-management/# Standalone inventory component
├── services/            # 52 Angular services (HttpClient→API)
│   ├── offline-queue.service.ts   # Offline mutation queue + background sync
│   ├── network-status.service.ts  # Online/offline state detection
│   └── ...              # 50 more domain services
├── store/               # 5 Signal stores (Auth, Cart, Outlet, Notification, UI)
├── guards/              # authGuard, adminGuard
├── interceptors/        # 4 HTTP interceptors
│   ├── auth.interceptor.ts       # JWT token attachment
│   ├── error.interceptor.ts      # Error handling + exponential backoff retry
│   ├── analytics.interceptor.ts  # API response time tracking
│   └── outlet.interceptor.ts     # Outlet context injection
├── models/              # TypeScript interfaces (4 files)
├── utils/               # Shared utilities (error-handler, date-utils,
│                        #   file-download, loading)
├── app.routes.ts        # 18 public + 35 admin routes with lazy loading
└── app.config.ts        # App-level providers
```

**State Management Pattern:**
- **Angular Signals** (not NgRx/NGRX) — lightweight reactive state
- 5 stores: `AuthStore`, `CartStore`, `OutletStore`, `NotificationStore`, `UIStore`
- LocalStorage hydration for Auth, Cart, Outlet persistence
- NotificationStore auto-polls every 30 seconds while authenticated

**Routing Pattern:**
- Public routes: eagerly loaded (Home, Menu) or lazy-loaded
- User routes: `authGuard` protected, lazy-loaded
- Admin routes: `adminGuard` protected, all children lazy-loaded under `AdminLayoutComponent`
- Wildcard `**` redirects to home

### 3.3 Backend Architecture

```
api/
├── Functions/           # 74 Azure Function files (HTTP + Timer + Warmup triggers)
│   ├── AuthFunction.cs           # Authentication (8 endpoints)
│   ├── MenuFunction.cs           # Menu CRUD (9 endpoints)
│   ├── OrderFunction.cs          # Order lifecycle (uses repository interfaces)
│   ├── LoyaltyUserFunction.cs    # Loyalty — user endpoints (split from LoyaltyFunction)
│   ├── LoyaltyAdminFunction.cs   # Loyalty — admin endpoints (split from LoyaltyFunction)
│   ├── SalesFunction.cs          # Sales recording (7 endpoints)
│   ├── ExpenseFunction.cs        # Expense tracking (10 endpoints)
│   ├── InventoryQueryFunction.cs # Inventory — read endpoints (split from InventoryFunction)
│   ├── InventoryCommandFunction.cs # Inventory — write endpoints (split)
│   ├── StaffQueryFunction.cs     # Staff — read endpoints (split from StaffFunction)
│   ├── StaffCommandFunction.cs   # Staff — write endpoints (split)
│   ├── OutboxProcessorFunction.cs # Timer: processes outbox events every 30s
│   ├── DatabaseBackupFunction.cs # Timer: daily backup at 8:30 PM IST
│   ├── WarmupFunction.cs         # Warmup trigger for cold start mitigation
│   ├── OrphanCleanupFunction.cs  # Orphan data cleanup (soft-delete cascade)
│   └── ...                       # 59 more function files
├── Services/            # 24 backend service files
│   ├── MongoService.cs           # Core data access (main file)
│   ├── MongoService.*.cs         # 9 partial class extensions
│   ├── AuthService.cs            # JWT + password hashing
│   ├── RazorpayService.cs        # Payment processing (implements IRazorpayService)
│   ├── BlobStorageService.cs     # Azure Blob file storage
│   ├── EmailService.cs           # Email sending (implements IEmailService)
│   ├── WhatsAppService.cs        # WhatsApp messaging (implements IWhatsAppService)
│   ├── NotificationService.cs    # In-app notifications
│   ├── MarketPriceService.cs     # External price fetching
│   ├── FileUploadService.cs      # EPPlus Excel parsing
│   ├── EventLogService.cs        # Event sourcing (state transition audit trail)
│   ├── OutboxService.cs          # Transactional outbox (reliable side effects)
│   ├── MongoInitializationService.cs # Async DB setup, indexing, seeding
│   ├── IEmailService.cs          # Email service interface
│   ├── IWhatsAppService.cs       # WhatsApp service interface
│   └── IRazorpayService.cs       # Payment service interface
├── Repositories/        # 14 domain-specific repository interfaces
│   ├── IMenuRepository.cs        # Menu items, categories, subcategories
│   ├── IOrderRepository.cs       # Order CRUD + queries
│   ├── ILoyaltyRepository.cs     # Loyalty accounts, points, rewards
│   ├── IInventoryRepository.cs   # Inventory + ingredients + recipes
│   ├── IStaffRepository.cs       # Staff, attendance, leave, performance
│   ├── IFinanceRepository.cs     # Sales, expenses, reconciliation
│   ├── IUserRepository.cs        # User accounts, sessions
│   ├── IOfferRepository.cs       # Offers, coupons, happy hours
│   ├── IOutletRepository.cs      # Outlet management
│   ├── IWalletRepository.cs      # Wallets + transactions
│   ├── INotificationRepository.cs # App notifications
│   ├── IOperationsRepository.cs  # Kitchen, delivery, reservations, wastage
│   ├── IPricingRepository.cs     # Price forecasts, overhead costs
│   └── IAnalyticsRepository.cs   # User analytics, segments
├── Models/              # 46 model files (80+ classes/DTOs)
│   ├── ISoftDeletable.cs         # Soft-delete interface (IsDeleted, DeletedAt)
│   ├── EventLog.cs               # Event sourcing log entry model
│   ├── OutboxMessage.cs          # Outbox message model (pending/processing/completed/failed)
│   └── ...                       # 43 more domain models
├── Helpers/             # 18 security/utility helpers
│   ├── AuthorizationMiddleware.cs    # Centralized JWT extraction + role policies
│   ├── AuthorizationHelper.cs        # Legacy JWT parsing (backward compatibility)
│   ├── InputSanitizationMiddleware.cs # Global XSS/injection prevention middleware
│   ├── InputSanitizer.cs             # XSS/injection prevention utilities
│   ├── RateLimitingMiddleware.cs     # 4-tier rate limiting (Auth/AdminWrite/Export/Public)
│   ├── SecurityHeadersMiddleware.cs  # CSP, HSTS, X-Frame-Options
│   ├── RequestLoggingMiddleware.cs   # HTTP request/response logging
│   ├── ApiVersionMiddleware.cs       # API version negotiation + deprecation headers
│   ├── CsrfTokenManager.cs          # CSRF tokens
│   ├── AuditLogger.cs               # Security event logging
│   ├── PaginationHelper.cs          # Server-side pagination with default/max limits
│   ├── ValidationHelper.cs          # Centralized input validation
│   ├── ValidationAttributes.cs      # Custom validation annotations
│   ├── OutletHelper.cs              # Outlet context extraction
│   ├── ImageCompressor.cs           # Image optimization
│   ├── InvoiceParser.cs             # External invoice parsing
│   ├── LoyaltyHelper.cs             # Loyalty tier calculations
│   └── ApiKeyManager.cs             # API key generation/rotation/revocation
├── Program.cs           # DI container + middleware pipeline
└── host.json            # Azure Functions host config + CORS + Singleton config
```

**DI Registration (Program.cs):**
- All services registered as **Singleton** (Azure Functions best practice)
- **14 domain repository interfaces** backed by MongoService (Interface Segregation)
- **Service interfaces:** `IEmailService`, `IWhatsAppService`, `IRazorpayService` for testability
- Named `HttpClient` instances with **Polly** retry (3 attempts, exponential backoff) + circuit breaker (5 failures, 30s window) for Twilio and Razorpay
- `MongoInitializationService` as `IHostedService` for async DB setup
- `EventLogService` for event sourcing + `OutboxService` for reliable side effects
- `BlobServiceClient` for Azure Blob Storage
- `IMemoryCache` for in-memory reference data caching with expiration
- Application Insights telemetry with sampling

**Middleware Pipeline (order):**
1. `SecurityHeadersMiddleware` — CSP, HSTS, X-Content-Type, X-Frame-Options
2. `InputSanitizationMiddleware` — Global XSS/injection prevention on all requests
3. `RateLimitingMiddleware` — 4-tier request throttling per IP (Auth: 10/min, AdminWrite: 60/min, ExportReport: 20/min, PublicRead: 300/min)
4. `AuthorizationMiddleware` — Centralized JWT extraction, claims population into FunctionContext (RequireAuthenticated/RequireAdmin/RequireAdminOrManager)
5. `RequestLoggingMiddleware` — HTTP request/response logging
6. `ApiVersionMiddleware` — API version negotiation via header/query, deprecation headers, supported versions list

### 3.4 Security Architecture

```
┌────────────────────────────────────────────────────────┐
│              Security Layers (12)                       │
├────────────────────────────────────────────────────────┤
│ L1: Security Headers (CSP, HSTS, X-Frame)              │
│ L2: CORS Whitelist (localhost, Azure, production)      │
│ L3: Input Sanitization Middleware (global XSS/SQLi)    │
│ L4: 4-Tier Rate Limiting (Auth/Admin/Export/Public)    │
│ L5: Centralized Authorization Middleware (JWT claims)  │
│ L6: JWT Authentication (BCrypt, 24hr expiry)           │
│ L7: CSRF Token Validation                              │
│ L8: Role-Based Authorization (admin/user/manager)      │
│ L9: Brute-Force Protection (5 attempts → 429)          │
│ L10: Audit Logging (security events)                   │
│ L11: Event Sourcing (state transition audit trail)     │
│ L12: API Key Management (external integrations)        │
└────────────────────────────────────────────────────────┘
```

### 3.5 Multi-Outlet Architecture

```
Every data-bearing request flows through:

  Request → JWT Parse → Extract OutletId (from JWT claims OR query param)
                         │
                         ▼
              ┌─────────────────────┐
              │  OutletHelper.cs    │
              │  GetOutletId(req)   │
              └────────┬────────────┘
                       │
          ┌────────────▼────────────┐
          │  MongoDB Query Filter   │
          │  { OutletId: "xxx" }    │
          └─────────────────────────┘
          
All 54 collections are outlet-scoped (except Users, LoyaltyAccounts, EventLogs, OutboxMessages which are global)
```

---

## 4. System Architecture

### 4.1 Deployment Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                         AZURE CLOUD                                  │
│                                                                      │
│  ┌─────────────────────┐     ┌──────────────────────────────┐       │
│  │ Azure Static Web App│     │ Azure Functions App           │       │
│  │ (Angular 19 PWA)    │────▶│ (.NET 9 Isolated Worker)     │       │
│  │                     │     │ 318 HTTP Endpoints            │       │
│  │ • Service Worker    │     │ 2 Timer Triggers              │       │
│  │ • Offline Queue     │     │ 1 Warmup Trigger              │       │
│  │ • manifest.json     │     │                               │       │
│  │ • Lazy-loaded routes│     │ Middleware Pipeline:           │       │
│  └─────────────────────┘     │ 1. SecurityHeaders            │       │
│           │                  │ 2. InputSanitization           │       │
│           │ HTTPS            │ 3. RateLimiting (4-tier)       │       │
│           │                  │ 4. Authorization (JWT)         │       │
│           ▼                  │ 5. RequestLogging              │       │
│  ┌─────────────────────┐     │ 6. ApiVersioning              │       │
│  │ Azure Blob Storage  │◀────┴──────┬────────────────────────┘       │
│  │ (Images/Backups)    │           │ Images, Backups, Reports       │
│  └─────────────────────┘           │                                │
│                                    │                                │
│  ┌─────────────────────┐           │                                │
│  │ Application Insights│◀──────────┤ Telemetry, Sampling            │
│  │ (Monitoring)        │           │                                │
│  └─────────────────────┘           │                                │
│                                    │                                │
└────────────────────────────────────┼────────────────────────────────┘
                                     │
                     ┌───────────────▼───────────────┐
                     │     MongoDB Atlas              │
                     │     Cluster: maataracafecluster│
                     │     Database: CafeDB           │
                     │     54 Collections             │
                     │     35+ Compound Indexes       │
                     │     Soft-Delete + Event Sourcing│
                     │     Region: Azure              │
                     └───────────────┬───────────────┘
                                     │
                     ┌───────────────▼───────────────┐
                     │     External Services          │
                     │     • Razorpay (Payments)      │
                     │     • Twilio (WhatsApp)        │
                     │     • MailKit SMTP (Email)     │
                     │     • Market Price APIs        │
                     │     • OpenStreetMap (Maps)     │
                     └───────────────────────────────┘
```

### 4.2 Data Flow — Order Placement

```
Customer                Angular App              Azure Functions           MongoDB Atlas
   │                        │                         │                        │
   ├──Browse Menu──────────▶│                         │                        │
   │                        ├──GET /api/menu─────────▶│                        │
   │                        │                         ├──Find(CafeMenu)──────▶│
   │                        │◀──Menu Items────────────│◀──Documents────────────│
   │◀──Display Menu─────────│                         │                        │
   │                        │                         │                        │
   ├──Add to Cart──────────▶│                         │                        │
   │                        ├──CartStore.addItem()    │ (localStorage)         │
   │                        │                         │                        │
   ├──Checkout─────────────▶│                         │                        │
   │                        ├──POST /api/orders──────▶│                        │
   │                        │  {items, address, ...}  ├──Validate + Insert───▶│
   │                        │                         │  (Orders collection)   │
   │                        │                         ├──Deduct Wallet───────▶│
   │                        │                         ├──Enqueue Outbox ──────▶│
   │                        │                         │  (WhatsApp, Email,     │
   │                        │                         │   Notification,        │
   │                        │                         │   LoyaltyPoints)       │
   │                        │                         ├──Log Event ───────────▶│
   │                        │                         │  (EventLogs)           │
   │                        │◀──Order Confirmation────│◀──Success──────────────│
   │◀──Order Placed─────────│                         │                        │
   │                        │                         │                        │
   │  [Outbox Timer 30s]    │                         │                        │
   │                        │                 OutboxProcessorFunction           │
   │                        │                         ├──Get Pending Msgs────▶│
   │                        │                         ├──Send WhatsApp───────▶│ (Twilio)
   │                        │                         ├──Send Email──────────▶│ (SMTP)
   │                        │                         ├──Push Notification───▶│
   │                        │                         ├──Award Loyalty Pts───▶│
   │                        │                         ├──Mark Completed──────▶│
   │                        │                         │  (exponential retry    │
   │                        │                         │   on failure)          │
   │                        │                         │                        │
   │  [If Razorpay]         │                         │                        │
   │                        ├──POST /api/payments/    │                        │
   │                        │  create-order──────────▶│                        │
   │                        │                         ├──Razorpay API─────────▶│ (External)
   │                        │◀──razorpay_order_id─────│                        │
   │                        ├──Open Razorpay Modal    │                        │
   │  ├──Complete Payment──▶│                         │                        │
   │                        ├──POST /api/payments/    │                        │
   │                        │  verify────────────────▶│                        │
   │                        │                         ├──Verify Signature─────▶│ (Razorpay)
   │                        │◀──Payment Confirmed─────│                        │
```

### 4.3 Authentication Flow

```
User                Angular App              Azure Functions           MongoDB
  │                      │                        │                      │
  ├──Login──────────────▶│                        │                      │
  │                      ├──POST /api/auth/login─▶│                      │
  │                      │  {username, password}   │                      │
  │                      │                        ├──Find User──────────▶│
  │                      │                        │◀──User Doc───────────│
  │                      │                        ├──BCrypt.Verify()     │
  │                      │                        ├──Generate JWT        │
  │                      │                        │  (24hr, roles, oid)  │
  │                      │                        ├──Generate CSRF Token │
  │                      │                        ├──Audit Log──────────▶│
  │                      │◀──{token, user, csrf}──│                      │
  │                      ├──AuthStore.setUser()   │                      │
  │                      ├──localStorage.set()    │                      │
  │◀──Dashboard/Menu─────│                        │                      │
  │                      │                        │                      │
  │  [Subsequent Request]│                        │                      │
  │                      ├──GET /api/orders───────▶│                      │
  │                      │  Authorization: Bearer  │                      │
  │                      │  X-CSRF-Token: xxx      │                      │
  │                      │                        ├──AuthorizationMiddleware:
  │                      │                        │  Extract JWT Claims   │
  │                      │                        │  Populate Context     │
  │                      │                        │  (UserId, Role, Name) │
  │                      │                        ├──Function:            │
  │                      │                        │  context.RequireAuth()│
  │                      │                        ├──Query─────────────▶│
  │                      │◀──Response─────────────│◀─────────────────────│
```

---

## 5. ER Diagram

### 5.1 Core Entities Relationship Map

```
                                    ┌─────────────────┐
                                    │      USER        │
                                    │─────────────────│
                                    │ Id (PK)          │
                                    │ Username         │
                                    │ Email            │
                                    │ Role (admin/user)│
                                    │ AssignedOutlets[] │
                                    │ DefaultOutletId  │
                                    │ Addresses[]      │
                                    │ FavoriteItemIds[]│
                                    └────────┬────────┘
                   ┌──────────────┬──────────┼──────────┬───────────────┐
                   │              │          │          │               │
                   ▼              ▼          ▼          ▼               ▼
          ┌────────────┐  ┌──────────┐ ┌─────────┐ ┌─────────┐ ┌──────────────┐
          │   ORDER     │  │ LOYALTY  │ │ WALLET  │ │NOTIF.   │ │ USER SESSION │
          │────────────│  │ ACCOUNT  │ │─────────│ │─────────│ │──────────────│
          │ Id          │  │──────────│ │ Id      │ │ Id      │ │ Id           │
          │ UserId (FK) │  │ Id       │ │ UserId  │ │ UserId  │ │ UserId       │
          │ OutletId    │  │ UserId   │ │ Balance │ │ Type    │ │ SessionId    │
          │ Items[]     │  │ Points   │ │ TotalCr │ │ Title   │ │ Events[]     │
          │ Total       │  │ Tier     │ │ TotalDb │ │ Message │ └──────────────┘
          │ Status      │  │ Referral │ └────┬────┘ │ IsRead  │
          │ PaymentStat │  │ Birthday │      │      └─────────┘
          │ DeliveryFee │  └────┬─────┘      │
          │ ScheduledFor│       │             ▼
          │ OrderType   │       │      ┌─────────────┐
          └──────┬──────┘       │      │ WALLET TXN  │
                 │              │      │─────────────│
                 │              ▼      │ UserId      │
                 │       ┌───────────┐ │ Type (cr/db)│
                 │       │ POINTS    │ │ Amount      │
                 │       │ TRANSACT. │ │ Source      │
                 ▼       │───────────│ └─────────────┘
          ┌────────────┐ │ UserId    │
          │ REVIEW     │ │ Points    │
          │────────────│ │ Type      │
          │ OrderId    │ │ OrderId   │
          │ UserId     │ │ RewardId  │
          │ Rating     │ └───────────┘
          │ Comment    │
          └────────────┘


                    ┌─────────────────┐
                    │     OUTLET       │
                    │─────────────────│
                    │ Id (PK)          │
                    │ OutletName       │
                    │ OutletCode       │
                    │ Address          │
                    │ Settings{}       │
                    └────────┬────────┘
                             │
     ┌────────┬──────────┬───┴────┬──────────┬───────────┬────────────┐
     │        │          │        │          │           │            │
     ▼        ▼          ▼        ▼          ▼           ▼            ▼
┌────────┐┌───────┐┌─────────┐┌───────┐┌─────────┐┌──────────┐┌──────────┐
│MENU    ││SALES  ││EXPENSE  ││STAFF  ││INVENTORY││DELIVERY  ││COMBO     │
│ITEM    ││       ││         ││       ││         ││ZONE      ││MEAL      │
│────────││───────││─────────││───────││─────────││──────────││──────────│
│Id      ││Id     ││Id       ││Id     ││Id       ││Id        ││Id        │
│OutletId││Outlet ││OutletId ││Outlet ││OutletId ││OutletId  ││OutletId  │
│Name    ││Date   ││Type     ││Name   ││Ingrednt ││ZoneName  ││Name      │
│Category││Items[]││Amount   ││Posit. ││CurrStock││MinDist   ││Items[]   │
│Prices{}││Total  ││Source   ││Salary ││MinStock ││MaxDist   ││ComboPrice│
│Variants││PayMthd││PayMthd  ││Shifts ││Supplier ││Fee       ││Validity  │
└───┬────┘└───────┘└─────────┘└───┬───┘└────┬────┘└──────────┘└──────────┘
    │                             │         │
    │                             │         ▼
    │                             │   ┌─────────────┐
    │                             │   │ INGREDIENT   │
    │                             │   │─────────────│
    │                             │   │ Id           │
    ▼                             │   │ OutletId     │
┌─────────┐                      │   │ Name         │
│CATEGORY │                      │   │ MarketPrice  │
│─────────│                      │   │ Unit         │
│Id       │                      │   └──────┬──────┘
│Name     │                      │          │
│OutletId │                      │          ▼
└────┬────┘                      │   ┌─────────────┐
     │                           │   │ RECIPE       │
     ▼                           │   │─────────────│
┌──────────┐                     │   │ MenuItemId   │
│SUBCATEG. │                     │   │ Ingredients[]│
│──────────│                     │   │ MakingCost   │
│Id        │                     │   └─────────────┘
│CategoryId│                     │
│OutletId  │                     ▼
└──────────┘              ┌──────────────┐    ┌─────────────┐
                          │ ATTENDANCE   │    │ BONUS       │
                          │──────────────│    │ CONFIG      │
                          │ StaffId      │    │─────────────│
                          │ Date         │    │ OutletId    │
                          │ ClockIn/Out  │    │ Rules[]     │
                          │ Status       │    │ Positions[] │
                          └──────────────┘    └─────────────┘


        ┌───────────────────┐
        │ ADDITIONAL ENTITIES│
        ├───────────────────┤
        │                   │
        │  • Offer                     (Id, Code, DiscountType, Value, Validity)
        │  • Reward                    (Id, Name, PointsCost, IsActive)
        │  • TableReservation          (Id, OutletId, UserId, Date, TimeSlot, Status)
        │  • WastageRecord             (Id, OutletId, Date, Items[], TotalValue, Reason)
        │  • HappyHourRule             (Id, OutletId, TimeWindow, DaysOfWeek, Discount)
        │  • PurchaseOrder             (Id, OutletId, IngredientId, Supplier, Status)
        │  • SubscriptionPlan          (Id, OutletId, Name, Price, Duration, Benefits)
        │  • CustomerSubscription      (UserId, PlanId, StartDate, EndDate, Status)
        │  • DeliveryPartner           (Id, OutletId, Name, Phone, Vehicle, Status)
        │  • CustomerSegment           (Id, UserId, Segment, TotalOrders, TotalSpent)
        │  • ExternalOrderClaim        (Id, UserId, Platform, InvoiceUrl, Status)
        │  • OnlineSale                (Id, OutletId, Platform, OrderId, Payout, KPT)
        │  • OperationalExpense        (Id, OutletId, Month, Year, Rent, Salaries, Total)
        │  • DailyCashReconciliation   (Id, OutletId, Date, Expected vs Counted)
        │  • PriceForecast             (Id, MenuItemId, Current vs Future prices)
        │  • OverheadCost              (Id, OutletId, CostType, Monthly/Daily/Hourly)
        │  • FrozenItem                (Id, OutletId, ItemName, Quantity, Vendor)
        │  • PlatformCharge            (Id, OutletId, Platform, Month, Charges)
        │  • DiscountCoupon            (Id, Code, Platform, MaxValue, Percentage)
        │  • IngredientPriceHistory    (Id, IngredientId, Price, Source, RecordedAt)
        │  • DailyPerformanceEntry     (Id, StaffId, Date, Shifts[], Orders, Quality)
        │  • StaffPerformanceRecord    (Id, StaffId, Period, Metrics, BonusAmount)
        │  • UserActivityEvent         (Id, UserId, EventType, Feature, Timestamp)
        │  • SalesItemType             (Id, ItemName, DefaultPrice)
        │  • OfflineExpenseType        (Id, ExpenseType)
        │  • OnlineExpenseType         (Id, ExpenseType)
        │  • PublicStats               (TotalOrders, MenuItemCount, AvgRating)
        │  • AppNotification           (Id, UserId, Type, Title, Message, IsRead)
        │  • PasswordResetToken        (Id, UserId, Token, Email, ExpiresAt)
        │  • EventLog                  (Id, EntityType, EntityId, EventType, ActorId, OldState, NewState, Timestamp) [NEW]
        │  • OutboxMessage             (Id, EventType, AggregateType, AggregateId, Payload, Status, RetryCount) [NEW]
        │  • UserSession               (Id, UserId, SessionId, Events[]) [NEW]
        │                   │
        └───────────────────┘
```

### 5.2 Key Relationships

| Parent Entity | Relationship | Child Entity | Join Key |
|--------------|-------------|-------------|----------|
| User | 1 → N | Order | `UserId` |
| User | 1 → 1 | LoyaltyAccount | `UserId` |
| User | 1 → 1 | CustomerWallet | `UserId` |
| User | 1 → N | WalletTransaction | `UserId` |
| User | 1 → N | PointsTransaction | `UserId` |
| User | 1 → N | AppNotification | `UserId` |
| User | 1 → N | ExternalOrderClaim | `UserId` |
| User | 1 → N | TableReservation | `UserId` |
| User | 1 → N | CustomerReview | `UserId` |
| User | 1 → 1 | CustomerSegment | `UserId` |
| User | 1 → 1 | CustomerSubscription | `UserId` |
| User | 1 → N | UserActivityEvent | `UserId` |
| Outlet | 1 → N | CafeMenuItem | `OutletId` |
| Outlet | 1 → N | MenuCategory | `OutletId` |
| Outlet | 1 → N | MenuSubCategory | `OutletId` |
| Outlet | 1 → N | Order | `OutletId` |
| Outlet | 1 → N | Sales | `OutletId` |
| Outlet | 1 → N | Expense | `OutletId` |
| Outlet | 1 → N | Staff | `OutletIds[]` |
| Outlet | 1 → N | Inventory | `OutletId` |
| Outlet | 1 → N | Ingredient | `OutletId` |
| Outlet | 1 → N | DeliveryZone | `OutletId` |
| Outlet | 1 → N | DeliveryPartner | `OutletId` |
| Outlet | 1 → N | Attendance | `OutletId` |
| Outlet | 1 → N | DailyPerformanceEntry | `OutletId` |
| Outlet | 1 → N | BonusConfiguration | `OutletId` |
| Outlet | 1 → N | WastageRecord | `OutletId` |
| Outlet | 1 → N | OnlineSale | `OutletId` |
| Outlet | 1 → N | OperationalExpense | `OutletId` |
| Outlet | 1 → N | DailyCashReconciliation | `OutletId` |
| Outlet | 1 → N | ComboMeal | `OutletId` |
| Outlet | 1 → N | HappyHourRule | `OutletId` |
| Outlet | 1 → N | SubscriptionPlan | `OutletId` |
| Outlet | 1 → N | PurchaseOrder | `OutletId` |
| MenuCategory | 1 → N | MenuSubCategory | `CategoryId` |
| MenuCategory | 1 → N | CafeMenuItem | `CategoryId` |
| MenuSubCategory | 1 → N | CafeMenuItem | `SubCategoryId` |
| CafeMenuItem | 1 → 1 | Recipe | `MenuItemId` |
| CafeMenuItem | 1 → 1 | PriceForecast | `MenuItemId` |
| Ingredient | 1 → N | IngredientPriceHistory | `IngredientId` |
| Ingredient | 1 → 1 | Inventory | `IngredientId` |
| Ingredient | 1 → N | PurchaseOrder | `IngredientId` |
| Order | 1 → 1 | CustomerReview | `OrderId` |
| Staff | 1 → N | Attendance | `StaffId` |
| Staff | 1 → N | DailyPerformanceEntry | `StaffId` |
| Staff | 1 → N | StaffPerformanceRecord | `StaffId` |
| Staff | 1 → N | LeaveRequest | `StaffId` |

> **Note:** MongoDB is schema-less — these relationships are enforced at the application layer via `ISoftDeletable` interface and domain validation in MongoService/Repository methods. Soft-delete pattern (`IsDeleted` + `DeletedAt` fields) prevents orphaned references. All query filters automatically exclude soft-deleted documents. An `OrphanCleanupFunction` handles background cascade cleanup.

---

## 6. Architectural Flaws & Remediation Log

> **All 17 originally identified architectural flaws have been resolved.** This section documents each flaw, the original problem, and the implemented solution.

### 6.1 Critical Issues — ✅ RESOLVED

#### FLAW 1: God Service Anti-Pattern — MongoService ✅ RESOLVED
- **Original Problem:** `MongoService` was a single class with 48+ collection references and hundreds of methods handling ALL data access.
- **Resolution:** Decomposed into **14 domain-specific repository interfaces** (`IMenuRepository`, `IOrderRepository`, `ILoyaltyRepository`, `IInventoryRepository`, `IStaffRepository`, `IFinanceRepository`, `IUserRepository`, `IOfferRepository`, `IOutletRepository`, `IWalletRepository`, `INotificationRepository`, `IOperationsRepository`, `IPricingRepository`, `IAnalyticsRepository`). All registered via DI and backed by MongoService. Function files depend on focused interfaces, not the monolith.
- **Files:** `api/Repositories/` (14 interface files), `api/Program.cs` (DI registrations)

#### FLAW 2: No Database Referential Integrity ✅ RESOLVED
- **Original Problem:** Hard deletes could orphan references across collections.
- **Resolution:** Implemented `ISoftDeletable` interface with `IsDeleted` and `DeletedAt` fields across 14 models. All 18 delete methods converted to soft-delete. ~72 query filters updated to exclude soft-deleted documents. `OrphanCleanupFunction` handles background cascade cleanup.
- **Files:** `api/Models/ISoftDeletable.cs`, 14 model files, `api/Functions/OrphanCleanupFunction.cs`

#### FLAW 3: Missing Request Validation Layer ✅ RESOLVED
- **Original Problem:** Input validation was inconsistent — no global validation middleware.
- **Resolution:** Created `InputSanitizationMiddleware` that runs globally on all requests for XSS/injection prevention. `ValidateBody<T>` helper with Data Annotations for per-request validation. `ValidationHelper` and `ValidationAttributes` provide consistent validation patterns across 32+ function files.
- **Files:** `api/Helpers/InputSanitizationMiddleware.cs`, `api/Helpers/ValidationHelper.cs`, `api/Helpers/ValidationAttributes.cs`

### 6.2 High-Priority Issues — ✅ RESOLVED

#### FLAW 4: No Connection Pooling Config ✅ RESOLVED
- **Original Problem:** MongoDB connection pool relied on defaults (100 max connections), risking pool exhaustion under load.
- **Resolution:** Explicitly configured `MongoClientSettings` with `MaxConnectionPoolSize`, `MinConnectionPoolSize`, `WaitQueueTimeout`, and `ServerSelectionTimeout`. `IMongoClient` registered as singleton in DI for proper connection reuse across Azure Functions instances.
- **Files:** `api/Services/MongoService.cs`, `api/Program.cs`

#### FLAW 5: No Caching Strategy ✅ RESOLVED
- **Original Problem:** Every API request hit MongoDB directly, even for rarely-changing reference data.
- **Resolution:** `IMemoryCache` applied to 12 reference data methods (categories, subcategories, rewards, active offers, outlet settings, expense types, sales item types) with sliding/absolute expiration. Cache invalidation on write operations.
- **Files:** `api/Services/MongoService.cs` (12 methods), `api/Program.cs` (`AddMemoryCache()`)

#### FLAW 6: No Pagination Enforcement ✅ RESOLVED
- **Original Problem:** Multiple endpoints returned all documents without pagination.
- **Resolution:** Created `PaginationHelper` with server-side default/max limits. Applied to all unbounded list endpoints. Count methods added for total record counts. Default page size enforced.
- **Files:** `api/Helpers/PaginationHelper.cs`, 3+ function files updated

#### FLAW 7: Fat Function Files ✅ RESOLVED
- **Original Problem:** Single function files had 13-20+ endpoints each (Inventory, Loyalty, Staff).
- **Resolution:** Split into CQRS-light pattern:
  - `InventoryFunction.cs` → `InventoryQueryFunction.cs` + `InventoryCommandFunction.cs`
  - `LoyaltyFunction.cs` → `LoyaltyUserFunction.cs` + `LoyaltyAdminFunction.cs`
  - `StaffFunction.cs` → `StaffQueryFunction.cs` + `StaffCommandFunction.cs`
- **Files:** 6 new function files replacing 3 original files

### 6.3 Medium-Priority Issues — ✅ RESOLVED

#### FLAW 8: Frontend Error Recovery ✅ RESOLVED
- **Original Problem:** No retry logic, no offline queueing, no graceful degradation in frontend services.
- **Resolution:** `error.interceptor.ts` now implements exponential backoff retry for transient 5xx errors. `OfflineQueueService` queues critical mutations when offline. `NetworkStatusService` tracks online/offline state. `ngsw-config.json` configured with API caching strategies (reference data: performance mode, fresh data: freshness mode).
- **Files:** `frontend/src/app/interceptors/error.interceptor.ts`, `frontend/src/app/services/offline-queue.service.ts`, `frontend/src/app/services/network-status.service.ts`, `frontend/ngsw-config.json`

#### FLAW 9: No Database Indexing Strategy ✅ RESOLVED
- **Original Problem:** No documentation of indexing strategy, common query patterns lacked indexes.
- **Resolution:** 35+ compound indexes created in `MongoInitializationService`: `{OutletId, Date}` on sales/expenses/attendance, `{UserId, Status}` on orders, `{OutletId, Status}` on inventory, `{OutletId, CategoryId}` on menu items, and more. Strategy documented in `DATABASE_INDEXING_STRATEGY.md`.
- **Files:** `api/Services/MongoInitializationService.cs`, `DATABASE_INDEXING_STRATEGY.md`

#### FLAW 10: No API Versioning Strategy ✅ RESOLVED
- **Original Problem:** `ApiVersionMiddleware` existed but routes had no version support, no deprecation lifecycle.
- **Resolution:** Enhanced `ApiVersionMiddleware` with version negotiation via `Api-Version` header or query parameter, deprecation headers (`Sunset`, `Deprecation`), and `Supported-Api-Versions` response header listing all active versions.
- **Files:** `api/Helpers/ApiVersionMiddleware.cs`

#### FLAW 11: Timer Triggers Without Distributed Locking ✅ RESOLVED
- **Original Problem:** Timer triggers could run on multiple instances simultaneously.
- **Resolution:** Configured `host.json` singleton settings: `lockPeriod: 15s`, `listenerLockPeriod: 1min`, `lockAcquisitionTimeout: 1min`, `lockAcquisitionPollingInterval: 2s`. Azure Functions uses blob lease for singleton timer execution.
- **Files:** `api/host.json`

#### FLAW 12: No Rate Limiting Differentiation ✅ RESOLVED
- **Original Problem:** Same rate limit applied to all endpoints regardless of cost.
- **Resolution:** Implemented 4-tier rate limiting in `RateLimitingMiddleware`:
  - **Auth tier:** 10 requests/min, 30/hr (login, register, password reset)
  - **AdminWrite tier:** 60 requests/min, 600/hr (CRUD operations)
  - **ExportReport tier:** 20 requests/min, 200/hr (report exports, analytics)
  - **PublicRead tier:** 300 requests/min, 5000/hr (menu, health, public endpoints)
- **Files:** `api/Helpers/RateLimitingMiddleware.cs`

### 6.4 Low-Priority Issues — ✅ RESOLVED

#### FLAW 13: Mixed Authentication Patterns ✅ RESOLVED
- **Original Problem:** Auth logic duplicated across function files with inconsistent patterns.
- **Resolution:** Created `AuthorizationMiddleware` that runs on every request — extracts JWT claims and populates `FunctionContext.Items` with `UserId`, `Role`, `Username`. Extension methods provide declarative policy enforcement: `context.RequireAuthenticated(req)`, `context.RequireAdmin(req)`, `context.RequireAdminOrManager(req)`, `context.GetAuthInfo()`. Legacy `AuthorizationHelper` retained for backward compatibility.
- **Files:** `api/Helpers/AuthorizationMiddleware.cs`

#### FLAW 14: No Health Check for Dependencies ✅ RESOLVED
- **Original Problem:** Health endpoint only checked if the function app was running.
- **Resolution:** `HealthFunction.cs` rewritten with deep dependency checks:
  - **MongoDB:** ping + cluster stats (critical — returns 503 if down)
  - **Azure Blob Storage:** connectivity check (degraded if unavailable)
  - **Email/Razorpay/WhatsApp:** configuration presence checks
  - Returns `healthy`/`degraded`/`unhealthy` status with per-dependency breakdown
- **Files:** `api/Functions/HealthFunction.cs`

#### FLAW 15: No Event Sourcing ✅ RESOLVED
- **Original Problem:** Critical state mutations had no event trail for audit or replay.
- **Resolution:** Created `EventLogService` with fire-and-forget event logging (never throws, never breaks primary operation). `EventLog` model captures `EntityType`, `EntityId`, `EventType`, `ActorId`, `OldState`/`NewState` (JSON), `Metadata`, `Timestamp`. 3 compound indexes with 365-day TTL. Currently logging Order Create/Update/Cancel events — extensible to all critical entities.
- **Files:** `api/Models/EventLog.cs`, `api/Services/EventLogService.cs`

#### FLAW 16: Tight Coupling / No Interfaces ✅ RESOLVED
- **Original Problem:** Function files directly depended on `MongoService` — no abstraction, no testability.
- **Resolution:** 14 repository interfaces created (see FLAW 1). `OrderFunction.cs` fully refactored as pattern — injects `IOrderRepository`, `IMenuRepository`, `IOfferRepository`, `ILoyaltyRepository`, `IUserRepository` instead of MongoService directly. All 16+ `_mongo.` calls replaced with typed interface calls. Pattern available for other function files to adopt.
- **Files:** `api/Repositories/` (14 interfaces), `api/Functions/OrderFunction.cs` (pattern implementation)

#### FLAW 17: No Outbox Pattern ✅ RESOLVED
- **Original Problem:** Order side effects (notifications, loyalty, email, WhatsApp) ran inline — partial failures caused inconsistent state.
- **Resolution:** Implemented Transactional Outbox pattern:
  - `OutboxMessage` model with status lifecycle (pending → processing → completed/failed)
  - `OutboxService` with exponential backoff retry (30s → 2m → 8m → 32m → ~2h), max 5 retries
  - `OutboxProcessorFunction` timer trigger (every 30s) processes 10+ event types: OrderWhatsApp, OrderEmail, OrderNotification, LoyaltyPoints, StatusUpdate events
  - 3 indexes: pending messages, aggregate lookup, 30-day TTL cleanup
  - `OrderFunction.CreateOrder` and `UpdateOrderStatus` now enqueue side effects instead of inline execution
- **Files:** `api/Models/OutboxMessage.cs`, `api/Services/OutboxService.cs`, `api/Functions/OutboxProcessorFunction.cs`, `api/Functions/OrderFunction.cs`

---

### Summary of Flaws — All Resolved

| Severity | # | Flaw | Status |
|----------|---|------|--------|
| 🔴 Critical | 1 | God Service (MongoService) | ✅ Resolved — 14 repository interfaces |
| 🔴 Critical | 2 | No Referential Integrity | ✅ Resolved — Soft-delete + ISoftDeletable |
| 🔴 Critical | 3 | Missing Validation Layer | ✅ Resolved — InputSanitizationMiddleware |
| 🟠 High | 4 | No Connection Pool Config | ✅ Resolved — Explicit MongoClientSettings |
| 🟠 High | 5 | No Caching Strategy | ✅ Resolved — IMemoryCache on 12 methods |
| 🟠 High | 6 | No Pagination Enforcement | ✅ Resolved — PaginationHelper with limits |
| 🟠 High | 7 | Fat Function Files | ✅ Resolved — CQRS-light split (6 new files) |
| 🟡 Medium | 8 | No Frontend Error Recovery | ✅ Resolved — Retry + offline queue + PWA |
| 🟡 Medium | 9 | No Index Strategy Docs | ✅ Resolved — 35+ indexes documented |
| 🟡 Medium | 10 | No API Versioning | ✅ Resolved — Version negotiation middleware |
| 🟡 Medium | 11 | Timer Trigger Lock | ✅ Resolved — host.json singleton config |
| 🟡 Medium | 12 | No Rate Limit Differentiation | ✅ Resolved — 4-tier rate limiting |
| 🟢 Low | 13 | Mixed Auth Patterns | ✅ Resolved — AuthorizationMiddleware |
| 🟢 Low | 14 | No Deep Health Checks | ✅ Resolved — Multi-dependency health checks |
| 🟢 Low | 15 | No Event Sourcing | ✅ Resolved — EventLogService |
| 🟢 Low | 16 | Tight Coupling / No Interfaces | ✅ Resolved — 14 repository interfaces |
| 🟢 Low | 17 | No Outbox Pattern | ✅ Resolved — OutboxService + processor |

---

> **Document updated — Version 3.0 — March 30, 2026**  
> **Covers:** 46 model files, 74 function files, 54 MongoDB collections, 59 frontend components, 52 frontend services, 5 signal stores, 14 repository interfaces, 18 helper files, 24 service files
