# API contract — Platform admin panel (admin-web)

Same API and conventions as the products: camelCase JSON, string enums, money in minor units (kobo), ProblemDetails
(`detail` is a sentence to show; `errors` keyed by camelCase field), dates ISO 8601. Admin web app: `admin-web`,
`http://localhost:4300` (`App:ProductWebUrls.admin`).

## Roles

`PlatformRole`: `SuperAdmin`, `Moderator`, `Finance`, `Support`. SuperAdmin can do everything. Every route below lists the roles
that can use it; others get **403** whose `detail` names the role needed, e.g. "You need the Finance role for this." or "You need the
Super admin role for this." (`code: "forbidden"`). Business routes say the same, e.g. "You need the Owner or Admin role for this."
Roles are read from the database on every
request, so a role change applies at once. A removed admin's sessions end, so their next request gets 401.

Customers' contact details (emails, phone numbers, and searching by them) are for Support and SuperAdmin only. Moderator and Finance
see names and roles; where an address identifies a row (health page) they see it masked as `a…@example.com`.

## Sign-in (no token needed; rate limited)

| Method | Path | Body | Returns |
|---|---|---|---|
| POST | `/api/platform/auth/login` | `{ email, password }` | `{ challengeId }` and emails a 6-digit code (template "admin.code"). Any failure (wrong password, not an admin, locked) is **400** with the same `detail`. Too many codes: 400 "Too many codes requested…" |
| POST | `/api/platform/auth/verify` | `{ challengeId, code }` | `AuthResponse { accessToken, refreshToken: "", expiresAt, user: { id, fullName, email, isPlatformAdmin, platformRole }, tenant: null }`. Wrong/expired code: 400; 5 wrong tries burn the challenge. |
| POST | `/api/platform/auth/logout` (admin token) | | 204. Ends this session on the server; the token gets 401 from then on. |

**Lockout.** The console keeps its own count of failed sign-ins, separate from the customer apps' lockout: 5 wrong passwords lock
console sign-in for 10 minutes (still the same 400). Failed customer sign-ins with an admin's email don't lock them out of the
console, and failed console sign-ins don't lock their customer account. A successful sign-in resets the count.

