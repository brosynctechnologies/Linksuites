# LinkSuite public site audit (2026-11-07)

Scope: public pages (landing, pricing, login, signup, forgot/reset, invite accept, public product pages) and root layouts of the seven apps, plus all of `hub-web`. Signed-in screens were skipped.

Because the redesign rebuilds the hub and the landing pages and removes all prices, this audit concentrates on what survives it: root layouts, metadata, robots/sitemap, icons, fonts, shared components (forms, buttons, nav, tabs, modals), the auth pages, the public product pages, and leftovers from copying one app to another. Landing-page copy and visuals are covered only in passing.

How it was checked:
- Contrast ratios were computed (WCAG 2.x relative luminance) from the tokens in each `src/app/globals.css`.
- Next 16 conventions were checked against `node_modules/next/dist/docs/01-app/03-api-reference/03-file-conventions/01-metadata/{robots,sitemap,opengraph-image}.md` and `04-functions/generate-metadata.md`. Metadata merges shallowly, so a page that sets `openGraph` replaces the root `openGraph` completely.
- `npx tsc --noEmit` passes for `hub-web`.
- `npm run lint`: `hub-web` has 2 errors (H10 below); `web`, `booking-web` and `care-web` are clean.

> **hub-web is being rewritten while this audit ran.** New files appeared mid-audit: `src/app/suite.css`, `src/components/suite/*`, `src/components/hub/screens.tsx` and `src/lib/{site,suite}.ts`. `globals.css` already uses the new `ls-` tokens, but `layout.tsx` and `page.tsx` are still the old ones. The hub findings describe that snapshot. They matter most for the shared `suite` components, which are meant to be copied verbatim into every app.

---

## Summary

| App | SEO | Semantics | Accessibility | Performance | Consistency |
|---|---|---|---|---|---|
| hub-web (3600) | partial | partial | partial | partial | partial |
| OrderLink `web` (3700) | partial | partial | **fail** | partial | **fail** |
| InvoiceLink `invoice-web` (3800) | partial | partial | **fail** | pass | **fail** |
| BookLink `booking-web` (3900) | **fail** | partial | **fail** | partial | **fail** |
| LearnLink `course-web` (4000) | **fail** | partial | **fail** | pass | **fail** |
| PropertyLink `property-web` (4100) | **fail** | partial | **fail** | partial | **fail** |
| SchoolLink `school-web` (4200) | partial | partial | **fail** | partial | **fail** |
| CareLink `care-web` (4400) | partial | partial | **fail** | pass | **fail** |

Severity counts:

| App | High | Medium | Low |
|---|---|---|---|
| hub-web | 3 | 6 | 5 |
| OrderLink | 3 | 7 | 5 |
| InvoiceLink | 2 | 5 | 4 |
| BookLink | 3 | 7 | 3 |
| LearnLink | 3 | 8 | 3 |
| PropertyLink | 5 | 9 | 3 |
| SchoolLink | 3 | 6 | 2 |
| CareLink | 3 | 5 | 2 |
| **Total** | **25** | **53** | **27** |

What is already good and should be kept:
- `next/font` is used everywhere, with `display: swap`.
- Every app has `metadataBase` from `NEXT_PUBLIC_SITE_URL`, and the compose file sets it per app (`docker-compose.vps.yml:95-135`).
- Titles use a template.
- Tokens and invite, reset and support pages are `noindex`.
- Every `<img>` has an `alt`.
- Labels are tied to inputs through `useId`, with `aria-invalid` and `aria-describedby`.
- `ErrorBox` uses `role="alert"`.
- Modals in course, property, school and care trap focus and return it.
- Buttons are 44px tall.
- All apps have reduced-motion rules.
- Heavy libraries (`maplibre-gl`, `hls.js`) are loaded with dynamic `import()`.
- No icon-only control without an accessible name was found on the public pages.

---

## Cross-cutting fixes (apply to every app)

### X1. Add `robots.ts` and `sitemap.ts` (no app has either)
`src/app/robots.ts`:
```ts
import type { MetadataRoute } from "next";
import { SITE_URL } from "@/lib/config";
export default function robots(): MetadataRoute.Robots {
  return {
    rules: { userAgent: "*", allow: "/", disallow: ["/app", "/dashboard", "/studio", "/learn", "/parent", "/student", "/portal", "/me", "/admin", "/support", "/invite", "/reset-password"] },
    sitemap: `${SITE_URL}/sitemap.xml`,
  };
}
```
Keep only the disallow entries for routes the app actually has.

`src/app/sitemap.ts`:
- Static pages: list `/`, `/signup` and the public marketing pages.
- Dynamic pages: list them from the public API. That means stores (OrderLink), courses and academies (LearnLink), listings, areas, agencies, agents and `/rent|buy|shortlet/[location]` (PropertyLink), and public booking pages (BookLink, only if the API exposes a list of public pages).
- Use `generateSitemaps` past 50,000 URLs.

