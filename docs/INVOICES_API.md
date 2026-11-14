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
| POST | `/api/business/logo` | multipart `file` → `BusinessDto` (Pro and Business; `402` on Free) |
| DELETE | `/api/business/logo` | 204 (every plan) |
| POST | `/api/business/signature` | multipart `file` → `BusinessDto` (Pro and Business; `402` on Free) |
| DELETE | `/api/business/signature` | 204 (every plan) |
| POST | `/api/business/stamp` | multipart `file` → `BusinessDto` (Pro and Business; `402` on Free) |
| DELETE | `/api/business/stamp` | 204 (every plan) |
| PUT | `/api/business/paystack` | `{ secretKey }` (`sk_test_...`/`sk_live_...`) → `BusinessDto` |
| DELETE | `/api/business/paystack` | → `BusinessDto` |

`PUT /api/business` and the six image routes need the **Owner** or **Admin** role; the Paystack routes need the Owner.

`BusinessDto = { legalName, logoUrl, address, email, phone, taxId, bankDetails, currency, country, paymentTermsDays, invoicePrefix, nextInvoiceNumber, quotePrefix, nextQuoteNumber, defaultTemplate, brandColor, defaultNotes, defaultTerms, remindersEnabled, reminderDaysBefore, reminderDaysAfter: number[], paystackConnected, tagline, registrationNumber, headerLine, secondaryColor, logoSize, fontStyle, documentTitle, showTaxId, showRegistrationNumber, showBankDetails, showSignature, signatureName, signatureRole, signatureImageUrl, stampImageUrl, footerText, brandImagesAvailable }`
`UpdateBusinessRequest` = the same fields minus `logoUrl`, `signatureImageUrl`, `stampImageUrl`, `country`, `paystackConnected`, `brandImagesAvailable`.
`bankDetails` is free text (bank, account name, account number) printed on invoices.
`reminderDaysBefore` (number or null): remind N days before the due date. `reminderDaysAfter`: remind N days after, e.g. `[1, 7, 14]`.
The Paystack key is the **business's own** key: online payments go straight to their Paystack account. It is stored encrypted and never returned.

### Letterhead (how documents look)

| Field | Values | Default | Notes |
|---|---|---|---|
| `tagline` | text, ≤ 100 | null | "Slogan or what you do" |
| `registrationNumber` | text, ≤ 40 | null | Printed as `Reg. no. {value}` |
| `headerLine` | `tagline`, `address`, `registration`, `none` | `address` | The one line under the business name. A choice with no value behind it prints nothing |
| `secondaryColor` | `#rrggbb` or null | null | Second accent. Null (or blank) means "35% darker than `brandColor`"; saved lower-case |
| `logoSize` | `small`, `medium`, `large` | `medium` | |
| `fontStyle` | `modern`, `serif`, `sans` | `modern` | Inter, Noto Serif, Noto Sans |
| `documentTitle` | `Invoice`, `Tax invoice`, `Bill`, `Pro forma invoice`, `Statement of account` | `Invoice` | Invoices only; quotes are titled "Quote" and receipts "Receipt" |
| `showTaxId`, `showRegistrationNumber`, `showBankDetails` | bool | true | See "Show switches" below |
| `showSignature` | bool | false | The signature block, in every design (Corporate included). When off, `signatureName` and `signatureRole` are also left out of `PublicBusinessDto` and the PDF |
| `signatureName`, `signatureRole` | text, ≤ 80 each | null | Printed under the signature rule |
| `footerText` | text, ≤ 160 | null | Printed above the page number on every PDF page |
| `signatureImageUrl`, `stampImageUrl` | read-only | null | Set by the upload routes |
| `brandImagesAvailable` | read-only | | Whether the plan prints the logo, signature and stamp |

Validation (`400`, errors keyed by field): the four choice fields must be one of the exact (case-sensitive) values above;
`secondaryColor` must be `#` and six hex digits, or blank; the text fields must be within their lengths.

On `PUT /api/business`, the choice fields and the switches (`headerLine` to `showSignature`) **keep their saved value when
omitted or null**. The six letterhead text fields (`tagline`, `registrationNumber`, `secondaryColor`, `signatureName`,
`signatureRole`, `footerText`) also **keep their saved value when omitted or null**, so a client that predates them can't wipe
them; **an empty (or blank) string clears them**. The web app sends `""` for a field the user emptied. (Every other text field
keeps the older rule: null or blank clears it.)

`defaultTemplate`: a premium design needs the plan only when it **changes**. A business that downgraded with a premium default
can still save its other settings (the default stays); switching to another premium design returns `402`, and a free design is
always accepted.

Existing businesses got these defaults from the `InvoiceDesigns` migration, except `headerLine`, which is `none` where the
business had no address.

### Logo, signature and stamp

