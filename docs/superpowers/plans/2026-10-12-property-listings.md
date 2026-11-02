# Property Listing Website Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the Property Listing Website (listings, search with map, leads, trust, inspections, reviews, short-let stays, boosts) as product `property` on the shared core, with its own web app `property-web`.

**Architecture:** A `Property/` module in each API layer, following the courses module: tenant-owned entities for agencies, tenantless seekers, cross-tenant public read service, hosted job with an advisory lock, platform Paystack with reference prefixes. The course earnings ledger becomes a shared `Payouts` module used by courses and stays. PostGIS stores listing points.

**Tech Stack:** ASP.NET Core 10, EF Core + Npgsql + NetTopologySuite, PostgreSQL 17 + PostGIS 3.5, SkiaSharp, QuestPDF (existing), xUnit v3 + Testcontainers, Next.js 16.4 + Tailwind 4 + MapLibre GL.

**Spec:** `docs/superpowers/specs/2026-10-12-property-listings-design.md`

## Global Constraints

- Product code `property`; policy `product:property`; web app `property-web` on port 4100; `App:ProductWebUrls.property = http://localhost:4100`; CORS for 4100.
- Money in minor units (kobo), currency `NGN` at launch. All `DateTimeOffset` values written to Postgres are UTC. Stay dates are `DateOnly` in `Africa/Lagos`.
- Entity ids are Guid v7 with `ValueGeneratedNever`, as elsewhere. Tenant entities derive `TenantEntity`; global ones do not.
- Errors are ProblemDetails: 402 plan limit, 409 conflict (dates/slot taken), 400 field errors, 403 wrong role/tenant, 404 unknown slug, 503 payments not configured.
- Docker image `postgis/postgis:17-3.5` everywhere (docker-compose, Testcontainers fixture).
- Plans: Free ₦0 (3 listings, 30-day expiry, 1 seat), Pro ₦10,000/month = 1_000_000 minor (30 listings, 60-day expiry, 3 seats, stats), Agency ₦35,000/month = 3_500_000 minor (unlimited, 60-day expiry, 10 seats, stats).
- Boosts: Featured 7 days 300_000; Featured 30 days 1_000_000; Top 7 days 500_000. Reference prefix `bst_`. Stay reference prefix `stay_`.
- Short-let: commission 10%; hold 15 minutes; earning available 24 h after check-in; guest cancel > 48 h before check-in = full refund, otherwise refund minus one night; host cancel = full refund.
- Duplicate photo: 64-bit difference hash, Hamming distance <= 6, other tenants only. Three open reports from distinct reporters hide a listing. Expiry warning 5 days before.
- Cost kinds: AgencyFee, LegalFee, Caution, ServiceCharge, Other. Any cost label containing "inspection" (case-insensitive) is rejected.
- Copy rules for UI: sentence case, plain verbs, no ALL-CAPS labels. Listing page always shows: "Never pay before you have inspected the property and met the agent."
- Free/open only: SkiaSharp (MIT), MapLibre GL (BSD), OpenFreeMap tiles (no key). No Google APIs.
- C# strings containing `\n` are written with the Edit/Write tools, never through shell scripts (known breakage in this repo).

## Review Focus

1. Stay dates that are invalid (check-out on or before check-in, check-in in the past, fewer than `min_nights`, more guests than `max_guests`, overlapping a host block) must give 400/409 with a field message, never a 500 or a booking. Test in Task 11.
2. Photo uploads that are not decodable images (HEIC from iPhones, PDFs renamed .jpg, 0 bytes) or larger than 15 MB must be refused at upload with a clear message, not left as Processing forever. Test in Task 6.
3. Search with an unknown location slug must return 404, and a malformed bbox (min > max, out of range, fewer than 4 numbers) must return 400, rather than silently returning all of Nigeria. Test in Task 7.
4. Renewing or resubmitting a listing when the agency is already at its plan limit (e.g. after a downgrade) must return 402, not exceed the limit. Test in Task 5.
5. A Paystack payment confirmed twice (webhook and verify racing) or with the wrong amount must not create two earnings or confirm a stay/boost. Test in Task 11 (stays) and Task 12 (boosts).

---

### Task 1: PostGIS everywhere

**Files:**
- Modify: `docker-compose.yml` (image), `api/tests/Saas.IntegrationTests/ApiFixture.cs:23` (image)
- Modify: `api/src/Saas.Infrastructure/Saas.Infrastructure.csproj` (add `Npgsql.EntityFrameworkCore.PostgreSQL.NetTopologySuite`, version matching the Npgsql EF provider)
- Modify: `api/src/Saas.Infrastructure/DependencyInjection.cs:27` (`npgsql.UseNetTopologySuite()`), `Persistence/Support.cs` design-time factory (same)
- Test: `api/tests/Saas.IntegrationTests/PlatformTests.cs` (new)

**Interfaces:**
- Produces: `NetTopologySuite.Geometries.Point` usable as an entity property mapped to `geography (Point, 4326)`.

