# Maa Tara Cafe Website

## Project Structure

- `frontend/`: Angular application
- `api/`: Azure Functions (.NET) backend

## Phone-order customer invoices

The customer's private phone-order tracking link includes **View invoice** and
**Download invoice** (a text receipt). No customer sign-in is required. Invoices
show the saved items, charges, total, current payment/order status and outlet
branding, and remain accessible after delivery. Reopening the invoice reloads it
so payment changes are reflected.

`GET /api/public/phone-orders/{id}/invoice?token={trackingToken}` requires the same
private token as tracking and returns `{ "invoiceText": "..." }`. Invalid tokens,
missing orders and non-phone orders return 404. Responses are private/no-store.
The invoice includes customer details; treat the shared link as confidential.
Deploy both the API and frontend to enable this feature.

## CSRF tokens and Azure workers

CSRF tokens are user-bound, valid for 60 minutes, and stored as SHA-256 hashes
in the shared MongoDB `CsrfTokens` collection, not in worker memory. All API
workers must use the same application database. The database identity needs
collection/index creation and read/write/delete permissions for this collection;
expiry and user indexes are created on first token issuance. Expired tokens
are rejected immediately and removed by MongoDB's TTL index.

Deploy the backend fix before retrying Kitchen Display updates. Previously
issued in-memory tokens will be rejected once; the frontend uses the returned
`csrf_token_invalid` challenge to refresh the token and retry once. CSRF
protection must remain enabled in production. No seed data or manual migration
is needed.

## Production CSP and service worker

Deploy the frontend build and its `staticwebapp.config.json` together.
Font Awesome loads from cdnjs; that origin is allowed in `style-src`,
`font-src`, and `connect-src` because the Angular service worker fetches these
assets too. Production critical-CSS inlining is disabled so Angular emits
ordinary stylesheet links rather than CSP-blocked inline `onload` handlers;
script and stylesheet minification remain enabled. Do not add `unsafe-inline`
to `script-src` to work around stylesheet loading.

Google Translate loads its widget stylesheet from
`https://www.gstatic.com/_/translate_http/` and calls
`https://translate-pa.googleapis.com`; the stylesheet and API origins are allowed
in the style/connect directives, with `/v1/` also allowed in `script-src` for its
JSONP language requests. Its sandboxed iframe needs one inline bootstrap script.
The `sha256-R6kjt5FwTd5vAw94Q08NLDZsSaGTzg4NsdIfKtECSp0=` source
allows only that exact script, verified against Google's `TE_20261115` widget,
not arbitrary inline JavaScript. If Google changes it, inspect the new bootstrap
and verify its hash before updating the policy; do not enable `unsafe-inline`
or `unsafe-eval`. `frame-src 'self'`, `frame-ancestors 'self'`, and
`X-Frame-Options: SAMEORIGIN` permit the widget's local helper frames while
continuing to prevent other origins from framing the cafe site.
Unrelated third-party scripts remain blocked.

The service-worker registration's policy version changes when its required CSP
changes, since installed workers retain their installation-time policy.
After deployment, the app applies downloaded updates on the next navigation
rather than interrupting the current screen. If an old worker still controls
the page, close the site's tabs and reopen it.

## Customer Journey & Traffic Intelligence

Open **Admin → Analytics → Customer Journey & Traffic**
(`/admin/customer-journey`). This is separate from the existing User Analytics
screen. Seven tabs cover Overview, Live Visitors, User Journey, Menu Intelligence,
Conversion & Abandonment, User Analytics, and Reports.

- **Visitor Journey Explorer:** search pseudonymous sessions or explicitly linked
  accounts; inspect chronological page, menu, cart, checkout and confirmed-order
  events. Per-page active time covers the whole selected journey, not just the
  current timeline page.
- **Insights:** daily traffic, navigation paths, category/item interest, viewed-item
  add/order rates, net item quantities, device/cart-value/delivery-charge
  abandonment breakdowns, first-time/returning comparisons, and CSV exports.
- **Consent:** tracking is off until opt-in. Account linking requires a separate
  permission. Declining does not prevent shopping. The persistent **Privacy
  choices** control stops tracking and requests erasure of the current session.
  Earlier anonymous history is not retrospectively linked on sign-in.
