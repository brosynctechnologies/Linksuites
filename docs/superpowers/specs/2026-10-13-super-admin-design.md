# Super admin panel: design

Date: 13 Oct 2026. A platform-wide admin app for the six-product SaaS (OrderLink, InvoiceLink, BookLink, LearnLink,
PropertyLink, and the School Management System when it exists).

Decisions agreed: a **separate `admin-web` app**; **roles stored in the app** (Super admin, Moderator, Finance, Support) with an
emailed code at every admin sign-in; **read-only view-as** for support.

## 1. Goal

One place where the people who run the platform can see and manage every business, user, plan and payment across all products,
clear the moderation queues, and investigate problems, without being able to change customer data by accident or unseen.
Success means:

- Any business or user in any product can be found in seconds and its state understood from one page.
- Every admin action is attributable: who, what, when, why, before and after.
- An admin can do only what their role allows; the API enforces it, not just the UI.
- Support can see exactly what a customer sees, read only, and the customer can see that it happened.

Out of scope: editing customer content on their behalf, automated Paystack transfers for payouts, refunds of plan subscriptions,
email campaigns to customers, custom report builder, feature flags.

## 2. Where it lives

- **App:** `admin-web/` (Next.js 16.4, same stack as the other web apps), port **4300**, intended for a private URL such as
  `admin.<domain>`. Config `App:ProductWebUrls.admin`, CORS for 4300.
- **API:** routes under `/api/platform/...` in a new `Platform/` module in each API layer. The existing product admin routes
  (`/api/admin/...`, `/api/admin/property/...`) stay and are re-gated by role (section 3).
- **LearnLink and PropertyLink `/admin` pages** are replaced by a page linking to admin-web. Their code is removed once admin-web
  covers the same queues.

## 3. Admin accounts and roles

New shared-core table `platform_admins` (not tenant-owned): `id, user_id (unique), role, added_by, created_at, removed_at, removed_by`.

| Role | Can do |
|---|---|
| SuperAdmin | Everything below, plus manage admins |
| Moderator | Course marketplace review and categories; property listings, verification, reports, reviews, locations |
| Finance | Plans, comped plans, payments, payouts, revenue |
| Support | Find businesses and users, suspend/reactivate businesses, unlock users and send reset emails, read-only view-as, audit log |

All roles see the Overview. Removed admins (`removed_at` set) have no access.

- **Bootstrap:** on startup, every email in `Platform:AdminEmails` that has a user account and no admin row gets a SuperAdmin row.
  After that, admins are managed in the panel. Removing an email from config does not remove the admin.
