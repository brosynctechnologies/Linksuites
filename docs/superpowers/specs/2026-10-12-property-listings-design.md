# Property Listing Website: design

Date: 12 Oct 2026. Product 5 of *Six SaaS Product Build Plans*, built on the shared multi-tenant core.
Scope agreed: the plan's MVP **plus phase 2** (inspections calendar, agent reviews, short-let bookings).
Out of scope: a native mobile app (the web app is fully responsive), 3D tours (a listing can hold a
tour or video link), a blog (area guides carry SEO), developer project pages, banner ads, partner leads.

## 1. Goal and positioning

Win on trust, not volume. A seeker in Lagos should be able to believe that a listing is real, still
available, posted once, by a checked agent, at a price whose full move-in cost is visible. Success means:

- Verified agents and agencies carry a badge that came from an admin review of ID and business documents.
- The same photos cannot be posted by a second agency without a moderator seeing both listings.
- No listing stays live past its expiry without the agent confirming it is still available.
- The listing page shows rent/price plus every extra cost and a total.
- Short-let guests pay the platform, so money can be held if the apartment turns out to be fake.

## 2. Tenancy model

| Who | Account | Notes |
|---|---|---|
| Agency, solo agent, landlord | Tenant with product `property` | Plans, billing, team seats and roles come from the core. Agents are team members. |
| Seeker / short-let guest | User with no tenant | Same pattern as course learners (`/api/auth/seeker/signup`, token without `tid`). |
| Moderator / super admin | Platform admin (`padm` claim, `Platform:AdminEmails`) | Shared admin area across products. |

Public search reads across tenants through `PropertySearchService` (explicit `IgnoreQueryFilters` with
`Status == Active`), as `CourseCatalogService` does. All writes go through tenant-scoped services.

## 3. Modules

Code lives under `Property/` in Domain, Application, Infrastructure and Api; the web app is
`property-web/` (Next.js 16, port 4100). Contract: `docs/PROPERTY_API.md`. Design plan: `docs/design/propertylink.md`.

| Unit | Responsibility |
|---|---|
| Listings | CRUD, add-listing wizard, costs, amenities, status machine, expiry and renew |
| Media | Photo upload, resize (thumb 400, card 960, full 1920 WebP), watermark, perceptual hash, duplicate flag |
| Search | Filters, location subtree, bounding box, ranking, map pins, SEO area pages |
| Leads | Enquiries, inspection slots and bookings, click and view stats |
| Trust | Verification requests, badges, reports, moderation queue, agent reviews |
| Seekers | Saved listings, saved searches, alerts |
| Short-lets | Availability, quotes, holds, stays, cancellation and refunds |
| Money | Plans, boosts (platform Paystack), host earnings via the shared ledger |

### Shared core changes

1. **Shared ledger.** `tutor_earnings` becomes `earnings` with `source` (`CourseOrder`, `Stay`) and
   `source_id`; `payouts` stays. `CourseRules.Balance` moves to `Payouts/LedgerRules` unchanged.
   Migration renames and backfills (`source = CourseOrder`, `source_id = order_id`). Course behaviour and
   tests must not change. The admin payout queue lists both products.
2. **Shared admin area.** Platform-admin endpoints move under `/api/admin/...` with one `platform-admin`
   policy (already exists); property adds moderation, verification, reports and locations.
3. **PostGIS.** docker-compose, the Testcontainers fixture and CI switch to `postgis/postgis:17-3.5`;
   the property migration runs `CREATE EXTENSION IF NOT EXISTS postgis`. EF uses
   `Npgsql.EntityFrameworkCore.PostgreSQL.NetTopologySuite`.
4. **Image processing.** SkiaSharp (MIT) in Infrastructure, with the Linux native assets package for Docker.

## 4. Data model

Global (not tenant-owned): `locations` (id, parent_id, kind State/City/Area, name, slug, centre point,
position), `property_types`, `amenities`, `saved_listings`, `saved_searches`.

Tenant-owned:

- `agency_profiles`: one per tenant. Name, slug, logo, phone, email, address, kind (Agency, Agent, Landlord), verified_at.
- `agent_profiles`: per listing member. user_id, display_name, slug, bio, phone, whatsapp, licence_no, verified_at, rating_avg, rating_count.
- `listings`: agent_profile_id, title, slug, description, purpose (Rent, Sale, ShortLet), property_type_id,
  location_id (area), address, point (geography Point, GiST index), price_minor, currency, price_period
  (Year, Month, Total, Night), bedrooms, bathrooms, toilets, size_sqm, furnished, tour_url, video_url, status
  (Draft, Pending, Active, Expired, Rented, Sold, Rejected, Hidden), moderation_note, published_at,
  expires_at, last_confirmed_at, featured_until, top_until, views_count. Short-let only: cleaning_fee_minor,
  min_nights, max_guests, check_in_time, check_out_time, house_rules.