- **Privacy controls:** **Privacy choices** is a normal-flow link at the bottom of
  each page, not a floating button covering content or mobile actions. Mobile
  footer spacing keeps it above fixed navigation when scrolled to the bottom.
  The preferences panel still opens on first visit and can be reopened from
  the footer.
- **Retention:** new detailed journey and consented legacy records expire within
  30 days using UTC MongoDB TTL indexes; reporting also excludes expired records
  immediately, without waiting for TTL cleanup. Existing pre-feature legacy
  analytics retain their previous policy and are not imported into this module.
  Consent expires after 30 days; changing permissions does not extend its deadline.
- **Privacy:** only normalized page names and allowlisted measurements are sent,
  not query strings, search text, addresses, passwords or payment credentials.
  Session tokens are capabilities: never publish them or add them to logs.
  Reports require an administrator and one selected outlet.

Final journey and legacy analytics batches use `fetch` with `keepalive: true`
and `credentials: 'omit'`. They authenticate with the consented journey capability
in the JSON body, not cookies or JWT headers. Do not replace these requests with
`sendBeacon`: its credentials-included mode conflicts with the API's
non-credentialed cross-origin policy. Deploy the frontend to apply this fix;
enabling credentialed CORS is not required.

### Metric definitions and coverage

These are **consented, attributed website journeys**, not a replacement for full
web/Zomato sales reporting. Collection begins after deployment and opt-in; there
is no historical backfill. Visitors are consent-scoped pseudonymous identifiers,
not guaranteed unique people. First-time means unrecognized in retained history.
Registered business accounts are explicitly labelled as an all-outlets count.
Known signed-in staff/admin traffic is reported separately and excluded from
customer funnels; genuinely anonymous staff cannot be recognized as staff.

Active engagement counts focused, visible activity only, stopping after 60
seconds without input. Live activity uses a 90-second freshness window and is
independent of historical date filters. Report dates are inclusive IST dates
within the last 30 calendar days.

Overall conversion is sessions with backend-confirmed order placements divided
by customer sessions. The sequential funnel additionally requires stages in
order; direct/restored carts can skip stages. Nonempty carts become abandoned
only after 30 minutes inactive, excluding carts continued into a new session.
An earlier purchase does not hide a later abandoned cart.

Orders are confirmed from saved backend records, never browser-supplied totals.
Cancelled/deleted orders remain placements but are excluded from net revenue and
item quantities. Item conversion requires ordering after viewing that item.
High-interest/low-conversion flags require at least five viewing sessions and
less than 10% viewer order conversion; they are investigation signals.

### Deployment and API checks

Deploy frontend and backend together: legacy ingestion now requires a valid
consented journey capability and can no longer silently link the JWT account.
The application database identity needs collection/index creation and normal
read/write/delete permissions for `JourneySessions`, `JourneyEvents` and the
legacy analytics collections. Indexes initialize on first use; no seed data
or migration/backfill is required.
Concurrent requests share one index-initialization task. Browser cancellations
cancel only that request's wait, not shared setup; setup has a one-minute timeout,
stops on host shutdown, and retries on a later request after failure. Live polling
waits for an in-flight response rather than cancelling it every 15 seconds.

Request examples, including authorization/retention errors, are in
[api/customer-journey.http](api/customer-journey.http). Reports explicitly reject
selections exceeding 20,000 retained sessions or 200,000 retained events;
narrow the date range instead of accepting silently truncated results.

## Zomato Sales & Payout Parser

Admin users can open **Tools → Zomato Sales & Payout Parser** and upload:
- Zomato order history CSV
- Zomato settlement XLSX

The parser returns owner-level insights (order summary, settlement summary,
reconciliation gaps, top discount constructs, and highest-deduction orders) and
can be reused for future reporting periods with the same two-file workflow.
It also includes detailed views like peak order hour, item-wise sold quantity,
daily/hourly trends, detailed discount metrics, and additional deduction details
from the settlement workbook tab, including adjusted additional deductions and
actual payout credited (settled payout + "Total Additions" − "Total Deductions"
adjusted amounts; section subtotals are not double-counted). It also parses
reviews & ratings from the order history CSV: rating distribution, positive/negative
share, written reviews, complaint tags, item-wise and daily ratings, KPT on low vs
high rated orders, and a per-order feedback list (lowest rating first).
Each parse is now persisted outlet-wise in the
database by report period, and admins can delete saved parser records for a
specific date period directly from the parser tool.

