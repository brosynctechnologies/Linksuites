# API contract — Property Listing Website (PropertyLink)

Same API and conventions as the other products: camelCase JSON, string enums, money in minor units (kobo), ProblemDetails
(`errors` keyed by camelCase field path, e.g. `costs[0].label`), `402` + `code: "plan_limit"`. Instants are ISO 8601 with offset;
dates `YYYY-MM-DD`; the business time zone is `Africa/Lagos`.

Web app: `property-web`, `http://localhost:4100` (`App:ProductWebUrls.property`). Links the API puts in emails:
`/listings/{agencySlug}/{listingSlug}`, `/agencies/{slug}`, `/agents/{slug}`, `/renew/{token}`, `/trips/{stayId}`,
`/me/viewings`, `/me/searches`, `/studio/listings/{id}`, `/studio/enquiries`, `/studio/viewings`, `/studio/stays`,
`/studio/verification`, `/studio/boosts/{id}` (Paystack return for boosts).

## Accounts and tokens

| Who | Sign up / log in | Token |
|---|---|---|
| Agency, solo agent or landlord | `POST /api/auth/signup` `{ fullName, email, password, country: "NG", product: "property", businessName, accountKind?: "Agency"\|"Agent"\|"Landlord" }`; login/otp with `product: "property"` | tenant token (roles Owner/Admin/Staff/Accountant) |
| Seeker / short-let guest | `POST /api/auth/seeker/signup` `{ fullName, email, password }`; `POST /api/auth/seeker/login` `{ email, password }`; refresh `/api/auth/refresh` | seeker token: `AuthResponse.tenant` is **null** |
| Moderator | a seeker (or any) account whose email is in `Platform:AdminEmails` | `user.isPlatformAdmin = true` |

Seeker routes (`/api/me/...`, bookings, reviews) accept any signed-in token. `GET /api/me` returns `{ user, tenant | null }`.

## Plans (`/api/billing`, product `property`)

| Plan | Price/month | Live listings | Listing stays up | Views & lead stats | Team seats |
|---|---|---|---|---|---|
| Free | ₦0 | 3 | 30 days | — | 1 |
| Pro | ₦10,000 | 30 | 60 days | ✓ | 3 |
| Agency | ₦35,000 | unlimited | 60 days | ✓ | 10 |

"Live" counts Active + Pending. Limit keys: `listings`, `listing_expiry_days`, `listing_stats`, `team_seats`.

## Enums

- `ListingPurpose`: Rent, Sale, ShortLet. `PricePeriod`: Year, Month (rent), Total (sale), Night (short-let).
- `ListingStatus`: Draft, Pending (moderation), Active, Expired, Rented, Sold, Rejected, Hidden (reported).
- `CostKind`: AgencyFee, LegalFee, Caution, ServiceCharge, Other. A cost label containing "inspection" is refused (400).
- `MediaStatus`: Processing, Ready, Failed. `AgencyKind`: Agency, Agent, Landlord. `LocationKind`: State, City, Area.
- `EnquiryStatus`: New, Replied, Closed. `InspectionStatus`: Requested, Confirmed, Done, Cancelled, NoShow.
- `StayStatus`: Pending (held 15 min, unpaid), Confirmed, Completed, Cancelled, Expired. `StayCanceller`: Guest, Host.
- `ReportReason`: Fake, AlreadyTaken, WrongPrice, Duplicate, Scam, Other. `VerificationStatus`: Pending, Approved, Rejected.
- `AlertFrequency`: Off, Instant, Daily. `BoostKind`: Featured, Top. `BoostStatus`: Pending, Active, Expired.
- `ReviewBasis`: Enquiry, Inspection, Stay. `ListingEventKind`: View, WhatsApp, Call.

## Public (no sign-in)

