# Six SaaS products on one shared core

Build plan: *Six SaaS Product Build Plans* (12 Oct 2026). Products share one multi-tenant core
(auth, tenants, billing, notifications, files, audit log) and are built in the plan's order.

| # | Step | Status |
|---|---|---|
| 1 | Shared core: tenants, Identity + JWT, refresh-token rotation, email OTP, plans/subscriptions, Paystack, outbox notifications, file storage, audit log | **Done** |
| 2 | WhatsApp Order System (MVP weeks 1-3) | **Done**: stores, catalogue with variants, delivery zones, discount codes, storefront, order then wa.me redirect, seller dashboard, QR/share, billing |
| 3 | Invoice Generator (MVP weeks 1-4) | **Done**: no-signup generator, clients/items/tax rates, invoice editor, 5 PDF templates, email/WhatsApp sharing, public pay page (business's own Paystack), payments and receipts, quotes to invoices, recurring invoices, reminders, CSV export. **Phase 2 done**: expenses with receipts, profit & loss report, multi-currency with daily exchange rates, team and accountant access (Business plan) |
| 4 | Booking System (MVP weeks 1-6) | **Done**: services with buffers and deposits, staff schedules with breaks and time off, availability engine, public booking page with deposit holds, manage/reschedule/cancel links, email reminders, WhatsApp reminder links, day/week calendar, walk-ins, no-shows, customers, reports, iCal feeds |
| 5 | Online Course Platform (MVP + marketplace) | **Done**: tutor academies with their own course pages, curriculum builder (video, text, quiz lessons, resources), video processing (HLS with FFmpeg, MP4 fallback), signed video links, learner accounts, Paystack checkout, coupons, progress and notes, Q&A, reviews, quizzes, certificates with public verification, marketplace with admin review, earnings ledger, 7-day refunds, payouts |
| 6 | Property Listing Website (MVP + phase 2) | **Done**: agencies, agents and landlords; listings for rent, sale and short-let with a true move-in cost breakdown; photo resizing, watermarking and duplicate-photo detection across agencies; search with filters, map and area pages; enquiries, WhatsApp/call tracking, viewing bookings; saved searches with instant and daily alerts; agent verification, moderation, reports, reviews; listing expiry with one-click renew; short-let bookings paid to the platform with commission, refunds and host payouts; paid boosts |
| 7 | Platform admin console | **Done**: admin roles (Super admin, Moderator, Finance, Support) with emailed-code sign-in; businesses and users across every product; suspension; read-only support view-as shown to the customer; plans, comps, payments, payouts; overview, audit log, job health |
| 8 | School Management System | **Done**: academic setup, students, offline attendance, scores and published results, report cards, fees to the school's own Paystack, announcements and messages, in-app notifications, parent and student portals, Excel and CSV import (templates, preview, all-or-nothing confirm) for students, staff, classes, subjects, scores, ratings, remarks, attendance, timetables, exam sittings, questions, Play lists, fees, payments and expenses |
| 9 | CareLink (clinics and small hospitals) | **Built, final review pending**: patient registration with duplicate checks and imports, appointments with doctor hours and slots, check-in queue, triage with vital-sign flags, consultations with ICD-10 diagnoses, prescriptions and tests with allergy checks and private result files, a logged record guard with emergency access, wards, beds, admissions with deposits, ward rounds, nursing notes, fluid and medication charts, discharge summaries, bills with bed-day charges, desk and online payments to the clinic's own Paystack, receipts and reports; a patient portal for patients and parents across clinics (signed record, who opened it, bills and Pay now, appointments, contact details, notices) |

## Layout

```
api/                       ASP.NET Core 10 modular monolith
  src/Saas.Domain          entities (Common, Core = shared, Orders = WhatsApp Order System, Invoices)
  src/Saas.Application     use cases, DTOs, FluentValidation validators, pricing rules
  src/Saas.Infrastructure  EF Core + PostgreSQL, Identity/JWT, Paystack, Brevo email, local/R2 storage, QuestPDF
  src/Saas.Api             minimal API endpoints, auth, rate limits, ProblemDetails, OpenAPI (/docs)
  tests/                   xUnit v3 unit tests + integration tests (Testcontainers PostgreSQL)
web/                       OrderLink: Next.js + TypeScript + Tailwind (marketing, seller dashboard, storefront)
invoice-web/               InvoiceLink: Next.js app for the Invoice Generator
booking-web/               BookLink: Next.js app for the Booking System
course-web/                LearnLink: Next.js app for the Online Course Platform
property-web/              PropertyLink: Next.js app for the Property Listing Website
school-web/                SchoolLink: Next.js app for the School Management System
care-web/                  CareLink: Next.js app for clinics (staff app and patient portal)
admin-web/                 Admin console for the people who run the platform (all products)
docs/API.md                API contract: shared core + WhatsApp Order System
docs/INVOICES_API.md       API contract: Invoice Generator
docs/INVOICES_PHASE2_API.md  API contract: expenses, reports, exchange rates, teams
docs/BOOKINGS_API.md       API contract: Booking System
docs/COURSES_API.md        API contract: Online Course Platform
docs/PROPERTY_API.md       API contract: Property Listing Website
docs/PLATFORM_API.md       API contract: platform admin console
docs/SCHOOL_API.md         API contract: SchoolLink (staff, parent and student portals, notifications, Excel and CSV imports)
docs/CARE_API.md           API contract: CareLink (staff roles, clinical record guard, patient portal, notifications)
docs/superpowers/          design spec and implementation plan for the property product
docs/design/               design plans for each web app
docker-compose.yml         local PostgreSQL on port 55432
```

Each product has its own folder in each layer and its own web app; all share one API, database and login.
One email can own a business in several products. Tokens carry a `prd` claim and only work on that
product's routes. A product plugs in with an `IProductProvisioner` (creates its starting data at signup)
and an `IUsageProvider` (plan usage on the billing page).

## Run locally

Needs .NET 10 SDK, Node 24 and Docker.

```bash
docker compose up -d
```

```bash
cd api && dotnet run --project src/Saas.Api
```

The API listens on http://localhost:5080, applies migrations and seeds plans on start in Development.
API reference: http://localhost:5080/docs. In development, emails (including login codes) are printed
to the API console instead of being sent.

```bash
cd web && npm install && npm run dev
```

OrderLink runs on http://localhost:3700. For InvoiceLink:

```bash
cd invoice-web && npm install && npm run dev
```

InvoiceLink runs on http://localhost:3800 and BookLink (`cd booking-web && npm run dev`) on http://localhost:3900, LearnLink (`cd course-web && npm run dev`) on http://localhost:4000, PropertyLink (`cd property-web && npm run dev`) on http://localhost:4100, SchoolLink (`cd school-web && npm run dev`) on http://localhost:4200, CareLink (`cd care-web && npm run dev`) on http://localhost:4400 (patient portal at `/portal`), the admin console (`cd admin-web && npm run dev`) on http://localhost:4300, and the LinkSuite hub that links to every app (`cd hub-web && npm run dev`) on http://localhost:3600. Copy each app's `.env.example` to `.env.local` if the API is elsewhere.

## Tests

```bash
cd api && dotnet test
```

Integration tests start a throwaway PostgreSQL container, so Docker must be running.

## How tenancy works

- Every tenant-owned entity implements `ITenantEntity`. `AppDbContext` adds a global query filter
  `TenantId == current tenant` to each of them, so queries can never return another tenant's rows.
  With no tenant set, the filter matches nothing.
- Signed-in requests get the tenant from the `tid` JWT claim (`TenantMiddleware`). Public storefront
  requests get it from the store slug (`StorefrontService.ResolveAsync`).
- `SaveChanges` stamps `TenantId` on new rows and throws on any write to a row of a different tenant.
- Entities marked `[Audited]` write before/after JSON to `audit_logs`.

## Configuration

Set these with user secrets or environment variables (`Section__Key`), never in committed files:

| Setting | Purpose |
|---|---|
| `ConnectionStrings:Default` | PostgreSQL connection string |
| `Jwt:SigningKey` | 32+ random characters; the API refuses to start without it |
| `App:WebBaseUrl`, `App:ProductWebUrls:invoices`, `App:ApiBaseUrl`, `Cors:Origins` | public URLs used in links, uploads and CORS |
| `Paystack:SecretKey` | enables plan upgrades; webhook URL is `POST /api/webhooks/paystack` |
| `Jobs:InvoicesEnabled` | recurring invoices and reminders every 15 minutes (on by default) |
| `RateLimits:AuthPerMinute`, `RateLimits:PublicWritesPerMinute` | per-IP limits for sign-in (20) and public orders/bookings/payments (10) |
| `Jobs:BookingsEnabled` | releases unpaid deposit holds and sends booking reminders every 2 minutes |
| `Platform:AdminEmails` | emails made **super admins** the first time they sign in; after that admins are managed in the admin console. If nobody can get in, someone with server access runs `dotnet Saas.Api.dll admin grant <email> <role>` (the account must exist; audited as `admin.grant_cli`; see `docs/PLATFORM_API.md`, Recovery) |
| `Jobs:PropertyEnabled`, `Photos:ProcessorEnabled` | property expiry/alerts/reminders job (every 2 minutes) and the photo processor (both on by default) |
| `Video:FfmpegPath`, `Video:ProcessorEnabled` | FFmpeg for HLS video (without it, uploads play as MP4); the processor runs in the API by default |
| `Media:SigningKey` | key for signed video links (defaults to one derived from the JWT key) |
| `Email:BrevoApiKey`, `Email:FromEmail`, `Email:FromName` | real email sending (otherwise logged) |
| `Storage:Provider=R2` + `Storage:R2*` | Cloudflare R2 for uploads (otherwise local `uploads/`) |
| `Database:MigrateOnStartup` | apply migrations at boot (on in Development) |

`appsettings.Development.json` contains a development-only JWT key; production must set its own.

## Deliberate choices and what is left

- Background work uses an outbox table plus hosted services (`FOR UPDATE SKIP LOCKED`, and a Postgres advisory
  lock for invoice jobs), not Hangfire yet. Video processing runs the same way (one upload at a time per instance, claimed atomically). Add Hangfire when report cards arrive.
- Invoice payments go to each business's **own** Paystack account (their key is encrypted with ASP.NET Data
  Protection, keys stored in Postgres). Taking a platform fee needs Paystack subaccounts/split payments later.
  Businesses can also point their Paystack webhook at `POST /api/webhooks/paystack/invoices/{tenantId}`.