Use **Custom Range** beside the saved-period selector to open the compact date
picker, select **From / To**, and click **Apply** to recalculate insights for
an inclusive range of report-local order dates, combining saved uploads. Order
IDs are deduplicated across overlapping uploads, with the latest uploaded row
winning. New parses retain source rows in the same outlet-scoped snapshot; older
summary-only snapshots must be re-uploaded once. Missing source coverage produces
an explicit error rather than a partial report. Date-range views are read-only
and do not create or delete snapshots.

Order-level payout is filtered by order date, not bank transfer date. Additional
adjustments and actual bank credit are shown only when the selection contains
complete, non-overlapping source-report periods. For partial or overlapping
periods these amounts are unavailable (not prorated or shown as zero).
`GET /api/reports/zomato-sales-payout/insights/range?startDate=yyyy-MM-dd&endDate=yyyy-MM-dd`
requires an admin and outlet context; incomplete source coverage returns HTTP 409.

### Kitchen & Pickup Performance

The parser's **Kitchen & Pickup** tab analyses delivered orders: mean, median,
P90 (linear interpolation), fastest/slowest orders, fixed duration distributions,
target breaches, daily trends, day-by-hour heatmaps, hourly volume and recurring
delay signals. Its KPT-versus-wait scatter shows four investigation groups;
the durations may overlap and are never added together.

Editable cafe targets default to **20 minutes KPT / 5 minutes rider wait**;
these are not Zomato SLAs. Targets apply to the current view, including its
optional immediately preceding equal-length-period comparison. Comparison
requires exact date coverage and reports unavailable data explicitly.
Timing changes use minutes; breach-rate changes use percentage points.

Cohorts cover items present, exact item/quantity combinations, total quantity,
distinct items, basket size and delivery type, with searchable, paginated results
and minimum-sample filters. Ready-marking groups and hourly marking issues,
rating/complaint rates by delay bucket, and searchable/filterable delayed-order
exceptions help investigate individual cases. Excess rider-wait minutes are
operational totals, not monetary loss estimates.

The Admin Dashboard also includes saved Zomato parser reports. Delivered orders
are merged into online sales (orders already present in legacy online sales are
skipped), payouts come from matching settlement rows (0 until a settlement is
uploaded), and settlement-only payouts are included. Platform items are
categorized by matching menu names (unmatched items appear as "Uncategorized");
per-item values are estimated from the order subtotal.
Item-wise Sales shows web/app, Zomato and combined web + Zomato quantities
alongside the total across all selected sources, both on screen and in the CSV.
Web/app includes dine-in, takeaway and phone orders; the source filter applies
to every quantity column. Matched item names and categories are grouped together.

The dashboard's **Total Revenue** card and breakdown panel show
`Total Revenue = Web Sales + Zomato Net Payout` (plus Swiggy, other online and
recorded offline sales when present). Zomato Net Payout = order-level payouts −
report additional deductions (ads etc.) + additional additions. Weekly
adjustments are applied only when the whole report period is inside the selected
range; partially overlapping reports are flagged and never prorated.

All metrics show sample counts. Zero waits are valid; blank and malformed/negative
timings are distinguished and excluded from duration denominators. Missing dates,
older source-validation coverage and small samples are flagged. Times remain
report-local. Item timings describe whole orders containing the item, not its
individual cooking time; rating/complaint associations do not establish causation.

Timing facts are saved with new snapshots and recalculated for custom dates.
Older snapshots lazily rebuild timing facts from retained source uploads without
replacing their financial summaries. Latest uploads win for overlapping order IDs.
Summary-only records need the original reports uploaded again; older retained
sources also need re-uploading to fully recover malformed-input provenance.
Missing source coverage is shown with an upload action, not an endless Retry
prompt. Upload the original CSV and XLSX for the missing dates and click
**Parse Reports** before requesting that range again. Missing comparison data
does not block current-period analytics.

## Customer Access

