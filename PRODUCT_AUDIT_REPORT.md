# Product Audit Report — Maa Tara Cafe Management Website

**Audit Date:** March 29, 2026  
**Auditor Role:** Product Owner  
**Tech Stack:** Angular 19 + .NET 9 Azure Functions + MongoDB Atlas + Azure Blob Storage + Razorpay + Twilio

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
| **Frontend** | Angular 19.2, Standalone Components, Angular Signals, SCSS |
| **Backend** | .NET 9, Azure Functions V4 (Isolated Worker) |
| **Database** | MongoDB Atlas (34+ collections, comprehensive indexes) |
| **Storage** | Azure Blob Storage + CDN (images, backups) |
| **Payments** | Razorpay (create, verify, refund) |
| **Communication** | Twilio WhatsApp API + Gmail SMTP |
| **Auth** | JWT + BCrypt + CSRF + Rate Limiting |
| **Monitoring** | Azure Application Insights |
| **CI/CD** | Azure Static Web Apps + Functions deployment |

### What's Already Built (Solid Foundation)

| Domain | Completion | Key Capabilities |
|--------|-----------|-----------------|
| **Auth & Security** | 95% | JWT, CSRF, rate limiting, brute-force protection, BCrypt, audit logging, security headers, input sanitization |
| **Menu Management** | 80% | CRUD, categories/subcategories, variants, images (Blob + CDN), bulk Excel upload, search, favorites |
| **Order System** | 70% | Create/track/cancel, multi-status workflow, receipt upload, Razorpay + COD, admin status management, **coupon discounts, loyalty discounts, tax + platform charge** |
| **Payments** | 90% | Full Razorpay lifecycle — create order, verify signature, refund processing |
| **Loyalty & Bonuses** | 85% | 4-tier system (Bronze→Platinum), points, rewards, referrals, birthday bonus, external claims (Zomato/Swiggy invoices), point expiry |
| **Staff & HR** | 85% | CRUD, daily performance logging, KPIs, bonus calculation engine, shift tracking, salary management |
| **Inventory** | 75% | Stock tracking, transactions, low-stock alerts, auto-deduction on order, frozen items |
| **Expenses & Revenue** | 85% | Offline/online sales, expense types (offline + online), overhead costs, cash reconciliation, platform charges |
| **Analytics** | 70% | User analytics, session tracking, API performance, admin dashboard with SVG charts, period filtering |
| **Notifications** | 80% | In-app (30s polling), email (SMTP), WhatsApp (Twilio), per-user preferences |
| **Multi-Outlet** | 80% | Outlet-scoped data, outlet selector, per-outlet settings/tax/staff assignment |
| **Uploads & Storage** | 90% | Menu images, profile pictures, receipt images, Excel/CSV bulk import, auto-compression |
| **DevOps** | 85% | CI/CD (Azure), daily DB backups (34 collections), Application Insights, Swagger/OpenAPI docs |

### Backend Inventory

- **80+ API endpoints** across 30+ function files
- **34+ MongoDB collections** with comprehensive indexes
- **7 services**: MongoService, AuthService, BlobStorageService, NotificationService, EmailService, WhatsAppService, RazorpayService
- **4 middleware**: SecurityHeaders, RateLimiting, RequestLogging, ApiVersion

### Frontend Inventory

- **41 components** (13 customer-facing + 28 admin)
- **33 services** covering all domains
- **4 HTTP interceptors**: Auth, Error, Outlet, Analytics
- **2 route guards**: authGuard, adminGuard
- **3 signal stores**: auth.store, cart.store, outlet.store

---

## 2. Missing Implementations

### Priority 1 — Critical Gaps (Directly Hurt Revenue)