| Method | Path | Returns |
|---|---|---|
| GET | `/api/listings?purpose=&location=&type=&minPrice=&maxPrice=&beds=&amenities=pool,parking&verified=true&bbox=w,s,e,n&sort=recommended\|newest\|price_asc\|price_desc&page=&pageSize=` | `SearchResultDto` |
| GET | `/api/listings/{agencySlug}/{listingSlug}` | `ListingDetailDto` (Active only; 404 otherwise) |
| POST | `/api/listings/{id}/events` `{ kind }` | 204. Call on page view (`View`) and on WhatsApp/call button taps. |
| POST | `/api/listings/{id}/enquiries` `{ name, phone, email?, message? }` | 204 (rate limited; sends the token's user id if signed in) |
| POST | `/api/listings/{id}/report` `{ reason, details? }` | 204; 409 "You've already reported this listing…" if this user (or, signed out, this IP) already reported it, including two reports sent at the same moment. Signed-out reporters are stored as an HMAC of their IP (keyed with the server secret), so the stored value can't be reversed by hashing every IP. |
| GET | `/api/listings/{id}/inspection-times` | `string[]` of instants (next 14 days, ≥ 2 h ahead) |
| GET | `/api/listings/{id}/availability?from=&to=` | `{ booked: [{ start, end }], blocked: [{ start, end }] }` (end exclusive) |
| POST | `/api/listings/{id}/stay-quote` `{ listingId, checkIn, checkOut, guests }` | `{ nights, nightlyMinor, subtotalMinor, cleaningMinor, totalMinor, currency }` or 400/409 |
| GET | `/api/listings/renew/{token}` | `{ title, status, expiresAt, note }` without renewing, so the `/renew/{token}` page can show the listing and ask first. 400 on `token` if invalid or expired. |
| POST | `/api/listings/renew/{token}` | `{ title, status, expiresAt, note }` (renews; the renew link in expiry emails) |
| GET | `/api/locations?parent={slug}` | `LocationDto[]` (no parent = the 37 states) |
| GET | `/api/areas/{slug}` | `AreaGuideDto` (any location: state, city or area) |
| GET | `/api/agents/{slug}` | `AgentPageDto` |
| GET | `/api/agencies/{slug}` | `AgencyPageDto` |

Errors: unknown `location` → 404; malformed `bbox` (not 4 finite numbers, out of range, w ≥ e or s ≥ n; `NaN` and `Infinity` are refused) → 400 on `bbox`; `minPrice > maxPrice` → 400; unknown `type`/`amenities` → 400.

```ts
LocationDto { id, name, slug, kind, parentSlug, lat, lng }
ListingCardDto { id, title, slug, agencySlug, agencyName, verified, purpose, typeName, priceMinor, currency, pricePeriod, bedrooms,
  bathrooms, sizeSqm, areaName, cityName, coverUrl, photoCount, featured, top, lat, lng, publishedAt }
SearchResultDto { items: Paged<ListingCardDto>, pins: [{ id, lat, lng, priceMinor, slug, agencySlug }] (≤ 500; link a pin to `/listings/{agencySlug}/{slug}`), location: LocationDto | null }
Paged<T> { items: T[], page, pageSize, total }
ListingDetailDto { id, title, slug, description, purpose, type: { name, slug }, breadcrumb: LocationDto[] (state → city → area),
  address, lat, lng, priceMinor, currency, pricePeriod, bedrooms, bathrooms, toilets, sizeSqm, furnished, tourUrl, videoUrl,
  photos: [{ thumbUrl, cardUrl, fullUrl, width, height }], costs: [{ kind, label, amountMinor }], moveInTotalMinor,
  amenities: [{ name, slug }], shortLet: { cleaningFeeMinor, minNights, maxGuests, checkInTime, checkOutTime, houseRules } | null,
  agent: AgentCardDto, agency: AgencyCardDto, publishedAt, lastConfirmedAt, similar: ListingCardDto[] }
AgentCardDto { id, displayName, slug, photoUrl, phone, whatsApp, verified, ratingAverage, ratingCount }
AgencyCardDto { id, name, slug, kind, logoUrl, verified, phone }
AgentReviewDto { id, userName, rating, comment, basis, createdAt }
AgentPageDto { agent: AgentCardDto, bio, licenceNo, agency: AgencyCardDto, listings: ListingCardDto[], reviews: AgentReviewDto[] }
AgencyPageDto { agency: AgencyCardDto, bio, address, agents: AgentCardDto[], listings: ListingCardDto[], ratingAverage, ratingCount }
AreaGuideDto { location, breadcrumb, areas: LocationDto[] (children), activeCount, rentByBedrooms: [{ bedrooms (5 = 5+), medianMinor, count }]
  (yearly rents), medianSaleMinor | null, medianNightlyMinor | null, latest: ListingCardDto[] (8) }
```

`moveInTotalMinor` = price + every cost (for rent: first year/month plus fees). Photos: `thumbUrl` 400 px, `cardUrl` 960 px,
`fullUrl` 1920 px wide WebP, watermarked with the agency name (except thumbnails).

## Seekers (any signed-in token)

| Method | Path | Body / returns |
|---|---|---|
| GET | `/api/me/saved-listings` | `ListingCardDto[]` (still-live ones) |
| POST / DELETE | `/api/me/saved-listings/{listingId}` | 204 (POST is idempotent) |
| GET | `/api/me/saved-searches` | `SavedSearchDto[]` |
| POST / PUT | `/api/me/saved-searches[/{id}]` `{ name, filter: ListingFilter, frequency }` | `SavedSearchDto` (max 20; unknown location/type/amenity → 400) |
| DELETE | `/api/me/saved-searches/{id}` | 204 |
| GET | `/api/me/enquiries` | `[{ id, listingId, listingTitle, listingUrl, agencyName, message, status, createdAt }]` |
| POST | `/api/listings/{id}/inspections` `{ at, phone?, notes? }` | `InspectionDto`; 400 if `at` isn't an offered time, `phone` is over 30 characters or `notes` over 1,000; 409 if just taken, 403 on own listing |
| GET | `/api/me/inspections` | `InspectionDto[]` |
| POST | `/api/me/inspections/{id}/cancel` | 204 |
| POST | `/api/stays` `{ listingId, checkIn, checkOut, guests, phone? }` | `{ stayId, authorizationUrl, totalMinor, holdExpiresAt }`; 400 if `phone` is over 30 characters; 409 "Those dates were just booked"; 503 when payments aren't set up |
| POST | `/api/stays/{id}/verify` `{ reference }` | `MyStayDto` (Paystack returns to `{web}/trips/{stayId}?reference=…`) |
| POST | `/api/stays/{id}/cancel` | `MyStayDto` |
| GET | `/api/me/stays` | `MyStayDto[]` |
| POST | `/api/agents/{slug}/reviews` `{ rating 1-5, comment? }` | `AgentReviewDto`; 403 unless the user enquired ≥ 24 h ago, had a Done viewing or a Completed stay with this agent (and isn't in the agency); 409 if already reviewed |
| POST | `/api/reviews/{id}/report` | 204 (hides the review until a moderator decides) |

```ts
ListingFilter { purpose?, location? (slug), type? (slug), minPrice?, maxPrice?, minBeds?, amenities?: string[], verifiedOnly: boolean, bbox?: [w,s,e,n] }
SavedSearchDto { id, name, filter: ListingFilter, frequency, lastSentAt, createdAt }
InspectionDto { id, listingId, listingTitle, areaName, seekerName, seekerPhone, agentName, agentPhone, scheduledAt, status, notes }
MyStayDto { id, listingId, listingTitle, listingUrl, coverUrl, areaName, checkIn, checkOut, guests, nights, totalMinor, currency, status,
  holdExpiresAt, refundMinor, agentName, agentPhone (once confirmed), checkInTime, checkOutTime, address (once confirmed), houseRules }
```

Short-let rules: check-out after check-in, check-in not in the past, `minNights` ≤ nights ≤ 90, 1 ≤ guests ≤ `maxGuests`, no host-closed
dates. Price = nights × nightly + cleaning. Guest cancels > 48 h before 14:00 on check-in day: full refund; later: refund minus one night;
not after check-in. Host cancels: full refund. A paid stay is refunded once: the cancel claims it (Confirmed → Cancelled) before
the refund is sent, so a second cancel at the same time (a double click, or guest and host together) gets 409 "This stay has already
been cancelled." If Paystack refuses the refund, the stay goes back to Confirmed and the cancel fails.

Payments that don't confirm a stay:
- **Wrong amount or currency.** The stay isn't confirmed. The guest is emailed that the payment didn't go through (template
  "stay.payment_mismatch"), and every active Finance and SuperAdmin admin is emailed to sort it out ("payments.mismatch").
- **Cancelled before the payment arrived.** The whole payment is refunded. The refund is claimed with a conditional update first, so
  the webhook and the return page refund it only once. The guest is emailed "stay.refunded".
- **Paid after the hold ran out, and someone else took the dates.** Refunded in full, the stay is Cancelled, and the guest is emailed
  "stay.refunded".

## Agency studio (`product: property` token)

Reads: any member, except earnings, payouts and the payout account, which only Owner/Admin can read. Writes: Owner/Admin/Staff
unless noted (money: Owner/Admin; payouts and payout account: Owner).

| Method | Path | Body / returns |
|---|---|---|
| GET | `/api/property/listings?status=` | `StudioListingSummaryDto[]` |
| POST | `/api/property/listings` | `SaveListingRequest` → `StudioListingDto` (status Draft) |
| GET / PUT / DELETE | `/api/property/listings/{id}` | `StudioListingDto` / `SaveListingRequest` / 204 |
| POST | `/api/property/listings/{id}/submit` | `StudioListingDto` → Active (verified agency, no reports, no duplicate photo) or Pending; 402 at the plan limit (Active, Pending and Hidden listings all count); 400 if a photo is still processing or failed |
| POST | `/api/property/listings/{id}/renew` | Active: extends expiry; Expired/Rented/Sold: republishes (plan limit applies) |
| POST | `/api/property/listings/{id}/status` `{ status: Rented\|Sold\|Draft }` | `StudioListingDto` |
| POST | `/api/property/listings/{id}/media` | multipart `file` (JPEG/PNG/WebP ≤ 15 MB, ≤ 30 per listing) → `ListingMediaDto` (Processing; poll the listing until Ready) |
| PUT | `/api/property/listings/{id}/media/order` `{ ids }` | 204 (every photo once) |
| DELETE | `/api/property/listings/{id}/media/{mediaId}` | 204 |
| GET / POST | `/api/property/listings/{id}/blocks` | `BlockDto[]` / `{ start, end (exclusive), note? }` → `BlockDto`; 409 over booked nights |
| DELETE | `/api/property/listings/{id}/blocks/{blockId}` | 204 |
| GET | `/api/property/profile/agency` · PUT (Owner/Admin) | `AgencyProfileDto` / `{ name, slug, kind, phone?, email?, address?, bio? }` |
| GET | `/api/property/profile/agents` | `AgentProfileDto[]` (team) |
| GET / PUT | `/api/property/profile/agent` | my `AgentProfileDto` / `{ displayName, bio?, phone?, whatsApp?, licenceNo? }` |
| GET | `/api/property/enquiries?status=` | `EnquiryDto[]` |
| POST | `/api/property/enquiries/{id}/status` `{ status }` | 204 |
| GET / PUT | `/api/property/inspection-slots` | `SlotDto[]` / `{ slots: [{ weekday, start: "09:00", end: "17:00", slotMinutes }] }` (my weekly windows) |
| GET | `/api/property/inspections?from=&to=` | `InspectionDto[]` |
| POST | `/api/property/inspections/{id}/status` `{ status, proposedAt? }` | `InspectionDto` (proposedAt moves it) |
| GET | `/api/property/stays?from=&to=` | `StudioStayDto[]` |
| POST | `/api/property/stays/{id}/cancel` (Owner/Admin) | 204 (full refund to the guest; counted against the agency) |
| GET | `/api/property/stats?days=30` | `ListingStatsDto`; 402 on Free |
| GET | `/api/property/verification` | latest `VerificationDto` or 204 |
| POST | `/api/property/verification` (Owner/Admin) | multipart `idDocument` (required), `businessDocument?`, `licenceNo?` (JPEG/PNG/WebP/PDF ≤ 10 MB) → `VerificationDto`; 409 while one is pending |
| GET | `/api/property/boost-options` | `[{ code, kind, days, priceMinor, currency, name }]` |
| POST | `/api/property/listings/{id}/boosts` `{ code }` | `{ boostId, authorizationUrl, amountMinor }`; 409 unless Active |
| POST | `/api/property/boosts/{id}/verify` `{ reference }` | `BoostDto` (Paystack returns to `{web}/studio/boosts/{id}?reference=…`) |
| GET | `/api/property/boosts` | `BoostDto[]`. Each paid boost adds its days in the database (from the current end, or now if none), so two boosts paid at the same moment both count. |
| GET | `/api/property/earnings` (Owner/Admin) | `{ currency, availableMinor, pendingMinor, paidOutMinor, entries: [{ id, stayId, description, grossMinor, feeMinor, netMinor, availableAt, createdAt, status }] }` |
| GET (Owner/Admin) / POST (Owner) | `/api/property/payouts` | `PayoutDto[]` / `{ amountMinor }` (minimum ₦5,000; 409 without a bank account) |
| GET (Owner/Admin) / PUT (Owner) | `/api/property/payout-account` | `{ bankName, accountNumber, accountName }` or 204 |

In a support view (see `docs/PLATFORM_API.md`), `accountNumber` in the payout account and the payouts list is masked as `••••1234`.

Team, billing and invites use the shared core routes (`/api/team`, `/api/billing`, `/api/me/businesses`, `/api/auth/switch`).

```ts
SaveListingRequest { title, description?, purpose, propertyTypeId, locationId (an Area), address?, lat, lng (Nigeria), priceMinor, pricePeriod,
  bedrooms, bathrooms, toilets, sizeSqm?, furnished, tourUrl?, videoUrl?, costs?: [{ kind, label, amountMinor }] (≤ 10),
  amenityIds?: Guid[], shortLet?: { cleaningFeeMinor, minNights, maxGuests, checkInTime? "HH:mm", checkOutTime?, houseRules? } (required for ShortLet),
  agentProfileId? }
StudioListingDto { id, title, slug, description, purpose, propertyTypeId, locationId, address, lat, lng, priceMinor, currency, pricePeriod,
  bedrooms, bathrooms, toilets, sizeSqm, furnished, tourUrl, videoUrl, status, moderationNote, publishedAt, expiresAt, featuredUntil,
  topUntil, viewsCount, costs, amenityIds, shortLet, moveInTotalMinor, agentProfileId, publicUrl, media: ListingMediaDto[], createdAt, updatedAt }
StudioListingSummaryDto { id, title, slug, purpose, status, priceMinor, currency, pricePeriod, areaName, coverUrl, viewsCount, expiresAt,
  featuredUntil, topUntil, updatedAt }
ListingMediaDto { id, fileName, thumbUrl, cardUrl, fullUrl, status, position, error, duplicateOfListingId, duplicateOfAgency }
AgencyProfileDto { id, name, slug, kind, logoUrl, phone, email, address, bio, verified, verifiedAt, publicUrl }
AgentProfileDto { id, userId, displayName, slug, bio, phone, whatsApp, licenceNo, photoUrl, verified, ratingAverage, ratingCount }
EnquiryDto { id, listingId, listingTitle, name, phone, email, message, status, createdAt }
StudioStayDto { id, listingId, listingTitle, guestName, guestPhone (once confirmed), checkIn, checkOut, guests, nights, totalMinor,
  commissionMinor, status, refundMinor, cancelledBy, createdAt }
ListingStatsDto { days, views, whatsAppClicks, callClicks, enquiries, listings: [{ listingId, title, status, views, whatsAppClicks, callClicks, enquiries }] }
VerificationDto { id, kind, status, licenceNo, note, documents: [{ index, label, fileName, contentType }], createdAt, reviewedAt }
BoostDto { id, listingId, listingTitle, code, kind, days, amountMinor, status, paidAt, endsAt, createdAt }
```

Reference data for the wizard: `GET /api/locations` (states), `?parent=lagos` (areas); property types and amenities come from
`GET /api/property-types` and `GET /api/amenities` (below).

| GET | `/api/property-types` | `[{ id, name, slug }]` |
|---|---|---|
| GET | `/api/amenities` | `[{ id, name, slug }]` |

## Platform admin (`isPlatformAdmin`)

| Method | Path | Body / returns |
|---|---|---|
| GET | `/api/admin/property/moderation` | `ModerationItemDto[]` (Pending and Hidden) |
| POST | `/api/admin/property/listings/{id}/decision` `{ approve, note? }` | 204 (reject needs a note); 409 if it isn't Pending or Hidden; 409 on approve when the agency is at its plan's listing limit (e.g. a Hidden listing after a downgrade): reject with a note instead, or the agency ends another listing or upgrades |
| GET | `/api/admin/property/verifications?status=` | `AdminVerificationDto[]` |
| GET | `/api/admin/property/verifications/{id}/documents/{index}` | the file (fetch with the token, show as a blob) |
| POST | `/api/admin/property/verifications/{id}/decision` `{ approve, note? }` | 204 (approval releases the agency's waiting listings) |
| GET | `/api/admin/property/reports?status=Open` | `[{ id, listingId, listingTitle, listingStatus, agencyName, reason, details, status, createdAt }]` |
| POST | `/api/admin/property/reports/{id}/decision` `{ uphold }` | 204 (dismissing the last open report restores a hidden listing) |
| GET | `/api/admin/property/reviews` | hidden reviews `[{ id, agentName, agentSlug, userName, rating, comment, reportCount, status, createdAt }]` |
| POST | `/api/admin/property/reviews/{id}/decision` `{ restore }` | 204 |
| GET / POST / PUT | `/api/admin/property/locations[/{id}]` | `[{ id, parentId, kind, name, slug, lat, lng, position }]` / `{ name, slug?, kind, parentId?, lat?, lng?, position? }` |
| GET | `/api/admin/payouts?status=Requested` | shared queue (courses and property): `PayoutDto & { product, businessName, currency }` |
| POST | `/api/admin/payouts/{id}/paid` `{ reference }` · `/reject` `{ note }` | `PayoutDto` |

```ts
ModerationItemDto { listingId, title, status, note, agencyName, agencySlug, agencyVerified, hostCancellations, openReports, coverUrl,
  priceMinor, pricePeriod, areaName, duplicates: [{ myPhotoUrl, theirPhotoUrl, theirListingId, theirListingTitle, theirAgency }], submittedAt,
  photos: string[] (a URL per photo, full size where there is one), publicUrl /* the listing's public page */ }
AdminVerificationDto { id, tenantId, agencyName, agencySlug, kind, status, licenceNo, note, submittedByName, submittedByEmail, documents, createdAt }
```

## What happens in the background

- Photos are processed within seconds (three sizes, watermark, duplicate check). A photo matching another agency's sends the listing to moderation.
  The original upload is stored with the content type detected from the file (JPEG, PNG or WebP), not the type the uploader sent. It is
  deleted once processed, and also when processing fails, since it can carry the camera's GPS location. A photo is marked Ready only
  after the original has been deleted; if deleting it fails, the photo stays Processing and is tried again when its claim goes stale
  (10 minutes).
- Listings expire 30 (Free) or 60 days after publishing; the agent gets a "still available?" email 5 days before with a one-click renew link.
- Instant saved searches are emailed when a matching listing first goes live; daily ones get one digest a day with new matches.
- Confirmed viewings get a reminder 24 hours before. Unpaid stay holds free their dates after 15 minutes; stays complete on check-out day.
- Host earnings (total − 10% commission) become available 24 hours after check-in and are paid out through the shared payout queue.