Customers can browse the menu and add items to the cart without signing in.
`GET /api/menu` is public; an optional `X-Outlet-Id` filters the results, and
customer visibility rules still apply. Some individual and category menu
endpoints require an outlet ID. Checkout, submitting dine-in orders, and making
reservations require authentication; staff functionality remains role-protected.
The `/reservations` page itself is public. The homepage Reserve button opens
the shared booking form in a popup. Guests can enter details, then use Sign in
to Reserve; their draft survives in-app login navigation and returns to the
reservation page for review and explicit confirmation (not automatic submission).
Drafts are held in memory only and are not retained across a browser reload.
My Reservations is shown only when signed in; the API continues to require
authentication for booking and private reservation access.
The cart and selected table remain available when a customer signs in.

In-app notifications target registered account IDs only. Guest placeholders
(including `guest_dinein` and `guest_takeaway`) are skipped before preference
lookups or notification writes; registered-user notification preferences still apply.

### My Profile

The profile overview shows the signed-in customer's photo, contact details,
live rewards balance, and three latest orders. Quick actions open orders,
favorites, saved addresses, loyalty rewards, and account settings. Edit Profile
opens a modal editor, while Favorites, Manage Addresses, and Account Settings
open their own modal views instead of an inline settings section. Account
Settings contains password and notification controls. Modals support Escape,
backdrop dismissal, focus restoration, and background scroll locking.
Password, notification preferences, photo management,
one-time personal fields, and checkout/payment recovery remain available.
Delivered orders can be reordered through the existing menu-availability/cart
flow; loading and retry states distinguish failed requests from empty accounts.

### Redeem Points

The loyalty page uses a turquoise-and-lime rewards layout with live balance,
tier progress, configured reward costs, and expandable points history (latest
entry first). Redemption still requires sufficient points and confirmation;
controls are disabled while a redemption is pending. Card/QR, tier benefits,
birthday bonuses, referrals, transfers, and external claims remain available.
Food icons and the bundled cafe artwork are decorative; the reference design's
example rewards and balances are not seeded into the account.

### Homepage quick navigation

The hero displays the full bundled pizza-and-drink image without cropping,
independently of live menu images. Desktop text overlays the image with a dark
gradient; on tablets, text sits above the unobscured image. Mobile places the
full image first, followed by centered copy and equal-width action buttons.

The mobile bottom bar opens separate dialogs for
Our Story, Offers, Rewards, and Contact. Dialogs support Escape, backdrop
dismissal, focus restoration, and scrolling without scrolling the page behind.
Our Story uses public statistics and recorded customer reviews. Its served-order
and menu/review counts are fetched afresh each time the dialog opens, bypassing
service-worker caching; loading and failure states replace stale counts. Its order
count covers Zomato/Swiggy imports, not all ordering channels. The displayed
Zomato rating of 4.2 is manually supplied, not fetched from Zomato.
Offers displays active configured deals and their codes and limits. Guests see
the loyalty overview; personal points and available rewards require login.
Contact uses the outlet address and clickable phone details, with Instagram,
Facebook, WhatsApp, and Google Maps icons inside the turquoise-and-lime card.
The quick category strip advances one category every three seconds when it
overflows. On mobile (up to 600px), a repeated visual set allows seamless forward
looping with safe-area edge spacing for curved displays; desktop resets to the
beginning and repeats. Its scrollbar is hidden;
manual scrolling and category links remain available. Automatic movement pauses
on hover, keyboard focus, hidden tabs, open popups, or reduced-motion preferences.
The Find Our Cafe artwork area supports a separate five-second looping carousel
with the same pause behavior. Its five supplied cafe photos display in their
provided order, filling the artwork area with proportional cropping, then repeat
from the first photo. The adjacent
text and maps link remain static.
These details are no longer repeated as inline sections below Discover. The
statistics strip, announcement banner, checkout recovery/reorder cards, category
grid, loyalty/offers panels, outlets/map, and reviews are removed from the page.
The mobile navigation and a compact copyright footer remain; the footer branding
and phone block are removed. Desktop Offers and Visit Us open the
corresponding dialogs. Desktop Our Story opens the same story popup as the
mobile bottom navigation.
Customer-only (`user`) accounts have no Dashboards or Operations dropdowns in
the navbar. Redundant customer Home links are removed (the logo returns home),
and Menu sits inside the Account dropdown instead of a separate navigation row.
A single Account menu contains Overview, Menu, My Orders, Reservations,
Subscriptions, Rewards, Profile and Logout; cart access and notifications remain.
Staff/admin and mixed-role accounts retain their additive workspace navigation.
Guests see direct Menu and Cart links, not Operations, My Account or Reserve
navbar links. The homepage Reserve popup and public reservation page remain
available; private account navigation is limited to signed-in users.
The customer account button displays the signed-in name; dropdown links are
highlighted only when their destination is active, rather than always emphasizing Profile.
The desktop cart sits next to Order Now, using a dark SVG icon on a lime
44px button with an item-count badge. Mobile keeps the same prominent cart
button in the header.
Navbar action buttons have 12px spacing; notifications match the cart's
lime button, dark SVG icon, and unread-count badge styling.
Mobile profile dropdowns toggle by tap and expand within the navigation drawer.
Hover opening and delayed closing are limited to desktop fine-pointer devices.
Instagram, Facebook, and WhatsApp icons link to the cafe's accounts below the
copyright. The benefits strip is removed; free delivery within 3 km on orders
of ₹299 or more is displayed inside the Offers dialog. This is promotional
copy only; it does not change delivery-zone fee calculations.

