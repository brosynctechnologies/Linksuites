# API contract — Booking System (BookLink)

Same API and conventions as `API.md` / `INVOICES_API.md`: camelCase JSON, string enums, money in minor units (`...Minor`),
ProblemDetails errors, `402` + `code: "plan_limit"` = upgrade needed, `409` conflicts.
**Instants** (`startsAt`, `endsAt`, ...) are ISO 8601 with offset, e.g. `"2026-10-14T10:30:00+01:00"`; display them in the
business's `timezone` (an IANA id, e.g. `Africa/Lagos`). **Dates** are `"YYYY-MM-DD"`; **clock times** are `"HH:mm"`.
Weekdays are numbers: 0 = Sunday … 6 = Saturday.

## Auth

Shared auth with `product: "bookings"`:
- signup `{ fullName, email, password, country, product: "bookings", businessName }`
- login / otp verify `{ ..., product: "bookings" }`
- team invites, `/api/me/businesses`, `/api/auth/switch` work as in `INVOICES_PHASE2_API.md` (accept page lives at `{web}/invite/{token}`).
Roles: Owner/Admin manage settings, services, staff, team; Staff create and manage bookings and customers; Accountant reads and sees reports.
Writes by a role that may not write return `403`.

## Plans (`/api/billing`)

| Plan | Price/month | Staff | Bookings/month | Deposits & email reminders | Branding | Team seats |
|---|---|---|---|---|---|---|
| Free | ₦0 | 1 | 50 | — | shown | 1 |
| Solo | ₦5,000 | 1 | unlimited | ✓ | removed | 2 |
| Team | ₦12,000 | 5 | unlimited | ✓ | removed | 6 |
| Business | ₦25,000 | unlimited | unlimited | ✓ | removed | 15 |

Limit keys: `staff_members`, `bookings_per_month`, `deposits`, `reminders`, `remove_branding`, `team_seats`. `usage.items` lists staff, bookings this month, team seats.

## Business settings

| Method | Path | Body / returns |
|---|---|---|
| GET / PUT | `/api/booking-business` | `BookingBusinessDto` / `UpdateBookingBusinessRequest` |
| POST | `/api/booking-business/logo`, `/api/booking-business/cover` | multipart `file` → `BookingBusinessDto` |
| PUT / DELETE | `/api/booking-business/paystack` | `{ secretKey }` → `BookingBusinessDto` |
| GET / PUT | `/api/booking-business/hours` | `[{ weekday, open, close, closed }]` (7 rows) |

`BookingBusinessDto = { name, slug, category, description, logoUrl, coverUrl, address, phone, whatsappNumber, email, timezone, currency, country, brandColor, policies, cancellationHours, slotIntervalMinutes, minNoticeMinutes, maxDaysAhead, bookingEnabled, paystackConnected, bookingUrl }`
`UpdateBookingBusinessRequest` = the same minus `logoUrl`, `coverUrl`, `currency`, `country`, `paystackConnected`, `bookingUrl`.
`category`: `Salon`, `Barber`, `Spa`, `MakeUp`, `Photography`, `Clinic`, `Fitness`, `Other`.
`slotIntervalMinutes` 5–60 (default 15); `minNoticeMinutes` 0–10080 (default 60); `maxDaysAhead` 1–365 (default 60); `cancellationHours` 0–168 (default 24).

## Services

| Method | Path | Body | Returns |
|---|---|---|---|
| GET / POST, PUT / DELETE `{id}` | `/api/services` | `SaveServiceRequest` | `ServiceDto` |

`SaveServiceRequest = { name, description, durationMinutes, bufferBeforeMinutes, bufferAfterMinutes, priceMinor, depositType, depositValue, isActive, position, staffIds }`
`depositType`: `None`, `Fixed` (value in minor units), `Percent` (1–100). Deposits need a plan with `deposits` and a connected Paystack key; otherwise bookings are confirmed without one.
`ServiceDto = SaveServiceRequest & { id, depositMinor }` (`depositMinor` = the deposit for the price).

## Staff, schedules, time off