- `listing_costs`: kind (AgencyFee, LegalFee, Caution, ServiceCharge, Other), label, amount_minor.
- `listing_amenities`, `listing_media` (keys for 3 sizes, position, status Processing/Ready/Failed,
  phash bigint, duplicate_of_media_id).
- `enquiries`: listing, seeker user (optional), name, phone, email, message, status (New, Replied, Closed).
- `inspection_slots`: agent's weekly windows (weekday, start, end, slot minutes). `inspections`: listing,
  seeker, agent, scheduled_at, status (Requested, Confirmed, Done, Cancelled, NoShow), notes.
- `listing_stats_daily`: listing, day, views, whatsapp_clicks, call_clicks, enquiries.
- `verification_requests`: kind, documents (private storage keys), status (Pending, Approved, Rejected), note, reviewed_by, reviewed_at.
- `listing_reports`: listing, reporter, reason (Fake, AlreadyTaken, WrongPrice, Duplicate, Scam, Other), details, status.
- `agent_reviews`: agent_profile, user, rating 1-5, comment, status (Published, Hidden), basis (Enquiry, Inspection, Stay).
- `boosts`: listing, kind (Featured, Top), starts_at, ends_at, amount_minor, payment reference, status.
- `stay_blocks`: listing, daterange (host-blocked).
- `stays`: listing, guest user, check_in, check_out (daterange), guests, nights, nightly_minor, cleaning_minor,
  total_minor, commission_minor, status (Pending, Confirmed, Completed, Cancelled, Expired), hold_expires_at,
  reference `stay_...`, refund_minor. Exclusion constraint (`btree_gist`): no two stays with status Pending or
  Confirmed on one listing may overlap, and none may overlap a `stay_block` (enforced in the service, with
  the constraint guarding stays against each other).

## 5. Key flows and rules

**Publishing.** Draft, then submit. Goes Active at once if the agency is verified, has no open reports and
no photo is flagged duplicate; otherwise Pending in the moderation queue. A moderator approves (Active) or
rejects with a note (Rejected, editable and resubmittable). Plan limit counts Active + Pending listings.

**Duplicate photos.** Each processed photo gets a 64-bit difference hash. A Hamming distance of 6 or less
to a Ready photo of **another tenant's** listing sets `duplicate_of_media_id` and forces the listing to
Pending. The queue shows both listings side by side. Same-tenant duplicates are allowed (one agency, one property).

**Expiry.** Active listings expire 30 days (Free) or 60 days (paid) after publishing or last confirmation.
An hourly job emails a "still available?" link 5 days before (one-click renew, or mark Rented/Sold), and
moves overdue listings to Expired, out of search. Renewing an Expired listing re-runs the publishing rules.

**Search.** `GET /api/listings` takes purpose, location slug (whole subtree), type, min/max price, min beds,
amenities, verified only, bbox, sort (`recommended`, `newest`, `price_asc`, `price_desc`), page.
Recommended order: Top boost active, Featured active, verified agency, newest. Returns cards and up to 500
map pins. Prices compare in minor units within one currency (NGN at launch).

**Costs.** Detail shows each cost and a total move-in (rent: price + all costs; short-let: computed per stay).
The label "inspection fee" is refused by validation. The page shows a fixed warning never to pay before inspecting.

**Leads.** Enquiries notify the agent by email (outbox) and appear in the inbox. WhatsApp and call buttons
record clicks through `POST /api/listings/{id}/events` (rate-limited, counted daily). Views are counted once
per visitor per day (hashed IP + listing in a short-lived cache).

**Inspections.** Agents set weekly slots; seekers request a free slot. The agent confirms or proposes
another time. Emails to both; reminders 24 h before. No payment.

**Saved searches and alerts.** A seeker saves the current filters with frequency Instant or Daily. When a
listing first becomes Active, instant matches get an outbox email. A daily job sends one digest of new
matches since `last_sent_at`.

**Reviews.** A seeker may review an agent once, if they enquired more than 24 h ago, had a Done inspection,
or a Completed stay. Published at once; a report hides it pending moderation. Rating updates the agent profile.