| # | Gap | Location | Impact |
|---|-----|----------|--------|
| **M1** | ~~**No coupon/promo code at checkout**~~ | ✅ **DONE (Sprint 1)** — Coupon input at checkout validates against offers API. Supports percentage/flat/BOGO discounts with max discount cap, usage limits, date range, and min order validation. Discount shown in order summary and order history. | ~~Offers are useless decoration.~~ Fixed. |
| **M2** | ~~**No tax display at checkout**~~ | ✅ **DONE (Sprint 1)** — Tax (2.5%) and Platform Charge (2.5%) displayed in checkout summary before placing order. Also shown in order history. Backend calculates and stores both. | ~~Customers don't know their exact total.~~ Fixed. |
| **M3** | **No order detail page** | No `/orders/:id` route. Orders expand inline only. No shareable order link. | Poor post-purchase UX. Can't share or bookmark order status. |
| **M4** | ~~**No reorder capability**~~ | ✅ **DONE (Sprint 1)** — "🔄 Reorder" button on past orders adds all items to cart and navigates to /cart. | ~~Friction for repeat customers.~~ Fixed. |
| **M5** | **No customer review/rating submission** | Reviews page is read-only, pulls only 5-star Zomato/Swiggy reviews. Customers who order directly can't rate or review. | Zero feedback loop for direct orders. |
| **M6** | ~~**Loyalty points not usable at checkout**~~ | ✅ **DONE (Sprint 1)** — Toggle to use loyalty points at checkout. Auto-calculates max usable points (1 point = ₹0.25). Backend deducts points and records transaction. Discount shown in summary. | ~~Loyalty program feels pointless.~~ Fixed. |
| **M7** | ~~**QR code is a fake placeholder**~~ | ✅ **DONE (Sprint 1)** — Real scannable QR code using `qrcode` npm package with `QRCode.toCanvas()`. | ~~QR feature is non-functional.~~ Fixed. |

### Priority 2 — High-Impact Missing Features

| # | Gap | Location | Impact |
|---|-----|----------|--------|
| **M8** | **No delivery fee calculation** | No zone-based or distance-based fee. No minimum order validation at checkout. | Revenue leakage on small/distant orders. |
| **M9** | **No order scheduling** | No "Schedule for later" time picker. All orders are immediate only. | Missing catering/advance-order use case. |
| **M10** | **No real-time order tracking** | Order status requires manual page refresh. No polling on the orders page after placing. | Customer anxiety — "Where is my food?" |
| **M11** | **No menu item sorting** | Customers can search and filter by category but can't sort by price, popularity, or rating. | Poor discoverability on large menus. |
| **M12** | **No menu item detail modal/page** | Grid cards show name + price only. No expanded view with full description, ingredients, variants list, nutritional info. | Lost upsell opportunity for items with variants. |
| **M13** | **No admin report export** | Dashboard has charts but no CSV/PDF export. No P&L statement. No tax report. | Admin must screenshot or manually compile data for accountants. |
| **M14** | **No supplier management** | Inventory tracks stock levels but no supplier directory. No purchase orders. No reorder automation. | Inventory management is half-built. |

### Priority 3 — Nice-to-Haves (Expected by Modern Users)

| # | Gap | Impact |
|---|-----|--------|
| **M15** | No PWA (Progressive Web App) — no install prompt, no offline mode, no push notifications | Mobile users can't install app or receive push. |
| **M16** | ~~No dietary tags (Veg 🟢 / Non-Veg 🔴 / Vegan / Jain badges) on menu items~~ | ✅ **DONE (Sprint 1)** — `dietaryType` field on MenuItem model (veg/non-veg/egg/vegan). Color-coded badges (🟢/🔴/🟡) on menu cards. Admin dropdown to set type. |
| **M17** | No spicy level indicator on items | Common expectation for food apps. |
| **M18** | No estimated preparation/delivery time | Customers don't know how long to wait after ordering. |
| **M19** | No global search bar in navbar | Users must navigate to the menu page to search for items. |
| **M20** | No dark mode toggle | Modern UX expectation, especially for evening/night browsing. |
| **M21** | No multi-language support (at minimum Hindi) | Limits reach across India. |
| **M22** | No customer testimonial submission form on landing page | Only shows imported platform reviews. |
| **M23** | No "Frequently Bought Together" / recommendations engine | Lost upsell revenue on every order. |
| **M24** | No order invoice/receipt PDF generation | Customers can't get a formal downloadable bill. |
| **M25** | No stock-out auto-disable on menu | Out-of-stock items still show (with badge) but confuse users. Should be hideable. |
| **M26** | No tip/gratuity option at checkout | Small but meaningful for staff morale and retention. |
| **M27** | No unit tests (backend or frontend) | High-risk deployments with no safety net. |
| **M28** | Scheduled price updater is commented out (`PriceUpdateScheduler.cs`) | Requires manual ingredient price updates. |

