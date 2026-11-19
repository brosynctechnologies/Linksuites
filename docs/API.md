# API contract — shared core + WhatsApp Order System

Base URL: `http://localhost:5080` in development. All routes are under `/api`.
JSON is camelCase. Enums are strings. Money is always an integer in **minor units**
(kobo/cents) in fields ending `Minor`; the client formats it (`priceMinor / 100`).
The server always recomputes prices; the client never sends prices for orders.

Errors are RFC 7807 ProblemDetails:
`{ "title": "...", "status": 400, "detail": "...", "errors": { "field": ["msg"] }, "code": "plan_limit" }`
`401` = access token missing/expired (call refresh), `402` with `code: "plan_limit"` = upgrade needed,
`404` not found, `409` conflict (e.g. slug taken, store closed).

Authenticated routes need `Authorization: Bearer <accessToken>`. The token carries the tenant,
so there is no tenant id in URLs.

## Auth

| Method | Path | Body | Returns |
|---|---|---|---|
| POST | `/api/auth/signup` | `{ fullName, email, password, storeName, whatsappNumber, country, product: "whatsapp-orders" }` (`country` = ISO code, e.g. `"NG"`) | `AuthResponse` |
| POST | `/api/auth/login` | `{ email, password, product: "whatsapp-orders" }` | `AuthResponse` |
| POST | `/api/auth/refresh` | `{ refreshToken }` | `AuthResponse` |
| POST | `/api/auth/logout` | `{ refreshToken }` | 204 |
| POST | `/api/auth/otp/request` | `{ email }` | 204 (emails a 6-digit code) |
| POST | `/api/auth/otp/verify` | `{ email, code, product: "whatsapp-orders" }` | `AuthResponse` |
| GET | `/api/me` | — | `{ user, tenant }` |
| POST | `/api/auth/password/forgot` | `{ email, product? }` | 204 whether or not the account exists (emails a reset link if it does); 400 on `email` if missing |
| POST | `/api/auth/password/reset` | `{ email, token, newPassword }` | 204; 400 on `email`, `token` or `newPassword` if missing; 400 on `token` if invalid or expired |
| POST | `/api/auth/password/change` (auth) | `{ currentPassword, newPassword, refreshToken? }` | 204; 400 on `currentPassword` or `newPassword` if missing; 400 on `currentPassword` if wrong |

| GET | `/api/me/businesses` (auth) | — | `[{ tenantId, name, role, current }]`: the user's active businesses of the token's product |
| POST | `/api/auth/switch` (auth) | `{ tenantId }` | `AuthResponse` for that business (403 if not a member) |
| POST | `/api/me/businesses` (Owner) | `{ name, product?, whatsappNumber?, country?, slug?, accountKind? }` | `AuthResponse` for the new business |

`AuthResponse = { accessToken, refreshToken, expiresAt, user: { id, fullName, email }, tenant: { id, name, slug, role, product } }`

### Several businesses under one sign-in (Wave 3)

`POST /api/me/businesses` starts another business for the signed-in owner (Owner of the current business; 403 otherwise) without a new
account. `product` defaults to the current business's product (any product key works, so an owner can also start, say, an InvoiceLink
business from OrderLink); `country` defaults to the current business's. It is set up exactly like that product's sign-up: same
provisioning (OrderLink needs `whatsappNumber`; only SchoolLink may pick `slug`, others get one from the name; PropertyLink reads
`accountKind`), the owner's membership and the welcome email. It starts on the product's default (free) plan: plans and their limits
belong to each business. The answer is a session for the new business. At most 10 owned businesses per product per account (400).
The apps show it as "Add another store" (OrderLink, More) and "Add another business" (InvoiceLink, BookLink, LearnLink, PropertyLink,
More); another product is reached through the hub with the same email and password.
One email can own a business in several products; `product` picks which one a token is for. Tokens only work on their own product's routes (`403` otherwise).
Access tokens last 15 minutes; refresh tokens 30 days and rotate on every refresh.

### Passwords

The same routes serve every product and the admin console; full rules are in `docs/PLATFORM_API.md` (Passwords).
- The reset email links to `{web}/reset-password#email=…&token=…`. The values are in the URL fragment, so they never reach a server
  log; the page reads them from `location.hash`. `product` (a product key, or `"admin"`) picks the app; without it the link goes to
  the app of the user's first business.
