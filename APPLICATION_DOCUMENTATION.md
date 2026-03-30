# Maa Tara Cafe — Comprehensive Application Documentation

> **Version:** 2.0 | **Last Updated:** March 30, 2026  
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
│  TypeScript 5.x | SCSS | PWA (Service Worker)                  │
│  State: Angular Signals (5 stores)                              │
│  HTTP: HttpClient with interceptors                             │
│  Routing: Lazy-loaded modules with Guards                       │
│  UI: Custom SCSS (Sky Blue #0EA5E9 + Lime #84CC16)             │
│  Maps: Leaflet (lazy-loaded) | Payments: Razorpay (lazy)       │
│  Charts: Chart.js | QR: qrcode | Excel: SheetJS                │
│  Icons: Font Awesome 6.4                                        │
├─────────────────────────────────────────────────────────────────┤
│                      API GATEWAY                                │
│  Azure Functions V4 (Isolated Worker, .NET 9)                   │
│  68 Function files | 250+ HTTP endpoints | 2 Timer triggers     │
│  Route Prefix: /api/                                            │
│  Auth: JWT (BCrypt, 24hr expiry)                                │
│  Middleware: SecurityHeaders → RateLimit → Logging → Versioning │
│  OpenAPI/Swagger auto-generated                                 │
├─────────────────────────────────────────────────────────────────┤
│                    BACKEND SERVICES                              │
│  MongoService (10 partial classes, 48 collections)              │
│  AuthService (JWT + BCrypt)                                     │
│  BlobStorageService (Azure Blob — images, backups)              │
│  EmailService (Gmail SMTP)                                      │
│  WhatsAppService (Twilio API)                                   │
│  RazorpayService (Payment processing)                           │
│  MarketPriceService (External ingredient prices)                │
│  NotificationService (In-app push)                              │
│  FileUploadService (EPPlus Excel parsing)                       │
│  MongoInitializationService (DB setup, indexes, seeding)        │
├─────────────────────────────────────────────────────────────────┤
│                      DATA LAYER                                 │
│  MongoDB Atlas (Cluster: maataracafecluster)                    │
│  Database: CafeDB | 48 Collections                              │
│  All data scoped by OutletId (multi-tenant)                     │
├─────────────────────────────────────────────────────────────────┤
│                   EXTERNAL SERVICES                             │
│  Razorpay (Payments) | Twilio (WhatsApp)                        │
│  Gmail SMTP (Email) | Azure Blob Storage (Files)                │
│  OpenStreetMap/Leaflet (Maps) | Market Price APIs               │
└─────────────────────────────────────────────────────────────────┘
```

### 3.2 Frontend Architecture

```
frontend/src/app/
├── components/          # 60 standalone Angular components
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
│   └── ...              # 49 more admin/user components
├── services/            # 50 Angular services (HttpClient→API)
├── store/               # 5 Signal stores (Auth, Cart, Outlet, Notification, UI)
├── guards/              # authGuard, adminGuard
├── interceptors/        # HTTP interceptors (auth token, error handling)
├── models/              # TypeScript interfaces
├── utils/               # Shared utilities (error handler)
├── app.routes.ts        # Centralized route config with lazy loading
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
├── Functions/           # 68 Azure Function files (HTTP + Timer triggers)
│   ├── AuthFunction.cs           # Authentication (8 endpoints)
│   ├── MenuFunction.cs           # Menu CRUD (9 endpoints)
│   ├── OrderFunction.cs          # Order lifecycle (6 endpoints)
│   ├── LoyaltyFunction.cs        # Loyalty program (14 endpoints)
│   ├── SalesFunction.cs          # Sales recording (7 endpoints)
│   ├── ExpenseFunction.cs        # Expense tracking (10 endpoints)
│   ├── InventoryFunction.cs      # Inventory management (20+ endpoints)
│   ├── StaffFunction.cs          # Staff HR management (13 endpoints)
│   └── ...                       # 60 more function files
├── Services/            # 10+ backend services
│   ├── MongoService.cs           # Core data access (main file)
│   ├── MongoService.*.cs         # 9 partial class extensions
│   ├── AuthService.cs            # JWT + password hashing
│   ├── RazorpayService.cs        # Payment processing
│   ├── BlobStorageService.cs     # File storage
│   ├── EmailService.cs           # Email sending
│   ├── WhatsAppService.cs        # WhatsApp messaging
│   ├── NotificationService.cs    # In-app notifications
│   └── MarketPriceService.cs     # External price fetching
├── Models/              # 43 model files (80+ classes/DTOs)
├── Helpers/             # 15 security/utility helpers
│   ├── AuthorizationHelper.cs    # JWT parsing, role checks
│   ├── InputSanitizer.cs         # XSS/injection prevention
│   ├── RateLimitingMiddleware.cs # Request rate limits
│   ├── SecurityHeadersMiddleware.cs  # CSP, HSTS, X-Frame
│   ├── CsrfTokenManager.cs      # CSRF tokens
│   ├── AuditLogger.cs           # Security event logging
│   └── ...
├── Program.cs           # DI container + middleware pipeline
└── host.json            # Azure Functions host config + CORS
```

**DI Registration (Program.cs):**
- All services registered as **Singleton** (Azure Functions best practice)
- Named `HttpClient` instances with **Polly** retry + circuit breaker for Twilio and Razorpay
- `MongoInitializationService` as `IHostedService` for async DB setup
- `IMemoryCache` for in-memory request caching

**Middleware Pipeline (order):**
1. `SecurityHeadersMiddleware` — CSP, HSTS, X-Content-Type, X-Frame-Options
2. `RateLimitingMiddleware` — Request throttling per IP
3. `RequestLoggingMiddleware` — HTTP request/response logging
4. `ApiVersionMiddleware` — API versioning

### 3.4 Security Architecture

```
┌───────────────────────────────────────────────┐
│              Security Layers                   │
├───────────────────────────────────────────────┤
│ L1: Security Headers (CSP, HSTS, X-Frame)     │
│ L2: CORS Whitelist (localhost, production)     │
│ L3: Rate Limiting (per IP)                     │
│ L4: Input Sanitization (XSS detection)         │
│ L5: JWT Authentication (BCrypt, 24hr expiry)   │
│ L6: CSRF Token Validation                      │
│ L7: Role-Based Authorization (admin/user)      │
│ L8: Brute-Force Protection (5 attempts → 429)  │
│ L9: Audit Logging (all security events)         │
│ L10: API Key Management (external integrations) │
└───────────────────────────────────────────────┘
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
          
All 48 collections are outlet-scoped (except Users, LoyaltyAccounts which are global)
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
│  │                     │     │ 250+ HTTP Endpoints           │       │
│  │ • Service Worker    │     │ 2 Timer Triggers              │       │
│  │ • manifest.json     │     │                               │       │
│  │ • Lazy-loaded routes│     │ Middleware:                    │       │
│  └─────────────────────┘     │ • SecurityHeaders             │       │
│           │                  │ • RateLimiting                │       │
│           │ HTTPS            │ • RequestLogging              │       │
│           │                  │ • ApiVersioning               │       │
│           ▼                  └──────┬────────────────────────┘       │
│  ┌─────────────────────┐           │                                │
│  │ Azure Blob Storage  │◀──────────┤ Images, Backups, Reports       │
│  │ (Images/Backups)    │           │                                │
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
                     │     48 Collections             │
                     │     Region: Azure              │
                     └───────────────┬───────────────┘
                                     │
                     ┌───────────────▼───────────────┐
                     │     External Services          │
                     │     • Razorpay (Payments)      │
                     │     • Twilio (WhatsApp)        │
                     │     • Gmail SMTP (Email)       │
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
   │                        │                         ├──Update LoyaltyPts───▶│
   │                        │                         ├──Send Notification───▶│
   │                        │                         ├──Deduct Wallet───────▶│
   │                        │◀──Order Confirmation────│◀──Success──────────────│
   │◀──Order Placed─────────│                         │                        │
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
  │                      │  X-CSRF-Token: xxx      ├──JWT Validate       │
  │                      │                        ├──Extract UserId      │
  │                      │                        ├──Check Role          │
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

> **Note:** MongoDB is schema-less — these relationships are enforced at the application layer, not by database constraints. There are no foreign key constraints. Referential integrity is maintained by application code in MongoService methods.

---

## 6. Architectural Flaws & Recommendations

### 6.1 Critical Issues

#### FLAW 1: God Service Anti-Pattern — MongoService
- **Problem:** `MongoService` is split across 10 partial class files but remains a **single class with 48 collection references and hundreds of methods**. It handles ALL data access for every feature — menu, orders, loyalty, inventory, staff, analytics, etc.
- **Impact:** Violates Single Responsibility Principle. Any change risks regressions across unrelated features. Difficult to unit test, difficult to understand, and creates tight coupling.
- **Recommendation:** Decompose into domain-specific repository services: `MenuRepository`, `OrderRepository`, `LoyaltyRepository`, `InventoryRepository`, `StaffRepository`, etc. Each owns its collections and exposes focused interfaces. Use dependency injection to compose them.

#### FLAW 2: No Database Referential Integrity
- **Problem:** All entity relationships are enforced in application code only. MongoDB has no foreign key constraints. If application code skips a check, orphaned references can occur (e.g., deleting a menu item doesn't cascade to orders, recipes, or combos referencing it).
- **Impact:** Data inconsistency risk. Deleting an Outlet doesn't cascade-delete its menu items, staff assignments, orders, expenses, inventory, etc.
- **Recommendation:** Implement soft-delete patterns (set `IsDeleted = true`) instead of hard deletes. Add background cleanup jobs for orphaned data. Consider MongoDB Change Streams for cascading updates. Add validation checks before delete operations.

#### FLAW 3: Missing Request Validation Layer
- **Problem:** Input validation is done inconsistently — some functions validate manually, some use `ValidationHelper`, some don't validate at all. There's no global validation middleware.
- **Impact:** Potential for invalid data entering the database. Each function file has to independently remember to sanitize and validate.
- **Recommendation:** Implement a centralized validation middleware that runs before function execution. Use FluentValidation or Data Annotations with a global validator. Apply `InputSanitizer` at the middleware level rather than per-function.

### 6.2 High-Priority Issues

#### FLAW 4: Singleton MongoService with No Connection Pooling Config
- **Problem:** `MongoService` is registered as Singleton and creates a single `MongoClient`. The MongoDB connection pool settings are not explicitly configured — relying on defaults (100 max connections).
- **Impact:** Under high load, connection pool exhaustion could occur. Azure Functions can scale to many instances, each with its own connection pool.
- **Recommendation:** Explicitly configure `MongoClientSettings` with `MaxConnectionPoolSize`, `MinConnectionPoolSize`, `WaitQueueTimeout`, and `ServerSelectionTimeout`. Consider using the `IMongoClient` singleton pattern recommended by the MongoDB driver documentation.

#### FLAW 5: No Caching Strategy
- **Problem:** `IMemoryCache` is registered but appears minimally used. Every API request hits MongoDB directly for data that changes infrequently (categories, rewards, offers, outlet settings, expense types, sales item types).
- **Impact:** Unnecessary database load. Higher latency for frequently-accessed, rarely-changed data.
- **Recommendation:** Implement caching for reference data: categories, subcategories, rewards, active offers, outlet settings, expense types, sales item types. Use `IMemoryCache` with sliding/absolute expiration. Invalidate on write operations.

#### FLAW 6: No Pagination on Several List Endpoints
- **Problem:** Multiple endpoints return ALL documents without pagination: `GetAllSales`, `GetAllExpenses`, `GetAllStaff`, `GetMenu`, `GetAllOrders` rely on the caller to paginate but some don't enforce limits.
- **Impact:** As data grows, unbounded queries will cause timeouts, high memory usage, and slow responses.
- **Recommendation:** Enforce server-side pagination with default/max limits on all list endpoints. Use `PaginationHelper` consistently across all listing functions.

#### FLAW 7: Fat Function Files
- **Problem:** Some Azure Function files contain many HTTP endpoints in a single class (e.g., `InventoryFunction.cs` with 20+ endpoints, `LoyaltyFunction.cs` with 14 endpoints, `StaffFunction.cs` with 13 endpoints).
- **Impact:** Large files are harder to maintain, review, and test. Single file changes affect many unrelated endpoints.
- **Recommendation:** Split large function files by sub-domain: `InventoryQueryFunction.cs` + `InventoryCommandFunction.cs`, `LoyaltyUserFunction.cs` + `LoyaltyAdminFunction.cs`. Follow CQRS-light pattern.

### 6.3 Medium-Priority Issues

#### FLAW 8: Frontend Services Lack Error Recovery
- **Problem:** Most frontend services use a generic `handleServiceError()` which logs and rethrows. There's no retry logic, no offline queueing, no graceful degradation.
- **Impact:** Any transient network error fails the operation completely. No offline capability despite being a PWA.
- **Recommendation:** Add HTTP interceptor-level retry for transient errors (5xx, timeout). Implement offline queue for critical mutations (orders, clock-in/out). Use the Service Worker for background sync.

#### FLAW 9: No Database Indexing Strategy Documentation
- **Problem:** `MongoInitializationService` creates some indexes at startup, but there's no documentation of the indexing strategy. Common query patterns may lack indexes.
- **Impact:** Query performance degrades as collections grow. Without compound indexes on frequently-filtered fields (`OutletId` + `Date`, `UserId` + `Status`), full collection scans occur.
- **Recommendation:** Audit all query patterns. Ensure compound indexes for: `{OutletId, Date}` on sales/expenses/attendance, `{UserId, Status}` on orders, `{OutletId, Status}` on inventory, `{OutletId, CategoryId}` on menu items. Document all indexes.

#### FLAW 10: No API Versioning Strategy
- **Problem:** `ApiVersionMiddleware` exists but routes have no version prefix (e.g., `/api/v1/menu`). All 250+ endpoints share the same namespace.
- **Impact:** Breaking changes to any endpoint affect all consumers. No way to deprecate old endpoints while supporting new versions.
- **Recommendation:** Add version prefix to routes. Support version negotiation via route (`/api/v1/`) or header (`Api-Version`). Plan deprecation lifecycle for breaking changes.

#### FLAW 11: Timer Triggers Without Distributed Locking
- **Problem:** `PriceUpdateScheduler` and `DatabaseBackupFunction` use Azure Functions Timer Triggers. If the app scales to multiple instances, each instance runs the timer independently.
- **Impact:** Duplicate price updates. Duplicate backup operations.
- **Recommendation:** Azure Functions Timer Trigger uses blob lease for singleton execution by default — verify this is working by checking `host.json` `singleton` settings. Alternatively, use a distributed lock in MongoDB.

#### FLAW 12: No Rate Limiting Differentiation
- **Problem:** `RateLimitingMiddleware` applies the same rate limit to all endpoints regardless of operation type.
- **Impact:** Expensive operations (report exports, analytics queries) should have lower limits than cheap reads (health check, menu listing). Login endpoint should have tighter limits than browsing.
- **Recommendation:** Implement tiered rate limits: stricter for auth endpoints (prevent brute force), moderate for admin operations, relaxed for public reads. Consider per-user rate limiting in addition to per-IP.

### 6.4 Low-Priority / Improvement Opportunities

#### FLAW 13: Mixed Authentication Patterns
- **Problem:** Some functions use `AuthorizationHelper.GetUserId()` for auth, some check `AuthorizationHelper.IsAdmin()`, and the pattern is inconsistent. Auth logic is duplicated across function files.
- **Impact:** Risk of forgetting auth checks on new endpoints. No centralized authorization policy enforcement.
- **Recommendation:** Create a centralized `[Authorize]`-like middleware or filter that can be declaratively applied. Implement role-based policies: `RequireAdmin`, `RequireUser`, `RequireAuthenticated`.

#### FLAW 14: No Health Check for Dependencies
- **Problem:** `HealthFunction.cs` exists but likely only checks the function app is running, not whether MongoDB, Blob Storage, Razorpay, or email services are reachable.
- **Impact:** The app may report healthy while a critical dependency is down.
- **Recommendation:** Implement deep health checks that ping MongoDB (`db.runCommand({ping:1})`), check blob storage connectivity, and verify external service availability. Return degraded status when non-critical services are down.

#### FLAW 15: No Event Sourcing for Critical Operations
- **Problem:** Order status changes, payment events, inventory adjustments, and loyalty point operations are direct state mutations. If something goes wrong, there's no event trail to replay or audit.
- **Impact:** Difficult to debug issues like "where did the loyalty points go?" or "why was the order marked delivered?"
- **Recommendation:** For critical entities (Orders, Payments, Inventory, Loyalty), maintain an event log collection that records every state transition with timestamp, actor, old state, new state. This complements the existing `AuditLogger` which focuses on security events.

#### FLAW 16: Tight Coupling Between Functions and MongoService
- **Problem:** Function files directly depend on `MongoService` methods. There's no abstraction layer (no `IMenuService`, `IOrderService` interfaces).
- **Impact:** Cannot swap implementations, cannot unit test functions independently, cannot mock data access.
- **Recommendation:** Introduce domain service interfaces (`IMenuService`, `IOrderService`, etc.) between Functions and MongoService. This enables unit testing with mocks and allows future implementation changes.

#### FLAW 17: No Outbox Pattern for Cross-Cutting Concerns
- **Problem:** Order creation involves multiple side effects: loyalty points, notifications, wallet deduction, delivery partner assignment. These happen in a single method call — if one fails partway, partial state is committed.
- **Impact:** An order could be created but loyalty points not awarded, or wallet deducted but notification not sent.
- **Recommendation:** Implement the Transactional Outbox pattern: write the order and side-effect events to MongoDB in a single transaction, then process events asynchronously. This ensures atomicity and eventual consistency.

---

### Summary of Flaws by Severity

| Severity | # | Flaw | Effort |
|----------|---|------|--------|
| 🔴 Critical | 1 | God Service (MongoService) | High |
| 🔴 Critical | 2 | No Referential Integrity | Medium |
| 🔴 Critical | 3 | Missing Validation Layer | Medium |
| 🟠 High | 4 | No Connection Pool Config | Low |
| 🟠 High | 5 | No Caching Strategy | Medium |
| 🟠 High | 6 | No Pagination Enforcement | Medium |
| 🟠 High | 7 | Fat Function Files | Medium |
| 🟡 Medium | 8 | No Frontend Error Recovery | Medium |
| 🟡 Medium | 9 | No Index Strategy Docs | Low |
| 🟡 Medium | 10 | No API Versioning | High |
| 🟡 Medium | 11 | Timer Trigger Lock | Low |
| 🟡 Medium | 12 | No Rate Limit Differentiation | Low |
| 🟢 Low | 13 | Mixed Auth Patterns | Medium |
| 🟢 Low | 14 | No Deep Health Checks | Low |
| 🟢 Low | 15 | No Event Sourcing | High |
| 🟢 Low | 16 | Tight Coupling / No Interfaces | High |
| 🟢 Low | 17 | No Outbox Pattern | High |

---

> **Document generated by automated codebase scan — March 30, 2026**  
> **Covers:** 43 model files, 68 function files, 48 MongoDB collections, 60 frontend components, 50 frontend services, 5 signal stores