## Sign In

The mobile sign-in illustration is clipped to its SVG crop so tall/narrow
Android viewports do not expose the surrounding source image. Space is reserved
for the Home shortcut, and the form remains scrollable when the keyboard opens.

Checkout uses a turquoise/lime responsive design, with order details beside the
live summary on desktop and above it on mobile. Saved addresses, coupons,
loyalty redemption, scheduling, QR payment, and the final review step remain
available. Delivery messaging uses actual address checks rather than a fixed
free-delivery promotion from the visual reference.

Guest mobile navigation uses the turquoise/lime theme with Sign In.
Home and Menu rows are hidden on mobile; the logo still links Home,
the cart shortcut and landing-page information dialogs remain available.
On desktop, guest inner-page headers show only the Home-linked logo, cart, and
Sign In in the same turquoise/lime theme; Home and Menu rows remain hidden.

The Cart uses a turquoise cafe backdrop, rounded order-item and summary panels,
responsive quantity controls and a warm-gradient checkout action. Live totals,
loyalty previews, customizations, checkout recovery and dine-in actions remain.
Order notes retain their 500-character limit. Delivery terms are confirmed at
checkout rather than displaying an unverified free-delivery promotion.

Desktop Forgot Password follows the same two-column cafe theme, with a lime
Send Reset Link button, accessible email input, OR divider and Back to Login
link. Its success/error states and reset-email behavior are preserved; the
navbar is hidden on recovery. Mobile stacks centered cafe branding, the
translucent reset card and food artwork against the matching turquoise backdrop.

Desktop sign-in uses the same full-width turquoise background and two-column
composition as Create Account, with cafe artwork on the left and a rounded
translucent card on the right, without empty solid-color side bands.

On mobile, Create Account uses the matching turquoise cafe theme with centered
branding, a rounded translucent form, paired name and gender/birth-date fields,
lime submit action and food artwork. The customer navbar is hidden on mobile
registration, with an arrow/home exit instead. Desktop registration uses a
two-column turquoise layout with cafe branding and food artwork on the left,
a rounded form card on the right, and no customer navbar.
Existing validation, password strength, success confirmation and return URLs remain.

The login page uses the supplied cafe welcome artwork as its bundled background
with a rounded white sign-in card and lime actions. The artwork fits without
cropping on desktop. Mobile uses centered welcome branding above a translucent
rounded card, an OR divider, an outlined account-creation action, and food artwork
below the form. Responsive spacing fits standard desktop and mobile viewports
without page scrolling. Very small windows and zoomed views can
still scroll to keep all controls accessible. The full navbar is hidden only on
login; a Back to Home link provides an exit. Username/password authentication,
role-based destinations, return-URL handling, password recovery and account
creation are preserved. Password visibility can be toggled by keyboard.

## My Orders

The customer My Orders page uses the turquoise/lime theme with responsive
photo cards, actual item quantities, status badges and a delivery-progress strip.
Order thumbnails come from the live menu; an order-type illustration is used
when no matching image exists. Search matches order IDs and item names and
combines with the existing fulfillment/status filters. Table Bookings remains
available and supports searches by booking ID, customer, date or table.
Delivered cards expose Reorder and View Details; active orders link to tracking.
Inline expansion preserves payment, receipt, cancellation and dining-bill actions.
Quick reorder presets remain below the list. Admin order controls are retained.