The token lasts **8 hours** and cannot be refreshed: when it expires (or any call returns 401), show the sign-in screen.
Customer-app tokens are refused on `/api/platform/*` (403), even for admins. In development the code is printed in the API console log.
"Forgot password?" on the sign-in screen uses the shared password routes with `product: "admin"` (see [Passwords](#passwords-every-product-and-the-console)).

### Sessions

Admin-panel tokens and support-view tokens are backed by a server-side session (table `auth_sessions`, keyed by the token's `jti`).
Every request with one of these tokens checks that its session is still open. Once it has ended, the request gets **401** with
`detail` "Your session has ended. Sign in again." and `code: "session_ended"`, even though the token hasn't expired.

A session ends when:
- the admin signs out (`POST /api/platform/auth/logout`);
- support ends a view (`POST /api/support-view/end` or `POST /api/platform/view-as/{sessionId}/end`);
- support uses "Sign out everywhere" on the user, or the user resets or changes their password (all of that user's admin-panel sessions);
- a super admin removes the admin (all of their admin-panel sessions);
- it reaches its expiry (8 hours for the console, 30 minutes for a support view).

Customer-app access tokens are not session-backed: they keep working for up to 15 minutes after sign-out everywhere or a password
reset. Their refresh tokens are revoked at once.

### Current admin
`GET /api/platform/me` (any role) → `{ id, fullName, email, platformRole, sessionExpiresAt }`. The role is read from the database, so
call this on load and every few minutes and rebuild the menus from it. 403 once the admin has been removed.

## Passwords (every product and the console)

No token needed, except for `change`. Rate limited like sign-in.

| Method | Path | Body | Returns |
|---|---|---|---|
| POST | `/api/auth/password/forgot` | `{ email, product? }` | 204 whether or not the account exists. Emails a reset link (template "password.reset") when it does. 400 on `email` "Enter your email address." when it's missing. |
| POST | `/api/auth/password/reset` | `{ email, token, newPassword }` | 204. 400 on `email` when missing; 400 on `token` "This reset link is invalid or has expired. Ask for a new one." (also when missing); 400 on `newPassword` "Enter a new password." when missing, or when it breaks the password rules (8 characters minimum). |
| POST | `/api/auth/password/change` (any signed-in token) | `{ currentPassword, newPassword, refreshToken? }` | 204. 400 on `currentPassword` "Enter your current password." when missing, or "Your current password is wrong."; 400 on `newPassword` as above. |

- **Link.** `{web}/reset-password#email=…&token=…`. The token is in the URL fragment, so it never reaches a server or its logs. The
  reset page reads both values from `location.hash` and posts them to `/reset`.
- **Which app.** `product` is a product key or `"admin"` (the console). Without it, the link goes to the app of the user's first
  business; a user with no business (a learner or seeker) gets the default web app.
- **Limits.** The token is valid for 10 minutes and works once. At most 3 reset emails per address per hour; extra requests still return
  204 and send nothing.
- **Reset.** Sets the password, clears any lockout, revokes every refresh token, ends every admin-panel session, writes audit
  `user.password_reset` and emails "your password was changed" (template "password.changed").
- **Change.** Same effects (audit `user.password_change`), except that the refresh token sent in the body is kept, so the app that
  made the change stays signed in. Admin-panel sessions all end, including the current one if the change was made from the console.
- **A reset confirms the email** (final fix pass B). The link proves the mailbox, so a never-confirmed account becomes confirmed
  (`EmailConfirmedAt` = now). Every earlier refresh token is revoked as above, the security stamp is rotated, and the patient portal
  refuses access tokens issued before the confirmation. A later code sign-in keeps the reset password.

## Sign-up confirmation (every product; final fix pass B)

Every password sign-up, business or customer, is confirmed with the 6-digit emailed code before the account can be used. The code is the
same one the code sign-in uses (template "otp", 10 minutes, 5 tries, at most 3 codes per address per 10 minutes).

| Method | Path | Body | Returns |
|---|---|---|---|
| POST | `/api/auth/signup` | as before | **`{ confirmationRequired: true, email }`** and emails a code; no tokens. The business is created now. A **confirmed** account adding a product with its password still gets `AuthResponse` straight away. |
| POST | `/api/auth/signup/confirm` | `{ email, code, password, product? }` | `AuthResponse` for the business of `product`. Rate limited as `otp`. |
| POST | `/api/auth/{learner\|parent\|patient\|seeker}/signup` | `{ fullName, email, password }` | **`{ confirmationRequired: true, email }`** and emails a code; no tokens. |
| POST | `/api/auth/{learner\|parent\|patient\|seeker}/signup/confirm` | `{ email, code, password }` | `AuthResponse` (tenant null). Rate limited as `otp`. |
| POST | `/api/auth/otp/request` | `{ email }` | 204. "Send a new code" on the code step uses this. |

- **Before the code.** A new sign-up's account has no password. Password login (`/api/auth/login` and every customer `/login`) answers
  **403** `code: "email_unconfirmed"`, "Confirm your email first. Enter the 6-digit code we emailed you, or ask for a new one.". The
  login page then asks for a code (`/otp/request`) and posts it to the matching `/signup/confirm` with the password the user typed.
- **Confirm.** `password` is checked against the password rules before the code is used (400 on `password`; the code still works). A
  wrong or expired code is 400 "That code is wrong or has expired. Request a new one.". On a never-confirmed account the code proves the
  mailbox: the account's password becomes this `password`, the email is confirmed (`EmailConfirmedAt`), the security stamp is rotated and
  every refresh token is revoked. On a confirmed account the password is never changed; the code just signs in.
- **A second sign-up on an unconfirmed address** (someone else's, or the same person's retry) gets the same answer and a new code goes to
  the mailbox. Nobody can use the account without that code, and whoever enters it sets the password. A business sign-up adds its business
  to the account unless the account already has one for that product. On an account from before codes at sign-up (it has a password) it
  adds one only when the password matches. Customer sign-up on a **confirmed** address: 400 on `email` "You already have an account with
  this email. Log in instead.".
- **Confirmed passwords are kept.** A code sign-in (`/api/auth/otp/verify`, `/api/auth/{parent|patient}/otp/verify`) never removes the
  password of a confirmed account.
- **Accounts from before this change** that were never confirmed (they have a password) keep password login. Their first code sign-in
  still takes the account over (Task 15 R1/R3): the password is removed, the stamp rotated, and every refresh token revoked. A reset, a
  `/signup/confirm` or a team invite confirms them instead, keeping the password set there.
- **Team invites.** `POST /api/auth/invites/{token}/accept` confirms the email (the link was emailed). A confirmed account proves its
  password as before. An address with an unconfirmed account takes the invitee's `password` and `fullName`, and its earlier sessions
  end. `GET /api/public/invites/{token}` reports `hasAccount: false` for an unfinished sign-up, so the page asks for a name and password.

## Everything else (`Authorization: Bearer <admin token>`)

### Overview (any role)
`GET /api/platform/overview` →
```ts
{ products: [{ product, businesses, newThisMonth, paying, suspended }],   // product keys: whatsapp-orders, invoices, bookings, courses, property, school, care
  mrrMinor,                                                                 // NGN monthly recurring revenue (the reporting currency)
  mrrByCurrency: [{ currency, monthlyMinor }],                              // every currency, sorted by code
  fees: { courseFeesMinor, shortLetCommissionMinor, boostsMinor, totalMinor },   // this calendar month
  queues: { listings, courses, verifications, reports, reviews, payouts, payoutsMinor },
  failedEmails24h, jobs: JobRunDto[] }
```
MRR counts active, paid (not comped) subscriptions at the price frozen on each subscription when it was paid, not today's list price
(yearly plans count as price / 12). Currencies are never added together. Comps are left out of MRR and of `paying`.

### Health (any role)
`GET /api/platform/health` → `{ jobs: [{ name, lastStartedAt, lastSucceededAt, lastError }], outbox: { pending, sent, failed },
failures: [{ id, templateKey, recipient, error, attempts, createdAt }] }` (latest 50 failed emails). Job names: email, invoices, bookings,
property, photos, videos, school, care (CareLink: reminders, bed-day charges, missed doses, no-shows, payment rechecks, every minute). A job is healthy when `lastSucceededAt` is recent (email/photos/videos: minutes; others: within 20 minutes).
`recipient` is the full address for Support and SuperAdmin only; other roles get it masked (`ada@example.com` → `a…@example.com`).

### Businesses (Support, Finance; actions as noted)
| Method | Path | Body | Returns |
|---|---|---|---|
| GET | `/api/platform/businesses?q=&product=&status=Active\|Suspended\|Closed&plan=&page=&pageSize=` | | `Paged<{ id, name, slug, product, status, planName, ownerEmail, createdAt }>`; `q` matches name, slug and owner name, plus owner email and phone for Support and SuperAdmin; `plan` is a plan code ("free" includes no subscription). `ownerEmail` is null for other roles. |
| GET | `/api/platform/businesses/{id}` | | `BusinessDetailDto` |
| POST | `/api/platform/businesses/{id}/suspend` (Support) | `{ reason }` (10-300 chars) | 204; 409 if already suspended (or closed). See [Suspension](#suspension). |
| POST | `/api/platform/businesses/{id}/reactivate` (Support) | `{ reason }` | 204; 409 if already active (or closed) |
| POST | `/api/platform/businesses/{id}/view-as` (Support) | `{ reason }` | `{ token, expiresAt, product, webUrl, sessionId }` — open `webUrl` in a new tab (it is the product app's `/support#token=…`). Read only, 30 minutes at most. |
| POST | `/api/platform/view-as/{sessionId}/end` (Support) | | 204. Ends that support view early; its token gets 401 from the next request. 204 also when it had already ended. |
| POST | `/api/platform/businesses/{id}/comp` (Finance) | `{ planCode, until, reason }` | 204; gives the plan free until `until` (must be future) |

```ts
BusinessDetailDto { id, name, slug, product, status, country, currency, createdAt, planName,
  subscription: { id, planCode, planName, priceMinor, status, currentPeriodStart, currentPeriodEnd, provider /* paystack | comp */ } | null,
  usage: [{ key, label, used, limit /* -1 unlimited */ }],
  members: [{ userId, name, email /* null unless Support or SuperAdmin */, role, status, lastLoginAt }],
  webUrl, recentAudit: AuditRowDto[] /* 20 */,
  payments: [{ id, amountMinor, currency, status, reference, createdAt }], payouts: [{ id, amountMinor, status, reference, createdAt, paidAt }] }
```
`subscription.priceMinor` is the plan's current list price. `usage` limits come from the subscription's snapshot (see [Plans](#plans)).

### Suspension

- **Status check.** Every request with a business token reads the business's status from the database (no cache), so every
  server refuses a suspended or closed business at once: **403** "This business is suspended. Contact support." (`code: "suspended"`).
  Support-view tokens are exempt, so support can look at a suspended business.
- **Sign-in.** Suspending changes the status and revokes the business's refresh tokens in one database transaction. Sign-in to a
  suspended business is refused with the same 403 message.
- **Team.** Invitations can't be created (the token is refused as above) or accepted (403, same message). The invite preview still loads.
- **Public pages** of every product are hidden (404).
- **Bookings.** A staff member's calendar feed (`/api/public/staff-calendar/{feedToken}.ics`) returns 404. A customer can still open
  their manage-booking page, but cancel, reschedule and deposit payment return **409** "{Business} is currently unavailable. Contact them directly."
- **Other products.** Storefront orders and invoice/quote payments are refused because their public pages are hidden. Learners keep
  access to courses they bought.
- **SchoolLink.** Staff are blocked like every other business (403 "This business is suspended. Contact support."). The school's
  public sign-in page is hidden (404). Parent and student portal routes that reach that school return **403** "This school's portal is unavailable."
  Reminders, broadcasts and stranded-payment checks skip suspended schools.
- **CareLink.** Staff are blocked like every other business. Patient portal routes that reach that clinic return **403** "This clinic's
  portal is unavailable." (after the link check, so an unlinked patient is still 404); the clinic drops out of the portal's patient
  list, and a portal invite for it can't be previewed or accepted (403). The care job (reminders, no-shows, missed doses, bed charges,
  low deposits, stranded payments) skips suspended clinics. A Paystack webhook for a suspended clinic is still applied (the money was taken).
- The owner is emailed the reason when the business is suspended or reactivated.

### Users (Support)
| Method | Path | Returns |
|---|---|---|
| GET | `/api/platform/users?q=` (≥ 2 chars; email, name or phone) | `[{ id, name, email, createdAt, lastLoginAt, businesses }]` (max 50) |
| GET | `/api/platform/users/{id}` | `{ id, name, email, phone, emailConfirmed, createdAt, lastLoginAt, lockedOut, lockoutEnd, adminRole, memberships: [{ tenantId, name, product, role, status, businessStatus }], activity: { courses, stays, enquiries, viewings, savedSearches } }` |
| POST | `/api/platform/users/{id}/unlock` | 204 |
| POST | `/api/platform/users/{id}/send-login-code` | 204 — emails a one-time sign-in code |
| POST | `/api/platform/users/{id}/send-password-reset` | 204 — emails a password reset link to the app of the user's first business (default web app if none); same rules as `/api/auth/password/forgot`, including the 3-per-hour limit |
| POST | `/api/platform/users/{id}/sign-out-everywhere` | 204 — revokes every refresh token and ends every admin-panel session of the user |

Users routes are Support only, so they show emails and phones and search by them.

### Money (Finance)
| Method | Path | Returns |
|---|---|---|
| GET | `/api/platform/payments?product=&status=&from=&to=&page=&pageSize=` | `Paged<{ id, kind: Subscription\|Course\|Stay\|Boost, product, businessId, businessName, amountMinor, currency, status, reference, createdAt, paidAt }>` |
| GET | `/api/platform/payments.csv?product=&status=&from=&to=` | CSV file (fetch with the token, save as a blob) |

Payments come from one SQL `UNION ALL` of four sources: course orders (amount > 0), paid stays, paid boosts and subscription payments
(every status). Filters, newest-first ordering, the total count and paging all run in the database, so the list and the CSV are
complete; there is no per-source cap. `pageSize` is 1-100 (default 50); `status` matches case-insensitively; `from`/`to` filter on
`createdAt`. The CSV has columns `date,kind,product,business,amount,currency,status,reference` and covers the whole filtered range.
Every text cell is quoted, and a cell starting with `=`, `+`, `-`, `@`, tab or carriage return gets a leading `'`, so a spreadsheet
opens it as text rather than running it as a formula.

| Method | Path | Returns |
|---|---|---|
| GET | `/api/admin/payouts?status=Requested\|Paid\|Rejected` | shared payout queue (courses + property): `[PayoutDto & { product, businessName, currency, bankName, accountNumber, accountName, amountMinor, status, reference, requestedAt, paidAt, note }]` |
| POST | `/api/admin/payouts/{id}/paid` `{ reference }` · `/reject` `{ note }` | `PayoutDto` |
| GET | `/api/platform/plans` | `[{ id, product, code, name, priceMinor, currency, interval, limits, features, isDefault, isHidden, sortOrder, activeSubscribers }]` |
| POST | `/api/platform/plans` | `{ product, code, name, priceMinor, isHidden, sortOrder, limits, features }` → plan |
| PUT | `/api/platform/plans/{id}` | same body (product and code can't change) → plan. Edits apply to new subscriptions and renewals only (see below); the default free plan can't be hidden. |

### Plans

- **Snapshots.** When a subscription is paid (first payment or renewal) or comped, the plan's price, currency, limits and features
  are copied onto it. The business gets that snapshot until the period ends, so a plan edit (price, limits or features) reaches a
  paying business only at its next renewal, which takes the plan as it is then. Active subscriptions that existed before snapshots
  were backfilled from their plan's list values at the time of the migration (not the amount actually paid).
- **Comps** are snapshotted at the list price, used only to rank them against paid periods. When several subscriptions run at once,
  the business gets the one with the highest snapshot price.
- **Hidden plans** aren't offered at checkout. A business that has (or had) a paid subscription to a hidden plan can still check out
  that plan to renew it; anyone else gets 404. A comp doesn't count.

Limit keys per product are listed in `docs/API.md`, `INVOICES_API.md`, `BOOKINGS_API.md`, `COURSES_API.md`, `PROPERTY_API.md`,
`SCHOOL_API.md`; `-1` = unlimited, 0/1 = feature off/on. School usage on a business is students and staff seats. School fee
payments go to the school's own Paystack account and are not platform payments; school **subscription** payments appear in
`GET /api/platform/payments` like the other products.

CareLink limit keys are in `CARE_API.md`: `team_seats`, `beds` (active beds), `online_payments` (0/1, patient bill payments online)
and `remove_branding`. Plans: Free (₦0: 3 staff, 0 beds, no online payments), Clinic (₦25,000 a month: 15 staff, 20 beds, online
payments) and Hospital (₦75,000 a month: 60 staff, 150 beds, online payments, no branding). Care usage on a business is staff seats,
active beds and patients (every registered patient, unlimited). Patient bill payments go to the clinic's own Paystack account and are
not platform payments. The console's business detail never shows clinical facts.

### Moderation (Moderator) — existing routes
- Courses (`docs/COURSES_API.md`, admin section): `GET /api/admin/course-reviews`, `POST /api/admin/course-reviews/{id}` `{ approve, note }`,
  `GET/POST/PUT /api/admin/categories[/{id}]`.
- Property (`docs/PROPERTY_API.md`, platform admin section): `/api/admin/property/moderation`, `/listings/{id}/decision`, `/verifications`
  (+ `/documents/{index}` as a blob), `/verifications/{id}/decision`, `/reports`, `/reports/{id}/decision`, `/reviews`, `/reviews/{id}/decision`,
  `/locations`. Each moderation item includes `photos` (a URL per photo, full size where there is one) and `publicUrl` (the listing's public page).
- `GET /api/admin/overview` (courses figures) is available to any admin.

### Audit log (SuperAdmin, Support)
`GET /api/platform/audit?tenantId=&actorId=&entityType=&action=&product=&from=&to=&page=&pageSize=` → `Paged<AuditRowDto>`;
`action` matches by prefix (e.g. `business.`, `support.`, `admin.`, `plan.`, `user.`). `product` keeps rows of that product's
businesses (rows with no business are left out).
```ts
AuditRowDto { id, tenantId, businessName, product /* null when the row has no business */, actorId, actorName, action, entityType,
  entityId, beforeJson, afterJson /* JSON strings or null */, ip, createdAt }
```
Admin actions: `admin.login`, `admin.add`, `admin.role`, `admin.remove`, `business.suspend`, `business.reactivate`, `business.comp`,
`support.view_as.start`, `plan.create`, `plan.update`, `user.unlock`, `user.send_login_code`, `user.send_password_reset`,
`user.sign_out_everywhere`, `admin.grant_cli` (the [recovery command](#recovery); no actor). Users' own actions: `user.password_reset`, `user.password_change`. Customer data changes are also logged
(`added`/`modified`/`deleted` on audited entities). Ending a session (sign-out, end view) is not audited; a support view's end time
is on the session and shows in Account activity.

### Admins (SuperAdmin)
| Method | Path | Body | Returns |
|---|---|---|---|
| GET | `/api/platform/admins` | | `[{ id, userId, name, email, role, addedAt, addedByName }]` |
| POST | `/api/platform/admins` | `{ email, role }` | admin; 400 on `email` "Enter the email of the account to add." when it's missing; 404 if no account with that email (they sign up first, as a seeker/learner or any product); 409 if already an admin. Emails them. |
| PUT | `/api/platform/admins/{id}` | `{ role }` | admin; 409 when demoting the last SuperAdmin |
| DELETE | `/api/platform/admins/{id}` | | 204; 409 for the last SuperAdmin. Ends all of the admin's console sessions. |

Role changes and removals run in a transaction that first takes a Postgres advisory lock (`pg_advisory_xact_lock`), then loads,
checks and saves. Two super admins demoting or removing each other at the same moment therefore run one after the other, and the
second gets 409 "There must always be at least one super admin…".

### Recovery

If nobody can get into the console (every super admin lost, or a config admin removed), someone with access to the server runs:

```
dotnet Saas.Api.dll admin grant <email> <SuperAdmin|Moderator|Finance|Support>
```

It makes that account an active admin with the role (restoring a removed one) and exits instead of starting the API. The account must
already exist (sign up first); the command never creates logins. Exit codes: 0 granted, 1 no account with that email, 2 bad command
(prints the usage). Audited as `admin.grant_cli`. The admin then signs in to the console as usual, with their password and an emailed code.

## In-app notifications (`/api/me/notifications`)

Shared with every product; SchoolLink is the first to write them. Any signed-in token (staff, parent, student, seeker, learner)
reads its own rows. A business token (including support view) is scoped to that business; a customer token (`tenant` null) sees
every row for that user. Support view can list them but must not mark them read (writes are refused as usual).

| Method | Path | Returns |
|---|---|---|
| GET | `/api/me/notifications?unread=&page=` | `Paged<{ id, product, kind, title, body, link, tenantId, createdAt, readAt }>` newest first, 20 a page |
| GET | `/api/me/notifications/unread-count` | `{ count }` (capped at 99; the UI shows 9+) |
| POST | `/api/me/notifications/{id}/read` | 204. 404 for someone else's row |
| POST | `/api/me/notifications/read-all` | 204 |

`link` is an app path (`/app/messages/…`, `/parent/fees`, `/student/results`). Read notifications older than 90 days and unread
older than 180 days are deleted by the school job. Full SchoolLink routes: `docs/SCHOOL_API.md`.

**CareLink.** Clinic staff use this feed as it is (`care.patient_waiting`, `care.critical_vital`, `care.missed_dose`,
`care.low_deposit`, `care.emergency_access`, payment alerts, import results, portal links). Patient portal accounts do **not**: a
customer token here sees every product's and every clinic's rows, so care-web's portal reads the per-patient feed
`GET /api/care-portal/patients/{id}/notifications` (this account, this clinic, this patient) and marks items read one by one with
`POST /api/me/notifications/{id}/read` (never `read-all`, which would also mark other clinics' and products' rows). Patient notice links
are `/portal/...?patient={id}`. No notice carries a diagnosis, a result or a drug name. Full routes: `docs/CARE_API.md`.

## Support view in the product apps

Every product web app (OrderLink 3700, InvoiceLink 3800, BookLink 3900, LearnLink 4000, PropertyLink 4100, SchoolLink 4200, CareLink 4400) has a `/support` page:
1. Read `token` from `location.hash` (`#token=…`), then remove the hash with `history.replaceState`.
2. Store the session in **sessionStorage** only (this tab), as a session whose `tenant` comes from `GET /api/me` with that token; no
   refresh token. Redirect to the app's dashboard.
3. While the token has claim `ro=1`, show a fixed banner on every signed-in page: "Viewing as support (read only). Ends at 14:32." with
   an "End view" button that calls `POST /api/support-view/end` with the token (204), then clears the session and closes the tab.
   Hide or disable sign-out, business switching and billing actions.
4. Bank account numbers are masked as `••••1234` in a support view (property payout account, course academy payout account, and
   the payouts list). The console's Finance payout queue shows them in full.
5. Any write returns 403 "Support view is read only." — show it as a normal error toast; nothing else to handle.
   `POST /api/support-view/end` is the only write a support-view token may make (403 with any other token).
6. When the view ends (401: expired, or ended from the banner or the console), show "Support view ended" instead of the login form.
7. CareLink: a support view never has clinical access. Clinical reads return 403 "Only doctors and nurses can see clinical notes.", the
   patient access log and patients' portal invites and links return 403, and the patient portal (`/portal`) refuses the support token.

Customers see support views of their business: `GET /api/account/activity` (any member) →
`[{ sessionId, adminName, reason, startedAt, endsAt, endedAt }]` (last 90 days). `endsAt` is when the view would have expired;
`endedAt` is when it was ended early, or null. Each product app's settings page shows it as "Account activity".