- Roles: Owner, Admin, Staff, Accountant. Every member can read; writes are limited per group with `WritableBy(...)`,
  reports/exports need the `reports` policy, team management needs Owner/Admin. Role changes reach a user's token on
  its next refresh (15 minutes at most); removing a member revokes their refresh tokens at once.
- Exchange rates come from open.er-api.com (free, no key, daily; the UI must show "Rates by Exchange Rate API").
  Each invoice, payment and expense stores the rate it used, so reports never change when rates move. The business
  currency is the reporting currency and is locked once invoices, quotes or expenses exist.
- Invoices created before phase 2 were given an exchange rate of 1 by the migration (correct for same-currency invoices).
- Not built: separate "multiple businesses" signup per user (a user can belong to several via invites and switch between them).
- WhatsApp phase 2 (Cloud API inbox, online order payments, inventory, staff seats, custom domains) is not built.
- Before launch: privacy policy and terms, NDPA/GDPR review, Sentry/PostHog/UptimeRobot, daily backups with a restore test.

## How bookings stay safe

- `AvailabilityEngine` (pure, unit-tested) offers a slot when the service fits inside a staff member's schedule
  clipped to opening hours, and the service plus its buffers overlaps nothing they are busy with. It works in the
  business's time zone, skips the missing hour when clocks go forward and offers the repeated hour once.
