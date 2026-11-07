# LinkSuite design system

The shared frame for the eight LinkSuite public sites: the hub (`hub-web`, port 3600) and the public pages of the seven apps.
The hub is the reference implementation. Every pattern below exists in `hub-web/src` and is meant to be copied, not re-invented.

Binding decisions from the owner:

- **Shared frame, own colour.** One layout system, type scale, spacing, header, footer and set of section patterns. Each app keeps
  its signature colour from its own `globals.css` and one hero visual built from its real screens. The hub is neutral and shows all
  seven colours.
- **No prices on any public page.** Each site says it is free to start, lists what the free plan includes and what paid plans add,
  and says that you upgrade inside the app. Pricing pages are removed and redirect to the landing page's `#plans` section.
- **More attractive** for owners on phones: clear, warm, confident, Nigerian in context, with real product imagery.

## Who, and the one job

Owners and managers of Nigerian small businesses, mostly on a mid-range Android phone on mobile data, arriving from WhatsApp, a
search or a friend. Their question: "does this run *my* kind of business, and what will it cost me?"

- The hub's job: **get the visitor to the right app** in as few taps as possible (pick your business, then start free).
- Each app site's job: **start free**.

## The idea: the street of signboards

Every small business in Lagos, Ibadan or Aba announces itself with a painted signboard: a flat plate of strong colour, the
business's trade in heavy condensed letters, a thin painted frame line just inside the edge. LinkSuite borrows that object.

- On the hub, the business-type picker **is** a row of signboards, one per app, each painted in that app's colour and saying the
  visitor's trade in their words ("A salon, barber or studio"). It is the hub's main action and its one memorable element.
- On every site, the closing call to action is a full-width signboard band in the app's colour.
- The LinkSuite mark is a tiny signboard painted in the seven colours.

Everything else stays quiet: chalk ground, ink text, rules instead of cards, no gradients, no decorative shadows.

## Tokens

All shared tokens live in `hub-web/src/app/suite.css`, which every site copies verbatim. Everything is prefixed `ls-` (or `app`)
so it never collides with an app's own palette (`ink`, `line`, `paper` and so on stay the app's own).

### Neutral base

| Name | Token | Hex | Role |
|---|---|---|---|
| Chalk | `ls-chalk` | `#F1F2EE` | Page ground. A cool, slightly grey white, like a whitewashed wall. Never cream |
| Concrete | `ls-concrete` | `#E5E7E1` | The one alternate section ground (the plans section) |
| Surface | `ls-surface` | `#FFFFFF` | Panels, the free-plan side, alternate sections |
| Line | `ls-line` | `#D3D6CE` | Decorative rules between sections and rows. Never the only edge of a control |
| Field | `ls-field` | `#7D828A` | The border of every control and input: 3.9:1 on Surface, 3.4:1 on Chalk, 3.1:1 on Concrete |
| Ink | `ls-ink` | `#1A1C20` | Text, the hub's buttons, the focus ring |
| Ink 2 | `ls-ink-2` | `#3D414A` | Secondary text (9.1:1 on Chalk) |
| Muted | `ls-muted` | `#565B64` | Small print and labels (6.1:1 on Chalk, 5.5:1 on Concrete) |
| Night | `ls-night` | `#1A1C20` | The footer band |
| On night | `ls-on-night` | `#C9CCD2` | Text on Night (10.6:1) |

