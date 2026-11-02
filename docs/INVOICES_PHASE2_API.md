# API contract — Invoice Generator phase 2

Adds expenses, a profit report, multi-currency with live exchange rates, and team/accountant access.
Conventions as in `INVOICES_API.md` (minor units, `YYYY-MM-DD` dates, ProblemDetails, `402` + `code: "plan_limit"`).
Exchange rates are JSON numbers (e.g. `1580.25`).

## Plans

| Plan | Price | Adds |
|---|---|---|
| Free | ₦0 | 10 invoices/month, 1 seat |
| Pro | ₦4,000/month | unlimited invoices, premium templates, reminders, recurring, Pay-now links, 1 seat |
| **Business** (new) | ₦10,000/month | everything in Pro + **expenses**, **profit report**, **team up to 10 seats** |

Plan limit keys: `expenses` (0/1), `reports` (0/1), `team_seats` (number). `GET /api/billing` → `usage.items` now also lists
`{ key: "team_seats", label: "Team seats", used, limit }`.

## Roles and permissions

`tenant.role` in `AuthResponse`: `Owner`, `Admin`, `Staff`, `Accountant`.

| Action | Owner | Admin | Staff | Accountant |
|---|---|---|---|---|
| View invoices, quotes, clients, items, expenses, recurring | ✓ | ✓ | ✓ | ✓ |
| Create/edit/send invoices, quotes, clients, items, recurring; record/delete payments | ✓ | ✓ | ✓ | — |
| Add/edit expenses | ✓ | ✓ | ✓ | — |
| Profit report, CSV exports | ✓ | ✓ | — | ✓ |
| Business settings, tax rates, reminders, templates | ✓ | ✓ | — | — |
| Team: invite, change roles, remove | ✓ | ✓ (cannot touch the Owner) | — | — |
| Billing upgrade, Paystack key | ✓ | — | — | — |

Forbidden actions return `403`. The UI should hide what a role can't do.

## Team