- [ ] **Step 1: Write the failing test** `Postgis_is_available` in `PlatformTests`: `SELECT postgis_version()` via `api.WithDbAsync(db => db.Database.SqlQueryRaw<string>("SELECT postgis_version() AS \"Value\"").SingleAsync())`, assert it starts with `"3."`.
- [ ] **Step 2: Run** `cd api && dotnet test --project tests/Saas.IntegrationTests -- --filter-method "*Postgis_is_available"`. Expected: FAIL (function postgis_version does not exist).
- [ ] **Step 3: Switch images and add the package.** The test passes once the image changes (extension is created by the Task 3 migration; for this test run `CREATE EXTENSION IF NOT EXISTS postgis` inside the test first).
- [ ] **Step 4: Run the full suite** `cd api && dotnet test`. Expected: all 112 existing tests plus the new one pass.
- [ ] **Step 5: Recreate the local DB container:** `docker compose up -d` (volume kept). Expected: `saas-postgres` healthy on 55432.
- [ ] **Step 6: Commit** `git commit -m "chore: switch Postgres to the PostGIS image"`

### Task 2: Shared earnings and payouts ledger

**Files:**
- Create: `api/src/Saas.Domain/Payouts/Payouts.cs` (move `PayoutAccount`, `Payout`, `PayoutStatus` here; new `Earning`, `EarningSource`)
- Create: `api/src/Saas.Application/Payouts/LedgerRules.cs` (move `LedgerEntry`, `Ledger`, `EarningState`, `Balance` from `CourseRules`; `CourseRules` keeps forwarding members only if tests need them, otherwise update callers)
- Create: `api/src/Saas.Application/Payouts/PayoutService.cs` (tenant side: balance, request payout, list) and `PayoutAdminService.cs` (list all products, mark paid, reject)
- Modify: `Courses/CourseCore.cs`, `StudioPeopleService.cs`, `CourseAdminService.cs`, `StudioService.cs`, `Common/Abstractions.cs`, `Persistence/AppDbContext.cs`, `CourseEndpoints.cs` (delegate payout routes to the new services; route paths unchanged)
- Create: migration `SharedLedger`
- Test: existing `CourseFlowTests`, `CourseRulesTests` (moved ledger cases to `LedgerRulesTests`); new `PayoutAdminTests`

**Interfaces:**
- Produces:
  - `enum EarningSource { CourseOrder, Stay }`
  - `class Earning : TenantEntity { EarningSource Source; Guid SourceId; string Description; Guid UserId; SaleChannel? Channel; Guid? CourseId; long GrossMinor; int FeePercent; long FeeMinor; long NetMinor; DateTimeOffset AvailableAt; bool Reversed; }` — DbSet `Earnings`, table `earnings`, unique index `(Source, SourceId)`.
  - `LedgerRules.Balance(IReadOnlyList<LedgerEntry>, long requestedOrPaidMinor, long paidMinor, DateTimeOffset now) -> Ledger` (unchanged semantics); `LedgerRules.MinimumPayoutMinor = 500_000`.
  - `PayoutService.LedgerAsync(ct) -> Ledger`, `RequestAsync(long amountMinor, ct) -> PayoutDto`, `ListAsync(ct)`; `PayoutAdminService.ListAsync(PayoutStatus?, ct) -> IReadOnlyList<AdminPayoutDto>` where `AdminPayoutDto` adds `string Product` and `string BusinessName`; `MarkPaidAsync(id, reference, ct)`, `RejectAsync(id, note, ct)`.
- Consumes: nothing new.

- [ ] **Step 1: Write the failing test** `PayoutAdminTests.Admin_queue_shows_the_product_of_each_payout`: create a course payout as in `CourseFlowTests.Earnings_unlock_after...`, call `GET /api/admin/payouts?status=Requested`, assert the entry has `product == "courses"` and `businessName == "Payout Academy"`.
- [ ] **Step 2: Run it.** Expected: FAIL (no `product` property).
- [ ] **Step 3: Move types and services**, update callers, keep every route and JSON shape the same except the added admin fields.
- [ ] **Step 4: Create the migration** `dotnet ef migrations add SharedLedger -p src/Saas.Infrastructure -s src/Saas.Api`, then edit it by hand: rename table `tutor_earnings` to `earnings`, add `source` (int, default 0), rename `order_id` to `source_id`, add `description` (backfill `UPDATE earnings e SET description = c.title FROM courses c WHERE c.id = e.course_id`), make `course_id` and `channel` nullable, replace the unique index on `order_id` with `(source, source_id)`. No drop/create of the table (data must survive).
- [ ] **Step 5: Run** `cd api && dotnet test`. Expected: all pass, including every course test unchanged.
- [ ] **Step 6: Update** `docs/COURSES_API.md` admin payouts row with `product, businessName`.
- [ ] **Step 7: Commit** `git commit -m "refactor: share the earnings and payouts ledger across products"`

### Task 3: Property domain, migration and reference data

