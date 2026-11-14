# InvoiceLink design plan

**Subject.** Freelancers, consultants and small agencies (Nigeria first) who bill clients, chase payment and hand records to an accountant.
**Primary job.** Make a document the client takes seriously, then see at a glance who owes what.
The product's real object is the **paper document**: invoices, quotes and receipts. The app is a desk; the documents sit on it.

## Palette

| Name | Hex | Role |
|---|---|---|
| Graphite | `#1E2430` | Text, sidebar |
| Desk | `#EDF0F4` | App background: a cool grey desk the white documents sit on |
| Paper | `#FFFFFF` | Documents and panels |
| Ink blue | `#2B47C9` | Brand accent: primary actions, links, focus (fountain-pen ink, deeper than default UI blue) |
| Paid green | `#13795B` | Paid, accepted, positive profit |
| Overdue red | `#B83227` | Overdue, declined, losses, destructive actions |
| Amber | `#A86A12` | Partially paid, expiring soon |
| Rule | `#D9DEE6` | Borders, table rules |

The business's own `brandColor` still colours its documents (preview and PDF); ink blue is only InvoiceLink's chrome.

## Type

- **Fraunces** (display serif, optical size on): page titles, document titles ("Invoice"), and the big amounts (balance due, dashboard money figures). Weight 500-650; it gives amounts the gravity of a printed figure.
- **Hanken Grotesk** (UI and body): everything else, 400/500/600, `tabular-nums` for every amount column.
- Scale (rem): 0.8125, 0.875, 1, 1.125, 1.375, 1.75, 2.5, 3.5. Body 1rem / 1.55. Sentence case labels, no all caps.

## Layout

```
Desktop ≥1024
┌──────────┬────────────────────────────────────────────┐
│ graphite │ Invoices                      [New invoice]│
│ sidebar  │ tabs: All  Draft  Sent  Overdue  Paid       │
│ business │ ────────────────────────────────────────────│
│ switcher │ INV-0042  Bola Ltd   26 Oct   ₦96,750  Paid│  table rows, right-aligned amounts
│ nav      │ ...                                        │
└──────────┴────────────────────────────────────────────┘

Invoice view / editor
┌──────────┬───────────────────────────┬───────────────┐
│ sidebar  │  ┌─ paper document ─────┐  │ actions panel │
│          │  │  [PAID stamp]         │  │ balance       │
│          │  │  lines, totals        │  │ payments      │
│          │  └───────────────────────┘  │ activity      │
└──────────┴───────────────────────────┴───────────────┘
Mobile: sidebar becomes a bottom bar (Home, Invoices, Clients, More); actions panel stacks above the document as a sticky action row.
```

Left-aligned app. Money columns right-aligned with tabular figures. Lists are tables or ruled rows, not card grids.

## The one memorable thing

**The document on the desk, with a rubber stamp.** Every invoice/quote preview is a real-looking sheet of paper (white, A4 proportion on desktop, subtle paper shadow, generous margins) on the grey desk. Status is shown on the document as an ink stamp — slightly rotated, double-ruled border, uppercase letterforms inside the stamp only (PAID, OVERDUE, VOID, ACCEPTED), in the status colour at ~85% opacity — matching the PDF. Everywhere else status is a quiet text badge.

The landing hero is the free generator itself: the paper document filling in live as the visitor types, beside a short headline. The product demonstrates itself; no stock illustration.

## Principles

- Calm and exact. Accountants read this; numbers must line up and never wrap awkwardly.
- Radius by hierarchy: documents 4px (paper is nearly square), panels 12px, inputs/buttons 8px, badges 999px. Shadow only on the paper and on floating elements.
- Status colours always paired with words.
- Motion only in response to actions (stamp lands with a brief scale when an invoice becomes Paid; modal and sheet open). Respect `prefers-reduced-motion`.
- Visible focus: 3px ink-blue ring, 2px offset.
- Avoid: all-caps eyebrows, "→" in buttons, decorative gradients, middle-dot meta strings where layout can separate the facts, identical shadowed cards for everything.