Radii, three for three kinds of thing: `ls-sign` 4px (signboards, swatches), `ls-control` 8px (buttons, inputs),
`ls-panel` 14px (panels, the phone's stage). One shadow, `ls-phone`, only under drawn phones.

### The app colour

One set of runtime variables carries the app's colour. Tailwind utilities read them: `bg-app`, `text-app-ink`, `text-app-text`,
`bg-app-hover`, `bg-app-wash` (9% tint), `border-app-line`.

| Variable | Meaning | Rule |
|---|---|---|
| `--app` | Signature colour: primary buttons, signboards, swatches, the closing band | From the app's `globals.css` |
| `--app-ink` | Text on `--app` | At least 4.5:1 on `--app` |
| `--app-text` | The colour used for accent *text* on light grounds (step numbers, the app name, inline links) | At least 4.5:1 on Chalk and on `bg-app-wash` |
| `--ls-focus` | Focus ring colour | Ink by default. Never set it to `--app` unless that is 3:1 against the ground |

The hub leaves `--app` at Ink, so its buttons are ink. Inside each app's band on the hub, `appStyle(p)` from `products.ts` sets the
three variables on the wrapper. Each app sets them once on `:root`:

| App | `--app` | `--app-ink` | `--app-text` | Source in the app |
|---|---|---|---|---|
| OrderLink | `#F2A93B` | `#1B1F3B` | `#87560C` | marigold, ink, marigold-deep |
| InvoiceLink | `#2B47C9` | `#FFFFFF` | `#2B47C9` | ink (the blue) |
| BookLink | `#C2255C` | `#FFFFFF` | `#A01C4B` | rose, rose-dark |
| LearnLink | `#0B6E6E` | `#FFFFFF` | `#0B6E6E` | lagoon (fixed: an academy's own colour never reaches the marketing pages) |
| PropertyLink | `#33309A` | `#FFFFFF` | `#33309A` | adire |
| SchoolLink | `#6B1D33` | `#FFFFFF` | `#6B1D33` | blazer |
| CareLink | `#0F766E` | `#FFFFFF` | `#0F766E` | scrub |

Measured: text on `--app` 5.5 to 11.4:1; `--app-text` on Chalk 4.9 to 10.1:1. Marigold itself is only 1.8:1 on Chalk, which is
why OrderLink's accent text, links and focus ring never use it.

Known clash: LearnLink `#0B6E6E` and CareLink `#0F766E` are 1.1:1 apart. They never sit next to each other in hub order, and every
swatch is labelled by name. Changing either is an owner decision, because it changes that app.

## Type

Loaded with `next/font/google`. Two families, clearly distinct:

- **Bricolage Grotesque** (variable, axes `opsz` and `wdth`) for headings, signboards, the wordmark and step numbers. Set narrow
  (`wdth` 78 to 92) and heavy (700 to 800), it reads like hand-painted shop lettering, and its optical sizes keep it clean small.
- **Onest** (400, 500, 600; `latin` and `latin-ext`, which carries the ₦ sign) for everything else. Open shapes and clear figures
  that stay readable at 13px on a cheap screen.

The rule that keeps the family together: **the frame speaks LinkSuite, the screens speak the app.** Headings, body, header and
footer use the two suite faces on every site. Inside a hero visual or a screenshot, the app's own face and colours stay, because
that is what the product looks like.

Scale, as utilities in `suite.css` (rem; body 1rem/1.6):

| Utility | Size | Face | Use |
|---|---|---|---|
| `ls-h1` | 2.5 to 4.75 (fluid), lh 0.98 | Bricolage 800, wdth 80 | The page's one H1 |
| `ls-h2` | 1.75 to 2.75 (fluid), lh 1.04 | Bricolage 800, wdth 84 | Section titles, app names |
| `ls-h3` | 1.375, lh 1.15 | Bricolage 700, wdth 92 | Sub-headings, plan names |
| `ls-sign-text` | 1.5 to 1.625, lh 1.02 | Bricolage 800, wdth 78 | Signboards only |
| `ls-h4` | 1.0625, lh 1.35 | Onest 600 | Feature and step titles, FAQ questions |
| `ls-lead` | 1.1875 to 1.25, lh 1.5 | Onest 400 | The paragraph under a heading |
| `ls-body` | 1, lh 1.6 | Onest 400 | Body |
| `ls-small` | 0.875, lh 1.45 | Onest | Notes, footer |
| `ls-label` | 0.8125, lh 1.3 | Onest 600 | Small labels, in sentence case |
| `tnum` | | | Tabular figures wherever numbers line up |

No all-caps, no letter-spaced eyebrows, no italic or coloured single word in a headline. Lines stay under about 70 characters
(`max-w-[38rem]` for leads, `max-w-[60ch]` for answers).

## Spacing and layout

- 4px base; Tailwind's spacing scale.
- `ls-container`: max 72rem, gutters 16px (phone), 24px (from 640px), 32px (from 1024px).
- `ls-section-y`: 56px top and bottom on phones, 88px from 768px.
- Mobile first and left aligned. Only one breakpoint changes structure for most sections (`md`, 768px): one column becomes two.
- Sections are separated by a 1px `ls-line` rule. Content is not chopped into identical cards.

```
Phone                              Desktop (lg)
+------------------------------+   +----------------------------------------------+
| [mark] LinkSuite   Apps Plans |   | [mark] LinkSuite        Apps Plans Questions |
+------------------------------+   +------------------+---------------------------+
| Run your business             |   | Run your         | What do you run?          |
| on one account.  (h1)         |   | business on one  | +----------+ +----------+ |
| lead, three short facts       |   | account.         | | sign     | | sign     | |
| What do you run?              |   | lead, facts      | +----------+ +----------+ |
| +--------------------------+  |   |                  | ... 7 signs + "not sure"  |
| | A food business or shop  |  |   +------------------+---------------------------+
| | OrderLink: orders on WA  |  |   | Seven apps (h2)                              |
| +--------------------------+  |   | name, job, points, [Start free] | [phone   ] |
| ... seven signboards          |   |                                  | [rising  ] |
+------------------------------+   +----------------------------------------------+
```

## Header and footer

Both are in `hub-web/src/components/suite/` and are identical on every site. Neither is sticky: on a phone the screen belongs to
the content.

**`SuiteHeader({ app?, nav, action? })`**

- A skip link to `#main`, first in the tab order. Every page using the header renders `<main id="main" tabIndex={-1}>`.
- Hub: the mark and "LinkSuite". App: the mark alone, as a link to the hub labelled "Part of LinkSuite: all apps", then the app's
  name in `text-app-text`, linking to `/`.
- Up to four quiet links (`nav`). Mark the less important ones `wide: true` to hide them below 640px. On app sites: `Plans`
  (`/#plans`), `Log in`, and the one action, `Start free`, in the app colour.
- Same-origin links use `next/link`; the hub link is a plain `<a>` because it is another origin.

**`SuiteFooter({ app?, links, note? })`**

- A Night band (`ls-on-dark`, so focus rings turn white).
- First column: the mark and "LinkSuite" (hub) or "Part of LinkSuite" (app), linking to the hub, with one sentence.
- "The apps" (hub) or "Other LinkSuite apps" (app): every sibling with its colour swatch, linking to its business landing page.
- On the hub only, a "Sign in" column (id `sign-in-list`) with each app's business sign-in page. LearnLink and PropertyLink use
  `/teach/login` and `/studio/login`, because the hub speaks to tutors and agents, not learners and home seekers.
- The site's own links (`links`): plans, questions, legal pages when they exist.

Sibling links where apps work together belong in the page body as well as the footer: BookLink mentions InvoiceLink for corporate
clients, SchoolLink mentions LearnLink for lessons, and so on. Link by `appHref(product, product.landingPath)`.

## Section patterns

All in `hub-web/src/components/suite/sections.tsx`. Each is a `<section>` labelled by its own `<h2>`.

| Pattern | Component | Notes |
|---|---|---|
| Hero | page-specific | H1 in `ls-h1` (what the business gets, in the owner's words), one `ls-lead`, the main action, one line of facts ("Free to start, no card needed"), and the hero visual. On the hub the picker replaces the action and visual |
| Who it's for | `Section` with a short list | Name the trades plainly: "Salons, barbers, braiders, make-up artists and studios". No personas or invented quotes |
| How it works | `Section` + `StepList` | Only for a real sequence (3 or 4 steps), so the numbers mean something |
| Features with screenshots | `Section` + `FeatureList` | 3 or 4 items. Each `visual` is a crop of a real screen on a `bg-app-wash` stage |
| Plans | `Section id="plans"` + `PlansBlock` | Free plan and "Paid plans add", no amounts. Free side carries the main action |
| FAQ | `Section id="faq"` + `Faq` | Native `<details>`; the question is plain text in `<summary>` |
| Final call to action | `FinalCta` | Full-width signboard band in `--app`, one `ls-h2` and one inverse button |

The plans block's fixed words: "Free plan", "Free for as long as it fits. No card needed.", "Paid plans add", "Upgrade inside
{App} when you outgrow the free plan.", "Each plan and its price is shown in naira inside {App} before you pay." The free and paid
lines come from `products.ts` (`free`, `paid`), which mirrors the seeded plans in
`api/src/Saas.Infrastructure/Persistence/Support.cs`.

## Buttons, links, cards, focus

- `lsButton(variant, size)` in `ui.ts`. 48px tall (`lg`) or 44px (`md`), 8px radius, Onest 600, sentence case.
  - `app`: the one main action per view, in `--app` (Ink on the hub). Label says what happens: "Start CareLink free",
    "Set up your school free".
  - `secondary`: Surface with a 1px `ls-field` ring (3:1 or more), ink ring on hover.
  - `inverse`: on Night or app-coloured bands.
  - `quiet`: header links.
- No arrows appended to labels. No more than two buttons side by side.
- `lsLink`: inline links are always underlined, in `--app-text`.
- WhatsApp buttons (apps only): `#147A3B` with white text (5.4:1) and the WhatsApp glyph, never `#1DA851` (white on it is 3.1:1).
- "Cards": the only boxed things are plan panels, the phone stage and form panels. They use a 1px `ls-line` border, 14px radius,
  no shadow.
- Focus: one ring everywhere, `outline: 3px solid var(--ls-focus); outline-offset: 3px`. Ink on light grounds, white on Night
  (`ls-on-dark`), `--app-ink` on app-coloured bands (`ls-on-app`). Never remove it.
  `suite.css`'s `:focus-visible` rule is unlayered, so it also beats each app's own (layered) focus rule inside the signed-in app.
  Each app sets `--ls-focus` in its `globals.css` (on `:root`, plus a class for its dark sidebars or bars) so every ring stays 3:1
  against its ground; never edit the copied `suite.css` for this.
- Hit targets at least 44 by 44px, including footer links.

## The one motion moment

- **Hub:** a wet-paint sheen sweeps once across each signboard at load, left to right, one after another (`.ls-paint`, a
  `::after` sheen, 700ms, 80ms apart, starting at 150ms). Nothing else on the page moves on its own.
- **Never hide content behind an animation.** The sign is fully drawn from the first frame; only the sheen moves, with no fill
  mode. A browser that pauses animations (a background tab, a battery saver) must still show everything. Don't use clip-path,
  opacity or transform entrances on content.
- **App sites:** the same `.ls-paint` keyframes, once, on the hero visual only (or on its steps in order, with `--ls-i` set to
  each step's index). Remove every other entrance animation (`animate-reveal`, `animate-step-in`, fades on sections).
  `.ls-paint` sets `position: relative` outside any layer, which overrides Tailwind's `absolute` or `fixed` on the same element.
  Put it on an unpositioned element, and move any positioning to a wrapper.
- Responses to the visitor's own actions are allowed: colour change on hover, the FAQ plus turning into a cross.
- **Reduced motion:** `.ls-paint` only animates inside `prefers-reduced-motion: no-preference`; with reduce, everything is drawn
  at once. Smooth scrolling is turned off too. CSS only, no JavaScript.

## Images and screenshots

- **No stock photos and no abstract blobs.** Product imagery is the product.
- **Draw screens in code** from the app's own components and data shapes: `OrderTicket`, `InvoicePreview`, `AppointmentCard`,
  `LearningPath`, `ListingCard`, the report card, the wristband. Put a phone screen in `PhoneFrame` (from `suite/phone.tsx`), or a
  paper document on a `bg-app-wash` stage. The hub's seven drawings are in `hub-web/src/components/hub/screens.tsx`; each one
  names the real screen it mirrors.
- Phones rise from the floor of their stage: a 26rem stage with `overflow-hidden`, the phone cropped at the bottom. Keep the screen's
  key action (Send order on WhatsApp, Pay now, Pay deposit and book) inside the visible top 23rem.
- **Sample data:** Nigerian names, places and realistic naira amounts for the business's own goods (a plate of jollof, a year's
  rent). Never ratings, review counts, customer counts or testimonials. Never a LinkSuite plan price.
- **Alt text:** `PhoneFrame` takes a `label` that says what the screen shows ("BookLink on a phone: booking knotless braids at
  Kemi's Hair Studio for Saturday at 10:30, with a deposit."). The drawing inside is `aria-hidden`.
- **If raster screenshots are added later:** `next/image` with `sizes`, explicit width and height, AVIF or WebP, no wider than
  twice the displayed width, `priority` only on the hero image, captured from the real app at 390px (phone) and 1280px (desktop),
  with sample data as above.

## SEO and metadata

Per site, in `src/app/layout.tsx`:

```ts
export const metadata: Metadata = {
  metadataBase: new URL(SITE_URL),
  title: { default: "CareLink: clinic software for Nigerian clinics and hospitals", template: "%s | CareLink" },
  description: "One or two plain sentences, 140 to 160 characters, naming the trade and Nigeria.",
  applicationName: APP_NAME,
  robots: { index: true, follow: true },
  openGraph: { ...baseOpenGraph, url: "/", title: DEFAULT_TITLE, description: DEFAULT_DESCRIPTION },
  twitter: { ...baseTwitter, title: DEFAULT_TITLE, description: DEFAULT_DESCRIPTION },
};
```

- `baseOpenGraph = { type: "website", siteName: APP_NAME, locale: "en_NG" }` and `baseTwitter = { card: "summary_large_image" }`
  live in `src/lib/site.ts` (see the hub's). Next merges metadata **shallowly**: a page that sets `openGraph` or `twitter` replaces
  the root object, so those pages always spread the base and set `title`, `description` and `url` along with any `images`.
- Each page has its own `title` (sentence case, the job first: "Invoices and payment links for freelancers") and `description`.
  The landing page uses `title: { absolute: ... }`.
- **Canonical:** never put `alternates.canonical` in the root layout. Every page that doesn't set its own inherits it, so every
  course, listing or store would claim to be the home page. Each indexable page sets its own canonical (the landing page `"/"`);
  noindex and signed-in layouts set `alternates: {}`.
- Titles target real searches: "invoice app Nigeria", "school management software Nigeria", "clinic software Lagos".
- **Open Graph image:** `src/app/opengraph-image.tsx`, 1200 by 630, an `ImageResponse` with the headline, the app name on a
  signboard plate in `--app`, and the suite mark with "Part of LinkSuite". The hub's is the template. Next adds `og:image` and
  `twitter:image` automatically.
- **Icons:** `src/app/icon.svg` (the app's mark) and `src/app/apple-icon.tsx` (180 by 180 `ImageResponse`). Delete the shared
  `favicon.ico`, which is byte-identical in every app.
- **Robots:** `src/app/robots.ts` allows `/`, disallows the signed-in and token areas the app has (`/app`, `/dashboard`,
  `/studio`, `/learn`, `/parent`, `/student`, `/portal`, `/me`, `/admin`, `/invite`, `/reset-password`), and points at the sitemap.
  Rules match by prefix, so anchor each one as a pair, `/app$` and `/app/`: a bare `/app` also blocks `/apple-icon`, and on apps
  with top-level tenant slugs (`/{store}`, `/{business}`) a bare `/support` blocks a tenant called `support-foods`.
- **Sitemap:** `src/app/sitemap.ts` lists `/`, the business landing (`/teach`, `/for-agents`), `/signup` and public product pages
  from the API (stores, courses, academies, listings, areas, agents, public booking pages). Use `generateSitemaps` past 50,000.
- **Not indexed:** login, forgot and reset password, invite, per-tenant sign-in, certificates (`robots: { index: false }`).
- **Structured data**, rendered with `JsonLd` from `sections.tsx` (it escapes `<`):
  - hub: Organization, WebSite and an ItemList of the seven SoftwareApplications (`hubJsonLd()` in `site.ts`);
  - each app's landing: `appJsonLd(product)` from `suite.ts` (SoftwareApplication, free Offer in NGN, publisher LinkSuite);
  - product pages keep the business's own data (a store item's or course's price in an `Offer`). Removing LinkSuite prices never
    removes a business's prices.
- `<html lang="en-NG">` on every site; `locale: "en_NG"` in Open Graph.

## Copy

- Sentence case everywhere: headings, buttons, labels, the footer.
- Lead with the job in the owner's words: "Take orders on WhatsApp", "Get paid for every invoice", "Your clinic, from front desk
  to ward". Plain verbs; buttons say what happens ("Start BookLink free", "Find your app", "Sign in to CareLink").
- Name trades, not segments: "food vendors, boutiques and small shops", not "SMEs".
- No prices. Say "free to start", "no card needed", "upgrade inside the app as you grow", "prices are shown in naira before you
  pay". Free-plan limits (50 bookings a month, 50 students) are fine; amounts are not.
- No invented social proof: no testimonials, logos, ratings, "trusted by thousands" or sample review counts, until real ones exist
  and the owner approves them.
- Claims must be true of the product today. "One sign-in" is true (one user account across products). "Money goes straight to
  you" is true for apps paying into the business's own Paystack; LearnLink marketplace sales are paid out by LinkSuite, so say so.
- No exclamation marks, no "simply", no "seamless", no em-dash fragments in labels.

## Accessibility checklist (WCAG 2.2 AA)

- [ ] One `<h1>` per page; headings in order; each section labelled by its `<h2>`.
- [ ] Landmarks: `header`, `nav` with an `aria-label` (Main, footer navs labelled by their headings), `main id="main"
      tabIndex={-1}`, `footer`. The skip link works.
- [ ] Text contrast 4.5:1 or more (3:1 for 24px+ or 19px bold). Use `--app-text`, never `--app`, for accent text on light grounds.
- [ ] Non-text contrast 3:1: control borders use `ls-field` (or the app's field token), focus rings 3:1 against their ground.
- [ ] Focus visible on every interactive element; never `outline: none` without a replacement.
- [ ] Targets 44 by 44px or larger.
- [ ] Every input has a visible `<label>`; placeholders are hints only and use `muted`, not `faint`.
- [ ] Forms: on a failed submit an error summary (`role="alert"`) is always shown, listing every problem, and focus moves to the
      first `[aria-invalid="true"]` field. Field errors are tied with `aria-describedby`.
- [ ] Mode switches (password or code) move focus to the new step's first field.
- [ ] Tabs: `role="tablist"`, roving `tabIndex` (active 0, others -1), Left and Right arrows move and select, `aria-controls` only
      for a rendered panel. Port `admin-web/src/components/ui.tsx:365`. Radio-like choices use native radio inputs.
- [ ] Drawn screens: `role="img"` with a descriptive label; the drawing `aria-hidden`; no focusable element inside `aria-hidden`.
- [ ] Status is a word as well as a colour.
- [ ] Motion: only `.ls-paint`, and nothing moves with reduced motion.
- [ ] Works at 320px wide and at 200% zoom with no horizontal scroll.
- [ ] `lang="en-NG"`.

## Porting guide (for each app)

Follow in order. "Copy" means byte-for-byte from `hub-web`; do not edit copied files. Keep each file's existing line endings.
Replace `<App>` with your app's name and use your row of the app colour table.

### 1. Copy the shared files

| From `hub-web` | To your app |
|---|---|
| `src/app/suite.css` | `src/app/suite.css` |
| `src/components/suite/ui.ts`, `suite-mark.tsx`, `suite-header.tsx`, `suite-footer.tsx`, `sections.tsx`, `phone.tsx` | `src/components/suite/` (same names) |
| `src/lib/products.ts` | `src/lib/suite.ts` (the suite components import `@/lib/suite`) |

### 2. Tokens

In `src/app/globals.css`, directly after `@import "tailwindcss";`:

```css
@import "./suite.css";

:root {
  --app: #0f766e;      /* your row: --app */
  --app-ink: #ffffff;  /* --app-ink */
  --app-text: #0f766e; /* --app-text */
}
```

Do not rename or remove the app's own tokens; the signed-in app still uses them. Add a `--color-field` token for input borders at
3:1 or more and use it in `inputClass` (values: web `#8a909c`, invoice `#8a909c`, booking `#8e8499`, course `#7f9294`, property
`#7f8c84`, school `#7c8696`, care `#7d8c88`), and change `placeholder:text-faint` to `placeholder:text-muted`. Check your focus
ring is 3:1 against its ground (OrderLink: use ink `#1b1f3b`, not marigold). WhatsApp buttons become `#147a3b` (hover `#106331`).

### 3. Fonts and the public route group

Create `src/app/(site)/layout.tsx`, so only public pages load the suite faces:

```tsx
import type { ReactNode } from "react";
import { Bricolage_Grotesque, Onest } from "next/font/google";

const display = Bricolage_Grotesque({ subsets: ["latin"], axes: ["opsz", "wdth"], variable: "--font-ls-display-face", display: "swap" });
const text = Onest({ subsets: ["latin", "latin-ext"], weight: ["400", "500", "600"], variable: "--font-ls-text-face", display: "swap" });

export default function SiteLayout({ children }: { children: ReactNode }) {
  return (
    <div className={`${display.variable} ${text.variable} flex min-h-dvh flex-col bg-ls-chalk font-ls-text text-ls-ink`}>{children}</div>
  );
}
```

Move into `(site)/` (URLs do not change): the landing `page.tsx`, and for LearnLink `teach/page.tsx`. PropertyLink creates
`(site)/for-agents/page.tsx`, its business landing (the hub links to it). Leave `login`, `signup` and the rest where they are.

### 4. Root layout

- `<html lang="en-NG">`.
- Metadata as in "SEO and metadata": `metadataBase`, title default and template, description, `applicationName`,
  `openGraph` and `twitter` built from `baseOpenGraph` and `baseTwitter` (add them to `src/lib/site.ts` or your `config.ts`, with
  `siteName: APP_NAME`). No canonical in the root layout; see "Canonical".
- Fix every page that sets `openGraph` to spread `baseOpenGraph` and set title, description and url.

### 5. The landing page

```tsx
import { appJsonLd, productByKey } from "@/lib/suite";
const app = productByKey("care"); // your key: orders, invoices, bookings, courses, property, school, care

export const metadata: Metadata = { title: { absolute: "..." }, description: "...", alternates: { canonical: "/" } }; // the landing page only

export default function Landing() {
  return (
    <>
      <JsonLd data={{ "@context": "https://schema.org", ...appJsonLd(app) }} />
      <SuiteHeader
        app={app}
        nav={[{ href: "/#plans", label: "Plans", wide: true }, { href: "/login", label: "Log in" }]}
        action={{ href: app.startPath, label: "Start free" }}
      />
      <main id="main" tabIndex={-1} className="flex-1 outline-none">
        {/* Hero: ls-h1, ls-lead, lsButton("app") "Start <App> free" + optional secondary, a facts line, the hero visual with .ls-paint */}
        <Section id="who" title="Who it's for">...</Section>
        <Section id="how" title="How it works"><StepList steps={...} /></Section>
        <Section id="features" tone="surface" title="..."><FeatureList items={[{ title, text, visual }]} /></Section>
        <Section id="plans" tone="concrete" title="Free to start. Upgrade inside <App> as you grow.">
          <PlansBlock appName={app.name} free={app.free} paid={app.paid} start={{ href: app.startPath, label: "Start free" }} />
        </Section>
        <Section id="faq" title="Questions"><Faq items={...} /></Section>
        <FinalCta title="..." action={{ href: app.startPath, label: "Start free" }} />
      </main>
      <SuiteFooter app={app} links={[{ href: "/#plans", label: "Plans" }, { href: "/login", label: "Log in" }, { href: "/support", label: "Help" }]} />
    </>
  );
}
```

- The hero visual is one composition of your real components (see "Images and screenshots"), with `.ls-paint` once. Delete other
  entrance animations.
- Keep `products.ts` copy in sync: if your free or paid lines change, change them in `hub-web/src/lib/products.ts` and recopy.
- Remove every amount from public copy, including "Free for up to ₦..." lines and plan tables. Keep free-plan limits.
- Mention one sibling app where they really connect, linked by `appHref`.

### 6. Remove pricing

- Delete `src/app/pricing/` where it exists (care-web, school-web, property-web).
- In `next.config.ts`, add:

  ```ts
  redirects() {
    return [{ source: "/pricing", destination: "/#plans", permanent: true }];
  },
  ```

  LearnLink: `destination: "/teach#plans"`. PropertyLink: `destination: "/for-agents#plans"`.
- Rename any `id="pricing"` section to `id="plans"` and change `#pricing` links to `#plans`.
- `grep -rn '/pricing\|#pricing' src`. Public links go to `/#plans`. Links inside the signed-in app (upgrade prompts, import
  limits, fee settings) go to the in-app plan page instead (for example `/app/settings`, `/dashboard/billing`,
  `/studio/billing`).

### 7. Auth pages (login, signup, forgot, reset, invite)

- Keep the app's own `AuthShell` look; this is product UI.
- Add under the form: a link to the hub, `<a href={hubUrl}>` with `<SuiteMark size={16} />` and the text "Part of LinkSuite".
- `AuthShell` renders `<main id="main" tabIndex={-1}>`.
- Metadata: a unique title; `robots: { index: false }` on login, forgot, reset and invite. Signup stays indexable.
- **Security fix, required in web, invoice-web, booking-web and course-web:** replace `nextPath()` in
  `src/components/auth-shell.tsx` with CareLink's origin-checked version (`care-web/src/components/auth-shell.tsx:46-56`): reject
  anything not starting with `/` or containing `\`, resolve against `window.location.origin`, and accept only the same origin.
- Forms: always show the error summary (`ErrorBox`, `role="alert"`), even when the error belongs to one field, and after a failed
  submit focus the first `[aria-invalid="true"]` field. After switching between password and code, focus the new first field.

### 8. Icons, OG image, robots, sitemap

- `src/app/icon.svg` (your mark), `src/app/apple-icon.tsx` (copy the hub's and draw your mark), delete `src/app/favicon.ico`.
- `src/app/opengraph-image.tsx`: copy the hub's and replace the headline and the seven plates with your headline and one signboard
  plate in your `--app`, plus "Part of LinkSuite".
- `src/app/robots.ts` and `src/app/sitemap.ts` as in "SEO and metadata".

### 9. Environment and tabs

- Add to `.env.example` every variable `suite.ts` reads: `NEXT_PUBLIC_HUB_URL`, and the seven `NEXT_PUBLIC_*LINK_URL` values
  (copy the block from `hub-web/.env.example`). Without them, links fall back to localhost.
- The deploy build must pass them too (`deploy/web.Dockerfile` ARGs and `x-web-build-args` in `docker-compose.vps.yml`); tell the
  controller if they are missing rather than editing deploy files yourself.
- Any `Tabs` component on a public page: port the arrow-key pattern from `admin-web/src/components/ui.tsx:365`.

### 10. Check

- `npx tsc --noEmit` and `npm run lint` pass.
- At 360px: the H1, the lead and "Start free" are on the first screen; no horizontal scroll; no amount anywhere on public pages
  (`grep -rn "₦" src/app/\(site\)` shows only sample business data inside drawn screens).
- Tab through the page: skip link first, focus visible everywhere, footer links reachable.
- With reduced motion turned on, nothing moves.
- `/pricing` redirects to `#plans`; `/robots.txt`, `/sitemap.xml`, `/opengraph-image` and `/icon.svg` respond.