### X2. Complete root metadata, add an OG image, and keep `openGraph` through the shallow merge
Add this to each root `layout.tsx` metadata:
```ts
applicationName: APP_NAME,
alternates: { canonical: "/" },
openGraph: { type: "website", siteName: APP_NAME, locale: "en_NG", url: "/" },
twitter: { card: "summary_large_image" },
```
- Add `src/app/opengraph-image.tsx`, a 1200×630 `ImageResponse` with the app's wordmark and accent. Next attaches it to `og:image` and `twitter:image` automatically.
- Pages that set `openGraph` (store, product, course, listing) replace the root object completely. Build them from a shared `baseOpenGraph` object and always set `title`, `description` and `url` along with `images`.

### X3. Icons
- All seven apps ship the same `src/app/favicon.ico` (15,086 bytes, byte-identical across apps), and the hub has no icon at all.
- Add a per-app `src/app/icon.svg` (the wordmark mark) and `src/app/apple-icon.png` (180×180).
- Drop the shared `favicon.ico` or regenerate it per app.

### X4. `lang="en-NG"` everywhere
`web`, `invoice-web`, `booking-web` and `course-web` use `lang="en"`; the rest use `en-NG`. Change `src/app/layout.tsx` in those four to `<html lang="en-NG">` so they match the others and `locale: "en_NG"`.

### X5. Skip link and a `<main id="main">` on every public shell
Only the OrderLink dashboard has a skip link. The new `SuiteHeader` (`hub-web/src/components/suite/suite-header.tsx:104-109`) adds one that targets `#main`, so every public frame must render `<main id="main" tabIndex={-1}>`:
- `AuthShell` in each app;
- `SitePage` and `SiteHeader` users (course, property, school, care);
- `web/src/components/store/store-shell.tsx`;
- `booking-web/src/app/[slug]/booking-page.tsx`;
- `invoice-web/src/components/site-header.tsx` users.

### X6. Input borders fail the 3:1 non-text contrast check (WCAG 1.4.11), High in every app
The field border is the only thing that shows where a field is.

| App | Token in `inputClass` | Ratio | Replace with |
|---|---|---|---|
| web | `border-line-strong` #cdd1dd on white (`web/src/components/ui.tsx:64`) | 1.53 | #8a909c (3.21) |
| invoice | `border-rule` #d9dee6 (`invoice-web/src/components/styles.ts:23`) | 1.35 | #8a909c (3.21) |
| booking | `border-line` #e6e1ec (`booking-web/src/components/styles.ts:23`) | 1.28 | #8e8499 (3.55) |
| course | `border-line` #dce3e3 (`course-web/src/components/styles.ts:25`) | 1.30 | #7f9294 (3.26) |
| property | `border-line` #d6dcd4 (`property-web/src/components/styles.ts:26`) | 1.40 | #7f8c84 (3.51) |
| school | `border-rule` #c9d5e3 (`school-web/src/components/styles.ts:26`) | 1.49 | #7c8696 (3.68) |
| care | `border-rule` #cbd8d4 (`care-web/src/components/styles.ts:26`) | 1.47 | #7d8c88 (3.51) |
| hub suite | `--color-ls-line-strong` #b4b8ae (`hub-web/src/app/suite.css`, ring of `secondary` buttons) | 2.02 white / 1.79 chalk | #868b82 (3.48 / 3.10) |

Add a `--color-field` token for the new values rather than darkening `line`/`rule`, which also draw decorative dividers.

### X7. Placeholder text in `text-faint` is under 4.5:1
| App | Ratio on white |
|---|---|
| invoice #8a93a3 | 3.10 |
| booking #9a8fa6 | 3.06 |
| course #82949b | 3.15 |
| property #7b8780 | 3.74 |
| school #7a8392 | 3.83 |
| care #7b8a86 | 3.61 |

Fix: change `placeholder:text-faint` to `placeholder:text-muted` in each `inputClass`. Every `muted` token is at least 5.2:1. OrderLink's `text-subtle` passes at 4.88.

### X8. Inline field errors are never announced
`FieldShell` renders the error as a plain `<p id="…-error">`, with no live region and no focus move. That is fine when `ErrorBox` (`role="alert"`) also shows. But property, school and care deliberately hide `ErrorBox` when the error belongs to a field (`error && !fieldError ? error : null`). On those forms a screen-reader user who submits a bad email hears nothing.

Fix:
1. Always render `ErrorBox` (it already lists `fieldMessages`).
2. After a failed submit, move focus to the first `[aria-invalid="true"]` field:
   ```ts
   requestAnimationFrame(() => formRef.current?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus())
   ```

### X9. Tabs and radio-button groups are not keyboard-complete
The `Tabs` components set `role="tab"` but have no roving `tabIndex` and no Arrow, Home or End keys:
- `course-web/src/components/ui.tsx:426`
- `property-web/src/components/ui.tsx:437`
- `school-web/src/components/ui.tsx:403`
- `care-web/src/components/ui.tsx:412`

They also emit `aria-controls` for panels that are not rendered.

