# API contract — Invoice Generator

Same API as the WhatsApp Order System (`http://localhost:5080`, see `API.md` for conventions):
camelCase JSON, string enums, money as integer **minor units** in `...Minor` fields, ProblemDetails
errors (`402` + `code: "plan_limit"` means upgrade needed). Dates without time are `"YYYY-MM-DD"` strings.
Quantities and percentages are JSON numbers (e.g. `quantity: 2.5`, `ratePercent: 7.5`).

All `/api/...` routes below except `/api/public/...` and `/api/auth/...` need a Bearer token for an
**invoices** account; tokens from other products get `403`.

## Auth (shared with the other products)

| Method | Path | Body | Notes |
|---|---|---|---|
| POST | `/api/auth/signup` | `{ fullName, email, password, country, product: "invoices", businessName }` | If the email already has an account for another product, the password must match and a new invoices business is added to that account. |
| POST | `/api/auth/login` | `{ email, password, product: "invoices" }` | `400` if the user has no invoices business. |
| POST | `/api/auth/refresh` | `{ refreshToken }` | |
| POST | `/api/auth/logout` | `{ refreshToken }` | |
| POST | `/api/auth/otp/request` | `{ email }` | |
| POST | `/api/auth/otp/verify` | `{ email, code, product: "invoices" }` | |
| GET | `/api/me` | | `{ user, tenant }` |

`AuthResponse = { accessToken, refreshToken, expiresAt, user: { id, fullName, email }, tenant: { id, name, slug, role, product } }`

## Business settings

| Method | Path | Body / returns |
|---|---|---|
| GET | `/api/business` | `BusinessDto` |
| PUT | `/api/business` | `UpdateBusinessRequest` → `BusinessDto` |
| POST | `/api/business/logo` | multipart `file` (jpeg/png/webp ≤ 5 MB) → `BusinessDto` |
| PUT | `/api/business/paystack` | `{ secretKey }` (`sk_test_...`/`sk_live_...`) → `BusinessDto` |
| DELETE | `/api/business/paystack` | → `BusinessDto` |

`BusinessDto = { legalName, logoUrl, address, email, phone, taxId, bankDetails, currency, country, paymentTermsDays, invoicePrefix, nextInvoiceNumber, quotePrefix, nextQuoteNumber, defaultTemplate, brandColor, defaultNotes, defaultTerms, remindersEnabled, reminderDaysBefore, reminderDaysAfter: number[], paystackConnected }`
`UpdateBusinessRequest` = the same fields minus `logoUrl`, `country`, `paystackConnected`.
`bankDetails` is free text (bank, account name, account number) printed on invoices.
`reminderDaysBefore` (number or null): remind N days before the due date. `reminderDaysAfter`: remind N days after, e.g. `[1, 7, 14]`.
The Paystack key is the **business's own** key: online payments go straight to their Paystack account. It is stored encrypted and never returned.

## Tax rates, clients, saved items

| Method | Path | Body | Returns |
|---|---|---|---|
| GET / POST, PUT / DELETE `{id}` | `/api/tax-rates` | `{ name, ratePercent }` | `TaxRateDto = { id, name, ratePercent }` |
| GET `?q=` / POST, GET / PUT / DELETE `{id}` | `/api/clients` | `SaveClientRequest` | `ClientDto` |
| GET `?q=` / POST, PUT / DELETE `{id}` | `/api/items` | `{ name, description, unitPriceMinor, taxRateId, unit }` | `ItemDto = { id, name, description, unitPriceMinor, taxRateId, unit }` |

