# Super Admin Panel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A platform-wide admin panel (`admin-web`) with stored admin roles, code-verified admin sign-in, business/user management, suspension, read-only view-as, plans/comps/payments/revenue, audit viewer and admin management.

**Architecture:** A `Platform/` module in each API layer under `/api/platform`, built on the shared core (tenants, users, plans, subscriptions, audit_logs, outbox). Roles live in a new `platform_admins` table and travel in the `padm` claim. View-as issues a short read-only tenant token enforced by a global API filter. The frontend is a new Next.js app built from a contract doc.

**Tech Stack:** ASP.NET Core 10, EF Core + Npgsql, xUnit v3 + Testcontainers, Next.js 16.4 + Tailwind 4.

**Spec:** `docs/superpowers/specs/2026-10-13-super-admin-design.md`

## Global Constraints

- Roles: `SuperAdmin`, `Moderator`, `Finance`, `Support` (enum `PlatformRole`, stored as strings). SuperAdmin satisfies every platform policy.
- Policies: `platform:any`, `platform:moderator`, `platform:finance`, `platform:support`, `platform:super`. All `/api/platform/*` routes except `/api/platform/auth/*` also require claim `amr=otp`.
- Admin token: no tenant, claims `padm=<role>` and `amr=otp`, lifetime 8 hours, no refresh token.
- View-as token: tenant token for the business with role `Owner`, extra claims `vas=<adminUserId>`, `ro=1`; lifetime 30 minutes; no refresh token. Reason 10-300 characters.
- Read-only rule: any request whose token has `ro=1` and method not in GET/HEAD/OPTIONS gets 403 "Support view is read only." before routing to a handler.
- Admin login OTP purpose `platform-login`, 10-minute expiry, 5 attempts, max 3 codes per 10 minutes per email.
- Suspended tenant: public pages hidden in every product; member login/OTP/refresh/switch → 403 "This business is suspended. Contact support."; tenant-status check middleware cached 1 minute per tenant.
- Plans are never deleted; `IsHidden` removes them from sale. Price edits never change a subscription's current period.
- Every admin write is audited in `audit_logs` with the admin as actor.
- Web app `admin-web`, port 4300, `App:ProductWebUrls.admin`, CORS 4300. Copy rules as other apps: sentence case, plain verbs.
- C# strings containing `\n` are written with Edit/Write, never shell scripts; node scripts use function replacements (`$` safe).

## Review Focus

1. A view-as token must not be able to change anything through any write route that exists now or is added later (not just the ones a test lists). Test in Task 4 enumerates every mapped endpoint with a write method.
2. A member of a business that gets suspended while their access token is still valid must be blocked on their next request, not after 15 minutes. Test in Task 3.
3. The last active SuperAdmin can't remove or demote themselves or be removed by another; removed admins lose access on their next request. Test in Task 5.
4. Admin sign-in must not reveal whether an email is an admin or the password is wrong (same status and message), and a customer-app token must not be accepted on `/api/platform`. Test in Task 2.
5. Editing a plan's price or limits must not change what an active subscriber already paid for their period; a comp ending must fall back to the business's previous plan. Test in Task 5.

---

### Task 1: Admin accounts, roles and re-gating

**Files:** Create `api/src/Saas.Domain/Core/PlatformAdmin.cs` (`PlatformAdmin`, `PlatformRole`), `api/src/Saas.Application/Platform/PlatformAccess.cs`; Modify `Common/Abstractions.cs` (DbSet `PlatformAdmins`), `AppDbContext.cs`, `Infrastructure/Auth/AuthService.cs` (padm claim = role), `Application/Auth/AuthDtos.cs` (`UserDto.PlatformRole`), `Saas.Api/Program.cs` + `CoreEndpoints.cs` (policies), `CourseEndpoints.cs` + `PropertyEndpoints.cs` (re-gate); migration `PlatformAdmins` (also adds `job_runs` table and `plans.is_hidden` used by later tasks). Test: `tests/Saas.IntegrationTests/PlatformRoleTests.cs`.

