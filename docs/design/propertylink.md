# PropertyLink design plan

Product: a Lagos-first property site where seekers find rentals, homes for sale and short-lets from **checked** agents, and
agencies manage listings, leads and stays. Contract: `docs/PROPERTY_API.md`. App: `property-web`, port 4100.

## Who and what

- **Seekers** (mostly on phones, often on mobile data) are burned by fake listings, hidden fees and "pay ₦10k to inspect" scams.
  Their primary job: find a real place and know what it will truly cost to move in.
- **Agents and landlords** (phone first, some on laptops in an office) post listings, answer WhatsApp leads, run viewings.
- **Moderators** clear the queue fast on a laptop.

The brand's one promise: **what you see is real, and what you pay is all here.**

## Colour

| Token | Hex | Use |
|---|---|---|
| Asphalt | `#17201C` | Text, the studio's dark rail, map labels (a green-tinted charcoal, not neutral black) |
| Plaster | `#EEF1EC` | App background: cool sage-grey of a fresh rendered wall. Never cream. |
| Surface | `#FFFFFF` | Panels, inputs, listing photos' frames |
| Adire | `#33309A` | Brand accent, from Yoruba indigo-dyed cloth: primary actions, links, selected filters, focus ring, map price pins. White text on it. |
| Seal | `#0E7C4A` | Verified only: the verified seal, "documents checked", approved states. Never used for buttons. |
| Ochre | `#A86A12` | Waiting states: Pending review, held dates, expiring soon |
| Brick | `#B3261E` | Errors, reports, rejected, destructive, and the "never pay before inspecting" warning icon |
| Line | `#D6DCD4` | Borders, table rules, the dotted leader lines in the cost ledger |
| WhatsApp | `#1DA851` | Only on "Chat on WhatsApp" buttons, with the WhatsApp glyph (brand colour, darkened for white text contrast) |

Contrast: Adire on white 11:1, Seal on white 5.5:1, Ochre on white 4.6:1 (text ≥ 14 px semibold only). Dark mode is not in scope.

## Type

- **Archivo** (variable, with width axis): display and numbers. Headlines at `wdth 112` (wide, like estate signboards); prices and
  money at `wdth 100`, weight 700, `tabular-nums`. The price is the loudest thing on every card and listing.
- **Instrument Sans**: all UI and body text, 400/500/600, 16 px body on phones, line-height 1.55, max 72 characters per line.
- Scale (rem): 0.8125 / 0.875 / 1 / 1.125 / 1.375 / 1.75 / 2.25 / 3 (home hero only). Labels are sentence case; no all-caps eyebrows.

## The memorable element: the true-cost ledger

Every listing page has a **move-in ledger**: a narrow, receipt-like panel listing the price and each cost on its own line, joined to
its amount by a dotted leader (like a printed bill), ending in a heavy rule and the **move-in total** in Archivo 700. Under it, a
fixed line with a Brick shield icon: "Never pay before you have inspected the property and met the agent." For short-lets the
ledger becomes the live quote (nights × price, cleaning, total) as dates are picked. This ledger is where the design spends its
boldness; everything around it stays quiet. Listing cards don't repeat it (the card data has no costs); they stay simple.

The verified seal: a small rounded-square Seal-green badge with a check and the word "Verified", next to agent and agency names
everywhere. Unverified agents show nothing (no grey "unverified" shaming), but the agency page explains how verification works.

## Layout

- **Home** (`/`): no stock-photo hero. A full-width Plaster band with a single large search: purpose tabs (Rent / Buy / Short-let),
  a location field with suggestions from `/api/locations`, beds and a max price, one Adire button. Below: "Featured in Lagos"
  (featured cards), a row of Lagos area chips linking to area guides, then a short three-line "How we keep listings real"
  (verified agents, duplicate-photo checks, listings expire unless confirmed) — plain sentences, not numbered steps.
- **Search** (`/rent/[location]`, `/buy/[location]`, `/shortlet/[location]`; `/rent` = all): desktop is a two-pane split, list left
  (cards in one column with photo left, details right) and the map right, sticky. "Search this area" button appears when the map
  moves. Phones: list by default, a floating "Map" toggle switches to full-screen map; filters open in a bottom sheet. URL holds
  every filter, so results are shareable and server-rendered.