- The link works for 10 minutes, once. At most 3 reset emails per address per hour.
- A reset or change signs the user out everywhere: every refresh token is revoked (a change keeps the `refreshToken` sent with it, so
  the current app stays signed in) and admin-console sessions end. Access tokens already issued keep working until they expire
  (15 minutes). The user is emailed that their password changed.
- A missing field is a 400 with a field error (`email` "Enter your email address.", `token`, `newPassword` "Enter a new password.",
  `currentPassword` "Enter your current password."), not a 500.
- Each web app has "Forgot password?" on its sign-in page, `/forgot-password` and `/reset-password` pages, and "Change password"
  in account settings. Learners change theirs at course-web `/learn/account`, seekers at property-web `/me/account`.

## Store settings (auth)

| Method | Path | Body | Returns |
|---|---|---|---|
| GET | `/api/store` | — | `StoreDto` |
| PUT | `/api/store` | `{ name, slug, description, whatsappNumber, currency, themeColor, isOpen, openingHours }` | `StoreDto` |
| POST | `/api/store/logo` | multipart `file` (jpeg/png/webp ≤ 5 MB) | `StoreDto` |
| POST | `/api/store/cover` | multipart `file` | `StoreDto` |

`StoreDto = { id, name, slug, description, logoUrl, coverUrl, whatsappNumber, currency, themeColor, isOpen, openingHours, country, storeUrl }`
`whatsappNumber` is stored in international digits (e.g. `2348031234567`); local input like `0803 123 4567` is accepted and normalised.

### Custom domain (Wave 3; plans with `custom_domain`, OrderLink Pro)

| Method | Path | Body | Returns |
|---|---|---|---|
| GET | `/api/store/domain` | — | `CustomDomainDto` |
| PUT | `/api/store/domain` (Owner) | `{ domain }` | `CustomDomainDto`; 402 if the plan doesn't include it; 400 on `domain` if malformed, a LinkSuite or forbidden domain, or used by another store |
| POST | `/api/store/domain/check` (Owner, Admin) | — | `CustomDomainDto` after a DNS check now |
| DELETE | `/api/store/domain` (Owner) | — | 204 |

`CustomDomainDto = { domain, status, error, checkedAt, verifiedAt, liveAt, included, served, dns: { type, name, value }, target, apexIp,
verification: { type, name, value }, releasesAt }`
- `domain` is stored bare and lower case (`https://Shop.Example.com/` becomes `shop.example.com`). Refused: anything that isn't a host
  name with a letter TLD (IPs, wildcards, one-label names); anything equal to, under, or a parent of `CustomDomains:Reserved` (default
  `brosync.ng`), the target host, or the apps' own hosts ("That is a LinkSuite address"); and the same for `CustomDomains:Forbidden`
  (default empty; the other sites served by the same nginx, e.g. Health Tutor's hosts; an entry may hold several comma-separated names,
  env `CUSTOM_DOMAINS_FORBIDDEN`).
- The owner adds two records. `dns` points the domain here: a subdomain gets `CNAME <name> → CustomDomains:Target`; an apex domain
  (`example.com`, `example.com.ng`) gets `A @ → CustomDomains:ApexIp`. `verification` proves the domain is theirs:
  `TXT _linksuite-verify.<name>` (apex: `_linksuite-verify`) `→ <token>`, a random 32-hex code made when the domain is set (kept when the
  same domain is set again, new for a different domain). Names are relative, as DNS panels ask for them.
- `status`: `PendingDns` (a record missing) → `Verified` (it resolves to the target's addresses or the apex IP AND the TXT record holds
  the token; pointing here alone is never enough, as every site on the server points here) → `Live` (the host script issued the
  certificate and wrote the nginx site) / `Failed` (points elsewhere, the certificate failed, or the host already serves the name;
  `error` says why). The API checks when the domain is set, on "Check again", and in a background job (pending every
  `CustomDomains:CheckMinutes`, failed hourly, verified and live daily). A failed lookup never changes the status; a live domain only
  fails when the DNS clearly says otherwise (points elsewhere, or a TXT record there with another value), never for a missing answer.
- `releasesAt`: for a claim that is `PendingDns` or `Failed`, when it is released. The background job clears a claim unverified for
  7 days (counted from when it was set, or from when a verified domain stopped verifying) and emails the owner
  (`store.domain_released`), so a claim nobody can verify never blocks the domain's real owner. Null once verified.
- `included`: the current plan includes custom domains. `served`: customers reach the store on it now. When the plan lapses the
  domain is kept but not served (back to the normal store link) and the host script removes its site; it comes back with the plan.