- **Guard:** the last active SuperAdmin cannot be removed or demoted (409).
- **Tokens:** the `padm` claim carries the role name (replacing today's `"true"`). Policies: `platform:any`, `platform:moderator`,
  `platform:finance`, `platform:support`, `platform:super`; SuperAdmin satisfies every policy. `UserDto.isPlatformAdmin` stays
  (true for any role) and gains `platformRole`.
- **Re-gating existing routes:** `/api/admin/course-reviews`, `/api/admin/categories`, `/api/admin/property/*` (except payouts) →
  Moderator; `/api/admin/payouts*` and `/api/admin/overview` → Finance (overview also any admin).

## 4. Admin sign-in

- `POST /api/platform/auth/login { email, password }` → if the user is an active admin and the password is right, emails a
  6-digit code (existing OTP mechanism, 10-minute expiry, 5 attempts) and returns `{ challengeId }`. Wrong password or non-admin:
  the same generic error, rate limited.
- `POST /api/platform/auth/verify { challengeId, code }` → admin token: no tenant, `padm` role, **8-hour** access token and no
  refresh token (sign in again after 8 hours). Recorded in `audit_logs` (`admin.login`).
- Customer-app logins keep issuing `padm` for admins so LearnLink/PropertyLink links keep working until those pages are removed,
  but admin-web only accepts tokens from the platform login (claim `amr=otp`).

## 5. Read-only view-as

- `POST /api/platform/businesses/{tenantId}/view-as { reason (required, 10-300 chars) }` (Support) → `{ token, product, webUrl,
  expiresAt }`. The token is a normal tenant token for that business's **owner role** with extra claims `vas=<adminUserId>` and
  `ro=1`, 30-minute lifetime, no refresh token.
- A global API filter rejects every non-GET/HEAD request carrying `ro=1` with 403 `"Support view is read only."`, before any
  handler runs. Endpoints that change state on GET: none exist; a test asserts the filter on every mapped write route.
- Audit: `support.view_as.start` (admin, business, reason, expiry). Ending early (`POST /api/platform/view-as/end`) or expiry is
  recorded lazily as `support.view_as.end` when the panel closes it or the next view-as starts.
- Customer visibility: new `GET /api/account/activity` (any member of the business) lists support views of the business in the
  last 90 days: when, how long, the admin's display name, reason. Each product app's settings page shows it (one shared component
  pattern; each app adds a small "Account activity" section).
- The product web apps accept `?support_token=` on a `/support` route: they store the token in sessionStorage only (not
  localStorage), show a fixed banner "Viewing as support (read only). Ends at 14:32." and disable sign-out/business switching.

## 6. Suspension

`POST /api/platform/businesses/{id}/suspend { reason }` and `/reactivate { reason }` (Support). Suspended (`TenantStatus.Suspended`)
means:

- **Public:** every public read hides it — storefronts, invoice/quote pages, booking pages, course catalogue and academy pages
  (already done), and **property search, listing, agent and agency pages, stay quotes and bookings (new)**.
- **Members:** login, OTP, refresh and switch for that business return 403 `"This business is suspended. Contact support."`;
  existing access tokens stop working within their 15-minute life because a middleware checks tenant status (cached 1 minute).
- **Money:** pending payouts stay in the queue; Finance decides. Paystack webhooks still record payments (never drop money).
- Owner is emailed (`business.suspended` / `business.reactivated`) with the reason.

## 7. Pages and API

All under `/api/platform` unless noted. Lists are paged (`page`, `pageSize` ≤ 100) and sorted newest first.

| Page | Role | API |
|---|---|---|
| Overview | any | `GET /overview`: per product {businesses, newThisMonth, paying}, MRR (sum of active paid subscriptions' monthly price), platform fees this month (course fees, short-let commission, boosts), payouts requested (count, total), queue counts, failed notifications (24 h), last run of each background job |
| Businesses | Support, Finance | `GET /businesses?q=&product=&status=&plan=`; `GET /businesses/{id}` (product, slug, status, plan + subscription, usage via existing usage providers, members, public URL, last 20 audit rows, payments, payouts) |
| Business actions | Support / Finance | suspend, reactivate, view-as (Support); `POST /businesses/{id}/comp { planCode, until, reason }` (Finance) creates a subscription with provider `comp` |
| Users | Support | `GET /users?q=`; `GET /users/{id}` (memberships across products, learner/seeker activity counts, last login, lockout); `POST /users/{id}/unlock`, `/send-reset`, `/sign-out-everywhere` |
| Moderation | Moderator | existing routes, re-gated (section 3) |
| Payments | Finance | `GET /payments?product=&status=&from=&to=` across subscription payments, course orders, stays and boosts; `GET /payments.csv` |
| Payouts | Finance | existing `/api/admin/payouts*` |
| Plans | Finance | `GET /plans`; `PUT /plans/{id} { name, priceMinor, limits, features, isHidden }`; `POST /plans` (new plan for a product). Never deleted. A price change affects new subscriptions and renewals only; paid periods keep what they paid (the UI states this) |
| Audit log | SuperAdmin, Support | `GET /audit?tenantId=&actorId=&entityType=&action=&from=&to=` with before/after JSON |
| Admins | SuperAdmin | `GET /admins`; `POST /admins { email, role }` (user must exist; emails them); `PUT /admins/{id} { role }`; `DELETE /admins/{id}` |
| Jobs and email | any | `GET /health`: last run per hosted job, outbox counts by status, latest 50 failed notifications with error |

`Plan` gains `IsHidden` (hidden plans aren't offered on billing pages but keep working for existing subscribers). Background jobs
record their last successful run in a new `job_runs` table (`name, last_started_at, last_succeeded_at, last_error`).

## 8. Errors and safety

ProblemDetails throughout. 403 `"You need the Finance role for this."` style messages for role failures. Destructive or
customer-visible actions (suspend, comp, plan price change, role change, admin removal, mark payout paid) need a confirmation
dialog; suspend and view-as need a typed reason. Every admin write is audited with the admin as actor and the business as tenant.
Admin endpoints are rate limited per admin.

## 9. Frontend (`admin-web`)

A working tool: dense tables, sticky filters, keyboard shortcuts for queues (`a` approve, `r` reject, `j/k` next/previous),
desktop-first but usable at 360 px for urgent moderation. Its own design plan (`docs/design/adminlink.md`) written with the
frontend-design skill before any UI code; it must look clearly different from customer apps (so nobody confuses a view-as tab
with the admin panel). Session in sessionStorage; 8-hour expiry shows a sign-in screen.

## 10. Testing

- Unit: role → policy mapping; plan price change does not touch existing subscription periods.
- Integration: each role limited to its routes (table-driven over every `/api/platform` and `/api/admin` route); admin login
  needs the code; non-admin gets the generic error; view-as token refuses every write route (reflection over the endpoint data
  source), expires in 30 minutes, start is audited and shown in account activity; suspension hides public pages in every
  products and blocks member login/refresh/switch; last SuperAdmin can't be removed; comp creates an active subscription that
  ends on its date; bootstrap from config is idempotent; existing course and property admin tests keep passing.
- Browser: sign in with code, find a business, view-as in a product app (banner, writes refused), suspend and reactivate, edit a
  plan, approve a queue item, add and remove an admin.

## 11. Build order

1. Admin accounts, roles, bootstrap, policies, re-gating existing routes.
2. Admin sign-in with code; `job_runs` and health.
3. Businesses, users, suspension (incl. property public pages and member access).
4. View-as with read-only filter and account activity.
5. Plans (incl. `IsHidden`), comps, payments, overview metrics, audit viewer, admins management.
6. Contract `docs/PLATFORM_API.md`, design plan, `admin-web`, support banner in each product app, removal of LearnLink and
   PropertyLink admin pages, end-to-end check, README/CI/launch updates.