## Reservations

The customer Reservations page uses a turquoise photo hero, lime booking tabs
and confirmation button, rounded booking fields and selectable time chips.
Existing cafe photos illustrate the hero and the "Before You Confirm" panel.
The form and guidance sit side by side on desktop and stack with the form first
on smaller screens. Public booking, sign-in draft recovery, outlet availability,
time slots and guest counts, authenticated submissions, and My Reservations
actions are unchanged. The homepage's compact reservation popup shares the
booking controls without the full-page hero, photos or management tabs.

## Meal Subscriptions

The customer Subscriptions page uses a turquoise hero, lime section controls and
subscribe buttons, and responsive meal-plan cards. Plan names, prices, durations,
included items and benefits come from the subscription API, not the design reference.
Plan category filters wrap on narrow screens so their labels remain fully visible
without horizontal scrolling.
The desktop combo-menu category filters also wrap into full-height rows; mobile
continues to use the category drawer.
Cards use a configured plan image, an included item's image, or a matching live
menu item's image; category illustrations appear when no image is available.
The custom-combo introduction also links from curated plans to the live menu
builder. Existing category filters, mobile selection/schedule steps, delivery
options, checkout, payment requests, and subscription management remain available.

Admins manage plans in a searchable, paginated table with status and duration
filters. **Create Plan** opens the details drawer, followed by full-page menu
selection and review/publish steps. Fixed and per-day entry both save a canonical
full-period base price; the duration discount is applied once at checkout.
The admin table crosses out the base price when discounted and shows the
discounted total rounded to whole rupees, with its effective daily rate below.
This display rounding does not change checkout or payment amounts.
Plans retain their configured images, benefits, delivery flag and category.
Drafts are inactive and unavailable to customer purchases; existing paid
subscriptions are not changed by plan edits.

Menu selections support quantities and individual serving weekdays. The weekly
pattern repeats over the subscription and is copied into the purchased basket.
Reusable named menu sets are stored per outlet in `SubscriptionMenuSets` and can
be applied or deleted. Existing items without serving days remain everyday items.
This schedule describes the basket; it does not introduce automatic delivery
order generation.

The admin overview uses confirmed (`paid`) subscriptions: lifetime subscription
count, current/prior IST calendar-month payment counts and actual `AmountPaid`
revenue, and distinct current paid customers per plan (including valid pauses).
Pending payment confirmation remains on the management page. Profitability is
an estimate from recorded menu making prices, quantities and scheduled weekdays,
assuming a start today in IST and excluding delivery/labour/overheads. Missing
costs are reported rather than treated as zero; the full margin is withheld.
Feature checkboxes persist descriptive benefits, not new fulfilment workflows.

New subscriptions and plans support only these durations, for both curated plans
and custom combos:

| Duration | Discount | Pauses per subscription | Maximum days per pause |
| --- | --- | --- | --- |
| 7 days | 3% | 1 | 3 |
| 14 days | 5% | 1 | 5 |
| 30 days | 10% | 2 | 7 |

Pause allowances apply across the entire purchased subscription, not per month.
Choose a duration in **My Subscriptions**; deliveries resume automatically at that
deadline, or earlier using **Resume Deliveries**. The subscription end date extends
by the days actually paused, rounded up for partial days and capped at the selected
duration. Remaining pauses and the automatic-resume time (IST) appear beside the controls.
Existing 7-, 14- and 30-day subscriptions use the new pause caps without changing
paid amounts or stored discounts. Legacy non-standard durations retain a two-pause,
seven-days-per-pause allowance; they cannot be purchased again.
The backend enforces these limits, records pause history, and uses versioned
updates to prevent duplicate extensions from simultaneous requests.
`ResumeSubscriptionPauses` runs every minute; subscription reads also reconcile
overdue pauses if the timer was delayed. Existing open pauses without a deadline
are capped at their duration's pause limit. Completed pauses from before pause
history was introduced cannot be counted retroactively against the allowance.
The function host requires its normal storage connection
for timer scheduling. Example API requests are in [subscriptions.http](api/subscriptions.http).

## Timezone Standard

IST (India Standard Time) is the business timezone. Calendar dates and timestamp
instants are distinct: a calendar date is not a UTC instant, while new dine-in
timestamps are stored as UTC instants and displayed in IST. Other legacy flows
still need field-by-field review; do not apply a blanket timestamp conversion.