| Method | Path | Body | Returns |
|---|---|---|---|
| GET | `/api/team` | | `{ members: [MemberDto], invites: [InviteDto], seats: { used, limit } }` |
| POST | `/api/team/invites` | `{ email, role }` (`Admin`, `Staff` or `Accountant`) | `InviteDto` (402 when seats are full, 409 if already a member or invited) |
| POST | `/api/team/invites/{id}/resend` | | `InviteDto` |
| DELETE | `/api/team/invites/{id}` | | 204 |
| PATCH | `/api/team/members/{id}` | `{ role }` | `MemberDto` (can't change the Owner, can't make someone Owner) |
| DELETE | `/api/team/members/{id}` | | 204 (can't remove the Owner) |

`MemberDto = { id, userId, fullName, email, role, isYou, joinedAt }` (`id` is the membership id)
`InviteDto = { id, email, role, invitedAt, expiresAt }`
Seats used = members + pending invites. Invites expire after 7 days; the email links to `{web}/invite/{token}`.

**Accepting an invite (no sign-in needed):**

| Method | Path | Body | Returns |
|---|---|---|---|
| GET | `/api/public/invites/{token}` | | `{ businessName, email, role, hasAccount, expired }` |
| POST | `/api/auth/invites/{token}/accept` | `{ fullName, password }` | `AuthResponse` for that business |

If `hasAccount` is true, only `password` is needed (their existing password). Otherwise `fullName` + a new password (min 8) create the account.

**Several businesses:** a user can belong to more than one invoices business (their own plus ones they were invited to).

| Method | Path | Body | Returns |
|---|---|---|---|
| GET | `/api/me/businesses` | | `[{ tenantId, name, role, current }]` (same product as the token) |
| POST | `/api/auth/switch` | `{ tenantId }` | `AuthResponse` for that business |

## Exchange rates and multi-currency

The business's currency (`BusinessDto.currency`) is its **base currency** for dashboards and reports. It can't be changed
once invoices or expenses exist (`400`).

| Method | Path | Returns |
|---|---|---|
| GET | `/api/exchange-rates` | `{ base, asOf, source, rates: [{ currency, toBase }] }` — `toBase` = value of 1 unit of `currency` in base, e.g. `{ currency: "USD", toBase: 1580.25 }` for an NGN business |

Rates come from open.er-api.com (free, updated daily); show "Rates by Exchange Rate API" with a link to `https://www.exchangerate-api.com` wherever rates are shown.

Changes to existing shapes:
- `SaveInvoiceRequest`, `SaveQuoteRequest`: optional `exchangeRate` (number). Omit to use today's rate. Ignored when the currency is the base currency.
- `InvoiceDto`, `QuoteDto`: add `baseCurrency`, `exchangeRate`, `totalBaseMinor`; `InvoiceDto` also `balanceBaseMinor`.
- `InvoiceSummaryDto`: add `totalBaseMinor`, `balanceBaseMinor`.
- `PaymentDto`: add `exchangeRate`, `baseAmountMinor` (rate on the payment date).
- `InvoiceDashboardDto`: all amounts are now in the base currency (`currency`), converted with each invoice's saved rate.
- `/api/invoices/export.csv` adds `Exchange rate` and `Total (base)` columns.
If no rate can be found, creating a foreign-currency invoice returns `503` with `code: "fx_unavailable"`: ask the user to type a rate.

## Expenses (Business plan)

| Method | Path | Body | Returns |
|---|---|---|---|
| GET | `/api/expenses?from=&to=&category=&q=&page=1&pageSize=20` | | `Paged<ExpenseDto>` |
| GET | `/api/expenses/categories` | | `string[]` (defaults plus ones already used) |
| GET | `/api/expenses/{id}` | | `ExpenseDto` |
| POST / PUT `{id}` | `/api/expenses` | `SaveExpenseRequest` | `ExpenseDto` (402 below Business) |
| DELETE | `/api/expenses/{id}` | | 204 |
| POST | `/api/expenses/{id}/receipt` | multipart `file` (jpeg/png/webp/pdf ≤ 5 MB) | `ExpenseDto` |
| DELETE | `/api/expenses/{id}/receipt` | | `ExpenseDto` |
| GET | `/api/expenses/export.csv?from=&to=` | | `text/csv` |

`SaveExpenseRequest = { date, category, vendor, description, amountMinor, currency, taxMinor, paymentMethod, reference, clientId, notes, exchangeRate }`
(`vendor`, `description`, `reference`, `clientId`, `notes`, `exchangeRate` optional; `taxMinor` is the VAT included in `amountMinor`, default 0; `paymentMethod` uses the invoice payment methods.)
`ExpenseDto = SaveExpenseRequest & { id, baseAmountMinor, receiptUrl, clientName, createdAt }`
Default categories: Advertising, Bank charges, Equipment, Internet & phone, Rent, Salaries, Software, Supplies, Transport, Utilities, Other.

## Profit report (Business plan; Owner, Admin, Accountant)

| Method | Path | Returns |
|---|---|---|
| GET | `/api/reports/profit?from=&to=` | `ProfitReportDto` (defaults to this year) |
| GET | `/api/reports/profit.csv?from=&to=` | `text/csv` (one row per month) |

Cash basis: income is payments received in the period; expenses are expenses dated in the period. All amounts are in the base currency.
```
ProfitReportDto = {
  currency, from, to,
  incomeMinor, expensesMinor, profitMinor,          // profit = income - expenses
  invoicedMinor,                                    // invoices issued in the period (not draft/void)
  outstandingMinor,                                 // unpaid balance today, all open invoices
  taxCollectedMinor,                                // tax on invoices issued in the period
  taxOnExpensesMinor,                               // tax included in expenses in the period
  expensesByCategory: [{ category, amountMinor }],  // largest first
  months: [{ month: "2026-10", incomeMinor, expensesMinor, profitMinor }]  // every month in the range, zeros included
}
```