**Interfaces — Produces:**
- `PlatformAdmin : Entity { Guid UserId; PlatformRole Role; Guid? AddedBy; DateTimeOffset? RemovedAt; Guid? RemovedBy }`, unique index on `UserId`.
- `PlatformAccess.RoleOfAsync(Guid userId, string? email, ct) -> PlatformRole?`: active row's role; if none and `email` is in `Platform:AdminEmails`, creates a SuperAdmin row (lazy bootstrap) and returns SuperAdmin.
- `AppClaims.Amr = "amr"`, `AppClaims.ViewAs = "vas"`, `AppClaims.ReadOnly = "ro"`.

- [ ] Write failing tests: `Config_admin_is_bootstrapped_as_super_admin` (admin email signs in → `user.platformRole == "SuperAdmin"`, row exists); `Moderator_cannot_reach_finance_routes` (insert Moderator row for a user, sign in → `/api/admin/payouts` 403, `/api/admin/property/moderation` 200, `/api/admin/course-reviews` 200); `Finance_cannot_reach_moderation` (`/api/admin/property/moderation` 403, `/api/admin/payouts` 200); `Removed_admin_loses_access` (set RemovedAt → 403 on next token).
- [ ] Run, expect FAIL. Implement. Run full suite: all existing admin tests still pass.

### Task 2: Admin sign-in with code, job runs, health

**Files:** Create `Application/Platform/PlatformAuth.cs` (DTOs), `Infrastructure/Auth/PlatformAuthService.cs`, `Application/Platform/HealthService.cs`, `Infrastructure/Jobs/JobRuns.cs` (`IJobRuns.RecordAsync(name, started, succeeded, error)`); Modify the four hosted services (invoices, bookings, property jobs, notification dispatcher) to record runs; Create `Saas.Api/Endpoints/PlatformEndpoints.cs`. Test: `PlatformAuthTests.cs`.

**Produces:** `POST /api/platform/auth/login {email,password}` → `{challengeId}`; `POST /api/platform/auth/verify {challengeId, code}` → `AuthResponse` (refreshToken empty string, tenant null); `GET /api/platform/health` → `{ jobs: [{name,lastStartedAt,lastSucceededAt,lastError}], outbox: {pending,sent,failed}, failures: [{id,templateKey,recipient,error,createdAt}] }`.

- [ ] Failing tests: `Admin_signs_in_with_password_and_emailed_code` (code read from the outbox notification `otp`; token works on `/api/platform/health`, expires in ~8 h); `Wrong_password_and_non_admin_look_the_same` (both 400 with identical detail); `Customer_token_is_refused_on_platform_routes` (seeker token of an admin email → 403 on `/api/platform/health`); `Wrong_code_five_times_burns_the_challenge`.
- [ ] Implement; run; full suite.

### Task 3: Businesses, users, suspension

**Files:** Create `Application/Platform/PlatformBusinessService.cs`, `PlatformUserService.cs`, `Saas.Api/Infrastructure/TenantStatusMiddleware.cs`; Modify `AuthService` (login/otp/refresh/switch refuse suspended tenant), property public reads (`PropertySearchService.Live`, agent/agency pages, `StayService.ShortLetAsync`, `LeadService.SendAsync`, `InspectionService.LiveListingAsync`, reports) to require tenant Active; notification templates `business.suspended`, `business.reactivated`. Test: `PlatformBusinessTests.cs`.

**Produces:** `GET /api/platform/businesses?q=&product=&status=&plan=&page=` → `Paged<BusinessRowDto(id,name,slug,product,status,planName,ownerEmail,createdAt)>`; `GET /api/platform/businesses/{id}` → `BusinessDetailDto` (business, subscription, usage `UsageItemDto[]`, members, publicUrl, recentAudit, payments, payouts); `POST .../{id}/suspend|reactivate {reason}`; `GET /api/platform/users?q=`; `GET /api/platform/users/{id}` → `UserDetailDto(user, memberships[{tenantId,name,product,role,status}], lastLoginAt, lockedOut, activity{courses,stays,enquiries,inspections})`; `POST /users/{id}/unlock|send-reset|sign-out-everywhere`.