## Document designs

The documents (invoices, quotes, receipts) come in ten designs. Every one is something a client, an auditor or a bank accepts
without a second look: the designs change the styling, never the order a client reads in. Spec:
`docs/superpowers/specs/2026-11-10-invoice-designs-design.md`. API fields: `docs/INVOICES_API.md` ("Letterhead").

### The ten

| Key | Plan | Character |
|---|---|---|
| `classic` | Free | Plain business letterhead: logo and name left, title and meta right in the accent, a 2px accent rule under the header, tinted table head |
| `modern` | Free | Confident colour band: full-width accent band with white or ink text and the logo on a white chip, a second-accent strip under it, ink table head, zebra rows |
| `minimal` | Free | Quiet and white: light large title, hairline rules only; the balance-due figure is the only colour on the page |
| `bold` | Free | Poster-like: a 10pt accent edge down the left of every page, a larger extra-bold name, a very large accent title, a filled accent total bar |
| `elegant` | Free | Formal and centred: serif headings, the italic title framed by fine double accent rules, centred totals |
| `executive` | Pro | Boardroom: an accent sidebar (left 40% of the header) holds the logo, name and line; second-accent table head; totals in a boxed panel |
| `corporate` | Pro | Bank and firm style: a meta strip (number, dates, balance due) between two thin accent rules, a grid table |
| `ledger` | Pro | Accountant's ledger: compact letterhead with the registration number boxed top right, a ruled title bar, every row ruled, tabular figures, totals in a double-ruled box |
| `studio` | Pro | Creative agency: a geometric corner shape (accent quarter circle over a second-accent block), an oversized name, an open table, balance due in an accent card |
| `prestige` | Pro | Luxury, heritage: logo or a monogram of the business's initials in a fine double accent frame, centred serif, second-accent rules, serif figures |

Premium designs show a "Pro" pill and a lock in the picker; the locked note reads "The five premium templates are part of Pro.
Upgrade to use them." A document saved in a premium design keeps it after a downgrade. Each design's web component
(`invoice-web/src/components/invoice-designs/<key>.tsx`) opens with a comment block giving its exact sizes, rules and fills;
that block is the reference for both renderers.

### Anatomy (every design, in this order)

1. **Letterhead**: logo (paid plans), business name, the line under the name.
2. **Title** and **meta**: number ("Invoice no.", "Quote no." or "Receipt no." whatever the title), issue date, due date or
   "Valid until" (receipts: date paid, invoice, method, reference).
3. **Parties**: "Bill to" ("Prepared for" on quotes, "Received from" on receipts) with the status stamp on the right.
4. **Items table**: Description, Qty, Price, Amount; a line's discount and tax print small under its description.
5. **Totals**: Subtotal, Discount, each tax, Shipping, then on invoices a **Total** row, Amount paid (when anything is paid),
   and the emphasised **Balance due**. Invoices show Balance due even when nothing is paid. On quotes the emphasised figure is
   Total. Receipts list Invoice total and Paid before (an ordinary row each), headline **Amount paid** (the payment the
   receipt acknowledges) and print **Balance remaining** as an ordinary row under it. Amounts taken off use the minus sign
   (−, U+2212) on the web and in the PDF.
   **Signature block** (when switched on, in every design): beside the totals, on their left and bottom-aligned, so it sits
   with the amounts it authorises (on narrow web sheets it drops under them). A 200pt rule labelled "Authorised signature",
   name and role centred under it. The signature image (paid, at most 48pt tall) sits on the rule; the space for it is only
   kept when there is a signature or stamp image, otherwise there is 18pt (1.5rem) to sign in. The stamp image (paid, at
   most 80pt square) sits at the left end of the rule, mostly outside the block, two thirds above the rule, as a real stamp
   would. On the free plan the rule, name and role still print, so the document can be signed by hand. When the switch is
   off, the signature name and role are not sent to clients at all (public DTO and PDF model).
