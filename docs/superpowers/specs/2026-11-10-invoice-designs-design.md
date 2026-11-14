# InvoiceLink: ten invoice designs and an editable letterhead

Date: 10 November 2026. Status: draft for the owner's approval.

## What the owner asked for

- Ten designs instead of five: five regular (free plan) and five premium (Pro and Business plans).
- Every design professional and standard: something a client, an auditor or a bank accepts without a second look.
- Much more editable, and logos everywhere, including the free generator.
- A bigger business name. The email under it goes; that line shows the business's slogan (what it does), its address or its registration number instead.

## Decisions (owner, 10 November 2026)

1. **The split.** The five existing designs (Classic, Modern, Minimal, Bold, Elegant) become free. Five new designs are premium. Nobody loses a design they use today.
2. **The line under the name.** The business picks one in Settings: slogan, address or registration number.
3. **Email and phone.** They move to a small contact row lower on the document, beside the payment details, so clients can still reach the business.
4. **What a business can change.** Brand colour plus an optional second accent colour; logo, with a size (small, medium, large); font style (modern, classic serif, clean sans); the document title; show or hide tax ID, registration number, bank details and a signature line; a custom footer line.
5. **Images are paid** (owner, 10 November 2026, second message). Uploading a logo, a signature image or a company stamp is a Pro and Business feature. Signature and stamp uploads are in this release.

## Principles

- **One design, one look.** The web preview and the PDF of a design must look like the same document: same header layout, same order of blocks, same colours, same type family. Today they drift (Bold and Elegant most), so the existing five are rebuilt to match too.
- **Standard invoice anatomy, every design.** Letterhead (logo, name, the chosen line), title, document number and dates, bill to, line table, totals with balance due, payment details, contact row, notes and terms, signature (if on), footer line. Designs vary the styling, never the order a client reads in.
- **Print-safe.** A4, 40pt margins, nothing important within 10mm of the edge, legible in greyscale, text at least 8pt in the PDF.
- **The business's colours, never broken contrast.** Brand and accent colours fill bands and rules; text on them is chosen automatically (white or ink) for at least 4.5:1. Accent text on white uses a darkened shade when the chosen colour is too light.
- **Copy rules (house style).** Sentence case, no all-caps labels outside the status stamp, no exclamation marks.

## The ten designs

All ten carry the full anatomy above and every customisation. "Accent" is the brand colour; "second accent" falls back to a shade of the brand colour when not set.

| # | Name | Plan | Character | Letterhead | Table and totals |
|---|---|---|---|---|---|
| 1 | Classic | Free | Plain business letterhead | Logo and name left, title right in accent, accent rule under | Tinted header row, accent total rule |
| 2 | Modern | Free | Confident, colour band | Full-width accent band, white text, logo on a white chip | Dark header row, zebra rows, accent total |
| 3 | Minimal | Free | Quiet, lots of white | Name left, light large title, no colour except the total | Hairline rules only |
| 4 | Bold | Free | Strong, poster-like | Very large accent title, thick accent edge on the left | Filled accent header, filled total bar |
| 5 | Elegant | Free | Formal, centred | Centred logo and name, serif, fine double accent rule | Accent underline header, centred totals block |
| 6 | Executive | Premium | Boardroom | Accent sidebar down the left third of the header holding logo, name and line; title and meta on the right | Second-accent header row, boxed totals panel |
| 7 | Corporate | Premium | Bank and firm style | Logo left, title and number top right, two thin accent rules framing the meta strip (number, dates, balance due) across the page | Grid table with light column rules, signature block standard |
| 8 | Ledger | Premium | Accountant's ledger | Compact letterhead, ruled ledger paper feel, registration number prominent | Every row ruled, tabular figures, totals in a double-ruled box |
| 9 | Studio | Premium | Creative agencies | Large geometric accent block in the top corner (second accent shape behind), oversized name | Card-style total ("Balance due" in a coloured panel) |
| 10 | Prestige | Premium | Luxury, heritage | Logo or monogram in a fine accent frame, serif, second accent used as a gold-like rule | Wide spacing, serif figures, thin rules |

Picker names, thumbnails and the landing page show which are premium ("Pro" pill); locked designs work as today (lock icon, link to billing).

## The letterhead

- **Business name enlarged** in every design: about 1.75rem on the web and 20-22pt in the PDF (Bold and Studio larger by design), wrapping cleanly up to two lines. The name is the trading name (`LegalName` field today).
- **The line under the name** (one, chosen in Settings, "Line under your name"):
  - **Slogan or what you do**: new field, up to 100 characters, e.g. "Bespoke tailoring for men and women".
  - **Address**: the existing address, on one line where it fits.
  - **Registration number**: new field "Registration number" (e.g. "RC 1234567"), shown as "Reg. no. RC 1234567"; free text up to 40 characters.
  - Default for existing businesses: the address if they have one, else nothing; once they fill in a slogan the setting is theirs to change.
- **Logo** (paid): shown in every design at the chosen size (small, medium, large; default medium), aspect kept, never stretched. On the free plan the letterhead has no logo; every design is drawn to look complete without one.

## Contact row

Email, phone and, when the address isn't the line under the name, the address, in one small row near the bottom beside payment details ("Contact" heading). The tax ID and registration number (when shown) sit in the same area unless the design places the registration number in the letterhead.

## Customisation (Settings, a new "Invoice design" card)

The card has a live preview of the business's default design with sample lines, updating as they change:

- **Design**: the existing default-template picker, now ten.
- **Brand colour** (exists) and **Second accent** (new, optional, `#rrggbb`).
- **Logo** (exists; moves into this card; paid) and **Logo size**: small, medium, large.
- **Font style**: modern (Hanken Grotesk-like sans), classic serif, clean sans. The PDF embeds matching open-licence fonts that include the naira sign (₦); the web uses the same families.
- **Document title**: Invoice, Tax invoice, Bill, Pro forma invoice, Statement of account. Quotes keep "Quote" and receipts keep "Receipt".
- **Show on documents**: tax ID, registration number, bank details, signature line (switches; defaults on for whatever is filled in, signature off).
- **Signature line**: "Authorised signature" rule with an optional name and role (e.g. "Ada Okafor, Director").
- **Signature image** (paid): a PNG, JPEG or WebP of a handwritten signature, drawn on the signature line. Transparent PNGs are recommended in the upload hint.
- **Company stamp** (paid): an image of the company stamp or seal, placed beside the signature, slightly overlapping the line as a real stamp would, at a fixed size, never covering totals or bank details.
- **Footer line**: up to 160 characters, e.g. "Thank you for your business." or legal text; printed on every PDF page above the page number.

Every plan can use the five free designs and all the text, colour, font, title, show/hide, signature-line and footer settings. Pro and Business add the five premium designs and the three images (logo, signature, stamp). Business data is read live, as today (changing settings changes how existing documents render).

### Images and the plan

- **Uploads**: logo (exists), signature and stamp (new) each have an upload and a remove action in the card. Same checks as the logo today: PNG, JPEG or WebP by magic bytes, up to 5 MB, stored under the business's own folder. Upload needs Pro or Business (a new plan limit key, `brand_images`, on in Pro and Business); on Free the controls show a lock and "Add your logo, signature and stamp with Pro" linking to billing.
- **Rendering**: images print only while the business is on Pro or Business. If a business downgrades, its images stay stored but stop printing (the web preview, public pages and PDFs all follow the same rule), and they come back on upgrade.
- **Businesses that uploaded a logo on the free plan before this release** would stop seeing it on documents under the same rule. Checked on production on 10 November 2026: one InvoiceLink business, no logos uploaded, so nobody is affected and no notice is needed.
- **Signature without an image**: the line, name and role still print, on every plan, so a document can be signed by hand after printing.

## The free generator (`/create`)

- Gets the free letterhead fields: slogan, registration number, line-under-name choice, font style, document title, second accent, signature line with name and role, footer line.
- No logo, signature or stamp images, because those are paid. Where the logo would go, the form shows "Add your logo, signature and stamp with InvoiceLink Pro" linking to sign-up.
- Offers the five free designs only, as today. The draft (kept in the browser) remembers the choices.
- The preview PDF endpoint accepts the new text fields; still rate limited.

## Data model (API)

`BusinessSettings` gains: `Tagline` (100), `RegistrationNumber` (40), `HeaderLine` (`tagline`, `address`, `registration`, `none`), `SecondaryColor` (`#rrggbb`, nullable), `LogoSize` (`small`, `medium`, `large`), `FontStyle` (`modern`, `serif`, `sans`), `DocumentTitle` (from the list above), `ShowTaxId`, `ShowRegistrationNumber`, `ShowBankDetails`, `ShowSignature` (bools), `SignatureName` (80), `SignatureRole` (80), `SignatureImageUrl`, `StampImageUrl`, `FooterText` (160). One migration with defaults that reproduce today's look as closely as possible (title "Invoice", font modern, logo medium, shows on, signature off, header line = address when set).

Endpoints: `POST`/`DELETE /api/business/signature`, `POST`/`DELETE /api/business/stamp`, and a new `DELETE /api/business/logo` (admin only, like the logo upload). Uploads return 402 with the plan message on Free.

`InvoiceTemplates.All` lists ten keys; `PremiumTemplates` gating is unchanged in mechanism. New limit key `brand_images` (Free 0, Pro 1, Business 1), seeded on the existing plans. Plan feature copy: Free "5 templates"; Pro "All 10 templates" and "Your logo, signature and stamp on documents".

Public and preview DTOs carry image URLs only when the plan allows them, so no screen has to apply the rule itself.

DTOs (business settings, public business, generator preview) and their TypeScript mirrors gain the same fields.

## Rendering

- **PDF (QuestPDF)**: one composer per design (header, parties, lines, totals, footer) sharing helpers for the common blocks, replacing the single "style record" layout. Fonts embedded and registered once. The renderer stays a pure layout of a pre-formatted model; the model gains the new fields.
- **Web preview (`InvoicePreview`)**: one component per design sharing the same blocks, matched to its PDF counterpart. Used everywhere it is today (editor, detail pages, public pages, generator, landing strip, settings card).
- **Thumbnails**: the picker's schematic thumbnails are redrawn for all ten.

## Testing and checks

- **API**: settings validation and defaults; template catalogue and gating (free can use the five free designs, premium needs Pro, downgrade keeps existing documents rendering); every design renders a valid PDF for invoice, quote and receipt, with and without images, for each font style; text in the PDF contains the business name, the chosen header line, the title and the footer line (text extraction); signature and stamp uploads and removals (Pro succeeds, Free gets 402, non-images rejected); images are left out of DTOs and PDFs on Free, including after a downgrade, and return on upgrade; the generator ignores image fields.
- **Visual review**: page images of all ten designs (from `QuestPdfRenderer.Build`) and the web previews side by side, at phone and desktop width, checked by the controller in the browser before review.
- **Web**: `tsc` and lint clean; production build of `invoice-web`.

## Out of scope

- Drawing a signature on screen (upload only).
- Per-invoice overrides of the design settings (the per-document template choice stays).
- Snapshotting business details onto issued invoices.
- Custom fonts uploaded by the business.