**Files:**
- Create: `api/src/Saas.Domain/Property/Property.cs` (all entities and enums in spec section 4)
- Modify: `Common/Abstractions.cs` (DbSets), `Persistence/AppDbContext.cs` (`ConfigureProperty`), `Persistence/Support.cs` (seed), `Billing/PlanService.cs` (`Products.Property = "property"`, add to `All`; `LimitKeys.Listings = "listings"`, `ListingExpiryDays = "listing_expiry_days"`, `ListingStats = "listing_stats"`)
- Create: `api/src/Saas.Infrastructure/Persistence/PropertySeed.cs` (locations, types, amenities)
- Create: migration `PropertyListings`
- Test: `api/tests/Saas.IntegrationTests/PropertySeedTests.cs`

**Interfaces:**
- Produces (names used by every later task):
  - Global: `Location { Guid? ParentId; LocationKind Kind; string Name; string Slug; Point Centre; int Position }` with `enum LocationKind { State, City, Area }`; `PropertyType { Name, Slug, Position }`; `Amenity { Name, Slug, Position }`; `SavedListing { UserId, ListingId, CreatedAt }` (key `(UserId, ListingId)`); `SavedSearch { UserId, Name, ListingFilter Filter (jsonb), AlertFrequency Frequency, DateTimeOffset? LastSentAt }`, `enum AlertFrequency { Off, Instant, Daily }`.
  - Tenant: `AgencyProfile`, `AgentProfile`, `Listing`, `ListingCost`, `ListingAmenity`, `ListingMedia`, `Enquiry`, `InspectionSlot`, `Inspection`, `ListingStatDay`, `VerificationRequest`, `ListingReport`, `AgentReview`, `Boost`, `StayBlock`, `Stay`, with fields and enums exactly as spec section 4 (C# names: `ListingPurpose { Rent, Sale, ShortLet }`, `PricePeriod { Year, Month, Total, Night }`, `ListingStatus { Draft, Pending, Active, Expired, Rented, Sold, Rejected, Hidden }`, `CostKind`, `MediaStatus { Processing, Ready, Failed }`, `EnquiryStatus`, `InspectionStatus`, `VerificationStatus`, `ReportReason`, `ReportStatus { Open, Upheld, Dismissed }`, `ReviewStatus`, `ReviewBasis`, `BoostKind { Featured, Top }`, `BoostStatus { Pending, Active, Expired }`, `StayStatus`, `AgencyKind { Agency, Agent, Landlord }`).
  - `ListingFilter` record (shared by search and saved searches): `(ListingPurpose? Purpose, string? Location, string? Type, long? MinPrice, long? MaxPrice, int? MinBeds, string[]? Amenities, bool VerifiedOnly, double[]? Bbox, string? Sort)`.
  - Indexes: GiST on `listings.point`; `(status, purpose, location_id, price_minor)`; unique `listings (tenant_id, slug)`; unique `agency_profiles.slug`, `agent_profiles.slug`, `locations.slug`; `listing_media.phash`; `listing_stats_daily (listing_id, day)` unique; `stays.reference` unique; `boosts.reference` unique.
  - Migration SQL by hand: `CREATE EXTENSION IF NOT EXISTS postgis; CREATE EXTENSION IF NOT EXISTS btree_gist;` and the stays exclusion constraint: `ALTER TABLE stays ADD CONSTRAINT stays_no_overlap EXCLUDE USING gist (listing_id WITH =, daterange(check_in, check_out, '[)') WITH &&) WHERE (status IN (0, 1));` (Pending=0, Confirmed=1).
  - Seed: 36 states + FCT; city Lagos (under Lagos State) with areas Lekki, Ikoyi, Victoria Island, Ajah, Yaba, Surulere, Ikeja, Gbagada, Magodo, Maryland, Ogudu, Ojodu, Festac, Ikorodu, Sangotedo, Chevron, Oniru, Banana Island; Abuja (FCT) and Port Harcourt (Rivers) as cities without areas. Area slugs are globally unique (`lekki-lagos`). Types: Apartment, Duplex, Terrace, Bungalow, Detached house, Self-contain, Room and parlour, Land, Shop, Office, Warehouse. Amenities: 24-hour power, Water supply, Security, Parking, Pool, Gym, Serviced, Fitted kitchen, Air conditioning, Wi-Fi, Boys' quarters, Elevator. Plans per Global Constraints.

- [ ] **Step 1: Write failing tests** `PropertySeedTests.Lagos_areas_are_seeded_under_Lagos` (`lekki-lagos` parent chain is Lagos city then Lagos State) and `Property_plans_are_seeded` (codes `free`, `pro`, `agency`; `pro` limits `listings = 30`, `listing_expiry_days = 60`).
- [ ] **Step 2: Run them.** Expected: FAIL.
- [ ] **Step 3: Add entities, model config, seed, migration** (`dotnet ef migrations add PropertyListings`, then add the SQL above in `Up`).
- [ ] **Step 4: Run** `cd api && dotnet test`. Expected: all pass.
- [ ] **Step 5: Commit** `git commit -m "feat(property): domain model, migration and Nigeria location seed"`

### Task 4: Pure rules (unit-tested)

**Files:**
- Create: `api/src/Saas.Application/Property/PropertyRules.cs`, `api/src/Saas.Application/Property/ImageHash.cs`
- Test: `api/tests/Saas.UnitTests/PropertyRulesTests.cs`, `ImageHashTests.cs`

**Interfaces:**
- Produces:
  - `PropertyRules.MoveInTotal(long priceMinor, IEnumerable<long> costsMinor) -> long`
  - `PropertyRules.Quote(long nightlyMinor, long cleaningMinor, DateOnly checkIn, DateOnly checkOut) -> StayQuote(int Nights, long SubtotalMinor, long CleaningMinor, long TotalMinor)`
  - `PropertyRules.Commission(long totalMinor) -> long` (10%, rounded half-away-from-zero), `CommissionPercent = 10`
  - `PropertyRules.GuestRefund(Stay stay, DateTimeOffset now, TimeZoneInfo zone) -> long` and `HostCancelRefund(Stay) -> long`
  - `PropertyRules.ExpiresAt(DateTimeOffset from, int days) -> DateTimeOffset`; `WarnAt(expiresAt) = expiresAt - 5 days`
  - `PropertyRules.RankKey(ListingRankInput, DateTimeOffset now) -> (int Top, int Featured, int Verified, long Fresh)` ascending sort key; `ListingRankInput(DateTimeOffset? TopUntil, DateTimeOffset? FeaturedUntil, bool Verified, DateTimeOffset PublishedAt)`
  - `PropertyRules.Matches(ListingFilter f, ListingMatchInput l, IReadOnlySet<Guid> locationSubtree) -> bool`
  - `PropertyRules.ValidateCostLabel(string) -> string?` (error message or null)
  - `ImageHash.DHash(ReadOnlySpan<byte> gray9x8) -> ulong`; `ImageHash.Distance(ulong, ulong) -> int`; `ImageHash.DuplicateThreshold = 6`

- [ ] **Step 1: Write the failing tests:**
  - `Quote_counts_nights`: nightly 5_000_000, cleaning 1_000_000, 10–13 Oct → `(3, 15_000_000, 1_000_000, 16_000_000)`.
  - `Guest_refund_is_full_more_than_48_hours_before` (total 16_000_000 → 16_000_000) and `Guest_refund_keeps_one_night_inside_48_hours` (→ 11_000_000); `Host_cancel_refunds_everything`.
  - `Commission_is_ten_percent`: 16_000_000 → 1_600_000.
  - `Rank_puts_top_then_featured_then_verified_then_newest` (four inputs sorted in that order; expired boosts count as none).
  - `Matches_respects_subtree_price_beds_and_amenities`.
  - `Cost_label_mentioning_inspection_is_rejected` ("Inspection fee", "INSPECTION" → message; "Agency fee" → null).
  - `DHash_of_same_gradient_is_identical_and_inverted_is_far` (distance 0 vs >= 32); `Distance_counts_bits` (0b1011 vs 0 → 3).
- [ ] **Step 2: Run** `cd api && dotnet test --project tests/Saas.UnitTests`. Expected: FAIL (types missing).
- [ ] **Step 3: Implement.** DHash: compare each pixel to its right neighbour across 8 rows of 9 grey pixels, bit set when left > right, row-major.
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "feat(property): pricing, refund, ranking and image-hash rules"`

### Task 5: Accounts and listings studio

**Files:**
- Create: `Saas.Application/Property/PropertyDtos.cs`, `PropertyCore.cs` (provisioner, usage, `AgencyAsync`, `PublishRulesAsync`), `ListingStudioService.cs`
- Create: `Saas.Api/Endpoints/PropertyEndpoints.cs` (`MapPropertyEndpoints`), register in `Program.cs` with policy `PropertyProduct`
- Modify: `Infrastructure/Auth/AuthService.cs` (seeker signup/login reuse the learner methods; routes `/api/auth/seeker/signup|login` call the same code), `Application/DependencyInjection.cs`, `appsettings.json` (ProductWebUrls, CORS 4100)
- Test: `api/tests/Saas.IntegrationTests/PropertyListingTests.cs`

**Interfaces:**
- Consumes: `IProductProvisioner`, `IUsageProvider`, `PlanService` limits, `PropertyRules.ValidateCostLabel`, `PropertyRules.ExpiresAt`.
- Produces:
  - `PropertyProvisioner` creates `AgencyProfile` (name = business name, slug unique, kind from `SignupRequest` extra field `agencyKind`, default Agency) and an `AgentProfile` for the owner.
  - Routes (all under `/api/property`, policy `product:property`, writes `WritableBy(Owner, Admin, Staff)`): `GET/POST /listings`, `GET/PUT/DELETE /listings/{id}`, `POST /listings/{id}/submit`, `POST /listings/{id}/renew`, `POST /listings/{id}/status` `{ status: Rented|Sold }`, `GET/PUT /profile/agency`, `GET/PUT /profile/agent`.
  - `SaveListingRequest(string Title, string Description, ListingPurpose Purpose, Guid PropertyTypeId, Guid LocationId, string Address, double Lat, double Lng, long PriceMinor, PricePeriod PricePeriod, int Bedrooms, int Bathrooms, int Toilets, int? SizeSqm, bool Furnished, string? TourUrl, string? VideoUrl, IReadOnlyList<CostInput> Costs, IReadOnlyList<Guid> AmenityIds, ShortLetInput? ShortLet)`; `CostInput(CostKind Kind, string Label, long AmountMinor)`; `ShortLetInput(long CleaningFeeMinor, int MinNights, int MaxGuests, string CheckInTime, string CheckOutTime, string? HouseRules)`.
  - Validation: lat 4–14, lng 2–15 (Nigeria bounds), location must be an Area, ShortLet purpose requires `ShortLet` input and `PricePeriod.Night`.
  - `PropertyCore.SubmitAsync(Listing)`: plan limit (Active + Pending count vs `listings`) → 402; any media not Ready → 400 naming the photo; Active if agency verified, no open reports, no duplicate media; else Pending. Sets `PublishedAt`, `ExpiresAt` from plan `listing_expiry_days`.

- [ ] **Step 1: Write failing tests:**
  - `Unverified_agency_listing_goes_to_moderation` (submit → `status == "Pending"`).
  - `Verified_agency_listing_goes_live_with_plan_expiry` (set `verified_at` via `WithDbAsync`; Free → `expiresAt ≈ now + 30 days`).
  - `Free_plan_allows_three_listings` (4th submit → 402).
  - `Renewing_at_the_plan_limit_is_refused` (3 Active, 1 Expired, renew → 402) — Review Focus 4.
  - `Inspection_fee_cost_is_rejected` (400 with field `costs[0].label`).
  - `Another_agency_cannot_read_my_listing` (404).
  - `Seeker_token_cannot_use_the_studio` (403).
- [ ] **Step 2: Run** `dotnet test --project tests/Saas.IntegrationTests -- --filter-class "*PropertyListingTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** services, validators, endpoints, provisioner and usage (`Listings used / limit`, `Team seats`).
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "feat(property): agency signup, listing studio and publishing rules"`

### Task 6: Photo pipeline and duplicate detection

**Files:**
- Modify: `Saas.Infrastructure.csproj` (`SkiaSharp`, `SkiaSharp.NativeAssets.Linux.NoDependencies`)
- Create: `Saas.Infrastructure/Property/PhotoProcessor.cs` (hosted service, same claim pattern as `VideoProcessor`; `ProcessNextAsync(ct) -> Task<bool>` public), `Saas.Application/Property/ListingMediaService.cs`
- Modify: `PropertyEndpoints.cs`, `Infrastructure/DependencyInjection.cs` (`Photos:ProcessorEnabled`, default true), test fixture sets `Photos:ProcessorEnabled=false`
- Test: `PropertyMediaTests.cs`

**Interfaces:**
- Produces: `POST /api/property/listings/{id}/media` (multipart `file`, ≤15 MB, JPEG/PNG/WebP sniffed by magic bytes, max 30 per listing) → `ListingMediaDto(Guid Id, string? ThumbUrl, string? CardUrl, string? FullUrl, MediaStatus Status, int Position, Guid? DuplicateOfListingId, string? DuplicateOfAgency)`; `PUT /media/order` `{ ids: [] }`; `DELETE /media/{id}`.
- Processing: decode with `SKBitmap.Decode`; failure → `Failed`. Resize to widths 400/960/1920 (no upscaling), encode WebP q80, draw the agency name watermark bottom-right at 4% of width with 40% white opacity on the 960 and 1920 sizes. Hash: resize to 9x8 greyscale, `ImageHash.DHash`. Compare with `Ready` media of other tenants via `IgnoreQueryFilters` where `bit_count(phash # @hash) <= 6` (raw SQL, `phash` stored as bigint). On a match set `DuplicateOfMediaId` and, if the listing is Active, move it to Pending with note "Photo matches another agency's listing".

- [ ] **Step 1: Write failing tests:**
  - `Photos_are_resized_and_hashed` (upload a 1200x800 generated PNG, `ProcessNextAsync`, status Ready, three URLs, card is WebP 960 wide).
  - `Same_photo_from_another_agency_is_flagged_and_held_for_review` (agency B uploads identical bytes → `duplicateOfListingId` = A's listing; B's submit → Pending).
  - `Same_photo_twice_in_one_agency_is_allowed`.
  - `Non_images_and_oversized_files_are_refused` (PDF bytes named .jpg → 400 "Upload a JPEG, PNG or WebP photo"; 16 MB → 400; 0 bytes → 400) — Review Focus 2.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "feat(property): photo resizing, watermark and duplicate detection"`

### Task 7: Public search, listing pages, agents, areas, stats

**Files:**
- Create: `Saas.Application/Property/PropertySearchService.cs`, `ListingEventsService.cs`
- Modify: `PropertyEndpoints.cs`
- Test: `PropertySearchTests.cs`

**Interfaces:**
- Consumes: `ListingFilter`, `PropertyRules.RankKey` ordering (implemented in SQL as `ORDER BY (top_until > now) DESC, (featured_until > now) DESC, agency verified DESC, published_at DESC`).
- Produces (anonymous):
  - `GET /api/listings?purpose=&location=&type=&minPrice=&maxPrice=&beds=&amenities=a,b&verified=&bbox=w,s,e,n&sort=&page=` → `{ items: Paged<ListingCardDto>, pins: [{ id, lat, lng, priceMinor }] (≤500), location: LocationDto? }`.
  - `GET /api/listings/{agencySlug}/{listingSlug}` → `ListingDetailDto` (gallery of Ready media, costs, `moveInTotalMinor`, amenities, agent card with `verified`, phone, whatsapp, agency, rating; short-let fields; `similar` 4 cards).
  - `GET /api/locations?parent=` (tree level), `GET /api/areas/{slug}` → `{ location, activeCount, medianRentMinor by bedrooms (Year period only), medianSaleMinor, latest 8 cards }`.
  - `GET /api/agents/{slug}`, `GET /api/agencies/{slug}` (profile + active listings + published reviews).
  - `POST /api/listings/{id}/events` `{ kind: View|WhatsApp|Call }` → 204; views deduplicated per hashed IP+listing per UTC day using `IMemoryCache`; rate limit `PublicWrites`.
  - Studio: `GET /api/property/stats?days=30` → per listing totals (Pro/Agency only, else 402).

- [ ] **Step 1: Write failing tests:**
  - `Search_by_city_includes_its_areas` (listings in Lekki and Yaba, `location=lagos` → both; `location=lekki-lagos` → one).
  - `Bbox_limits_results_to_the_map` (Lekki point inside, Yaba outside).
  - `Unknown_location_is_404_and_bad_bbox_is_400` (`location=atlantis` → 404; `bbox=5,6,3,4` and `bbox=1,2,3` → 400) — Review Focus 3.
  - `Only_active_listings_are_public` (Pending/Expired absent from search and detail 404).
  - `Filters_combine` (purpose, price range, beds, amenity).
  - `Views_are_counted_once_per_visitor_per_day`.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement** (bbox via `ST_MakeEnvelope(w,s,e,n,4326)` and `ST_Intersects` through NTS `Geometry.Intersects` translation; subtree via a recursive CTE or preloaded location tree cached 10 minutes).
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "feat(property): public search, map, listing, agent and area pages"`

### Task 8: Trust — moderation, reports, verification, admin

**Files:**
- Create: `Saas.Application/Property/PropertyAdminService.cs`, `VerificationService.cs`, `ReportService.cs`
- Modify: `PropertyEndpoints.cs`, `Notifications.cs` (templates `listing.approved`, `listing.rejected`, `verification.approved`, `verification.rejected`, `listing.hidden`)
- Test: `PropertyTrustTests.cs`

**Interfaces:**
- Produces:
  - `POST /api/listings/{id}/report` `{ reason, details }` (anonymous allowed; signed-in once per listing → 409 on repeat). Three Open reports with distinct reporters (user id, or hashed IP for anonymous) → listing Hidden + owner email.
  - Studio: `GET/POST /api/property/verification` (multipart: `idDocument`, `businessDocument` optional; stored under `private/verification/{tenant}/...`).
  - Admin (`platform-admin`): `GET /api/admin/property/moderation` (Pending + Hidden, with duplicate pairs), `POST /api/admin/property/listings/{id}/decision` `{ approve, note }` (reject requires note), `GET /api/admin/property/verifications`, `GET .../verifications/{id}/documents/{n}` → 302 to a 10-minute signed URL (`IFileStorage.TemporaryUrl` or the media signer for local storage), `POST .../verifications/{id}/decision`, `GET/POST /api/admin/property/reports/{id}/decision` `{ uphold }`, `GET/POST/PUT /api/admin/locations`.
  - Approving verification sets `verified_at` on agency and owner agent profile.
- [ ] **Step 1: Write failing tests:** `Three_reports_hide_a_listing`, `Same_user_cannot_report_twice`, `Admin_approves_pending_listing`, `Reject_needs_a_note`, `Verification_documents_are_admin_only` (agent B and seeker get 403/404 on the document URL), `Approved_verification_adds_the_badge` (search card `verified == true`).
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "feat(property): moderation, reports and agent verification"`

### Task 9: Leads, inspections, saved searches, reviews

**Files:**
- Create: `Saas.Application/Property/LeadService.cs`, `InspectionService.cs`, `SeekerService.cs`, `AgentReviewService.cs`
- Modify: `PropertyEndpoints.cs`, `Notifications.cs` (`enquiry.new`, `inspection.requested`, `inspection.confirmed`, `inspection.reminder`, `search.alert`)
- Test: `PropertyLeadTests.cs`

**Interfaces:**
- Produces:
  - `POST /api/listings/{id}/enquiries` `{ name, phone, email, message }` (anonymous or seeker; rate limited) → outbox email to agent. Studio: `GET /api/property/enquiries?status=`, `POST /api/property/enquiries/{id}/status`.
  - Studio slots: `GET/PUT /api/property/inspection-slots` (`[{ weekday, start, end, slotMinutes }]` per agent). Seeker: `GET /api/listings/{id}/inspection-times?from=` (next 14 days, Africa/Lagos, minus booked), `POST /api/listings/{id}/inspections` `{ at, notes }` (seeker only; taken → 409). Studio: `GET /api/property/inspections?from=&to=`, `POST /api/property/inspections/{id}/status` `{ status, proposedAt? }`. Seeker: `GET /api/me/inspections`, `POST /api/me/inspections/{id}/cancel`.
  - Seeker: `GET/POST/DELETE /api/me/saved-listings/{listingId}`, `GET/POST/PUT/DELETE /api/me/saved-searches` (`{ name, filter: ListingFilter, frequency }`), `GET /api/me/enquiries`.
  - `PropertyCore.OnListingActivatedAsync(Listing)` (called once, when `PublishedAt` is first set): Instant saved searches that `PropertyRules.Matches` get a `search.alert` email.
  - Reviews: `POST /api/agents/{slug}/reviews` `{ rating, comment }` (eligibility per spec; ineligible → 403 "You can review an agent after you've enquired, inspected or stayed"); `POST /api/reviews/{id}/report`; admin hide/restore.
- [ ] **Step 1: Write failing tests:** `Enquiry_reaches_the_agent_inbox_and_email_outbox`, `Inspection_slot_cannot_be_double_booked` (second → 409), `Instant_saved_search_is_emailed_when_a_match_goes_live` (non-match gets nothing), `Review_requires_an_earlier_enquiry` (fresh seeker 403; enquiry backdated 2 days → 200; second review → 409), `Rating_updates_the_agent_profile`.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "feat(property): enquiries, inspections, saved searches and reviews"`

### Task 10: Property jobs

**Files:**
- Create: `Saas.Infrastructure/Property/PropertyJobsHostedService.cs` (advisory lock key `0x1_1A5_0004`, every 2 minutes; hourly and daily tasks gated by last-run timestamps kept in memory plus idempotent queries)
- Modify: `Infrastructure/DependencyInjection.cs` (`Jobs:PropertyEnabled`, default true; fixture sets false), `Notifications.cs` (`listing.expiring`, `listing.expired`, `search.digest`)
- Test: `PropertyJobTests.cs`

**Interfaces:**
- Produces `PropertyJobs` scoped class with public methods (tests call them directly): `WarnExpiringAsync(now, ct)` (Active, `expires_at - 5d <= now`, not yet warned → email with renew link; set `ExpiryWarnedAt`), `ExpireAsync(now, ct)`, `SendDailyDigestsAsync(now, ct)` (Daily searches, matches published since `LastSentAt`, ≤10 per email, skip when none), `ExpireStayHoldsAsync(now, ct)`, `CompleteStaysAsync(now, ct)`, `RemindInspectionsAsync(now, ct)` (24 h before, once), `ExpireBoostsAsync(now, ct)`. Add `DateTimeOffset? ExpiryWarnedAt` and `bool ReminderSent` on Inspection in Task 3's model if missing (migration `PropertyJobs`).
- [ ] **Step 1: Write failing tests:** `Listing_is_warned_once_then_expires`, `Renew_link_restores_an_expired_listing` (renews via public token route `POST /api/listings/renew/{token}` signed with the media signer — add the route here), `Daily_digest_sends_only_new_matches`.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "feat(property): expiry, digests and reminder jobs"`

### Task 11: Short-let stays

**Files:**
- Create: `Saas.Application/Property/StayService.cs` (guest), `HostStayService.cs` (studio)
- Modify: `PropertyEndpoints.cs`, `CoreEndpoints.cs` webhook (references starting `stay_` → `StayService.ConfirmAsync`), `Notifications.cs` (`stay.confirmed`, `stay.new`, `stay.cancelled`)
- Test: `PropertyStayTests.cs`

**Interfaces:**
- Consumes: `PropertyRules.Quote/Commission/GuestRefund/HostCancelRefund`, `IPaymentGateway` (`InitializeAsync`, `VerifyAsync`, `RefundAsync`), `Earning` with `Source = Stay`, `IDbErrors` for the exclusion violation.
- Produces:
  - `GET /api/listings/{id}/availability?from=&to=` → booked and blocked date ranges.
  - `POST /api/listings/{id}/stay-quote` `{ checkIn, checkOut, guests }` → `StayQuoteDto(nights, subtotalMinor, cleaningMinor, totalMinor)`.
  - `POST /api/stays` `{ listingId, checkIn, checkOut, guests }` (seeker) → `{ stayId, authorizationUrl, totalMinor, holdExpiresAt }`; 409 "Those dates were just booked"; 503 if gateway not configured.
  - `POST /api/stays/{id}/verify` `{ reference }`; `POST /api/stays/{id}/cancel`; `GET /api/me/stays`.
  - `StayService.ConfirmAsync(reference, ct)`: idempotent; amount and currency must match; Confirmed; `Earning { Source = Stay, SourceId = stay.Id, GrossMinor = total, FeePercent = 10, FeeMinor = commission, NetMinor = total - commission, AvailableAt = check-in 00:00 Africa/Lagos + 24h }`.
  - Studio: `GET /api/property/stays?from=&to=`, `POST /api/property/stays/{id}/cancel` (full refund), `GET/POST/DELETE /api/property/listings/{id}/blocks`, earnings and payouts via `PayoutService` at `/api/property/earnings` and `/api/property/payouts`, payout account at `/api/property/payout-account`.
  - Cancellation reverses the earning and records `RefundMinor`; refunds go through `IPaymentGateway.RefundAsync`. A host cancellation also increments `AgencyProfile.HostCancellations` (int, added in this task's migration `PropertyStays`), shown to admins in moderation.
- [ ] **Step 1: Write failing tests:**
  - `Six_guests_racing_for_the_same_dates_get_one_stay` (parallel `POST /api/stays`, exactly 1 × 200, 5 × 409).
  - `Invalid_stay_dates_are_refused` (out ≤ in → 400; check-in yesterday → 400; nights < min → 400; guests > max → 400; overlaps host block → 409) — Review Focus 1.
  - `Paid_stay_confirms_and_records_host_earning` (10% fee, available 24 h after check-in).
  - `Confirming_twice_or_with_wrong_amount_is_safe` (verify + webhook → one earning; wrong amount → stays Pending) — Review Focus 5.
  - `Guest_cancel_inside_48_hours_keeps_one_night`, `Host_cancel_refunds_in_full`.
  - `Unpaid_hold_expires_and_frees_the_dates` (via `PropertyJobs.ExpireStayHoldsAsync`).
  - `Host_payout_appears_in_shared_admin_queue_as_property`.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "feat(property): short-let stays with platform payments and host payouts"`

### Task 12: Boosts

**Files:**
- Create: `Saas.Application/Property/BoostService.cs`
- Modify: `PropertyEndpoints.cs`, `CoreEndpoints.cs` webhook (`bst_` → `BoostService.ConfirmAsync`)
- Test: `PropertyBoostTests.cs`

**Interfaces:**
- Produces: `GET /api/property/boost-options` → `[{ code: "featured-7"|"featured-30"|"top-7", kind, days, priceMinor }]`; `POST /api/property/listings/{id}/boosts` `{ code }` (listing must be Active; Owner/Admin) → `{ boostId, authorizationUrl }`; `POST /api/property/boosts/{id}/verify` `{ reference }`; `GET /api/property/boosts`. Confirm: idempotent, amount check, sets `Listing.FeaturedUntil`/`TopUntil = max(current, now) + days`.
- [ ] **Step 1: Write failing tests:** `Top_boost_moves_listing_to_the_top_of_search`, `Boost_confirm_is_idempotent_and_checks_amount` — Review Focus 5, `Expired_boost_stops_ranking` (via `ExpireBoostsAsync`).
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** the whole suite `cd api && dotnet test`. Expected: all pass.
- [ ] **Step 5: Commit** `git commit -m "feat(property): paid boosts"`

### Task 13: Contract, design plan and property-web

**Files:**
- Create: `docs/PROPERTY_API.md` (every route above with request/response shapes, generated from the DTOs), `docs/design/propertylink.md` (frontend-design skill: palette, type, layout, the one memorable element, written and self-reviewed before code)
- Create: `property-web/` (Next.js 16.4, cacheComponents, partialPrefetching, `experimental.instantInsights.validationLevel = "manual-warning"`, Tailwind 4, `maplibre-gl`, port 4100 for dev and start), built by a subagent from the two docs
- Modify: `.claude/launch.json`, `.github/workflows/ci.yml` (property-web job), `README.md` (status row, layout, run, config, "How property trust works"), `api/src/Saas.Api/appsettings.json`

**Interfaces:**
- Consumes: `docs/PROPERTY_API.md` only. Pages per spec section 7. Public listing, search, agent, agency and area pages are Server Components fetching the API with `use cache` + `cacheLife("minutes")`; signed-in areas are client-guarded like LearnLink (`propertylink.auth` storage key, tenant nullable).
- [ ] **Step 1:** Write `docs/PROPERTY_API.md` and `docs/design/propertylink.md`.
- [ ] **Step 2:** Dispatch the frontend subagent with both docs, the LearnLink structure as reference, and these rules: no mock data, no `taskkill /IM node.exe`, lint and build must pass.
- [ ] **Step 3: Verify** `cd property-web && npm run lint && npm run build`. Expected: 0 errors, 0 warnings.
- [ ] **Step 4: Commit** `git commit -m "feat(property): PropertyLink web app"`

### Task 14: End-to-end check

- [ ] **Step 1:** Start `api` and `property-web` via the Browser pane, seed one verified agency with 6 Lagos listings (one short-let) via a script in the scratchpad (dev data only, clearly labelled demo agency).
- [ ] **Step 2:** In the browser: home search → map results → "search this area" → listing detail (cost total, warning line, WhatsApp link) → enquiry → seeker signup → save search → agent studio wizard creates a listing with photos → submit → admin moderation (with `Platform:AdminEmails` set in Development user secrets) → short-let quote and the 503 message without Paystack. Check phone width (375px) for search and listing pages.
- [ ] **Step 3:** Fix what breaks, re-run `cd api && dotnet test` and `npm run lint && npm run build`.
- [ ] **Step 4: Commit** `git commit -m "fix(property): end-to-end fixes"`