**Reports.** Any visitor may report; signed-in users once per listing. Three open reports from different
reporters move a listing to Hidden until a moderator decides.

**Verification.** Agent or agency uploads ID plus business documents (CAC, licence) to private storage.
Admin views them through short-lived signed links, approves (badge, `verified_at`) or rejects with a note.

**Short-let stays.** Guest picks dates. Quote = nights x nightly + cleaning. Booking creates a Pending stay
held 15 minutes (constraint prevents double booking), then a Paystack payment on the platform account.
Verified payment (amount and currency checked, idempotent) makes it Confirmed and records an earning:
total minus 10% commission, available 24 h after check-in. Guest cancellation more than 48 h before check-in:
full refund; later: refund minus one night. Host cancellation: full refund, and the cancellation is
recorded against the agency. A job expires unpaid holds and marks stays Completed after check-out.

**Plans and boosts** (property product, NGN): Free (3 active listings, 30-day expiry, 1 seat),
Pro ₦10,000/month (30 listings, 60-day expiry, 3 seats, stats), Agency ₦35,000/month (unlimited listings,
10 seats). Boosts: Featured 7 days ₦3,000, Featured 30 days ₦10,000, Top of search 7 days ₦5,000; paid on
the platform Paystack with reference `bst_...`, activated by the webhook or verify call.

## 6. API outline

Full contract goes in `docs/PROPERTY_API.md`. Groups:

- Public: `/api/listings`, `/api/listings/{slug}`, `/api/locations`, `/api/areas/{slug}` (guide stats),
  `/api/agents/{slug}`, `/api/agencies/{slug}`, `/api/listings/{id}/enquiries`, `/events`, `/report`,
  `/api/listings/{id}/stay-quote`.
- Seeker: `/api/auth/seeker/signup|login`, `/api/me/saved-listings`, `/api/me/saved-searches`,
  `/api/me/enquiries`, `/api/me/inspections`, `/api/me/stays`, `/api/stays` (book), `/api/stays/{id}/verify|cancel`, reviews.
- Agent studio (`product:property`): `/api/property/listings` CRUD, submit, renew, status, media upload and
  order, enquiries, inspection slots and inspections, stats, stays and blocks, earnings and payouts,
  verification, profiles, boosts.
- Admin (`platform-admin`): moderation, verification, reports, locations, reviews, payouts (shared).

## 7. Frontend (`property-web`)

Public pages render on the server with caching so they rank: home, `/rent|buy|shortlet/[...location]`
(list + MapLibre with OpenFreeMap tiles, "search this area", URL filters), listing detail, agent and agency
pages, area guides, pricing, verified-agent explainer. Seeker: saved, alerts, enquiries, inspections, trips.
Agent: dashboard, listings, 5-step wizard (basics, location pin, costs, photos, review), enquiries,
inspections calendar and slots, short-let calendar and stays, earnings and payouts, verification, team,
billing and boosts. Admin: moderation with duplicate comparison, verification, reports, locations, payouts.
A design plan following the frontend-design skill comes before any UI code.

## 8. Errors

ProblemDetails throughout. 402 for plan limits; 409 for taken dates or inspection slots ("those dates were
just booked"); 400 with field errors per wizard step; 403 for wrong tenant or missing role; 503 when
payments are not configured. Photo processing failure marks that photo Failed and blocks submit with its name.

## 9. Testing

- Unit: difference hash and Hamming distance, cost totals, stay quote and refund rules, expiry dates,
  ranking order, saved-search matching, ledger rules (moved, unchanged).
- Integration (Testcontainers `postgis/postgis`): signup and plan limit 402; publish (verified goes live,
  new goes Pending); cross-tenant duplicate photo forces Pending; search by subtree, bbox and filters;
  tenant isolation; enquiry and alert outbox; inspection booking conflict; 6 concurrent stays for the same
  dates give exactly 1; stay payment, earning, payout through the shared ledger; cancellation refunds;
  review eligibility; three reports hide a listing; boosts reorder search. All existing tests still pass.
- Browser: end-to-end check of public search, listing, enquiry, agent wizard, moderation and a stay.

## 10. Build order

1. Shared ledger generalisation and PostGIS switch (all existing tests green).
2. Domain, migration, location and type seed (Nigeria states + FCT, Lagos cities and areas).
3. Listings, media pipeline, search, publishing and moderation, expiry job.
4. Leads, inspections, saved searches and alerts, reviews, reports, verification.
5. Short-lets, boosts, plans.
6. Contract doc and design plan, then `property-web` (agent built), then end-to-end check and README/CI/launch updates.