`admin-web/src/components/ui.tsx:365` already implements the pattern (3 key handlers). Port it, give inactive tabs `tabIndex={-1}`, and emit `aria-controls` only for the active tab.

The same applies to `role="radio"` button groups:
- `property-web/src/app/home-search.tsx:28-41`
- `booking-web/src/app/[slug]/booking-page.tsx:297,457`

Use native `<input type="radio">` inside styled labels instead.

### X10. Open redirect in the older `nextPath()`
Four apps check only `startsWith("/") && !startsWith("//")`, so `?next=/\evil.com` sends a just-signed-in user off-site:
- `web/src/components/auth-shell.tsx:60`
- `invoice-web/src/components/auth-shell.tsx:38`
- `booking-web/src/components/auth-shell.tsx:32`
- `course-web/src/components/auth-shell.tsx:48`

Fix: copy the origin-checked version from `care-web/src/components/auth-shell.tsx:46-56`.

### X11. "Part of LinkSuite" and the hub URL never reach production
- **No app links back to the hub.** `LinkSuite` does not appear anywhere in the seven apps' `src`.
- **The hub URL is never set at build time.** The new suite components read `NEXT_PUBLIC_HUB_URL` (`hub-web/src/lib/products.ts:181`), but that variable is in neither `deploy/web.Dockerfile` (ARGs at lines 11-20) nor `x-web-build-args` (`docker-compose.vps.yml:13-22`). Every "Part of LinkSuite" link would therefore bake in `http://localhost:3600`.

