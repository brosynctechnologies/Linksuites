# Admin panel (admin-web) design plan

Product: the internal console for the people who run the platform. Contract: `docs/PLATFORM_API.md`. App: `admin-web`, port 4300.

## Who and what

Four kinds of operators, mostly on laptops, sometimes on a phone for an urgent queue item:
- **Moderators** clear queues fast (approve, reject with a note, next).
- **Support** finds a business or person in seconds, understands its state on one page, suspends, unlocks, views as the customer.
- **Finance** checks payments, marks payouts paid, edits plans, gives comps.
- **Super admins** manage the admins.

Primary job: **act correctly and quickly, and never mistake this console for a customer app.** Every action that touches a customer
says so before it happens and is attributable afterwards.

## Colour

| Token | Hex | Use |
|---|---|---|
| Graphite | `#1D2228` | Text; primary buttons (white text); the left rail |
| Concrete | `#E9EBEE` | App background (cool grey) |
| Surface | `#FFFFFF` | Panels, tables, dialogs |
| Caution | `#F5C518` | Only the console bar at the top and the focus ring. It marks "you are in the admin console". Never used for buttons. |
| Line | `#D3D7DD` | Table rules and borders |
| Ok | `#1E7B4A` | Active, paid, approved, healthy job |
| Wait | `#9A6400` | Pending, requested, in review, job late |
| Stop | `#B42318` | Suspended, failed, rejected, destructive buttons |
| Muted | `#5B6470` | Secondary text, timestamps |

Contrast: Graphite on Concrete 13:1; Stop/Ok/Wait on white ≥ 4.5:1 at 14 px semibold.

## Type

- **IBM Plex Sans**: all UI. 14 px base in tables, 15 px in forms, line-height 1.45. Weights 400/500/600.
- **IBM Plex Mono**: only for values people copy or compare: ids, payment references, slugs, tokens, before/after JSON. Not for labels.
- Numbers in tables: `tabular-nums`, right-aligned; money as `₦3,500,000`.

## The memorable element: the console bar

A 36 px bar fixed at the very top: Caution yellow with a thin graphite diagonal stripe at its left end. It shows "Admin console", the
admin's name and **role**, and the session time left ("7 h 12 m"). Every page has it; nothing in any customer app looks like it. When
an action affects a customer, the confirmation dialog repeats a slim version of the bar so the context is unmistakable. Everything
else is quiet: white panels, hairline tables, no shadows except dialogs.

## Layout

- **Left rail** (Graphite, 232 px, collapses to icons below 1280 px, becomes a bottom sheet menu on phones): Overview, Businesses, Users,
  Moderation (Listings, Courses, Verifications, Reports, Reviews, Locations, Categories), Money (Payments, Payouts, Plans), Audit log,
  Health, Admins. Items the role can't use are not shown. Queue items show their counts.
- **Overview**: one row of figures per product (businesses, new this month, paying, suspended), MRR and this month's fees, the queue
  counts as links, failed emails and job health. A dense grid, not hero numbers.
- **Lists** (businesses, users, payments, audit): a sticky filter row above a dense table; rows link to detail; pagination at the bottom;
  URL holds filters. Empty states say what to search for.
- **Business detail**: header with name, product chip, status, plan; action buttons right (View as, Suspend/Reactivate, Comp plan); tabs:
  Overview (usage bars, subscription, public link), Members, Payments, Payouts, Audit.
- **User detail**: header with name, email, lock state, admin role; memberships table (links to businesses); activity counts; actions
  (Unlock, Send sign-in code, Send password reset email, Sign out everywhere). Sign out everywhere also ends the user's console sessions.
- **Moderation queues**: a two-pane layout on desktop: the queue list left, the item right with large photos (listings), documents opened
  in an inline viewer (verification), duplicate pairs side by side. Keyboard: `j`/`k` next/previous, `a` approve, `r` reject (opens the
  note box), `?` shows shortcuts.
- **Plans**: grouped by product; edit in a side panel with limits as labelled number inputs (-1 = unlimited) and features as a list.
  The panel states: "Changes apply to new subscriptions and at renewal. Current paid periods keep their price, limits and features."
- **Audit log**: table with action, actor, business, time; expanding a row shows before/after side by side, changed keys highlighted.
- **Admins**: table with role select per row; add by email; remove; the last super admin's controls are disabled with an explanation.
- **Sign-in**: email + password, then a 6-box code input (paste fills all), "Code sent to a…@…". A "Forgot password?" link leads to
  `/forgot-password` (sends `product: "admin"`), and the emailed link opens `/reset-password`. Session expiry or a 401 with
  `code: "session_ended"` returns here with a note.

## Sessions

The console token is backed by a server-side session (see `docs/PLATFORM_API.md`, Sessions). Sign out calls
`POST /api/platform/auth/logout` before forgetting the token, so the token stops working everywhere. The session also ends on
"sign out everywhere", a password reset or change, or removal as an admin; the next request gets 401 and the console returns to
sign-in. The console calls `GET /api/platform/me` on load and every few minutes and rebuilds the rail and the console bar's role from it,
so a role change shows without waiting for a 403. Business detail and the console offer "End view" for an open support view
(`POST /api/platform/view-as/{sessionId}/end`).

Alignment: left, numbers right. Radius 4 px on inputs/buttons, 6 px on panels. Tap targets ≥ 40 px on phones.

## Confirmations

Suspend, reactivate, view as, comp, plan price change, role change, admin removal and mark payout paid open a dialog that names the
business/person and the effect in one sentence ("Hides Lekki Homes' public pages and signs its 3 members out."). Suspend, reactivate,
comp and view-as need a typed reason (10–300 characters, counter shown). The confirm button repeats the action ("Suspend business").

## Copy

Short, factual, no exclamation marks. Status words match the API (Active, Suspended, Pending, Paid). Errors show the API's `detail`.
Toasts confirm with the past tense of the button ("Business suspended").

## Quality floor

Keyboard first, visible Caution focus ring (2 px), reduced motion respected (the only motion: dialogs and side panels), no horizontal
page scroll at 360 px (wide tables scroll inside their panel), session token in sessionStorage only.

## Review against the brief

- First idea: a dark console with a neon accent; rejected as the generic "admin dashboard" look. The light, dense console with a single
  caution bar is quieter and makes the one signal (you are in admin) impossible to miss.
- Plex Mono is used only where values are compared or copied, not as decoration.