### Host endpoints (deploy/custom-domains.sh only)

Header `X-Host-Secret: <CustomDomains:HostSecret>` (env `CUSTOM_DOMAINS_HOST_SECRET`); 401 without it, or when no secret is configured.
The script passes the header to curl from a 600-permission temp file (`--config`), never as a command-line argument (visible in `ps`).

| Method | Path | Body | Returns |
|---|---|---|---|
| GET | `/api/internal/custom-domains` | — | `{ domains: [{ domain, status }] }`: Live domains, and Verified ones needing a certificate (not within an hour of a certificate failure), of active OrderLink businesses whose plan includes custom domains. `?format=text`: `<domain> <status>` lines closed by `# end` |
| POST | `/api/internal/custom-domains/report` | `{ domain, ok, error? }` | 204; `ok` makes a Verified domain Live; `ok: false` marks it Failed with the error. 404 for an unknown domain |

## Catalogue (auth)

| Method | Path | Body | Returns |
|---|---|---|---|
| GET | `/api/categories` | — | `CategoryDto[]` |
| POST / PUT `{id}` | `/api/categories` | `{ name, position }` | `CategoryDto` |
| DELETE | `/api/categories/{id}` | — | 204 (products become uncategorised) |
| GET | `/api/products?q=&categoryId=` | — | `ProductDto[]` |
| GET | `/api/products/{id}` | — | `ProductDto` |
| POST / PUT `{id}` | `/api/products` | `SaveProductRequest` | `ProductDto` (POST may return 402 plan_limit) |
| DELETE | `/api/products/{id}` | — | 204 |
| POST | `/api/products/{id}/images` | multipart `file` | `ProductDto` |
| DELETE | `/api/products/{id}/images/{imageId}` | — | `ProductDto` |

`CategoryDto = { id, name, position, productCount }`
`ProductDto = { id, categoryId, categoryName, name, slug, description, priceMinor, compareAtPriceMinor, inStock, stockQty, isActive, position, images: [{ id, url, position }], options: [{ name, values: string[] }], variants: [VariantDto] }`
`VariantDto = { id, optionValues: string[], title, priceMinor, inStock, sku }` — `priceMinor` null means "use product price"; `title` is e.g. `"Large / Red"`.
`SaveProductRequest = { name, description, categoryId, priceMinor, compareAtPriceMinor, inStock, stockQty, isActive, position, options: [{ name, values }], variants: [{ id?, optionValues, priceMinor?, inStock, sku }] }`
`stockQty` null = stock not tracked. If `options` is non-empty, `variants` should list each combination the seller sells.

## Delivery zones and discount codes (auth)

| Method | Path | Body | Returns |
|---|---|---|---|
| GET/POST, PUT/DELETE `{id}` | `/api/delivery-zones` | `{ name, feeMinor, etaText, isActive }` | `DeliveryZoneDto = { id, name, feeMinor, etaText, isActive }` |
| GET/POST, PUT/DELETE `{id}` | `/api/discount-codes` | `{ code, type, value, minOrderMinor, expiresAt, maxUses, isActive }` | `DiscountCodeDto = { id, code, type, value, minOrderMinor, expiresAt, maxUses, uses, isActive }` |

Discount `type` is `"Percent"` (value 1–100) or `"Fixed"` (value in minor units).

## Orders and dashboard (auth)

| Method | Path | Body | Returns |
|---|---|---|---|
| GET | `/api/orders?status=&q=&page=1&pageSize=20` | — | `Paged<OrderSummaryDto>` |
| GET | `/api/orders/{id}` | — | `OrderDto` |
| PATCH | `/api/orders/{id}/status` | `{ status, note }` | `OrderDto` |
| PATCH | `/api/orders/{id}/payment` | `{ paymentStatus }` (`"Unpaid"`/`"Paid"`) | `OrderDto` |
| GET | `/api/dashboard` | — | `DashboardDto` |

Order `status`: `New`, `Confirmed`, `Paid`, `Delivered`, `Cancelled`. `fulfilment`: `Delivery`, `Pickup`.
`Paged<T> = { items: T[], page, pageSize, total }`
`OrderSummaryDto = { id, number, code, customerName, phone, fulfilment, totalMinor, currency, status, paymentStatus, itemCount, createdAt }`
`OrderDto = OrderSummaryDto & { address, note, deliveryZoneName, subtotalMinor, deliveryFeeMinor, discountMinor, discountCode, items: [{ productId, variantId, name, variant, unitPriceMinor, quantity, lineTotalMinor }], history: [{ status, note, createdAt }], whatsappMessage, customerWhatsappUrl }`
`DashboardDto = { currency, todayOrders, todaySalesMinor, monthSalesMinor, newOrders, productCount, recentOrders: OrderSummaryDto[] }` (sales exclude cancelled orders)