- Upload: multipart field `file`, **JPEG, PNG or WebP by magic bytes** (the client's Content-Type is ignored), **5 MB or smaller**;
  otherwise `400` on `file` ("Upload a JPEG, PNG or WebP image." / "Images must be 5 MB or smaller.").
- **At most 4000 × 4000 pixels**, read from the file header without decoding it: a larger image gets `400` on `file` with
  "Images must be 4000 × 4000 pixels or smaller.", and a header whose size can't be read gets "This image could not be
  read. Upload a JPEG, PNG or WebP image." (The cap bounds the memory every PDF render needs to decode the image.)
- Plan: uploads need the `brand_images` limit. Without it the route returns `402` (`code: "plan_limit"`) **before reading the
  file**, with the message `Your logo, signature and stamp are part of the Pro plan. Upgrade to add them.`
- Uploading replaces the previous image of that kind and deletes the old stored file. Files are stored under
  `{tenantId}/business/{logo|signature|stamp}-{id}{ext}`.
- `DELETE` clears the URL and deletes the stored file. It works on every plan (removing is never paid), so a business that
  downgraded can still remove its images.

### Plan limit `brand_images`

`brand_images` is Free 0, Pro 1, Business 1. It gates both uploading and printing:

- **Images print only while the plan has `brand_images`.** The logo, signature and stamp appear in public DTOs, PDFs (invoice,
  quote, receipt) and the web previews only then.
- **On a downgrade nothing is deleted.** The images stay stored and stop printing everywhere at once (documents are rendered
  from live business settings). On upgrade they come back.
- The owner's `GET /api/business` always returns the stored URLs, with `brandImagesAvailable` saying whether they print. The web
  app drops them from its own previews when it is false.
- The PDF only fetches images from the app's own storage; an image that can't be fetched or decoded, or whose header declares
  more than 4000 pixels a side (an upload from before the cap), is left out rather than failing the PDF. Each image is decoded
  once per render.

The `InvoiceDesigns` migration added `brand_images` to the existing InvoiceLink plans and to the limit snapshots of active
subscriptions (1 where the snapshot has `premium_templates`), so paying businesses keep their logo.

### Show switches

`showTaxId`, `showRegistrationNumber` and `showBankDetails` are applied **by the server**: a hidden value is never sent to
clients. In `PublicBusinessDto`, `taxId` and `bankDetails` are null when switched off, and `registrationNumber` is null unless
`showRegistrationNumber` is on or `headerLine` is `registration`. `showSignature` works the same way: when it is off,
`signatureName` and `signatureRole` are null in `PublicBusinessDto` and the PDF has no signature block, in every design. The
PDF model leaves them out the same way. The owner's `BusinessDto` always carries the stored values.

Receipt PDFs headline **Amount paid** (the payment) after Invoice total and Paid before, with **Balance remaining** as an
ordinary row under it; the meta lists the receipt number, date paid, invoice, method and reference, and long references wrap.

Where they print: tax ID (`Tax ID: …`) and registration number (`Reg. no. …`, unless it is already the line under the name)
in the "Contact" block with email, phone and the address (unless the address is the line under the name). Bank details print
in "Payment details" on invoices only.

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

Templates, in order: `classic`, `modern`, `minimal`, `bold`, `elegant` (free), then `executive`, `corporate`, `ledger`,
`studio`, `prestige` (`isPremium`; `available` only with `premium_templates`, i.e. Pro and Business). Saving a premium template
on Free (as an invoice's `template`, or as a new `defaultTemplate`; an unchanged premium default is kept) returns `402`. A document saved with a premium template keeps
rendering in it after a downgrade. An unknown key renders as Classic. See `docs/design/invoicelink.md` for what each looks like.

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
`PublicInvoiceDto = { number, status, business: PublicBusinessDto, client: ClientSnapshot, issueDate, dueDate, currency, template, items: [LineDto], subtotalMinor, discountTotalMinor, taxTotalMinor, taxes, shippingMinor, totalMinor, amountPaidMinor, balanceMinor, notes, terms, canPayOnline, payments: [{ id, amountMinor, method, paidAt, receiptNumber }], showBranding }`
`PublicQuoteDto` = same shape for quotes with `expiryDate` instead of `dueDate`, no payments, plus `canRespond`.
`PublicBusinessDto = { name, logoUrl, address, email, phone, taxId, bankDetails, brandColor, tagline, registrationNumber, headerLine, secondaryColor, logoSize, fontStyle, documentTitle, showTaxId, showRegistrationNumber, showBankDetails, showSignature, signatureName, signatureRole, signatureImageUrl, stampImageUrl, footerText }`
In `PublicBusinessDto`, `logoUrl`, `signatureImageUrl` and `stampImageUrl` are null unless the business's plan has `brand_images`,
and hidden values are null (see "Show switches"), so the public pages render what they are given.
`PreviewRequest = { business: { name, address, email, phone, taxId, bankDetails, brandColor, tagline, registrationNumber, headerLine, secondaryColor, fontStyle, documentTitle, showSignature, signatureName, signatureRole, footerText }, client: { name, company, email, phone, address }, number, issueDate, dueDate, currency, template, items: [{ description, quantity, unitPriceMinor, discountPercent, taxRatePercent }], shippingMinor, notes, terms }`
The generator takes the free letterhead fields only, validated like `UpdateBusinessRequest` (errors keyed `business.<field>`).
It has no image fields (any sent are ignored) and never prints images; `logoSize` and the show switches are not accepted and use
their defaults. `headerLine` defaults to `address` when an address is given, else `none`. A premium or unknown `template`
renders as Classic.

## Billing

Same `/api/billing` and `/api/billing/checkout` as `API.md`. For invoices accounts, `usage.items` lists
`[{ key, label, used, limit }]` (e.g. invoices this month). Plans: **Free** (10 invoices a month, 5 templates,
branding) and **Pro** (unlimited invoices, all 10 templates, your logo, signature and stamp on documents, no branding,
reminders, recurring invoices, Pay-now links). **Business** adds expenses, the profit and loss report and a team.