- [ ] Failing tests: `Support_finds_a_business_by_owner_email`; `Suspended_business_disappears_from_public_pages` (property listing detail 404 + search excludes; an order store `/api/store/{slug}` 404); `Suspended_member_is_blocked_on_next_request` (token issued before suspend → 403 on `/api/property/listings`; login → 403); `Reactivation_restores_access`; `Finance_cannot_suspend` (403); `User_page_lists_memberships_across_products`.
- [ ] Implement; run; full suite.

### Task 4: Read-only view-as and account activity

**Files:** Create `Infrastructure/Auth/ViewAsService.cs`, `Saas.Api/Infrastructure/ReadOnlyTokenMiddleware.cs`, `Application/Platform/AccountActivityService.cs`; Modify `PlatformEndpoints.cs`, `CoreEndpoints.cs` (`GET /api/account/activity`). Test: `PlatformViewAsTests.cs`.

**Produces:** `POST /api/platform/businesses/{id}/view-as {reason}` → `{ token, expiresAt, product, webUrl }` (`webUrl = {productWeb}/support?token=…`); `POST /api/platform/view-as/end {token}`; `GET /api/account/activity` → `[{ adminName, reason, startedAt, endedAt }]`.

- [ ] Failing tests: `View_as_token_reads_like_the_owner` (GET `/api/property/listings` 200); `View_as_token_cannot_write_anywhere` (enumerate `EndpointDataSource` endpoints with POST/PUT/DELETE/PATCH; call each with the token and a dummy body → every one 403 with the read-only message; skip none); `View_as_needs_a_reason_and_support_role` (Moderator 403; reason "x" 400); `View_as_is_audited_and_shown_to_the_owner`; `View_as_expires_after_30_minutes` (token `exp` - `iat` = 1800 s).
- [ ] Implement; run; full suite.

### Task 5: Plans, comps, payments, overview, audit, admins

**Files:** Create `Application/Platform/PlatformPlansService.cs`, `PlatformMoneyService.cs`, `PlatformOverviewService.cs`, `PlatformAuditService.cs`, `PlatformAdminsService.cs`; Modify `BillingService`/plans listing to hide `IsHidden` plans from sale. Test: `PlatformManagementTests.cs`.

**Produces:** `GET/POST /api/platform/plans`, `PUT /api/platform/plans/{id}`; `POST /api/platform/businesses/{id}/comp {planCode, until, reason}`; `GET /api/platform/payments?product=&status=&from=&to=&page=` and `/payments.csv`; `GET /api/platform/overview`; `GET /api/platform/audit?...`; `GET/POST /api/platform/admins`, `PUT/DELETE /api/platform/admins/{id}`.

- [ ] Failing tests: `Plan_price_edit_keeps_current_periods` (active subscription's `CurrentPeriodEnd` and plan id unchanged, billing page shows new price for new signups); `Hidden_plan_is_not_offered_but_keeps_subscribers`; `Comp_gives_a_plan_until_its_date_then_falls_back`; `Payments_list_spans_products` (a course order and a stay appear with product names); `Overview_counts_businesses_and_fees`; `Audit_filters_by_business`; `Last_super_admin_cannot_be_removed_or_demoted`; `Super_admin_adds_a_moderator_who_can_moderate`; `Only_super_admin_manages_admins`.
- [ ] Implement; run; full suite.

### Task 6: Contract, design, admin-web, support banner, cleanup

**Files:** Create `docs/PLATFORM_API.md`, `docs/design/adminlink.md`, `admin-web/` (subagent); `/support` route + banner in `web`, `invoice-web`, `booking-web`, `course-web`, `property-web`; replace `course-web/src/app/admin` and `property-web/src/app/admin` with a page linking to admin-web; README, CI (admin-web job), `.claude/launch.json`, appsettings.

- [ ] Write contract and design plan; dispatch the frontend subagent with both and the rules used for property-web.
- [ ] Verify each touched web app: `npm run lint && npm run build` clean.
- [ ] Browser check: sign in with code (code from the API console log), find a business, view-as (banner, a save attempt shows the read-only message), suspend/reactivate, edit a plan, approve a queue item, add and remove an admin.