---

## 3. New Feature Recommendations

### Tier A — Differentiators (Would Set You Apart)

| # | Feature | Description | Business Value |
|---|---------|-------------|----------------|
| **N1** | **Table Reservation System** | Let dine-in customers book tables with time slots, party size, special requests. Admin sees reservation calendar with availability heatmap. | Reduces walk-away customers; enables capacity planning; adds a reason to visit the website. |
| **N2** | **Kitchen Display System (KDS)** | A dedicated tablet/screen view for kitchen staff showing incoming orders in real-time, sorted by priority, with "Start Preparing" / "Ready" buttons. Auto-updates order status. | Replaces paper tickets; reduces errors; tracks prep time automatically. |
| **N3** | **Customer Wallet / Prepaid Balance** | Let customers top-up a wallet (via Razorpay) and pay from balance. Offer 5-10% bonus on recharges (e.g., "Add ₹500, get ₹550"). | Locks in future revenue; faster checkout; reduces payment failures. |
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
| **Menu Browsing** | 65% | Search, category filter, favorites, add-to-cart, availability badge, **veg/non-veg/egg/vegan dietary badges** | No sorting, no item detail modal, no ratings, no nutritional info, no recommendations |
| **Cart & Checkout** | 75% | Address selection, Razorpay/COD, special notes, packaging charges, **coupon/promo codes, loyalty point redemption, tax (2.5%) + platform charge (2.5%) display** | No delivery fee, no scheduling, no minimum order check, no tips |
| **Order Tracking** | 50% | Status badges, cancel, receipt upload, admin status update, **reorder button** | No real-time tracking, no ETA, no order detail page, no rating prompt |
| **Customer Reviews** | 30% | Display 5-star platform reviews with pagination | Read-only, no customer submissions, no rating per order, no photo reviews, no moderation |
| **User Profile** | 60% | Edit info, change password, profile pic, addresses, favorites, notification prefs | No order history link, no loyalty view, no referral code display, no 2FA, no account deletion |
| **Loyalty Program** | 90% | Points, tiers, rewards, referrals, birthday bonus, external claims, **real QR code, points redeemable at checkout** | No points-based auto-tier-upgrade notification |

### Admin Panel Deep Dive

| Area | Completion | What Works | What's Missing |
|------|-----------|------------|----------------|
| **Dashboard** | 55% | 4 KPIs, 6-month sales chart, online/offline stats, top items/customers | No export, no date range picker, no profit margin viz, no outlet comparison, no drill-down |
| **Analytics** | 70% | Sales insights, expense breakdown, growth rate, peak days, user analytics | No predictive analytics, no goal tracking, no custom report builder |
| **Menu Admin** | 85% | CRUD, bulk upload, image management, category/subcategory management | No drag-drop reorder, no menu scheduling (seasonal items) |
| **Order Admin** | 65% | Status update workflow, order list, payment status badges | No KDS view, no thermal print, no batch status update |
| **Inventory** | 60% | Stock tracking, transactions, alerts, auto-deduction | No supplier management, no purchase orders, no wastage tracking, no auto-reorder |
| **Staff Admin** | 80% | CRUD, performance KPIs, daily logging, bonus calculation | No attendance/leave system, no shift scheduling calendar, no payroll export |
| **Financial** | 75% | Sales, expenses, overhead costs, cash reconciliation, platform charges | No P&L report, no GST report, no export to Tally/accounting software |

---

## 5. Recommended Implementation Sequence

### Sprint 1 — Quick Wins ✅ COMPLETED
*Maximum impact, minimum effort*

- [x] **M1** — Coupon/promo code at checkout (validates against offers API, percentage/flat/BOGO, usage limits, min order)
- [x] **M2** — Tax (2.5%) + Platform Charge (2.5%) display at checkout and order history
- [x] **M4** — Reorder button (adds all items from past order to cart, navigates to /cart)
- [x] **M6** — Loyalty points at checkout (toggle to use points, 1 pt = ₹0.25, backend deduction + transaction logged)
- [x] **M7** — Real QR code (replaced canvas placeholder with `qrcode` npm package, `QRCode.toCanvas()`)
- [x] **M16** — Veg/Non-Veg/Egg/Vegan badges on menu items (`dietaryType` field, color-coded 🟢🔴🟡 badges, admin dropdown)