| Method | Path | Body | Returns |
|---|---|---|---|
| GET / POST, PUT / DELETE `{id}` | `/api/staff` | `SaveStaffRequest` | `StaffDto` (POST may be 402) |
| POST | `/api/staff/{id}/photo` | multipart `file` | `StaffDto` |
| PUT | `/api/staff/{id}/schedule` | `[{ weekday, start, end }]` | `StaffDto` |
| GET / POST | `/api/staff/{id}/time-off` | `{ startsAt, endsAt, reason }` | `TimeOffDto[]` / `TimeOffDto` |
| DELETE | `/api/staff/{id}/time-off/{timeOffId}` | | 204 |

`SaveStaffRequest = { displayName, bio, email, color, isBookable, position, serviceIds, userId }` (`userId` links a team member's login so "My calendar" shows their bookings; optional)
`StaffDto = SaveStaffRequest & { id, photoUrl, schedule: [{ weekday, start, end }], calendarFeedUrl }`
A schedule may have several intervals per weekday; gaps between them are breaks. A day with no intervals is a day off.
`calendarFeedUrl` is a private iCal link (subscribe in Google/Apple Calendar).
`TimeOffDto = { id, staffId, startsAt, endsAt, reason }`
New businesses start with one staff member (the owner) working the business hours, and no services.

## Availability (also used by the business when booking by phone)

| Method | Path | Returns |
|---|---|---|
| GET | `/api/availability?serviceId=&staffId=&date=` | `AvailabilityDto` (ignores min notice, still respects hours and other bookings) |

`AvailabilityDto = { date, timezone, slots: [{ startsAt, endsAt, staffIds }] }` — `staffIds` lists who can take that slot; when `staffId` is omitted every eligible staff member is considered.

## Bookings

| Method | Path | Body | Returns |
|---|---|---|---|
| GET | `/api/bookings?from=&to=&staffId=&status=&q=&page=1&pageSize=50` | | `Paged<BookingSummaryDto>` (from/to are dates) |
| GET | `/api/bookings/{id}` | | `BookingDto` |
| POST | `/api/bookings` | `CreateBookingRequest` | `BookingDto` (402 when the monthly limit is reached; 409 slot taken) |
| PATCH | `/api/bookings/{id}` | `{ startsAt, staffId, notes }` (reschedule; any omitted field is kept) | `BookingDto` |
| POST | `/api/bookings/{id}/status` | `{ status, note }` (`Confirmed`, `Completed`, `Cancelled`, `NoShow`) | `BookingDto` |
| POST | `/api/bookings/{id}/payments` | `{ amountMinor, type, method, reference }` | `BookingDto` |
| POST | `/api/bookings/{id}/refund` | `{ amountMinor }` (online deposit back through Paystack) | `BookingDto` |
| GET | `/api/calendar?from=&to=&staffId=` | | `CalendarDto` |

`CreateBookingRequest = { serviceId, staffId, startsAt, customer: { name, phone, email }, source, notes, force }`
(`source`: `WalkIn`, `Phone`, `Online`; `force: true` books outside working hours, but never over another booking.)
`BookingSummaryDto = { id, code, startsAt, endsAt, status, customerName, customerPhone, serviceName, staffId, staffName, staffColor, priceMinor, depositMinor, paidMinor, source }`
`BookingDto = BookingSummaryDto & { customer: { id, name, phone, email, noShowCount }, serviceId, notes, holdExpiresAt, manageUrl, whatsappReminderUrl, payments: [{ id, amountMinor, type, method, reference, online, createdAt }], history: [{ status, note, createdAt }], reminders: [{ channel, sendAt, sentAt, status }], review }`
Booking `status`: `Pending` (waiting for deposit, slot held until `holdExpiresAt`), `Confirmed`, `Completed`, `Cancelled`, `NoShow`, `Expired` (hold ran out).
Payment `type`: `Deposit`, `Balance`, `Refund`; `method`: `Online`, `Cash`, `Transfer`, `Card`.
`whatsappReminderUrl` opens WhatsApp to the customer with a ready reminder message.
`review = { rating, comment, createdAt } | null`
`CalendarDto = { timezone, staff: [{ id, name, color }], bookings: BookingSummaryDto[], timeOff: TimeOffDto[], hours: [{ weekday, open, close, closed }] }`

## Customers

| Method | Path | Returns |
|---|---|---|
| GET | `/api/booking-customers?q=&page=&pageSize=` | `Paged<BookingCustomerDto>` |
| GET | `/api/booking-customers/{id}` | `BookingCustomerDto & { bookings: BookingSummaryDto[] }` |
| PUT | `/api/booking-customers/{id}` | `{ name, phone, email, notes }` → `BookingCustomerDto` |

`BookingCustomerDto = { id, name, phone, email, notes, bookingsCount, completedCount, noShowCount, totalSpentMinor, lastVisitAt }`

## Dashboard and reports

| Method | Path | Returns |
|---|---|---|
| GET | `/api/booking-dashboard` | `{ timezone, currency, today: BookingSummaryDto[], upcomingCount, pendingCount, revenueThisMonthMinor, bookingsThisMonth, bookingLimit, noShowRate }` |
| GET | `/api/booking-reports?from=&to=` | `BookingReportDto` (Owner, Admin, Accountant) |

`BookingReportDto = { currency, from, to, bookings, completed, cancelled, noShows, noShowRate, revenueMinor, depositsMinor, byService: [{ name, bookings, revenueMinor }], byStaff: [{ name, bookings, revenueMinor, noShows }], byDay: [{ date, bookings, revenueMinor }] }`
Revenue = payments received (deposits + balances − refunds) in the period. `noShowRate` = no-shows ÷ (completed + no-shows), 0–1.

## Public booking page (no sign-in)

| Method | Path | Body | Returns |
|---|---|---|---|
| GET | `/api/public/book/{slug}` | | `PublicBookingPageDto` |
| GET | `/api/public/book/{slug}/days?serviceId=&staffId=&month=YYYY-MM` | | `[{ date, available }]` for that month |
| GET | `/api/public/book/{slug}/availability?serviceId=&staffId=&date=` | | `AvailabilityDto` |
| POST | `/api/public/book/{slug}/bookings` | `PublicBookRequest` | `{ booking: PublicBookingDto, authorizationUrl, manageUrl }` |
| GET | `/api/public/bookings/{token}` | | `PublicBookingDto` |
| POST | `/api/public/bookings/{token}/pay` | `{ email }` | `{ authorizationUrl }` (while the hold is still valid) |
| POST | `/api/public/bookings/{token}/verify` | `{ reference }` | `PublicBookingDto` |
| POST | `/api/public/bookings/{token}/cancel` | | `PublicBookingDto` |
| POST | `/api/public/bookings/{token}/reschedule` | `{ startsAt, staffId }` | `PublicBookingDto` |
| POST | `/api/public/bookings/{token}/review` | `{ rating, comment }` | `PublicBookingDto` (only after Completed) |
| GET | `/api/public/bookings/{token}/ics` | | `text/calendar` |
| GET | `/api/public/staff-calendar/{feedToken}.ics` | | `text/calendar` (a staff member's bookings) |

`PublicBookingPageDto = { name, slug, category, description, logoUrl, coverUrl, address, phone, whatsappNumber, timezone, currency, brandColor, policies, cancellationHours, maxDaysAhead, bookingEnabled, showBranding, rating: { average, count } | null, services: [{ id, name, description, durationMinutes, priceMinor, depositMinor, staffIds }], staff: [{ id, displayName, photoUrl, bio, serviceIds }], hours: [{ weekday, open, close, closed }] }`
`PublicBookRequest = { serviceId, staffId, startsAt, customer: { name, phone, email }, notes }` — `staffId` null means "anyone available"; the server picks someone free. `startsAt` must be one of the offered slots.
If the service needs a deposit, the booking is created as `Pending` with a 10-minute hold and `authorizationUrl` is set: send the browser there. Paystack returns the customer to `{web}/b/{token}?reference=...`; that page calls `/verify`. Otherwise the booking is `Confirmed` and `authorizationUrl` is null.
`PublicBookingDto = { code, status, startsAt, endsAt, timezone, service: { id, name, durationMinutes }, staff: { id, displayName, photoUrl }, customerName, priceMinor, depositMinor, paidMinor, balanceMinor, currency, holdExpiresAt, canCancel, canReschedule, cancelDeadline, canReview, review, business: { name, slug, address, phone, whatsappNumber, logoUrl, brandColor, policies } }`
Cancel and reschedule are allowed until `cancelDeadline` (start − `cancellationHours`). Deposits are not refunded automatically; the policy text explains the business's rule.

Webhook for each business's Paystack: `POST /api/webhooks/paystack/bookings/{tenantId}`.
Reserved booking slugs: the shared reserved list plus `b`.
