# Product Audit Report — Maa Tara Cafe Management Website

**Audit Date:** October 23, 2026<br>
**Auditor Role:** Product Owner  
**Tech Stack:** Angular 19 + .NET 9 Azure Functions + MongoDB Atlas + Azure Blob Storage + Razorpay + Twilio

> **Current review note:** Some completion percentages, architecture counts, and sprint statements below are March 2026 estimates, not revalidated metrics. The current source review confirmed zero tax on in-house online, dine-in, and counter orders; market-price providers remain placeholders; recommendations have no customer-facing component; and automated test coverage is minimal. Current gaps below supersede conflicting historical claims.

---

## Table of Contents

1. [Current State Summary](#1-current-state-summary)
2. [Missing Implementations (Bugs / Incomplete)](#2-missing-implementations)
3. [New Feature Recommendations](#3-new-feature-recommendations)
4. [Feature Completion by Area](#4-feature-completion-by-area)
5. [Recommended Implementation Sequence](#5-recommended-implementation-sequence)

---

## 1. Current State Summary

### Architecture Overview

| Layer | Technology |
|-------|-----------|
| **Frontend** | Angular 19.2, Standalone Components, Angular Signals, SCSS, PWA (Service Worker) |
| **Backend** | .NET 9, Azure Functions V4 (Isolated Worker), 6-stage middleware pipeline |
| **Database** | MongoDB Atlas (54 collections, 35+ compound indexes) |
| **Storage** | Azure Blob Storage + CDN (4 containers: menu-images, profile-pictures, invoice-uploads, receipt-images) |
| **Payments** | Razorpay via `IRazorpayService` (create, verify, refund) + Polly retry/circuit breaker |
| **Communication** | Twilio WhatsApp API (`IWhatsAppService`) + Gmail SMTP (`IEmailService`) via MailKit + Polly |
| **Auth** | JWT + BCrypt + CSRF + 4-tier Rate Limiting + Authorization Middleware |
| **Messaging** | Outbox pattern (OutboxProcessorFunction every 30s) for reliable email/WhatsApp/notification delivery |
| **Architecture** | CQRS function splits, 13 active repository interfaces, service interfaces, MongoInitializationService (IHostedService) |
| **Monitoring** | Azure Application Insights + Request Logging Middleware + Audit Logger |
| **CI/CD** | GitHub Actions (backend → Azure Functions, frontend → Azure Static Web Apps) |

### What's Already Built (Solid Foundation)

| Domain | Completion | Key Capabilities |
|--------|-----------|-----------------|
| **Auth & Security** | 98% | JWT, CSRF, rate limiting (4-tier), brute-force protection, BCrypt, audit logging, security headers, input sanitization, 6-stage middleware pipeline, authorization middleware, API versioning |
| **Menu Management** | 90% | CRUD, categories/subcategories, variants, images (Blob + CDN + compression), bulk Excel upload, search, favorites, dietary badges, sort by price/name, item detail modal, AI recommendations |
| **Order System** | 95% | Create/track/cancel, multi-status workflow, receipt upload, Razorpay + COD, admin status management, coupon discounts, loyalty discounts, tax + platform charge, order detail page, PDF receipt generation (QuestPDF), customer reviews, reorder, scheduling, outbox-based side-effects |
| **Payments** | 95% | Full Razorpay lifecycle via IRazorpayService — create order, verify signature (constant-time), refund processing, Polly resilience |
| **Loyalty & Bonuses** | 95% | 4-tier system (Bronze→Platinum), points, rewards, referrals, birthday bonus, external claims (Zomato/Swiggy invoices), point expiry, CQRS split (Admin/User functions), WhatsApp notifications, outbox-based auto-award |
| **Staff & HR** | 90% | CRUD (Command/Query split), daily performance logging, KPIs, bonus calculation engine, shift tracking, salary management, attendance clock-in/out, leave balance |
| **Inventory** | 80% | Stock tracking (Command/Query split), transactions, low-stock email alerts, auto-deduction on order, frozen items (Excel export), auto-reorder with purchase orders |
| **Expenses & Revenue** | 90% | Offline/online sales, expense types (offline + online), overhead costs, cash reconciliation, platform charges, GST reports (GSTR-1/GSTR-3B), report export (CSV/Excel/PDF) |
| **Analytics** | 80% | User analytics, session tracking, API performance, admin dashboard with SVG charts, period filtering, customer segmentation (auto-tag), branch comparison |
| **Notifications** | 90% | In-app (30s polling + NotificationStore), email (MailKit/SMTP via outbox), WhatsApp (Twilio via outbox), per-user preferences, preference-aware sending |
| **Multi-Outlet** | 85% | Outlet-scoped data, outlet selector component + outlet interceptor, per-outlet settings/tax/staff assignment |
| **Uploads & Storage** | 95% | Menu images, profile pictures, receipt images, invoice uploads, Excel/CSV bulk import, ImageCompressor (SixLabors.ImageSharp), 4 Blob containers |
| **DevOps** | 90% | CI/CD (GitHub Actions), daily DB backups (34 collections → Blob with 30-day retention), Application Insights, Swagger/OpenAPI docs, MongoInitializationService (async startup) |

### Backend Inventory

- **73 function files** with 315+ HTTP endpoints, 2 timer triggers, 1 warmup trigger
- **54 MongoDB collections** with 35+ compound indexes
- **24 service files**: 10 MongoService partials + AuthService, BlobStorageService, NotificationService, EmailService, WhatsAppService, RazorpayService, MarketPriceService, EventLogService, OutboxService, FileUploadService, MongoInitializationService + 3 interfaces (IEmailService, IRazorpayService, IWhatsAppService)
- **6 middleware** (pipeline order): SecurityHeaders → InputSanitization → RateLimiting → Authorization → RequestLogging → ApiVersioning
- **13 active repository interfaces**: IMenuRepository, IOrderRepository, IUserRepository, ILoyaltyRepository, IOfferRepository, IFinanceRepository, IPricingRepository, IInventoryRepository, IStaffRepository, IOutletRepository, IOperationsRepository, INotificationRepository, IAnalyticsRepository
- **18 helper files**: validators, sanitizers, middleware, pagination, image compression, invoice parser, loyalty helper
- **46 model files** covering all domain entities

### Frontend Inventory

- **65 components** (59 feature + 4 shared + 1 standalone + 1 app root)
- **52 services** covering all domains
- **4 HTTP interceptors**: Error → Auth → Outlet → Analytics
- **2 route guards**: authGuard, adminGuard (1 file)
- **5 signal stores**: auth.store, cart.store, outlet.store, ui.store, notification.store
- **4 utilities**: date-utils, error-handler, file-download, loading
- **4 model files**: bonus, ingredient, outlet, staff
- **~49 routes**: 13 public + 6 auth-protected + 30 admin children (most lazy-loaded)

---

## 2. Missing Implementations

### Priority 1 — Critical Gaps (Directly Hurt Revenue)

| # | Gap | Location | Impact |
|---|-----|----------|--------|
| **M1** | ~~**No coupon/promo code at checkout**~~ | ✅ **DONE (Sprint 1)** — Coupon input at checkout validates against offers API. Supports percentage/flat/BOGO discounts with max discount cap, usage limits, date range, and min order validation. Discount shown in order summary and order history. | ~~Offers are useless decoration.~~ Fixed. |
| **M2** | **Tax/GST calculation and display** | ⚠️ **PARTIAL / TAX DISABLED** — Online, dine-in, and counter-sale backend flows currently store zero tax. Online orders use a flat ₹2 platform charge, not a 2.5% platform charge. GST reports aggregate stored/imported values, so correct tax calculation and filing readiness remain unverified. |
| **M3** | ~~**No order detail page**~~ | ✅ **DONE (Sprint 2)** — Dedicated `/orders/:id` route with full breakdown, timeline, progress stepper, review/rating, PDF receipt download. OrderDetail component with lazy loading. | ~~Poor post-purchase UX.~~ Fixed. |
| **M4** | ~~**No reorder capability**~~ | ✅ **DONE (Sprint 1)** — "🔄 Reorder" button on past orders adds all items to cart and navigates to /cart. | ~~Friction for repeat customers.~~ Fixed. |
| **M5** | ~~**No customer review/rating submission**~~ | ✅ **DONE (Sprint 2)** — ReviewFunction (rating 1-5 + text, validates order exists). CustomerReviews component + customer-review.service.ts. Public `/reviews` route. Post-order rating prompt on order detail page. | ~~Zero feedback loop.~~ Fixed. |
| **M6** | ~~**Loyalty points not usable at checkout**~~ | ✅ **DONE (Sprint 1)** — Toggle to use loyalty points at checkout. Auto-calculates max usable points (1 point = ₹0.25). Backend deducts points and records transaction. Discount shown in summary. | ~~Loyalty program feels pointless.~~ Fixed. |
| **M7** | ~~**QR code is a fake placeholder**~~ | ✅ **DONE (Sprint 1)** — Real scannable QR code using `qrcode` npm package with `QRCode.toCanvas()`. | ~~QR feature is non-functional.~~ Fixed. |

### Priority 2 — High-Impact Missing Features

| # | Gap | Location | Impact |
|---|-----|----------|--------|
| **M8** | ~~**No delivery fee calculation**~~ | ✅ **DONE (Sprint 3)** — Zone-based delivery fee with DeliveryZoneFunction + admin delivery zones UI. Configurable min/max distance, fee, free delivery threshold. Integrated into checkout. | ~~Revenue leakage.~~ Fixed. |
| **M9** | ~~**No order scheduling**~~ | ✅ **DONE (Sprint 3)** — Date+time picker in checkout, `scheduledFor` field on orders, admin visibility for scheduled orders. | ~~Missing catering use case.~~ Fixed. |
| **M10** | ~~**No real-time order tracking**~~ | ✅ **DONE (Sprint 2)** — 15-second polling on order detail page, progress stepper with pulse animation, live status indicator. | ~~Customer anxiety.~~ Fixed. |
| **M11** | ~~**No menu item sorting**~~ | ✅ **DONE (Sprint 2)** — Sort by price (low→high, high→low), name (A-Z, Z-A). | ~~Poor discoverability.~~ Fixed. |
| **M12** | ~~**No menu item detail modal/page**~~ | ✅ **DONE (Sprint 2)** — Full description, dietary badge, pricing grid, variants list, add-to-cart. | ~~Lost upsell opportunity.~~ Fixed. |
| **M13** | ~~**No admin report export**~~ | ✅ **DONE (Sprint 3)** — ReportExportFunction (CSV/Excel via EPPlus) + GstReportFunction (GSTR-1/GSTR-3B with HSN codes). Admin UI with date range picker + format selector. | ~~Manual data compilation.~~ Fixed. |
| **M14** | **No supplier management** | Inventory tracks stock levels but no supplier directory. PurchaseOrder model exists, partial generation via AutoReorder. | Inventory management partially built — auto-reorder works but no supplier CRUD. |

### Priority 3 — Nice-to-Haves (Expected by Modern Users)

| # | Gap | Impact |
|---|-----|--------|
| **M15** | ~~No PWA (Progressive Web App)~~ | ✅ **DONE (Sprint 4)** — manifest.webmanifest, @angular/service-worker, ngsw-config.json with app shell prefetch + API freshness caching. |
| **M16** | ~~No dietary tags (Veg 🟢 / Non-Veg 🔴 / Vegan / Jain badges) on menu items~~ | ✅ **DONE (Sprint 1)** — `dietaryType` field on MenuItem model (veg/non-veg/egg/vegan). Color-coded badges (🟢/🔴/🟡) on menu cards. Admin dropdown to set type. |
| **M17** | No spicy level indicator on items | Common expectation for food apps. |
| **M18** | No estimated preparation/delivery time | Customers don't know how long to wait after ordering. Kitchen Display tracks prep time but doesn't surface it to customers. |
| **M19** | No global search bar in navbar | Users must navigate to the menu page to search for items. |
| **M20** | No dark mode toggle | Modern UX expectation, especially for evening/night browsing. Kitchen Display has dark theme but not customer-facing. |
| **M21** | No multi-language support (at minimum Hindi) | Limits reach across India. |
| **M22** | ~~No customer testimonial submission form on landing page~~ | ✅ **DONE (Sprint 2)** — CustomerReviews component with review submission, public `/reviews` route. |
| **M23** | Customer-facing recommendations | ⚠️ **PARTIAL** — Backend recommendations and `recommendation.service.ts` exist, but no customer-facing component consumes the service. |
| **M24** | ~~No order invoice/receipt PDF generation~~ | ✅ **DONE (Sprint 2)** — ReceiptPdfFunction via QuestPDF (`GET /orders/{id}/receipt-pdf`). |
| **M25** | No stock-out auto-disable on menu | Out-of-stock items still show (with badge) but confuse users. Should be hideable. |
| **M26** | No tip/gratuity option at checkout | Small but meaningful for staff morale and retention. |
| **M27** | Automated test coverage | HIGH risk — no backend test project; four Angular spec files provide very limited coverage. `AppComponent` specs contain stale title/template assertions. The attempted Angular test run did not complete because Karma did not capture a browser. |
| **M28** | Market-price update workflow | **INCOMPLETE** — AGMARKNET and web-scraping providers return failure placeholders, and the scheduled trigger is commented out. |

---

## 3. New Feature Recommendations

### Tier A — Differentiators (Would Set You Apart)

| # | Feature | Description | Business Value |
|---|---------|-------------|----------------|
| **N1** | **Table Reservation System** | Let dine-in customers book tables with time slots, party size, special requests. Admin sees reservation calendar with availability heatmap. | Reduces walk-away customers; enables capacity planning; adds a reason to visit the website. |
| **N2** | **Kitchen Display System (KDS)** | A dedicated tablet/screen view for kitchen staff showing incoming orders in real-time, sorted by priority, with "Start Preparing" / "Ready" buttons. Auto-updates order status. | Replaces paper tickets; reduces errors; tracks prep time automatically. |
| **N3** | **Customer Wallet / Prepaid Balance** | Removed from product scope in July 2026. | — |
| **N4** | **Smart Feedback System** | Post-delivery, auto-send a 1-5 star rating prompt (push/WhatsApp/email). Negative ratings (≤3 stars) trigger instant admin alert with customer call-back option. | Closes the feedback loop; catches issues before they become Google/Zomato reviews. |
| **N5** | **Staff Mobile App / PWA** | Lightweight PWA for staff to check shifts, log attendance, view their performance scores, see daily targets, and request leaves. | Reduces admin overhead; empowers staff with self-service. |
| **N6** | **AI Menu Recommendations** | Based on order history + time of day + season, suggest items to each customer. "Morning? Try our masala chai combo." "You liked paneer tikka — try our paneer wrap." | Increases average order value by 15-25% (industry benchmark). |

### Tier B — Expected by Serious Cafe Businesses

| # | Feature | Description |
|---|---------|-------------|
| **N7** | **GST Tax Report Generator** | Auto-generate GSTR-1 and GSTR-3B reports from sales data. Export in CA-friendly Excel/CSV format with HSN codes. |
| **N8** | **Wastage Tracking Module** | Log daily food wastage by item. Compare against recipes. Identify loss patterns. Weekly wastage report for managers. |
| **N9** | **Attendance & Leave Management** | Staff clock-in/clock-out (geo-fenced optional). Leave balance tracking. Auto-calculate payroll days. Monthly attendance report. |
| **N10** | **Multi-Branch Comparison Dashboard** | Side-by-side outlet performance: revenue, orders, ratings, staff efficiency. "Which outlet is performing best this month?" |
| **N11** | **Customer Segmentation & CRM** | Auto-tag customers (New / Regular / VIP / Dormant based on order frequency). Auto-trigger re-engagement offers for dormant users via WhatsApp/email. |
| **N12** | **Print-Ready KOT (Kitchen Order Ticket)** | Generate thermal-printer-compatible order tickets (80mm receipt format). One-click print from admin orders view. |

### Tier C — Future Roadmap (Post-Launch Polish)

| # | Feature | Description |
|---|---------|-------------|
| **N13** | **Combo/Meal Deal Builder** | Admin creates bundled offers (burger + fries + drink at X% off). Customer selects from preset combos in menu. |
| **N14** | **Happy Hour Automation** | Auto-apply discounts during specific hours. E.g., "3-5 PM: All beverages 20% off." Time-based rules configurable by admin. |
| **N15** | **Ingredient Auto-Reorder** | When stock drops below reorder point, auto-generate purchase order. Optional auto-email to supplier. |
| **N16** | **Multi-Outlet Menu Variations** | Different pricing/availability per outlet from the same base menu. E.g., "Outlet A has dosa, Outlet B doesn't." |
| **N17** | **Customer Subscription Plans** | "Coffee Club: ₹999/month for 1 free filter coffee daily." Recurring Razorpay subscription with usage tracking. |
| **N18** | **Social Media Integration** | Auto-post new menu items or offers to Instagram/Facebook page. Pull reviews from Google Business Profile. |
| **N19** | **Delivery Partner Integration** | If self-delivery: driver assignment + GPS tracking. If third-party: Dunzo/Porter API integration for last-mile. |
| **N20** | **Voice Ordering (Experimental)** | "Alexa, order my usual from Maa Tara Cafe." Uses order history for personalization. |

---

## 4. Feature Completion by Area

### Customer-Facing UX Deep Dive

| Area | Completion | What Works | What's Missing |
|------|-----------|------------|----------------|
| **Menu Browsing** | Historical estimate | Search, category filter, favorites, add-to-cart, availability badge, dietary badges, sorting, and item details. Recommendation API exists but is not surfaced in customer UI. | No nutritional info, no spicy indicator, no customer-facing recommendations |
| **Cart & Checkout** | Historical estimate | Address selection, Razorpay/COD, special notes, packaging charges, coupon/promo codes, loyalty point redemption, delivery fee, scheduling, and order type | Tax currently calculates as zero in backend; platform charge is a flat ₹2 for online orders; no tips |
| **Order Tracking** | 95% | Status badges, cancel, receipt upload, admin status update, reorder button, real-time 15s polling, order detail page (`/orders/:id`), progress stepper, review/rating, PDF receipt download (QuestPDF), outbox-based notifications | No ETA prediction |
| **Customer Reviews** | 85% | ReviewFunction (1-5 stars + text, order-verified), CustomerReviews component, public `/reviews` route, customer-review.service.ts + reviews.service.ts | No photo reviews, no moderation workflow |
| **User Profile** | 75% | Edit info, change password, profile pic (Blob Storage), addresses (AddressFunction), favorites (FavoriteFunction), notification prefs | No 2FA, no account deletion |
| **Loyalty Program** | 95% | Points, tiers (Bronze/Silver/Gold), rewards, referrals, birthday bonus, external claims, real QR code, points redeemable at checkout, WhatsApp notifications, auto-award via outbox | No auto-tier-upgrade notification |
| **Wallet** | **0% (Removed)** | Wallet module removed from product (July 2026) | Feature discontinued |
| **Reservations** | **100%** | Book tables with time slots, party size, special requests, view/cancel my reservations | — |
| **Subscriptions** | **100%** | Browse subscription plans, view pricing/benefits/daily items, subscribe to plans | — |
| **PWA** | Historical estimate | Installable PWA, manifest, service worker, offline app shell, data caching, and Web Push subscription/sending code | Verify VAPID configuration and browser/service-worker delivery in deployment |

### Admin Panel Deep Dive

| Area | Completion | What Works | What's Missing |
|------|-----------|------------|----------------|
| **Dashboard** | 60% | 4 KPIs, 6-month sales chart, online/offline stats, top items/customers | No date range picker, no profit margin viz, no drill-down |
| **Analytics** | 75% | Sales insights, expense breakdown, growth rate, peak days, user analytics, customer segmentation (auto-tag), branch comparison | No predictive analytics, no goal tracking, no custom report builder |
| **Menu Admin** | 88% | CRUD, bulk upload, image management (Blob + compression), category/subcategory management, dietary type dropdown | No drag-drop reorder, no menu scheduling (seasonal items) |
| **Order Admin** | 90% | Status update workflow, order list, payment status badges, KDS Kanban board, KOT thermal print, order type/scheduling visibility, outbox-based notifications | No batch status update |
| **Inventory** | 82% | Stock tracking (CQRS split), transactions, email alerts (via IEmailService), auto-deduction, auto-reorder with reorder points, frozen items (Excel export), orphan cleanup | No supplier CRUD, no purchase order approval workflow |
| **Staff Admin** | 95% | CRUD (Command/Query split), performance KPIs, daily logging, bonus calculation, attendance clock-in/out, leave management, monthly report | No shift scheduling calendar, no payroll export |
| **Financial** | 92% | Sales, expenses, overhead costs, cash reconciliation, platform charges, GST GSTR-1/GSTR-3B reports, CSV/Excel/PDF export, date range reports | No P&L report, no Tally integration |
| **Kitchen** | **100%** | Kitchen Display System (4-column Kanban), real-time order queue, prep time tracking, KOT generation (80mm thermal) | — |
| **Reservations** | **100%** | Admin reservation list, status management, date filtering | — |
| **Wastage** | **100%** | Daily wastage logging by item, pattern analysis, weekly reports | — |
| **Delivery** | **100%** | Delivery zone management (zone-based fee rules), delivery partner CRUD, driver assignment | — |
| **Marketing** | **100%** | Combo meal builder, happy hour automation (time-based rules), customer subscriptions, customer segmentation (auto-tag) | — |
| **Reports** | **100%** | Export reports (CSV/Excel/PDF via EPPlus), GST reports (GSTR-1/GSTR-3B), branch comparison dashboard, receipt PDF (QuestPDF) | — |

---

## 5. Recommended Implementation Sequence

### Sprint 1 — Quick Wins ✅ COMPLETED
*Maximum impact, minimum effort*

- [x] **M1** — Coupon/promo code at checkout (validates against offers API, percentage/flat/BOGO, usage limits, min order)
- [ ] **M2** — Tax/GST is disabled in online, dine-in, and counter-sale calculations; online platform charge is currently a flat ₹2. Reconfirm tax and GSTIN requirements before marking complete.
- [x] **M4** — Reorder button (adds all items from past order to cart, navigates to /cart)
- [x] **M6** — Loyalty points at checkout (toggle to use points, 1 pt = ₹0.25, backend deduction + transaction logged)
- [x] **M7** — Real QR code (replaced canvas placeholder with `qrcode` npm package, `QRCode.toCanvas()`)
- [x] **M16** — Veg/Non-Veg/Egg/Vegan badges on menu items (`dietaryType` field, color-coded 🟢🔴🟡 badges, admin dropdown)

### Sprint 2 — Core UX (2-3 weeks) ✅ COMPLETED
*Complete the customer journey*

- [x] **M3** — Order detail page (dedicated `/orders/:id` route with full breakdown, timeline, actions)
- [x] **M5** — Customer review/rating (post-order rating prompt, 1-5 stars + text, backend API + order-detail UI)
- [x] **M10** — Real-time order tracking (poll order status every 15s on order detail page, progress stepper with pulse animation)
- [x] **M11** — Menu sorting (sort by price low→high, high→low, name A-Z, name Z-A)
- [x] **M12** — Menu item detail modal (full description, dietary badge, pricing grid, variants, add-to-cart)
- [x] **M24** — Receipt PDF generation (server-side PDF via QuestPDF with itemized bill, GST, outlet branding)

### Sprint 3 — Revenue Features ✅ COMPLETED
*Directly grows the business*

- [x] **M8** — Delivery fee calculation (zone-based pricing, distance/order-amount rules, DeliveryZoneFunction + admin UI + checkout integration)
- [x] **M9** — Order scheduling (date+time picker in checkout, scheduledFor field on orders, admin visibility)
- [x] **M13** — Admin report export (CSV/Excel/PDF export for sales, expenses, GST; ReportExportFunction + admin UI with date range & format picker)
- [ ] **N4** — Smart feedback system (deferred — existing review/rating system covers core use case)
- [x] **N7** — GST tax reports (GSTR-1/GSTR-3B format, HSN codes, monthly/quarterly; GstReportFunction + admin UI)

### Sprint 4 — Platform Features ✅ COMPLETED
*Elevate from website to platform*

- [x] **M15** — PWA conversion (manifest.webmanifest, @angular/service-worker, ngsw-config.json with app shell prefetch + API freshness caching, install prompt ready)
- [x] **N1** — Table reservation system (time slots, party size, special requests; TableReservationFunction + customer booking UI + admin reservations management)
- [ ] **N3** — Customer wallet (removed from product in July 2026)
- [x] **N12** — KOT thermal printing (80mm receipt format, KotFunction with print-ready HTML generation, one-click from kitchen display)

### Sprint 5 — Intelligence Layer ✅ COMPLETED
*Smart automation and insights*

- [x] **N2** — Kitchen Display System (real-time Kanban board with 4 columns: New/Preparing/Ready/Completed, prep time tracking, order cards with priority; KitchenDisplayFunction + dark-theme kitchen UI)
- [x] **N6** — AI menu recommendations (order history + time-of-day + seasonal context, personalized suggestions; RecommendationFunction + service)
- [x] **N8** — Wastage tracking module (daily logging by item, pattern analysis, weekly reports for managers; WastageFunction + admin UI)
- [x] **N10** — Multi-branch comparison dashboard (side-by-side outlet performance: revenue, orders, ratings, staff efficiency; BranchComparisonFunction + admin UI with bar charts + winner banner)
- [x] **N11** — Customer segmentation & CRM (auto-tag New/Regular/VIP/Dormant by order frequency, segment-based analytics; CustomerSegmentFunction + admin UI)

### Sprint 6 — Scale & Polish ✅ COMPLETED
*Long-term value builders*

- [x] **N9** — Attendance & leave management (clock-in/clock-out, leave balance, monthly report; AttendanceFunction + admin UI)
- [x] **N13** — Combo/meal deal builder (bundle items at discount, admin CRUD; ComboMealFunction + admin UI)
- [x] **N14** — Happy hour automation (time-based discount rules, configurable by admin; HappyHourFunction + admin UI)
- [x] **N15** — Ingredient auto-reorder (reorder point triggers, purchase order generation; AutoReorderFunction + admin UI)
- [x] **N17** — Customer subscription plans (recurring plans with daily items, usage tracking; SubscriptionFunction + customer plan browser + admin management)
- [x] **N19** — Delivery partner integration (driver assignment, partner management; DeliveryPartnerFunction + admin UI)
- [ ] **M27** — Unit & integration tests (deferred to post-launch)

---

## Summary

**The backend is enterprise-grade** — 54 collections, 73 function files, 315+ endpoints, 6-stage middleware pipeline, 13 active repository interfaces, outbox pattern, CQRS splits, Polly resilience, structured logging, and standardized error responses. The infrastructure is production-ready.

**Sprint 1 is COMPLETE** — the most impactful quick wins are shipped:
- ✅ Coupons, loyalty redemption, tax + platform charge display at checkout
- ✅ Reorder button on past orders
- ✅ Real QR codes on loyalty page
- ✅ Dietary badges (veg/non-veg/egg/vegan) on menu

**Sprint 2 is COMPLETE** — core customer UX journey is now full-featured:
- ✅ Order detail page (`/orders/:id`) with full breakdown, timeline, and actions
- ✅ Real-time order tracking (15s polling, progress stepper, live indicator)
- ✅ Customer review/rating system (ReviewFunction, 1-5 stars, CustomerReviews component)
- ✅ Menu sorting (price, name) and item detail modal (variants, dietary, pricing)
- ✅ Receipt PDF generation (QuestPDF, itemized bill, GST, outlet branding)

**Sprint 3 is COMPLETE** — revenue features driving business growth:
- ✅ Delivery fee calculation (zone-based pricing with admin delivery zone management)
- ✅ Order scheduling (date+time picker in checkout, scheduledFor field)
- ✅ Admin report export (CSV/Excel/PDF with date range picker)
- ✅ GST tax reports (GSTR-1/GSTR-3B format with HSN codes)

**Sprint 4 is COMPLETE** — platform capabilities unlocked:
- ✅ PWA conversion (manifest, service worker, offline app shell, API freshness caching)
- ✅ Table reservation system (customer booking + admin management)
- ❌ Customer wallet removed (July 2026)
- ✅ KOT thermal printing (80mm format, kitchen display integration)

**Sprint 5 is COMPLETE** — intelligence and insights layer:
- ✅ Kitchen Display System (dark-theme Kanban board, 4-column workflow, prep tracking)
- ✅ AI menu recommendations (order history + time-of-day + seasonal context)
- ✅ Wastage tracking (daily logging, pattern analysis, weekly manager reports)
- ✅ Multi-branch comparison (side-by-side outlet metrics, bar charts, winner banner)
- ✅ Customer segmentation (auto-tag New/Regular/VIP/Dormant, segment analytics)

**Sprint 6 is COMPLETE** — scale and polish features:
- ✅ Staff attendance & leave management (clock-in/out, leave balance, monthly report)
- ✅ Combo/meal deal builder (admin CRUD for bundled offers)
- ✅ Happy hour automation (time-based discount rules)
- ✅ Ingredient auto-reorder (reorder point triggers, purchase order generation)
- ✅ Customer subscription plans (recurring plans, usage tracking, customer browser)
- ✅ Delivery partner integration (driver assignment, partner management)

**Architecture Hardening is COMPLETE** — enterprise patterns applied:
- ✅ Outbox pattern for reliable side-effect delivery (email, WhatsApp, notifications, loyalty)
- ✅ CQRS function splits — Inventory→Command/Query, Loyalty→Admin/User, Staff→Command/Query
- ✅ 13 active repository interfaces backed by MongoService via DI
- ✅ Service interfaces (IEmailService, IRazorpayService, IWhatsAppService) for testability
- ✅ MongoInitializationService (IHostedService) for async startup
- ✅ 6-stage middleware pipeline — SecurityHeaders → InputSanitization → RateLimit → Authorization → RequestLogging → ApiVersioning
- ✅ Standardized error responses (`{ error = "message" }`) + structured logging (`LogError(ex, "...")`) across all functions
- ✅ Polly retry + circuit breaker on named HttpClients (WhatsApp, Razorpay)
- ✅ New functions: AddressFunction, FavoriteFunction, ExternalOrderClaimFunction, OrphanCleanupFunction, OutboxProcessorFunction, ReceiptPdfFunction, ReviewFunction, NotificationFunction
- ✅ Soft-delete (ISoftDeletable) + OrphanCleanupFunction for record lifecycle

**Current confirmed gaps (October 23, 2026):**
- Automated tests: no backend test project and only four Angular specs; the AppComponent spec is stale and the attempted run could not connect a browser.
- Ingredient market-price updates: provider implementations are placeholders; scheduler trigger is disabled.
- Tax/GST: in-house online, dine-in, and counter-sale flows store zero tax pending the business/GSTIN decision.
- Customer recommendations: backend and service exist, but no customer-facing component consumes them.
- Supplier management: no supplier directory or complete procurement workflow; purchase orders use free-text supplier names.
- External delivery marketplace sync: invoice-claim and internal delivery-partner flows exist, but direct Swiggy/Zomato order or payout sync is not implemented.
- Two-factor authentication is not implemented.
- Live database, payment-provider, web-push, and physical-printer behavior requires deployment validation.

The earlier sprint completion counts, effort estimates, and area percentages in this document are historical planning figures and should not be interpreted as current verified completion metrics.

---

*Updated by source review — October 23, 2026*