6. **Payment details** (bank details; invoices only) beside **Contact** (email, phone, address, tax ID, registration number).
7. **Notes** and **Terms**, side by side.
8. **Footer line** (centred, small, muted, with a rule in the design's colour), then "Made with InvoiceLink — free invoices
   in 60 seconds" on the free plan (the same words on the web and in the PDF) and, in the PDF, the number and "Page n of m"
   on every page.

### Page fit (PDF)

- A short invoice fits on one page in every design, with real data: a long business name, a client address of several
  lines, three-line bank details, notes, terms, the signature with name, role, signature image and stamp, a large logo.
  `InvoiceDesignPdfTests` renders that in all ten designs (with and without images) and checks the page count, and does the
  same for receipts with a 45-character Paystack reference and a 100-character manual one.
- Totals, the signature beside them, payment details, contact, notes and terms are one group that moves to the next page
  whole when it doesn't fit (QuestPDF `PreventPageBreak`); the totals row with the signature never splits. Only a group taller
  than a page (very long notes) splits, after the totals.
- User text is never laid out in an auto-sized cell: meta values (references, numbers), names, emails, account numbers and
  bank details wrap, breaking inside a long unbroken value when they must (web: `overflow-wrap: anywhere`). Meta rows wrap
  onto a second line; Corporate's strip runs four columns to a row; the stacked meta keeps its labels and wraps the values in
  its 200pt (16.75rem) box.
- Spacing is tighter than the first build: bill to 12pt below, info blocks 15pt above with 10pt under their rule and between
  rows, table rows 6pt padding, Modern and Executive bands 24/16pt, Bold's title 36pt, Prestige's header rules 6pt.

### Letterhead rules

- **Business name** is the largest text in the letterhead: 1.75rem on the web, 21pt in the PDF. Bold sets it at 2.25rem/27pt
  extra-bold, Studio at 2.5rem/30pt extra-bold and Executive (in its narrow sidebar) at 1.5rem/18pt. It is **never cut**: it
  wraps, and a name that would take more than two lines steps down one size (to 80%, rounded; Studio steps to 22pt). The rule
  is the same on both sides (`QuestPdfRenderer.NameSize`, `nameSizePt` in `model.ts`): a name longer than 3 × width ÷ size
  characters, with the width the name has in that design in points (283pt in two-column headers, 515pt centred, Executive's
  sidebar 180pt, Studio 345pt, Bold and Ledger less the logo and Ledger's registration box).
- **Studio** stacks the logo above its oversized name, so there the logo is at most the medium height; its title and the
  number and dates share one row across the full width below the corner shape.
- **The line under the name** is one choice: the slogan; the address on one line (line breaks become ", "); the registration
  number as "Reg. no. RC 1234567"; or nothing. A choice with no value behind it prints nothing.
- **Email and phone never sit under the name.** They are in the Contact block, with the address when it isn't the line under
  the name, "Tax ID: …" when shown, and "Reg. no. …" when shown and not already in the letterhead (Ledger's box counts as the
  letterhead).
- **Logo** (Pro and Business only) keeps its aspect inside a maximum box: small 2.5 × 7rem / 36 × 100pt, medium 3.5 × 10rem /
  50 × 140pt, large 4.75 × 13rem / 68 × 190pt. Every design looks complete without one.
- **Title** is the business's choice on invoices (Invoice, Tax invoice, Bill, Pro forma invoice, Statement of account);
  quotes say "Quote" and receipts "Receipt".
- Hidden values (tax ID, registration number, bank details switched off) are removed by the server before either renderer
  sees them.

### Colours

- **Accent** is the business's brand colour. **Second accent** is its optional second colour; when none is set it is the
  accent with each RGB channel multiplied by 0.65 (35% darker), so every design has two related colours to work with.
- The second accent appears only where a design calls for it: Modern's strip under the band; Executive's rule under the
  header, table head and balance-due rule and figure; Studio's block behind the quarter circle; Prestige's rules (under the
  name, table head, around the balance due, the signature rule and the footer rule). Classic, Minimal, Bold, Elegant,
  Corporate and Ledger use the accent alone.
- **Text on a colour fill** (bands, sidebars, table heads, bars, cards) is white or ink `#111827`, whichever has the higher
  WCAG contrast with the fill. It is always that solid colour, never faded with opacity: Modern's meta labels and the line
  under the name in Modern and Executive used to be 75% and 85% and fell below 4.5:1 (about 3.5:1 on `#2563eb`).
- **Accent-coloured text on white** (titles, figures) uses the colour darkened in 5% steps until it reaches 4.5:1 on white,
  or ink if no step does. A pale brand colour still prints legible titles.
- Tints (Classic's 10% table head, Modern's 12% balance block, Corporate's 10%, Ledger's 6%) are the accent at that opacity
  over white.
- Fixed neutrals: graphite `#1E2430` for text, slate `#4A5262` for the line under the name and secondary text, muted
  `#5B6474` for labels, rules `#D9DEE6` and `#E8EBF0`, Ledger's row rule `#C3CAD5`, fill `#F6F7F9`.
- The status stamp keeps the app's status colours (paid green, overdue red, amber, slate) and is the only uppercase text on
  the document.

### Fonts

| Font style | Family (web and PDF) |
|---|---|
| `modern` (default) | **Inter** |
| `serif` ("Classic serif") | **Noto Serif** |
| `sans` ("Clean sans") | **Noto Sans** |

- **Why these.** The naira sign (₦, U+20A6) must print in the font itself, in the PDF as well as on screen. Hanken Grotesk, the
  app's UI face, has no ₦ glyph, so `modern` is Inter. Noto Serif and Noto Sans both have it, are neutral, and cover
  latin-ext. All three are SIL OFL, so they can be embedded.
- These fonts are for the document sheet only. The app chrome keeps Fraunces and Hanken Grotesk.
- Elegant and Prestige set their headings (name, title, balance-due label) in Noto Serif whatever the style; Prestige also sets
  its figures in it. Body text follows the business's choice.
- **Web:** `next/font/google`, loaded with the `latin-ext` subset (Google serves ₦ only there), applied to the sheet.
- **PDF:** static TTFs (Light to ExtraBold, plus Italic) embedded in `Saas.Infrastructure/Invoices/Fonts/` and registered with
  QuestPDF once per process (`InvoiceFonts`). Noto Sans, then Noto Serif, are fallbacks for any missing character.
- Tabular figures: the web uses `tabular-nums` everywhere. The PDF asks for them in Noto Sans and Noto Serif but not in Inter,
  whose tabular digits have no Unicode value and would copy out of the PDF as garbage. The PDF also turns off Inter's
  contextual alternates for the same reason (it swaps the hyphen in "INV-0042").

### Keeping the web preview and the PDF the same

- **One design, one look.** Each design exists twice: a React component in `invoice-web/src/components/invoice-designs/` (the
  editor, detail pages, public pages, generator, Settings card and landing strip all use it) and a composer in
  `api/src/Saas.Infrastructure/Invoices/PdfRenderer.cs`. Both are built from the same shared blocks (letterhead, meta, bill to,
  line table, totals, info blocks, signature, footer) and follow the comment block at the top of the web file.
- **Units.** The PDF converts the web sizes at 1rem = 12pt and 1px = 0.75pt, on A4 with 40pt margins (bands and Bold's edge run
  to the page edge).
- **Same values from the same rules.** `resolveDoc` (`invoice-designs/model.ts`) on the web and `InvoiceWorkspace.Compose`
  on the API work out the title, the line under the name, the contact row, totals, the signature and the footer the same way.
  The colour maths is written twice to the same formulas: `invoice-web/src/lib/templates.ts` and
  `Saas.Infrastructure/Invoices/DocumentColors.cs`.
- **Same families.** Both sides use Inter, Noto Serif and Noto Sans.
- **Same plan rule.** Images reach a renderer only when the plan allows them: public DTOs and the PDF model leave them out,
  and the owner's previews drop them when `brandImagesAvailable` is false.
- **Checking.** `QuestPdfRenderer.Build` returns the QuestPDF document, so page images of all ten designs can be rendered
  and compared with the web previews side by side.