- PostgreSQL is the final guard: an exclusion constraint (`btree_gist`) refuses two bookings for one staff member whose
  blocked time overlaps. Two customers racing for one slot get exactly one booking; the other sees "just taken".
- Deposits hold a slot for 10 minutes (`Pending`). Stale holds are expired before each new booking is saved and by a job
  every 2 minutes. A deposit that arrives after its slot was taken is still recorded and the booking is flagged for refund.
- Automatic WhatsApp reminders need the paid WhatsApp Cloud API (phase 2); for now the business gets a one-tap
  "Remind on WhatsApp" link and customers with an email get reminders 24 and 2 hours before.

## How course money works

- **Academy sales** (from the tutor's own course page) pay the plan's academy fee: 10% on Free, 0% on Pro and Business.
  **Marketplace sales** (from the LearnLink catalogue) pay 30%. A course joins the marketplace only after a platform admin approves it.
- Learners pay the platform's Paystack account. Each sale records a tutor earning that becomes available 7 days later,
  after the refund window. Tutors can refund inside those 7 days; the earning is reversed and access removed.
- Payouts (minimum ₦5,000) are requested by the tutor and sent by hand by a platform admin, who records the transfer
  reference. Paystack Transfers can automate this later.
- Video links are signed per viewer and expire after a few hours. With R2 the API redirects to a short-lived presigned URL.
  The API Docker image includes FFmpeg; locally, install it and set `Video:FfmpegPath` to get adaptive HLS.
- Quizzes are graded on the server and timed attempts get a 60-second grace period. Certificates are issued when every
  lesson is done (paid plans) and anyone can check one at `/verify/{code}`.

## How property trust works

- **Verified agents.** Agencies upload ID and business documents (stored under `private/`, never served from `/uploads`; only
  platform admins open them through the API). Approval adds the verified seal and lets their listings go live without review.
  Before launch, put R2 verification documents in a separate private bucket.
- **Moderation.** New agencies' listings, listings whose photos match another agency's (64-bit difference hash, Hamming distance
  ≤ 6) and listings with three open reports wait for a moderator. Seekers can never be charged an "inspection fee": cost labels
  mentioning inspection are refused.
- **Fresh listings.** Listings expire after 30 days (Free) or 60 (paid). Agents get a "still available?" email 5 days before
  with a one-click renew link (signed, valid 14 days).
- **Short-lets.** Guests pay the platform. A PostgreSQL exclusion constraint stops two guests holding the same nights; unpaid
  holds free their dates after 15 minutes. Free cancellation until 48 hours before check-in, then the host keeps one night. Hosts
  earn the total minus 10%, available 24 hours after check-in, paid out through the same admin queue as course earnings.
- **Reviews** come only from seekers who enquired at least a day earlier, had a viewing, or stayed.
- Photos are resized to 400/960/1920 px WebP, watermarked with the agency name, and the original (with its GPS data) is deleted.
  iPhone HEIC photos are refused with instructions to share as JPEG.
- Maps use MapLibre with OpenFreeMap tiles (free, no key). The database image is `postgis/postgis:17-3.5`.

## Running the platform (admin console)

- Admins sign in at the admin console with their password **and a 6-digit code emailed every time** (printed in the API console in
  development). Sessions last 8 hours and can't be renewed.
- Roles: **Super admin** (everything, manages admins), **Moderator** (course and property queues, verification, reports, reviews,
  locations, categories), **Finance** (plans, comps, payments, payouts), **Support** (businesses, users, suspension, view-as, audit log).
  The API checks the role on every request, so removing an admin or changing a role takes effect immediately.
- **Suspending** a business hides its public pages in every product and blocks its members at their next request; payments still record.
- **View as** gives support a 30-minute, read-only look at a business through the product app (`/support`); every write is refused by
  the API, the session is audited, and the business sees it under Account activity.
- Plans are edited in the console and never deleted; hidden plans aren't offered to new customers. The startup seeder only adds missing
  plans, so console edits are never overwritten.