## Billing (auth)

| Method | Path | Body | Returns |
|---|---|---|---|
| GET | `/api/billing` | — | `{ currentPlan: PlanDto, subscription: { status, currentPeriodEnd }, plans: PlanDto[], usage: { products, maxProducts }, paymentsEnabled }` |
| POST | `/api/billing/checkout` | `{ planCode }` | `{ authorizationUrl }` — redirect the browser there (Paystack) |

`PlanDto = { code, name, priceMinor, currency, interval, limits: Record<string, number>, features: string[] }` (`-1` = unlimited)

## Public storefront (no auth)

| Method | Path | Body | Returns |
|---|---|---|---|
| GET | `/api/public/stores?page=1&pageSize=500` | — | `Paged<{ slug, updatedAt }>` — stores for the sitemap: active businesses with at least one product for sale, ordered by slug; `pageSize` up to 1000 |
| GET | `/api/public/stores/{slug}` | — | `PublicStoreDto` |
| GET | `/api/public/stores/{slug}/products` | — | `PublicProductDto[]` (active only) |
| GET | `/api/public/stores/{slug}/products/{productSlug}` | — | `PublicProductDto` |
| POST | `/api/public/stores/{slug}/quote` | `QuoteRequest` | `QuoteDto` |
| POST | `/api/public/stores/{slug}/orders` | `PlaceOrderRequest` | `{ code, number, totalMinor, currency, whatsappUrl, orderUrl }` |
| GET | `/api/public/orders/{code}` | — | `PublicOrderDto` |
| GET | `/api/public/domains/{host}` | — | `{ slug }` for a served custom domain (Live, plan includes it, business active); 404 otherwise. Cached a minute. |

On a custom domain the OrderLink web app (`web/src/proxy.ts`) serves the store at `/` (`/p/{product}`, `/cart`, `/checkout` too; the
app's own `/{slug}/...` and `/o/{code}` links work unchanged; dashboard and sign-in paths redirect to OrderLink's own address). CORS
on `/api/public/*` also accepts `https://<served custom domain>` origins (checked against the same cached list).

`PublicStoreDto = { name, slug, description, logoUrl, coverUrl, currency, themeColor, isOpen, deliveryZones: [{ id, name, feeMinor, etaText }], categories: [{ id, name }], showBranding, hasDiscounts }`
`PublicProductDto = { id, slug, name, description, priceMinor, compareAtPriceMinor, inStock, categoryId, images: [{ url }], options: [{ name, values }], variants: [{ id, optionValues, title, priceMinor, inStock }] }`
`QuoteRequest = { items: [{ productId, variantId, quantity }], fulfilment, deliveryZoneId, discountCode }`
`QuoteDto = { lines: [{ productId, variantId, name, variant, unitPriceMinor, quantity, lineTotalMinor, stockQty, productSlug }], subtotalMinor, deliveryFeeMinor, discountMinor, totalMinor, currency, discountMessage }`
`PlaceOrderRequest = QuoteRequest & { customerName, phone, address, note }` — `address` required for `Delivery`; `deliveryZoneId` required for `Delivery` when the store has zones.
After placing an order, send the browser to `whatsappUrl` (a `https://wa.me/...` link with the order pre-filled). The order is already saved, so the seller has a record even if the customer never presses send.
`PublicOrderDto = { code, number, storeName, storeSlug, status, paymentStatus, fulfilment, items: [{ name, variant, quantity, lineTotalMinor }], subtotalMinor, deliveryFeeMinor, discountMinor, totalMinor, currency, createdAt, whatsappUrl, showBranding, deliveryToBeAgreed }`
A quote line's `productSlug` is the product's current slug: carts link with it, so a renamed product's cart link still works. `deliveryToBeAgreed` is true for a delivery order placed while the store had no delivery zones (no fee): the WhatsApp message says "Delivery - to be agreed" instead of a ₦0 fee.

## Reserved store slugs

`api, o, login, signup, dashboard, pricing, admin, app, www, static, _next, uploads, about, terms, privacy, help, support` cannot be used as store slugs, so the web app can serve stores at `/{slug}`.