- **Listing** (`/listings/[agency]/[slug]`): gallery first (one large photo plus a strip; full-screen viewer with swipe), then a
  two-column body on desktop: facts, description, amenities, map, area link on the left; the ledger, agent card (WhatsApp, call,
  enquiry form, book a viewing or pick short-let dates) sticky on the right. Phones: a bottom bar with price and "Contact".
- **Agent / agency pages**: header with name, seal, rating, phone and WhatsApp; their live listings; reviews.
- **Area guide** (`/areas/[slug]`): typical yearly rent by bedrooms as a simple horizontal bar list (median + count), latest
  listings, child areas.
- **Seeker** (`/me`): saved homes, saved searches (with frequency toggle), enquiries, viewings, trips (`/trips/[id]` is also the
  Paystack return).
- **Studio** (`/studio`): Asphalt left rail from 1024 px, bottom bar below. Dashboard (live listings vs plan, new enquiries, today's
  viewings, stays this week, expiring soon), listings table, the 5-step wizard (Basics → Location pin → Costs → Photos → Review),
  enquiries inbox, viewings (agenda grouped by day + weekly windows editor), short-lets (calendar per listing with closed dates and
  stays), earnings and payouts, verification, boosts, stats (Pro), team and billing (shared core pages as in the other apps).
- **Admin** (`/admin`): moderation queue (duplicate pairs side by side), verification (documents opened as blobs), reports,
  hidden reviews, locations, payouts.

Alignment: left-aligned everywhere; numbers right-aligned in tables and the ledger. Radius: 6 px on inputs and buttons, 10 px on
panels, 0 on photos inside cards (photos run to the card edge), full round only on chips and pins. One soft shadow token for the
sticky panel and sheets only; cards use a 1 px Line border, not shadows.

## Components worth getting right

- **Listing card**: photo (4:3, `cardUrl`, lazy), price in Archivo with period ("/year", "/night"), title, beds·baths as icons with
  numbers, area and city, verified seal on the agency line, "Featured" Adire tag when featured. The whole card is one link.
- **Map**: MapLibre GL with OpenFreeMap style `https://tiles.openfreemap.org/styles/liberty`; price pins as small Adire pills with
  white text ("₦3.5m"), selected pin inverts; clusters as Adire circles with counts. Attribution visible.
- **Location pin step** (wizard): area picker first, map centred on the area, drag the pin; lat/lng shown small under the map.
- **Cost rows** (wizard): kind select + label + amount; the ledger preview updates live beside it on desktop, below on phones.
  Label containing "inspection" shows the API's error inline.
- **Photo uploader**: drag-drop and camera on phones, per-photo progress, Processing shimmer until Ready (poll every 3 s), Failed with
  the message, duplicate warning naming the other agency, drag to reorder.
- **Viewing picker**: next 14 days as a horizontal day strip, times as chips; sign-in sheet if needed.
- **Short-let date picker**: two-month calendar; booked and closed nights struck through; min-nights hint; the ledger as live quote.

## Copy

Plain, direct, Nigerian English without slang. Prices as `₦3,500,000/year`, short form on pins (`₦3.5m`). Buttons say what happens:
"Send enquiry", "Book viewing", "Reserve and pay", "Submit for review", "Renew listing". Empty states direct the next action
("No saved homes yet. Tap the heart on any listing to keep it here."). Errors say what to fix. Never "Oops".

## Quality floor

Responsive to 360 px, visible focus rings (Adire, 2 px offset), `prefers-reduced-motion` respected (the only motion: the sheet slide
and the gallery swipe), images with alt text from the listing title, form labels always visible, tap targets ≥ 44 px.

## Review against the brief (what I changed)

- First draft used a sunset-orange accent; changed to Adire indigo because orange sits too close to the generic warm-clay default and
  carries no meaning here. Adire is local, distinctive, and leaves green free for "verified" and WhatsApp.
- First draft had a photo hero on the home page; replaced with search-first, because seekers arrive with intent and stock photos
  undercut the "real listings" promise.
- Dropped numbered "1 · 2 · 3 how it works" steps: the trust points aren't a sequence.