### Sprint 2 — Core UX (2-3 weeks)
*Complete the customer journey*

- [ ] **M3** — Order detail page (dedicated `/orders/:id` route with full breakdown, timeline, actions)
- [ ] **M5** — Customer review/rating (post-order rating prompt, 1-5 stars + text, display on menu items)
- [ ] **M10** — Real-time order tracking (poll order status every 15s on order detail page, show progress stepper)
- [ ] **M11** — Menu sorting (sort by price low→high, high→low, popularity, newest)
- [ ] **M12** — Menu item detail modal (full description, variant selector, ingredients, related items)
- [ ] **M24** — Receipt PDF generation (server-side PDF with itemized bill, GST number, order details)

### Sprint 3 — Revenue Features (2-3 weeks)
*Directly grows the business*

- [ ] **M8** — Delivery fee calculation (distance/zone-based, minimum order threshold)
- [ ] **M9** — Order scheduling (time picker, scheduled status, auto-notify kitchen at prep time)
- [ ] **M13** — Admin report export (CSV/Excel export for sales, expenses, P&L summary)
- [ ] **N4** — Smart feedback system (auto-send rating request after delivery, alert on negative feedback)
- [ ] **N7** — GST tax reports (GSTR-1 format, HSN codes, monthly/quarterly summary)

### Sprint 4 — Platform Features (3-4 weeks)
*Elevate from website to platform*

- [ ] **M15** — PWA conversion (manifest.json, service worker, offline mode, install prompt)
- [ ] **N1** — Table reservation system (time slots, party size, admin calendar, confirmation notifications)
- [ ] **N3** — Customer wallet (top-up via Razorpay, pay from balance, bonus on recharge)
- [ ] **N12** — KOT thermal printing (80mm receipt format, one-click from orders admin)

### Sprint 5 — Intelligence Layer (3-4 weeks)
*Smart automation and insights*

- [ ] **N2** — Kitchen Display System (real-time order queue for kitchen, prep time tracking)
- [ ] **N6** — AI menu recommendations (order history based, time-of-day contextual suggestions)
- [ ] **N8** — Wastage tracking module (daily logging, pattern analysis, weekly reports)
- [ ] **N10** — Multi-branch comparison dashboard (side-by-side outlet performance)
- [ ] **N11** — Customer segmentation & CRM (auto-tagging, re-engagement automation)

### Sprint 6 — Scale & Polish (4+ weeks)
*Long-term value builders*

- [ ] **N9** — Attendance & leave management
- [ ] **N13** — Combo/meal deal builder
- [ ] **N14** — Happy hour automation
- [ ] **N15** — Ingredient auto-reorder
- [ ] **N17** — Customer subscription plans
- [ ] **N19** — Delivery partner integration
- [ ] **M27** — Unit & integration tests

---

## Summary

**The backend is robust** — 34+ collections, 80+ endpoints, solid security, comprehensive middleware stack. The infrastructure is production-ready.

**Sprint 1 is COMPLETE** — the most impactful quick wins are shipped:
- ✅ Coupons, loyalty redemption, tax + platform charge display at checkout
- ✅ Reorder button on past orders
- ✅ Real QR codes on loyalty page
- ✅ Dietary badges (veg/non-veg/egg/vegan) on menu

**Remaining gaps are customer-facing:**
1. **Checkout flow** — delivery fees, order scheduling, tips
2. **Post-purchase experience** — reviews, real-time tracking, order detail page
3. **Menu discovery** — sorting, detail views, recommendations

**Total inventory:**
- **28 missing implementations** identified — **6 completed (Sprint 1)**, 22 remaining
- **20 new feature recommendations** (6 differentiators, 6 business-expected, 8 future roadmap)
- **6 sprints** of prioritized work (Sprint 1 done)

---

*Generated by Product Audit — March 29, 2026*