`SaveClientRequest = { name, company, email, phone, address, taxId, notes }`
`ClientDto = SaveClientRequest & { id, invoiceCount, outstandingMinor }`
Deleting a client keeps its invoices (they store a copy of the client's details).

## Invoices

| Method | Path | Body | Returns |
|---|---|---|---|
| GET | `/api/invoices?status=&clientId=&q=&page=1&pageSize=20` | | `Paged<InvoiceSummaryDto>` |
| GET | `/api/invoices/{id}` | | `InvoiceDto` |
| POST | `/api/invoices` | `SaveInvoiceRequest` | `InvoiceDto` (402 when the monthly invoice limit is reached) |
| PUT | `/api/invoices/{id}` | `SaveInvoiceRequest` | `InvoiceDto` (not allowed for `Void`) |
| DELETE | `/api/invoices/{id}` | | 204 — drafts only; otherwise 409, use void |
| POST | `/api/invoices/{id}/send` | `{ to, message }` (both optional; `to` defaults to the client email) | `InvoiceDto` — emails the client a link; Draft becomes Sent |
| POST | `/api/invoices/{id}/mark-sent` | | `InvoiceDto` — use after sharing on WhatsApp |
| POST | `/api/invoices/{id}/void` | | `InvoiceDto` |
| POST | `/api/invoices/{id}/duplicate` | | `InvoiceDto` (new draft dated today) |
| GET | `/api/invoices/{id}/pdf` | | `application/pdf` |
| POST | `/api/invoices/{id}/payments` | `{ amountMinor, method, reference, paidAt, note, sendReceipt }` | `InvoiceDto` |
| DELETE | `/api/invoices/{id}/payments/{paymentId}` | | `InvoiceDto` (manual payments only) |
| GET | `/api/invoices/export.csv?status=&from=&to=` | | `text/csv` |
| GET | `/api/invoice-dashboard` | | `InvoiceDashboardDto` |
| GET | `/api/invoice-templates` | | `[{ key, name, isPremium, available }]` |

Invoice `status`: `Draft`, `Sent`, `Viewed`, `PartiallyPaid`, `Paid`, `Overdue`, `Void`.
`Overdue` is computed: sent/viewed/partially paid with a due date before today. It can be used as a list filter.
Payment `method`: `Cash`, `BankTransfer`, `Card`, `Online`, `Other`. `paidAt` is a date.

`SaveInvoiceRequest = { clientId, issueDate, dueDate, currency, template, items: [LineRequest], shippingMinor, notes, terms }`
`LineRequest = { itemId, description, quantity, unitPriceMinor, discountPercent, taxRateId }` (`itemId`, `taxRateId` nullable; `discountPercent` 0–100)

Line maths (the server is the source of truth; the editor can mirror it for the live preview):
```
gross        = round(quantity * unitPriceMinor)
discount     = round(gross * discountPercent / 100)
lineTotal    = gross - discount
lineTax      = round(lineTotal * ratePercent / 100)
subtotal     = sum(gross);  discountTotal = sum(discount);  taxTotal = sum(lineTax)
total        = subtotal - discountTotal + taxTotal + shippingMinor
balance      = total - amountPaid
```
`round` = round half away from zero to a whole minor unit.

`InvoiceSummaryDto = { id, number, clientName, issueDate, dueDate, currency, totalMinor, balanceMinor, status, createdAt }`
`InvoiceDto = { id, number, status, client: ClientSnapshot, issueDate, dueDate, currency, template, items: [LineDto], subtotalMinor, discountTotalMinor, taxTotalMinor, taxes: [{ name, ratePercent, amountMinor }], shippingMinor, totalMinor, amountPaidMinor, balanceMinor, notes, terms, publicUrl, whatsappShareUrl, sentAt, viewedAt, paidAt, quoteId, recurringScheduleId, payments: [PaymentDto], events: [{ type, note, createdAt }], createdAt }`
`ClientSnapshot = { id, name, company, email, phone, address, taxId }` (`id` null if the client was deleted)
`LineDto = { id, itemId, description, quantity, unitPriceMinor, discountPercent, taxRateId, taxName, taxRatePercent, lineTotalMinor }`
`PaymentDto = { id, amountMinor, method, reference, paidAt, receiptNumber, note, online }`
Event `type`: `Created`, `Sent`, `Viewed`, `Reminded`, `PaymentRecorded`, `PaymentRemoved`, `Paid`, `Voided`.
`InvoiceDashboardDto = { currency, outstandingMinor, overdueMinor, overdueCount, paidThisMonthMinor, draftCount, invoicesThisMonth, invoiceLimit, recentInvoices: InvoiceSummaryDto[] }` (`invoiceLimit` -1 = unlimited)

## Quotes

| Method | Path | Body | Returns |
|---|---|---|---|
| GET | `/api/quotes?status=&q=&page=&pageSize=` | | `Paged<QuoteSummaryDto>` |
| GET / PUT / DELETE | `/api/quotes/{id}` | `SaveQuoteRequest` | `QuoteDto` |
| POST | `/api/quotes` | `SaveQuoteRequest` | `QuoteDto` |
| POST | `/api/quotes/{id}/send` | `{ to, message }` | `QuoteDto` |
| POST | `/api/quotes/{id}/mark-sent` | | `QuoteDto` |
| POST | `/api/quotes/{id}/convert` | | `InvoiceDto` (the new draft invoice) |
| GET | `/api/quotes/{id}/pdf` | | `application/pdf` |

Quote `status`: `Draft`, `Sent`, `Accepted`, `Declined`, `Converted`, `Expired` (computed from `expiryDate`).
`SaveQuoteRequest = { clientId, issueDate, expiryDate, currency, template, items: [LineRequest], shippingMinor, notes, terms }`
`QuoteSummaryDto = { id, number, clientName, issueDate, expiryDate, currency, totalMinor, status, createdAt }`
`QuoteDto` = `InvoiceDto` fields without `dueDate`, payments, balance and `recurringScheduleId`, plus `expiryDate`, `convertedInvoiceId`.

## Recurring invoices (Pro)

| Method | Path | Body | Returns |
|---|---|---|---|
| GET | `/api/recurring` | | `RecurringDto[]` |
| POST / PUT `{id}` | `/api/recurring` | `SaveRecurringRequest` | `RecurringDto` (402 on Free) |
| DELETE | `/api/recurring/{id}` | | 204 |

`SaveRecurringRequest = { clientId, frequency, startDate, endDate, dueInDays, autoSend, isActive, currency, template, items: [LineRequest], shippingMinor, notes, terms }`
`frequency`: `Weekly`, `Monthly`, `Quarterly`, `Yearly`. A background job creates each invoice on its date and emails it when `autoSend` is true.
`RecurringDto = SaveRecurringRequest & { id, clientName, nextRunDate, lastRunDate, invoicesCreated, totalMinor }`

## Public (no sign-in)

| Method | Path | Body | Returns |
|---|---|---|---|
| GET | `/api/public/invoices/{token}` | | `PublicInvoiceDto` (marks the invoice viewed) |
| GET | `/api/public/invoices/{token}/pdf` | | PDF |
| POST | `/api/public/invoices/{token}/pay` | `{ email }` (optional if the client has one) | `{ authorizationUrl }` — redirect there |
| POST | `/api/public/invoices/{token}/verify` | `{ reference }` | `PublicInvoiceDto` |
| GET | `/api/public/invoices/{token}/receipts/{paymentId}/pdf` | | PDF receipt |
| GET | `/api/public/quotes/{token}` | | `PublicQuoteDto` |
| POST | `/api/public/quotes/{token}/accept` and `/decline` | | `PublicQuoteDto` |
| GET | `/api/public/quotes/{token}/pdf` | | PDF |
| GET | `/api/public/invoice-templates` | | `[{ key, name, isPremium }]` |
| POST | `/api/public/invoice-preview/pdf` | `PreviewRequest` | PDF — powers the free no-signup generator (free templates only, with branding) |

After Paystack checkout the payer returns to `{web}/i/{token}?reference=...`; the page should call `/verify` with that reference.
`PublicInvoiceDto = { number, status, business: { name, logoUrl, address, email, phone, taxId, bankDetails, brandColor }, client: ClientSnapshot, issueDate, dueDate, currency, template, items: [LineDto], subtotalMinor, discountTotalMinor, taxTotalMinor, taxes, shippingMinor, totalMinor, amountPaidMinor, balanceMinor, notes, terms, canPayOnline, payments: [{ id, amountMinor, method, paidAt, receiptNumber }], showBranding }`
`PublicQuoteDto` = same shape for quotes with `expiryDate` instead of `dueDate`, no payments, plus `canRespond`.
`PreviewRequest = { business: { name, address, email, phone, taxId, bankDetails, brandColor }, client: { name, company, email, phone, address }, number, issueDate, dueDate, currency, template, items: [{ description, quantity, unitPriceMinor, discountPercent, taxRatePercent }], shippingMinor, notes, terms }`

## Billing

Same `/api/billing` and `/api/billing/checkout` as `API.md`. For invoices accounts, `usage.items` lists
`[{ key, label, used, limit }]` (e.g. invoices this month). Plans: **Free** (10 invoices a month, 3 templates,
branding) and **Pro** (unlimited invoices, all templates, no branding, reminders, recurring invoices, Pay-now links).