Fix:
1. Add `ARG NEXT_PUBLIC_HUB_URL` to the Dockerfile.
2. Add `NEXT_PUBLIC_HUB_URL: ${HUB_URL}` to `x-web-build-args`.
3. Add it to each app's `.env.example`.
4. Port `SuiteHeader` and `SuiteFooter` (or at least the footer's "Part of LinkSuite" link) into every app.

### X12. Images
- All public images are plain `<img>` with no `width`/`height`. Most sit in fixed-size or `aspect-*` boxes, so there is little layout shift, but uploads are served at full size to phones.
- Fixed-size logos and avatars: add `width`/`height`.
- Product, course and listing photos: switch to `next/image` with `sizes` (OrderLink already has `remotePatterns` for R2 at `web/next.config.ts:17-22`; copy it to the other apps), or serve R2 variants through `srcSet` as `property-web` gallery does.

### X13. Structured data
Only the hub has a JSON-LD builder (`hub-web/src/lib/site.ts:54`), and it is not rendered yet.
- Add the `JsonLd` helper (`hub-web/src/components/suite/sections.tsx:192`) to each app.
- Emit the right schema per product page (listed under each app).
- Removing LinkSuite plan prices does not mean removing a store's, course's or listing's own price from `Offer`. Those are the business's data.

### X14. Low-value pages that are indexable
- Forgot-password pages have no `robots: { index: false }`.
- The per-tenant sign-in pages (`school-web /[slug]`) and the course certificate pages (which show students' names) are also indexable.

Add `robots: { index: false }` to all of them.

### X15. Mode switches lose focus
In the login forms, "Email me a code", "Use password" and "Back" unmount the button that has focus (for example `web/src/app/login/login-form.tsx:66-69`; the same pattern is in every `auth-forms.tsx`). Focus drops to `<body>`.

Fix: after `setMode`, focus the new step's first field, using `autoFocus` on that field or a ref.

---

## hub-web (port 3600)

**H1 High: the hub has no icon, and its structured data points at a missing logo.**
- There is no `src/app/favicon.ico`, `icon.*` or `public/`.
- `hubJsonLd()` sets `logo: ${SITE_URL}/icon.svg` (`src/lib/site.ts:55`), and that file returns 404.
- Fix: add `src/app/icon.svg` (the `SuiteMark` SVG) and `src/app/apple-icon.png`.

**H2 High: the hub URL is not baked in at build time.** The cause and fix are in X11 (`deploy/web.Dockerfile:11-20`, `docker-compose.vps.yml:13-22`). The hub's own `metadataBase` is fine, but every link back from the apps would point at localhost.

**H3 High: the current `page.tsx` sends tutors and agents to the wrong sign-in page.**
- `page.tsx:98,105` uses `p.url` and `${p.url}/login` for every app.
- For LearnLink that is the *learner* login, and for PropertyLink the *home-seeker* login. The hub row speaks to tutors and agents, who sign in at `/teach/login` and `/studio/login`.
- The new `products.ts` already has `landingPath`, `startPath` and `signInPath`. Make sure the rebuilt page only ever uses `appHref(p, p.signInPath)` and `appHref(p, p.landingPath)`.

**H4 Medium: no `robots.ts` or `sitemap.ts`.** Add both (X1). The sitemap is just `/`.

**H5 Medium: root metadata ignores `site.ts` and has no OG or Twitter tags.**
- `layout.tsx:21-26` still has the old title and description, with no `openGraph`, `twitter`, `alternates.canonical` or OG image.
- `hubJsonLd()` exists but is not rendered.
- Fix: build metadata from `SITE_TITLE` and `SITE_DESCRIPTION`, add the X2 fields and `opengraph-image.tsx`, and render `<JsonLd data={hubJsonLd()} />` in the page.

**H6 Medium: the font variables don't match the new CSS.**
- `layout.tsx:6-19` declares `--font-bricolage` and `--font-onest`.
- `suite.css` and `globals.css:22` read `--font-ls-display-face` and `--font-ls-text-face`, so the hub currently renders in system fonts.
- Fix: rename the variables to `--font-ls-display-face` and `--font-ls-text-face`.
- Bricolage also needs the `wdth` axis for `ls-h1` (`font-variation-settings: "wdth" 80`). Add `axes: ["wdth"]` and drop the fixed `weight` array, since variable axes need the variable font.

**H7 Medium: `page.tsx` uses tokens that no longer exist.** `text-ink`, `text-ink-2`, `text-muted`, `bg-surface`, `bg-paper` and `border-line` (`page.tsx:11-106`) were removed from `globals.css`, so the page is unstyled until the rebuilt page lands. Check this before shipping any intermediate build.

**H8 Medium: the skip link's target is missing.** `SuiteHeader` links to `#main`, but `page.tsx:18` has `<main>` without `id="main"`. Fix: `<main id="main" tabIndex={-1}>` on every page that uses `SuiteHeader`, in the hub and in every app.

**H9 Medium: secondary-button and FAQ outlines are too faint.** `--color-ls-line-strong` #b4b8ae is 2.02:1 on white and 1.79:1 on chalk. It is the only boundary of `lsButton("secondary")` (`src/components/suite/ui.ts:164`) and of the FAQ icon ring (`sections.tsx:161`). Change it to #868b82 (3.48 / 3.10).

**H10 Low: lint errors in `SuiteHeader`.** `suite-header.tsx:116,121` use `<a href="/">` and fail `@next/next/no-html-link-for-pages` (the 2 lint errors). Use `next/link` for the same-origin `/`, and keep `<a>` for `hubUrl`, which is another origin.

**H11 Low: FAQ headings inside `<summary>`.** `sections.tsx:159-160` puts an `<h3>` inside `<summary>`. Summary content is presentational in several browser and screen-reader combinations, so the heading is lost. Put the question as plain text in `<summary>`. If FAQ headings must be navigable, put a visible `<h3>` above each `<details>` and give the `<summary>` a short "Show answer" label.

**H12 Low: decorative links under `aria-hidden`.** The `Spectrum` component (`page.tsx:70-74`) puts links (`tabIndex={-1}`) inside `aria-hidden`. Make them `<span>`s. It goes away with the rebuild.

**H13 Low: two accents are nearly identical.** LearnLink #0B6E6E and CareLink #0F766E are 1.11:1 apart, so they look the same in `SuiteMark` and the footer swatches. Shift one of them, for example CareLink toward its `scrub-deep`, or LearnLink toward lime.

**H14 Low: `themeColor` doesn't match the new background.** `viewport.themeColor` #f3f4f6 (`layout.tsx:31`) should be the new chalk #f1f2ee.

---

## OrderLink, `web` (port 3700)

**W1 High: the focus ring fails contrast.** `:focus-visible` uses marigold #f2a93b (`src/app/globals.css:124-126`), which is 2.00:1 on white and 1.86:1 on paper. Use `outline: 3px solid var(--color-ink)` (16:1) on light grounds, and marigold only on the ink brand panel, where it is 8.05:1.

**W2 High: input and store-search borders are invisible.** Inputs: see X6 (`src/components/ui.tsx:64`). The storefront search box is worse: `border-line` #e3e6ee on `bg-paper` (`src/app/[slug]/page.tsx:157`) is about 1.2:1. Use the new field token.

**W3 High: no "Part of LinkSuite" link (X11).** The storefront's "Made with OrderLink" (`src/components/store/store-shell.tsx:15-20`) is fine as it is. The marketing and auth pages need the suite footer.

**W4 Medium: storefront pages have no `<main>`.** `store-shell.tsx:12` wraps all storefront pages (store, product, cart, checkout) without a `<main>`. Wrap `{children}` in `<main id="main">`.

**W5 Medium: cart and checkout have no H1.** `StoreTopBar` renders its `title` in a `<span>` (`store-shell.tsx:39`). Render `<h1>` when `title` is passed. On the product page the H1 is already the product name, so keep the store name as a span there.

**W6 Medium: open redirect in `nextPath()` (X10).** `src/components/auth-shell.tsx:57-61`.

**W7 Medium: store and product metadata are thin.**
- Product `openGraph` has no description or URL (`src/app/[slug]/p/[productSlug]/page.tsx:17`), and neither page has `twitter: { card: "summary_large_image" }`.
- There is no JSON-LD. Add `Product` with `offers` (`price`, `priceCurrency: store.currency`, `availability` from `inStock`) on product pages, and `Store` (`OnlineStore`) with `logo` and `url` on `/[slug]`.

**W8 Medium: storefront photos are full-size `<img>`.** Store and product photos are plain `<img>` served at original size (`[slug]/page.tsx:37-42,98,105-109`; `p/[productSlug]/page.tsx:42-48`; `components/store/cart-view.tsx:71`). The landing page promises "a store that loads fast on slow mobile data". `remotePatterns` is already configured (`next.config.ts:17-22`), so switch to `next/image` with `sizes="(max-width:560px) 50vw, 280px"` for the grid and `100vw` for the product gallery.

**W9 Medium: no robots or sitemap (X1).** The sitemap should list `/` and every public store and product.

**W10 Medium: no root OG or Twitter tags, and no OG image (X2).** `src/app/layout.tsx:19-26`.

**W11 Low: the product photo strip has an unused label.** It has `aria-label` and `tabIndex` but no role (`p/[productSlug]/page.tsx:41`). Add `role="region"`.

**W12 Low: login mode switches drop focus (X15).** `src/app/login/login-form.tsx:66-69,112,128,159`.

**W13 Low: prices to remove with the redesign.**
- Landing pricing: `src/app/page.tsx:21-38` and the `#pricing` nav link at line 146.
- `AuthShell` footer line "Free for up to 20 products." (`src/components/auth-shell.tsx:32`).

**W14 Low: `lang="en"`** (`src/app/layout.tsx:36`). See X4.

**W15 Low: shared favicon (X3).**

---

## InvoiceLink, `invoice-web` (port 3800)

**I1 High: input borders at 1.35:1 (X6).** `src/components/styles.ts:23`.

**I2 High: no "Part of LinkSuite" link (X11).** `src/components/site-header.tsx:30-52` (`SiteFooter`).

**I3 Medium: no robots or sitemap (X1).** `/create`, the free invoice generator, is the main search landing page and should be in the sitemap.

**I4 Medium: no root OG or Twitter tags, and no OG image (X2).** `src/app/layout.tsx:22-30`. `/create` would also benefit from `WebApplication` JSON-LD (`isAccessibleForFree: true`).

**I5 Medium: open redirect in `nextPath()` (X10).** `src/components/auth-shell.tsx:38`.

**I6 Medium: placeholders at 3.10:1 (X7).** `src/components/styles.ts:23`.

**I7 Medium: the sticky header can hide focused fields.**
- The header (`src/components/site-header.tsx:6`, 64px) stays on top while users tab through the long `/create` form, so focused fields can scroll under it (WCAG 2.4.11).
- There is also no skip link.
- Fix: add `html { scroll-padding-top: 5rem }` to `globals.css`, and add the X5 skip link.

**I8 Low: the invoice preview's label is ignored.** It is a `<div aria-label="Invoice preview">` (`src/components/generator.tsx:225`); a generic `div` ignores `aria-label`. Use `<section aria-label="Invoice preview">`.

**I9 Low: header links are small, and one goes to pricing.**
- Header nav links are about 36px tall (`site-header.tsx:12-23`). Add `min-h-11 inline-flex items-center`.
- The "Pricing" link at line 15 goes when prices are removed.

**I10 Low: `lang="en"`** (`src/app/layout.tsx:41`). See X4.

**I11 Low: README port.** `README.md:17` still says `localhost:3000`; the app runs on 3800.

---

## BookLink, `booking-web` (port 3900)

**B1 High: the public booking page has no real title and no server HTML.**
- `/[slug]` is the page businesses share in their Instagram bio, but it is fully client-rendered.
- Its static metadata is `title: "Book an appointment"` (`src/app/[slug]/page.tsx:6`) for every business, with no description, no OG image, and empty server HTML.
- Fix: fetch the public page in `generateMetadata`, returning:
  - `title: { absolute: \`${name} · Book online\` }`;
  - `description` from the business's about text;
  - `openGraph.images` from `coverUrl` or `logoUrl`.
- Render the header (`PublicHeader` in `src/components/public-parts.tsx:64-90`) on the server and keep only the booking steps client-side.

**B2 High: input borders at 1.28:1 (X6).** `src/components/styles.ts:23`.

**B3 High: no "Part of LinkSuite" link.** `BrandingFooter` (`src/components/public-parts.tsx:133-139`) links only to BookLink `/`, and marketing and auth pages have no suite footer (X11).

**B4 Medium: the public booking page has no `<main>`.** Wrap the steps in `src/app/[slug]/booking-page.tsx` (from about line 75) in `<main id="main">`.

**B5 Medium: unknown booking pages return 200.** An unknown slug renders "No booking page here" (`booking-page.tsx:27`) with HTTP 200. Call `notFound()` from the server component once B1 moves the fetch to the server.

**B6 Medium: no robots or sitemap (X1).** Disallow `/app`, `/b/`, `/invite` and `/support`.

**B7 Medium: no root OG or Twitter tags, and no OG image (X2).** `src/app/layout.tsx:18-26`.

**B8 Medium: open redirect in `nextPath()` (X10).** `src/components/auth-shell.tsx:32`.

**B9 Medium: placeholders at 3.06:1 (X7).** `src/components/styles.ts:23`.

**B10 Medium: the public booking page has no JSON-LD.** Add `LocalBusiness` (or `HairSalon`/`BeautySalon`) with `name`, `address`, `openingHoursSpecification`, `aggregateRating` (from `rating`), `image` and `url`.

**B11 Low: the staff picker is not a full radio group.** It uses `role="radio"` buttons with no arrow keys (`booking-page.tsx:297,457`). See X9.

**B12 Low: the manage-booking page can have no H1.** Its H1 is conditional (`src/app/b/[token]/manage-booking.tsx:110`, `{heading && <h1>}`). Always render one.

**B13 Low: `lang="en"`** (`layout.tsx:37`), and the README still says port 3000 (`README.md:17`).

---

## LearnLink, `course-web` (port 4000)

**C1 High: the marketplace has no robots or sitemap.**
- No `robots.ts` or `sitemap.ts`, on a marketplace whose growth depends on search.
- Fix: add a sitemap enumerating `/courses`, every `/courses/{academy}/{course}` and every `/a/{academy}` from the catalog API, plus `/teach`.
- Disallow `/learn`, `/studio`, `/checkout`, `/admin`, `/invite` and `/support`.

**C2 High: input borders at 1.30:1 (X6).** `src/components/styles.ts:25`.

**C3 High: no "Part of LinkSuite" link (X11).** `src/components/site-chrome.tsx:61-81` (`SiteFooter`).

**C4 Medium: course pages have thin metadata and no structured data.**
- Course `generateMetadata` (`src/app/courses/[academySlug]/[courseSlug]/page.tsx:23-31`) sets `openGraph` with images only. That replaces the root OG, so shares have no title or description.
- Fix: `openGraph: { title, description, type: "website", url, images }` and `twitter: { card: "summary_large_image" }`.
- Add `Course` JSON-LD: `name`, `description`, `provider` (academy as `Organization`), `aggregateRating`, `offers`, `hasCourseInstance` with `courseMode: "online"`.

**C5 Medium: academy pages have no image or JSON-LD.** Academy metadata (`src/app/a/[academySlug]/page.tsx:19-23`) has no OG image (use the logo or cover) and no `EducationalOrganization` JSON-LD.

**C6 Medium: catalogue filters change results silently and create indexable duplicates.**
- Filter changes (`src/app/courses/catalog-filters.tsx:29-34`) swap results without telling screen-reader users. Render the result count in a `role="status"` element that updates after navigation.
- Filtered URLs (`?category=&level=&sort=&price=`) are indexable duplicates. Set `alternates.canonical` to `/courses` (or to the category path) and `robots: { index: false }` when any sort or price filter is present.

**C7 Medium: tabs are not keyboard-complete (X9).** `src/components/ui.tsx:426`.

**C8 Medium: open redirect in `nextPath()` (X10).** `src/components/auth-shell.tsx:48`.

**C9 Medium: placeholders at 3.15:1 (X7).** `src/components/styles.ts:25`.

**C10 Medium: no root OG or Twitter tags, and no OG image (X2).** `src/app/layout.tsx:23-31`.

**C11 Medium: certificate pages expose students' names to search engines.** `src/app/certificates/[code]/page.tsx:11` is indexable and shows the student's name. Add `robots: { index: false }`; anyone with the link can still verify a certificate.

**C12 Low: header and footer links are small.** Footer links have no minimum height (`site-chrome.tsx:69-79`), and header "Browse" and "Teach" are `py-2` (about 36px, lines 48-53). Add `min-h-11 inline-flex items-center`.

**C13 Low: `lang="en"`** (`layout.tsx:42`), and the README still says port 3000 (`README.md:17`).

**C14 Low: tutors need the `/teach` funnel from the hub.** Hub links for LearnLink should go to `/teach`, `/teach/signup` and `/teach/login`. The new hub `products.ts` does this; confirm the rebuilt hub page uses it (H3).

---

## PropertyLink, `property-web` (port 4100)

**P1 High: the listings marketplace has no robots or sitemap.**
- The sitemap should list `/`, `/rent`, `/buy`, `/shortlet`, every `/{purpose}/{location}`, `/areas/*`, `/agencies/*`, `/agents/*`, every active `/listings/{agency}/{slug}`, and `/how-verification-works`.
- Disallow `/studio`, `/me`, `/trips`, `/renew`, `/admin`, `/invite` and `/support`.

**P2 High: the WhatsApp button fails text contrast.** The `whatsapp` variant is white on #1da851, 3.10:1 for 14px semibold text (`src/components/styles.ts:14`). It is the main contact action on every listing (`src/app/listings/[agency]/[slug]/contact.tsx:54`, `src/components/profile-bits.tsx:23`). Change `--color-whatsapp` to #147a3b (5.42:1) with hover #106a33, or keep the brand green and use ink text.

**P3 High: form errors are not announced.** The seeker signup, agent signup and listing enquiry forms hide `ErrorBox` when the error is a field error:
- `src/components/auth-forms.tsx:168,405`
- `src/app/listings/[agency]/[slug]/contact.tsx:170`

Fix: see X8.

**P4 High: input borders at 1.40:1 (X6).** `src/components/styles.ts:26`.

**P5 High: no "Part of LinkSuite" link (X11).** `src/components/site-chrome.tsx:55-80`.

**P6 Medium: the sort control has a duplicate ID and changes page on select.**
- `SortSelect` renders twice, in the phone row and the desktop row (`src/components/search/filters.tsx:190,195`), both with `id="sort"` (`:231-234`). That gives duplicate IDs, and the label is tied only to the first. Use `useId()`.
- The `<select>` also navigates on `onChange` (a change of context on input, WCAG 3.2.2). Acceptable if the result count is announced; better with an explicit Apply.

**P7 Medium: the photo viewer doesn't keep focus.**
- The full-screen viewer (`src/app/listings/[agency]/[slug]/gallery.tsx:98-112,128`) focuses Close but does not trap Tab or return focus to the opener. Reuse the `Modal` focus logic from `ui.tsx`.
- The "⤢ 1 / 4" button (`gallery.tsx:58-67`) is announced as "1 / 4". Add `aria-label={\`Open photos full screen, ${index + 1} of ${count}\`}`.
- Thumbnails set `aria-current={i === index}`, which outputs `"false"` on the others (`:77`). Use `aria-current={i === index ? "true" : undefined}`.

**P8 Medium: listing pages have thin metadata and no structured data.**
- Listing `generateMetadata` (`src/app/listings/[agency]/[slug]/page.tsx:48-52`) sets `openGraph` with images only, so shares lose the title and description. Add `openGraph.title`, `openGraph.description`, `url` and `twitter`.
- Add JSON-LD: `RealEstateListing` (or `Accommodation`) with an `Offer` (price, currency), `address`, `image` and `datePosted`.
- `l.breadcrumb` is already available, so add `BreadcrumbList` too.

**P9 Medium: search pages have no canonical.** `searchMetadata` (`src/components/search/search-page.tsx:24-31`) sets no canonical, so every filter, sort and page combination is indexable. Return `alternates: { canonical: "/rent/lagos" }` (the clean purpose and location path), and `robots: { index: false, follow: true }` when filters other than `page` are present.

**P10 Medium: agency and agent pages have only a title.** `src/app/agencies/[slug]/page.tsx:20` and `src/app/agents/[slug]/page.tsx:20` set no description, OG image or JSON-LD. Add a description, `openGraph.images` (logo or photo), and `RealEstateAgent` JSON-LD.

**P11 Medium: the map stylesheet loads on every page.** `@import "maplibre-gl/dist/maplibre-gl.css"` sits in `src/app/globals.css:2`, so the map CSS loads on login, signup and every page without a map. Import it from `src/components/maps.tsx` (CSS imports in client components are split per route).

**P12 Medium: tabs and the home-search purpose picker are not keyboard-complete.** Tabs: `src/components/ui.tsx:437`. The purpose picker uses `role="radio"` buttons without arrow keys (`src/app/home-search.tsx:28-41`). See X9.

**P13 Medium: placeholders at 3.74:1 (X7).** `src/components/styles.ts:26`.

**P14 Medium: no root OG or Twitter tags, and no OG image (X2).** `src/app/layout.tsx:22-30`.

**P15 Low: listing cards repeat the title.** The card image alt is the listing title, which repeats the card's link text (`src/components/listing-card.tsx:44`). Use `alt=""`.

**P16 Low: leftover LearnLink comments.** `src/components/icons.tsx:306` says "LearnLink additions", and `src/components/ui.tsx:346` says "Learner progress". Rename them to PropertyLink terms.

**P17 Low: pricing to remove.** `/pricing` (`src/app/pricing/page.tsx`) and the footer "Pricing for agents" link (`site-chrome.tsx:69`) go with the price removal. Keep "List a property" pointing at `/studio/signup`.

---

## SchoolLink, `school-web` (port 4200)

**S1 High: form errors are not announced.** The school signup, parent signup and demo request forms hide `ErrorBox` for field errors:
- `src/components/auth-forms.tsx:326,473`
- `src/app/demo/demo-form.tsx:84`

Fix: see X8.

**S2 High: input borders at 1.49:1 (X6).** `src/components/styles.ts:26`.

**S3 High: no "Part of LinkSuite" link (X11).** `src/components/site-chrome.tsx:57-77`.

**S4 Medium: the school's sign-in page is client-rendered and generic.**
- `/[slug]` has a static title, "Sign in to your school", that doesn't name the school (`src/app/[slug]/page.tsx:6`).
- It is fully client-rendered, and an unknown slug returns 200 (`school-sign-in.tsx:28-53`).
- Fix: `generateMetadata` that fetches the school, with `title: \`Sign in to ${school.name}\`` and `robots: { index: false }`, plus `notFound()` on 404.

**S5 Medium: the sign-in tabs are not keyboard-complete.** Parent, Student and Staff use `Tabs` with no arrow keys, and `aria-controls` points at panels that are not rendered (`src/components/ui.tsx:403-426`; `school-sign-in.tsx:67-68`). See X9.

**S6 Medium: the WhatsApp button fails text contrast.** The `whatsapp` variant is 3.10:1 (`src/components/styles.ts:14`; `src/components/ui.tsx:607`). Use the same fix as P2.

**S7 Medium: no robots or sitemap (X1).** Disallow `/app`, `/parent`, `/student`, `/invite`, `/support` and the per-school `/[slug]` sign-in pages.

**S8 Medium: no root OG or Twitter tags, and no OG image (X2).** `src/app/layout.tsx:25-33`.

**S9 Medium: placeholders at 3.83:1 (X7).** `src/components/styles.ts:26`.

**S10 Low: prices to remove.** `SCHOOL_PLANS` prices in `src/lib/config.ts:21-25` drive the public `/pricing` page. Keep the plan limits and drop the amounts when prices are removed.

**S11 Low: forgot-password is indexable.** `src/app/forgot-password/page.tsx:7` should set `robots: { index: false }` (X14).

---

## CareLink, `care-web` (port 4400)

**K1 High: signup errors are not announced.** The clinic signup and patient-portal signup forms hide `ErrorBox` for field errors (`src/components/auth-forms.tsx:326,466`). Fix: see X8.

**K2 High: input borders at 1.47:1 (X6).** `src/components/styles.ts:26`.

**K3 High: no "Part of LinkSuite" link (X11).** `src/components/site-chrome.tsx:49-70`.

**K4 Medium: the WhatsApp button fails text contrast.** The `whatsapp` variant is 3.10:1 (`src/components/styles.ts:14`; `src/components/ui.tsx:675`). Use the same fix as P2.

**K5 Medium: no robots or sitemap (X1).** Disallow `/app`, `/portal/` (except `/portal/sign-in`), `/invite` and `/support`.

**K6 Medium: no root OG or Twitter tags, OG image or JSON-LD.** `src/app/layout.tsx:24-32`. Add X2, plus `SoftwareApplication` JSON-LD with `applicationCategory: "HealthApplication"` on the landing page.

**K7 Medium: tabs are not keyboard-complete (X9).** `src/components/ui.tsx:412`.

**K8 Medium: placeholders at 3.61:1 (X7).** `src/components/styles.ts:26`.

**K9 Low: prices to remove.** `CARE_PLANS` prices (`src/lib/config.ts:22-26`) and `/pricing` go with the price removal.

**K10 Low: login mode switches drop focus (X15).** Staff and patient-portal sign-in (`src/components/auth-forms.tsx:184-246,342-399`).

---

## Ten most important fixes

1. **Make inputs visible (X6).** Darken input border tokens to at least 3:1 in all seven apps and in the hub's `ls-line-strong`. They are 1.28–1.53:1 today.
2. **Announce form errors (X8).** Stop hiding `ErrorBox` for field errors and focus the first invalid field. This affects the property, school and care signups, the property enquiry form and the school demo form.
3. **Fix the OrderLink focus ring (W1).** Marigold is 2.0:1; use ink.
4. **Fix the WhatsApp button (P2, S6, K4).** White on #1da851 is 3.10:1; use #147a3b. It is PropertyLink's main contact button.
5. **Get "Part of LinkSuite" working in production (X11, H2).** Add `NEXT_PUBLIC_HUB_URL` to `deploy/web.Dockerfile` and `docker-compose.vps.yml`, then port the suite header and footer into every app.
6. **Add `robots.ts` and `sitemap.ts` to all eight sites (X1).** This matters most for the LearnLink and PropertyLink marketplaces.
7. **Complete metadata (X2, X13).**
   - Add root `openGraph`, `twitter`, `canonical` and `opengraph-image.tsx`.
   - Stop course, listing and product pages wiping the OG title and description.
   - Add JSON-LD: `Product`, `Course`, `RealEstateListing`, `LocalBusiness`.
8. **Server-render BookLink's public booking page (B1, B5).** It needs a per-business title, OG image and a real 404. SchoolLink's `/[slug]` has the same problem in a smaller form (S4).
9. **Fix the open redirect in `nextPath()` (X10).** OrderLink, InvoiceLink, BookLink and LearnLink; copy CareLink's version.
10. **Fix hub basics before the rebuild ships (H1, H3, H6, H8).**
    - Add an icon (structured data points at a missing `/icon.svg`).
    - Use per-app `signInPath` (`/teach/login`, `/studio/login`).
    - Fix the font variable names.
    - Add `<main id="main">` for the skip link.

    Then port the keyboard-complete `Tabs` from `admin-web` (X9).
