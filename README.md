# Maa Tara Cafe Website

## Project Structure

- `frontend/`: Angular application
- `api/`: Azure Functions (.NET) backend

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
