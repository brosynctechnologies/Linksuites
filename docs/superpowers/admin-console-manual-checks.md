# Admin console: manual browser checks

The super-admin spec (§10, "Browser") asks for a click-through of the admin console. It was only partly done, so this is the list to
run by hand before a release that touches `admin-web` or the platform API. The API side of every item is covered by integration
tests (role table, suspension, comp, plans, transfers, errors); this checks the screens.

**Setup.** Run the API and `admin-web` locally (README, "Run locally"), plus one product app (property-web is easiest). Have three
accounts: a SuperAdmin, a second admin you can add and remove, and a business owner (see
`.superpowers/recheck/local-test-accounts.md`). Use a throwaway business, never a real one.

Tick each line. If a result differs, note the page, what you clicked and what you saw.

## Sign-in

- [ ] Open `/login`, enter the SuperAdmin email and password. **Expect** a code step; the code arrives by email (Mailpit locally).
- [ ] Enter a wrong code. **Expect** a plain error, still on the code step.
- [ ] Enter the right code. **Expect** the overview with queue counts.
- [ ] Sign in as a normal (non-admin) user. **Expect** the same generic "email or password is wrong" error as a bad password.

## Businesses: suspend and reactivate

- [ ] Businesses → search the test business → open it. **Expect** header with plan, status Active, sections Members, Plan, Payments, Payouts.
- [ ] Press **Suspend business**, give a reason, confirm. **Expect** status Suspended and an audit row "business.suspend" with the reason.
- [ ] In the product app, open the business's public page. **Expect** not found. Sign in as its owner. **Expect** login refused with the suspension message.
- [ ] Back in the console, press **Reactivate business**, confirm. **Expect** status Active, audit row, public page and owner login work again.
- [ ] Press **View as business**, confirm. **Expect** the product app opens with a read-only banner; any save shows "Support view is read only."

## Plans

- [ ] Money → Plans → pick the product → edit a paid plan's name or a limit, save. **Expect** the table updates; Paying and Comped counts unchanged.
- [ ] Change the plan's price. **Expect** a **Change price** confirmation naming how many paying subscribers keep their old price until they next pay (comps counted apart).
- [ ] Open the default (free) plan. **Expect** the note that changes apply to every business on it immediately.
- [ ] On the test business, **Comp plan** with a "Free until" date. **Expect** "Complimentary until {date}" in Current subscription and a Comped badge.

## Queues: approve and reject

- [ ] Overview → a queue with items (listings, verifications or courses). **Expect** the count matches the queue page.
- [ ] Moderation → Listings → open a Pending listing. **Expect** photos, duplicates (if any), reports and a **Submitted** time (Hidden listings say **Hidden**). Edit that listing in the studio, reload. **Expect** the Submitted time and queue place unchanged.
- [ ] Approve it. **Expect** it leaves the queue, the listing is live on the public site and the agency gets "listing.approved".
- [ ] Reject another with a note. **Expect** a note is required; the agency sees the note in the studio.
- [ ] Moderation → Verifications → approve one. **Expect** the agency shows as verified and its Pending listings go live.
- [ ] Moderation → Reports → uphold one on a live listing. **Expect** the listing is Hidden and appears in the listings queue.

## Admins: add and remove

- [ ] Admins → **Add an admin** with the second account's email and role Support. **Expect** it appears in the list; the person gets "admin.added".
- [ ] Sign in as that admin. **Expect** Support pages only (no Admins, no Payment settings); a direct URL to a forbidden page shows a not-allowed message.
- [ ] As SuperAdmin, **Change role** to Finance. **Expect** the role column updates and the audit log has the change.
- [ ] **Remove admin**. **Expect** they are gone from the list and their next request is signed out.
- [ ] Try to remove the last SuperAdmin. **Expect** a refusal ("at least one super admin").

## Errors

- [ ] Errors (open). **Expect** newest first, each with count, first and last seen. Open one: stack visible, no tokens, keys or passwords in it (they show as `[redacted]`).
- [ ] Mark one fixed. **Expect** it moves to the resolved filter.
- [ ] Trigger the same API error again (or wait for it). **Expect** it is open again and the support team gets an "Error back on LinkSuite" email.
- [ ] A browser error report of a fixed error. **Expect** it stays resolved (count goes up).

## Payment settings

- [ ] Money → Payment settings. **Expect** the current plan provider, the online switch and the bank account details.
- [ ] Turn **Take payments online** off, **Save settings**, confirm. **Expect** product billing pages offer bank transfer instead of card checkout.
- [ ] Change the bank details and save. **Expect** the transfer instructions in a product app show the new details; audit row written.
- [ ] Turn online payments back on. **Expect** checkout returns in the product apps.

## Bank transfers

- [ ] In a product app (online payments off), choose a plan and claim "I've sent the transfer". **Expect** Finance and SuperAdmin get "plan.transfer_claimed".
- [ ] Money → Bank transfers → the claim → **Confirm transfer**. **Expect** status Confirmed and the business moves to the plan (period from now).
- [ ] Claim again and **Reject transfer** with a note. **Expect** status Rejected; the business sees the note and its plan is unchanged.
- [ ] Confirm a claim that was already closed. **Expect** the **Confirm anyway** warning before it applies.

## Payments and payouts

- [ ] Money → Payments, filter by product and status, export CSV. **Expect** Refunded and Collected columns; the CSV header matches.
- [ ] Money → Payouts → mark one paid with a reference. **Expect** status Paid and the host or teacher sees it.