- Use `IstDateHelper` for IST business-date operations, and explicit UTC values
  for new dine-in timestamp instants.
- Text dates in reconciliation, frozen-item, and inventory imports use
  `dd/MM/yyyy`, `dd-MM-yyyy`, or `yyyy-MM-dd`.
- Frontend business date formatting and payload preparation should use utilities in `frontend/src/app/utils/date-utils.ts`:
	- `getIstInputDate(...)`
	- `getIstIsoString(...)`
	- `getIstFileStamp(...)`

## Validation Notes

- Backend build: `dotnet build` from `api/`
- Frontend build: `npm run build` from `frontend/`
- UTC-pattern audit can be run using repository audit scripts/commands used during migration.

### Staged UTC marker rollout

The local and deployed APIs can share a database. Keep
`Timezone__EnableUtcMarkers` unset or `false` while any API instance is running
code that cannot read additional MongoDB fields. Deploy the compatible code
to **every** instance first, including local instances that use the database.
Only after verifying that all instances are upgraded, set
`Timezone__EnableUtcMarkers=true` on each instance and restart it. Roll back only
to a version that tolerates these fields once marker writes have begun.

Markers identify new dine-in session creation, settlement, timeline, and order
creation timestamps. They do not classify older records or normalize every order
or financial field. Unmarked records retain the existing compatibility behavior;
the dine-in report's historical timestamp heuristic remains provisional. Until
markers are enabled, newly created unmarked records can still exhibit the
legacy mixed-clock issue. No historical records are migrated by this setting.

GST, UPI reconciliation, and web-sales order date filtering use the UTC marker
for creation timestamps. Unmarked orders are treated as legacy wall-clock values,
so historical unmarked UTC records still require reconciliation. Other order
timestamps may use a different convention and must not be interpreted using the
creation marker.

The kitchen queue resolves unmarked, session-linked dine-in order creation
timestamps as UTC, retaining the existing ObjectId-based compatibility check for
historical IST wall-clock records. Phone orders use their UTC creation-time
contract, independently of their hash-based IDs; an explicit `createdAtIsUtc=false`
retains IST wall-clock interpretation. Phone creation writes the optional UTC marker
only when the staged rollout flag is enabled. Older unmarked phone orders from
before the UTC-writing change cannot be distinguished by ID and require explicit
classification if historical correction is needed. Other unmarked orders remain IST wall-clock.
Its `createdAt` response includes an explicit `+05:30` offset, and elapsed minutes
and completed-today creation-date checks use the same normalized value. This does
not enable marker writes, migrate stored records, or reinterpret preparation timestamps.

The notification bell interprets notification creation timestamps as stored IST
wall-clock values, including legacy MongoDB values serialized with `Z`. Explicit
IST offsets are preserved. Relative ages and older notification dates therefore
remain independent of the browser timezone; stored notification data is unchanged.

The read-only historical audit found expense and sales dates stored at both
00:00 UTC and 18:30 UTC. Do not blanket-convert their boundaries or data.
Those fields require a separate data-contract audit and regression tests.
Expense create/update and spreadsheet imports construct unspecified-kind dates,
which the MongoDB driver serializes using the API host's local timezone. Sales
create/update accept a `DateTime` from JSON while spreadsheet imports construct
unspecified-kind dates. Date-range and summary consumers also serialize
unspecified-kind boundaries, and cash reconciliation uses a separate storage
range. Consequently, changing only a writer or a range query can shift records
or make related reports disagree. Historical dates cannot be classified solely
by their stored time of day.

Web-sales order lists select the most recent records separately for marked UTC
and unmarked legacy orders, then apply their existing safety limit after
normalization. Dine-in audit events are streamed for the candidate date range
and normalized before selecting the latest 500; their legacy classification
still uses the provisional ObjectId heuristic.

The marker rollout is not yet validated across all screens, existing mixed
records, or deployed/local API instances; do not enable it until those checks
are complete. In particular, cash reconciliation still uses a raw order
timestamp range, session lookup sorts raw creation timestamps, and history
deletion uses a raw cutoff. These paths need compatible treatment and tests
before marker rollout. The web-sales safety limit also still caps results used
for some counts and reports, rather than representing an unlimited total.
