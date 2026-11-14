# InvoiceLink invoice designs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Spec:** `docs/superpowers/specs/2026-11-10-invoice-designs-design.md` (binding; read it before your task).

**Goal:** Ten professional invoice designs (five free, five premium), an editable letterhead (bigger business name, a chosen line under it, contact row, title, font, colours, show/hide fields, signature line, footer), and paid logo, signature and stamp images, with the web preview and the PDF drawing each design the same way.

**Architecture:**

- **API** (`api/`): `BusinessSettings` gains the letterhead fields; `InvoiceTemplates` lists ten keys; a new plan limit `brand_images` gates image uploads and image rendering; `DocumentPdfModel` carries the letterhead; `QuestPdfRenderer` gets one composer per design over shared block helpers, with embedded fonts.
- **Web** (`invoice-web/`): `PreviewDoc` carries the same letterhead; `InvoicePreview` gets one component per design over shared blocks, matched to the PDF; Settings gets an "Invoice design" card; the free generator gets the free letterhead fields.

**Tech stack:** ASP.NET Core 10, EF Core + Npgsql, QuestPDF 2026.9.1, xUnit v3 + Testcontainers; Next.js 16.4.5, React 19, Tailwind 4.

---

## Global Constraints

**Process**

- Do not commit to the project's git. Snapshot through the plan workspace's shadow repo instead (the controller does this; implementers never run git).
- Never kill all node or dotnet processes. Never touch other projects' processes or containers.
- Stop this checkout's dev API (`Saas.Api.exe` on port 5080) before building the API, because it locks the DLLs; if you cannot, report it instead of killing anything else.
- Do not run `next build` and do not start dev servers; `npx tsc --noEmit` and `npm run lint` in `invoice-web/` are the web checks.
- C# strings containing `\n` are written with the Edit or Write tools, never by shell string replacement. Files keep their line endings and stay UTF-8 without BOM; from PowerShell write with `[IO.File]::WriteAllText(path, text, (New-Object Text.UTF8Encoding $false))`, never `Set-Content` or `Out-File`.
- Tests: `dotnet build api/tests/Saas.IntegrationTests`, then `api/tests/Saas.IntegrationTests/bin/Debug/net10.0/Saas.IntegrationTests.exe -class Saas.IntegrationTests.<ClassName>` (repeat `-class` for several). `dotnet test --project … -- --filter-class` does not work in this repo. Docker must be running (Testcontainers). Do not run the full suite (about an hour); run the classes you touched plus `InvoiceFlowTests` and `InvoicePhase2Tests`.
- Test-first for API behaviour: write the failing test, watch it fail, then make it pass. Save RED and GREEN output under the plan workspace as `t<N>-red.log` and `t<N>-green.log`.

**Copy**

- Sentence case everywhere; no all-caps labels outside the status stamp; no exclamation marks. Staff and product words as in the existing app.

**Templates (exact keys and names, in this order)**

| Key | Name | Premium |
|---|---|---|
| `classic` | Classic | no |
| `modern` | Modern | no |
| `minimal` | Minimal | no |
| `bold` | Bold | no |
| `elegant` | Elegant | no |
| `executive` | Executive | yes |
| `corporate` | Corporate | yes |
| `ledger` | Ledger | yes |
| `studio` | Studio | yes |
| `prestige` | Prestige | yes |

**Letterhead fields on `BusinessSettings` (exact names, limits, defaults)**

| Property | Column | Type / values | Limit | Default |
|---|---|---|---|---|
| `Tagline` | `tagline` | string, nullable | 100 | null |
| `RegistrationNumber` | `registration_number` | string, nullable | 40 | null |
| `HeaderLine` | `header_line` | `tagline` \| `address` \| `registration` \| `none` | | `address` |
| `SecondaryColor` | `secondary_color` | `#rrggbb`, nullable | 7 | null |
| `LogoSize` | `logo_size` | `small` \| `medium` \| `large` | | `medium` |
| `FontStyle` | `font_style` | `modern` \| `serif` \| `sans` | | `modern` |
| `DocumentTitle` | `document_title` | `Invoice` \| `Tax invoice` \| `Bill` \| `Pro forma invoice` \| `Statement of account` | | `Invoice` |
| `ShowTaxId` | `show_tax_id` | bool | | true |
| `ShowRegistrationNumber` | `show_registration_number` | bool | | true |
| `ShowBankDetails` | `show_bank_details` | bool | | true |
| `ShowSignature` | `show_signature` | bool | | false |
| `SignatureName` | `signature_name` | string, nullable | 80 | null |
| `SignatureRole` | `signature_role` | string, nullable | 80 | null |
| `SignatureImageUrl` | `signature_image_url` | string, nullable | | null |
| `StampImageUrl` | `stamp_image_url` | string, nullable | | null |
| `FooterText` | `footer_text` | string, nullable | 160 | null |

Migration data step: for existing rows, `header_line = 'none'` when `address` is null or blank (the column default `address` covers the rest).

**Plan limit**

- `LimitKeys.BrandImages = "brand_images"`. Free 0, Pro 1, Business 1.
- Plans are seeded insert-only, so a migration data step also sets `brand_images` on the existing InvoiceLink plan rows (only when the key is missing), and updates their `features` arrays: replace `"3 templates"` with `"5 templates"`, replace `"All 5 templates"` with `"All 10 templates"`, and append `"Your logo, signature and stamp on documents"` to Pro when absent. The seed in `Support.cs` gets the same values for fresh databases.
- Plan message for image uploads on Free (HTTP 402, `PlanLimitException`): `Your logo, signature and stamp are part of the Pro plan. Upgrade to add them.`
- Rendering rule: images (logo, signature, stamp) are included in documents, public DTOs and PDFs only while `HasFeatureAsync(brand_images)` is true for the business. They stay stored either way. The owner's settings DTO always returns the stored URLs plus `brandImagesAvailable`.

**Endpoints**

- `POST /api/business/logo` (exists): now 402 on Free.
- `DELETE /api/business/logo`, `POST`/`DELETE /api/business/signature`, `POST`/`DELETE /api/business/stamp`: admin only, same checks as the logo upload (PNG, JPEG or WebP by magic bytes, up to 5 MB, key `{tenantId}/business/{kind}-{guid}{ext}`, `StoredFile.Purpose` `business-logo` | `business-signature` | `business-stamp`). DELETE works on every plan (removing is never paid) and returns 204.

**Rendering rules (both PDF and web)**

- Anatomy order: letterhead (logo, name, chosen line) → title → number and dates → bill to → line table → totals with balance due → payment details and contact row → notes and terms → signature block (when `ShowSignature`) → footer line.
- Business name: 1.75rem on the web, 20-22pt in the PDF (Bold and Studio larger by design); wraps to at most two lines.
- Line under the name: `tagline` → the tagline; `address` → the address on one line; `registration` → `Reg. no. {RegistrationNumber}`; `none` or an empty value → nothing.
- Contact row: heading "Contact"; email, phone, and the address when it is not the line under the name; tax ID (`Tax ID: …`) when `ShowTaxId` and set; registration number (`Reg. no. …`) when `ShowRegistrationNumber`, set, and not already in the letterhead.
- Bank details print only when `ShowBankDetails` and set (invoices only, as today).
- Title: `DocumentTitle` on invoices; quotes "Quote"; receipts "Receipt".
- Logo sizes, as maximum box (web / PDF): small 2.5rem × 7rem / 36pt × 100pt; medium 3.5rem × 10rem / 50pt × 140pt; large 4.75rem × 13rem / 68pt × 190pt. Aspect kept.
- Signature block: right-aligned, 200pt wide rule labelled "Authorised signature", name and role below; signature image (max 48pt tall) sits on the rule; stamp image (max 80pt square) beside it, overlapping the rule by about a third of its height.
- Footer line: printed above the page number on every PDF page; on the web at the bottom of the sheet.
- Colours: accent = `BrandColor`; second accent = `SecondaryColor` or, when null, a 35% darker shade of the accent. Text on accent fills is white or ink `#111827`, whichever has the higher contrast; accent-coloured text on white uses the accent darkened until it reaches 4.5:1 on white.
- Fonts: `modern` = Hanken Grotesk, `serif` = a serif family, `sans` = a neutral sans; all OFL, and every family must contain the naira sign U+20A6 (verify with a glyph check, not assumption). The PDF embeds the same families as the web (TTF files under `api/src/Saas.Infrastructure/Invoices/Fonts/`, registered once at startup via `QuestPDF.Drawing.FontManager`). Elegant and Prestige use the serif family for headings whatever `FontStyle` says.

---

## Review Focus

- The web preview and the PDF of each design agree on layout, block order, colours and type family (Task 2-5 reviewers compare the two code paths for the designs in scope).
- Plan gating holds everywhere: uploads 402 on Free; images never leak into public DTOs, previews or PDFs on Free (including after a downgrade); premium designs gated as today.
- No regressions to numbering, totals, stamps, receipts, quotes, recurring invoices, the generator's rate limit, or the public pages.

---

### Task 1: API letterhead data, plan limit and image uploads

**Files:**

- Modify: `api/src/Saas.Domain/Invoices/Invoices.cs` (BusinessSettings fields)
- Modify: `api/src/Saas.Application/Invoices/InvoiceDtos.cs` (`InvoiceTemplates` ten keys; `BusinessDto`, `UpdateBusinessRequest`, `PublicBusinessDto`, `PreviewBusiness` and their validators gain the letterhead fields; `BusinessDto` gains `SignatureImageUrl`, `StampImageUrl`, `BrandImagesAvailable`)
- Modify: `api/src/Saas.Application/Billing/PlanService.cs` (`LimitKeys.BrandImages`)
- Modify: `api/src/Saas.Application/Invoices/InvoiceSetupService.cs` (update, uploads, deletes, gating)
- Modify: `api/src/Saas.Application/Invoices/InvoiceWorkspace.cs` and the services that build `PublicBusinessDto` (images only when allowed)
- Modify: `api/src/Saas.Api/Endpoints/InvoiceEndpoints.cs` (new endpoints)
- Modify: `api/src/Saas.Infrastructure/Persistence/Support.cs` (seed values)
- Modify: `api/src/Saas.Infrastructure/Persistence/AppDbContext.cs` (column config if needed)
- Create: migration `InvoiceDesigns` (columns, defaults, the header-line data step, the plan rows data step)
- Create: `api/tests/Saas.IntegrationTests/InvoiceDesignSettingsTests.cs`
- Modify: `invoice-web/src/lib/types.ts` (mirror the DTO changes only; no UI)

- [ ] **Step 1: Tests first.** In `InvoiceDesignSettingsTests`, cover:
  - Settings round-trip of every letterhead field; validation rejects an unknown `HeaderLine`, `LogoSize`, `FontStyle`, `DocumentTitle`, a bad `SecondaryColor`, and each over-limit string (field errors on the right field).
  - Defaults on a new business match the table (title `Invoice`, font `modern`, logo `medium`, shows true, signature false, header line `address`).
  - `GET /api/invoice-templates` returns the ten keys in order, with `available` false for the five premium ones on Free and true on Pro.
  - Free plan: `POST /api/business/logo`, `/signature`, `/stamp` → 402 with the exact plan message; Pro: 200 with a URL; a non-image file → 400; `DELETE` → 204 on Free and Pro and clears the URL.
  - Images in documents: a Pro business with a logo, signature and stamp sees all three URLs in the public invoice DTO; after switching to Free (cancel the subscription in the test) the public DTO has no image URLs, while `GET /api/business` still returns them with `brandImagesAvailable: false`; back on Pro they return.
  - The seeded Free and Pro InvoiceLink plans carry `brand_images` 0 and 1 and the updated feature strings.
- [ ] **Step 2: Implement** the fields, validators, DTOs, limit key, endpoints, gating, seed and migration (with both data steps) until the tests pass.
- [ ] **Step 3:** Mirror the DTO changes in `invoice-web/src/lib/types.ts`; `npx tsc --noEmit` and `npm run lint` clean.
- [ ] **Step 4:** Run `InvoiceDesignSettingsTests`, `InvoiceFlowTests`, `InvoicePhase2Tests`; save RED and GREEN logs.

### Task 2: PDF letterhead, fonts and the five free designs

**Files:**

- Modify: `api/src/Saas.Application/Invoices/InvoiceWorkspace.cs` (`DocumentPdfModel` gains: business name, header line text, contact lines, tax and registration lines, bank details or null, title, font style, logo bytes source and size, secondary colour, signature block (show, name, role, image), stamp image, footer text; `PdfModel` builds them under the rendering rules)
- Modify: `api/src/Saas.Application/Invoices/InvoiceService.cs`, `QuoteService.cs`, `PublicDocumentsService.cs` (callers; the generator's preview request accepts the new text fields and ignores any image fields)
- Modify: `api/src/Saas.Infrastructure/Invoices/PdfRenderer.cs` (replace the single style-record layout with shared block helpers plus one composer per design for `classic`, `modern`, `minimal`, `bold`, `elegant`; embedded fonts; logo, signature and stamp loading through the existing safe loader)
- Create: `api/src/Saas.Infrastructure/Invoices/Fonts/*.ttf` (+ licence files) and embed them
- Create: `api/tests/Saas.IntegrationTests/InvoiceDesignPdfTests.cs` (add a PDF text-extraction package to the test project, e.g. UglyToad.PdfPig)

- [ ] **Step 1: Tests first.** For each of the five free designs and each kind (invoice, quote, receipt): valid PDF; extracted text contains the business name, the chosen header line text, the title (`Tax invoice` when set; `Quote`; `Receipt`), the footer line, "Contact" and the email; does not contain the email directly under the name in the letterhead block (assert the header line is the second text line of page 1). With `ShowBankDetails` false the bank details are absent. With `ShowSignature` true "Authorised signature", the name and the role are present. Each `FontStyle` renders and the text contains `₦` correctly (extracted `U+20A6`). A Pro business's PDF with a PNG logo, signature and stamp renders (no exception, larger byte size than without); the same business on Free renders without them. The generator preview PDF accepts the new fields and ignores an inline image field.
- [ ] **Step 2: Implement.** Match the web preview layouts described in the spec's design table for the five free designs, under the Global Constraints rendering rules. Verify each font file contains U+20A6 with a small glyph check in the test or a one-off script, and note the result in the report.
- [ ] **Step 3:** Add a test-only helper that writes page-1 PNGs of all five designs (invoice with sample data, logo, signature and stamp) to `.superpowers/sdd/2026-11-10-invoice-designs/pdf-previews/` using QuestPDF image generation, run it, and list the files in the report.
- [ ] **Step 4:** Run `InvoiceDesignPdfTests`, `InvoiceDesignSettingsTests`, `InvoiceFlowTests`, `InvoicePhase2Tests`; save logs.

### Task 3: PDF for the five premium designs

**Files:**

- Modify: `api/src/Saas.Infrastructure/Invoices/PdfRenderer.cs` (composers for `executive`, `corporate`, `ledger`, `studio`, `prestige` over the Task 2 helpers)
- Modify: `api/tests/Saas.IntegrationTests/InvoiceDesignPdfTests.cs`

- [ ] **Step 1: Tests first.** The Task 2 assertions for each premium design and kind, on a Pro business; a Free business requesting a premium design still gets 402 on save (existing gating) and an invoice saved under Pro still renders its premium design after a downgrade.
- [ ] **Step 2: Implement** the five designs as described in the spec's design table (Executive accent sidebar in the header; Corporate meta strip with number, dates and balance due between two thin accent rules, signature block forced on; Ledger ruled rows and a double-ruled totals box with the registration number prominent in the letterhead; Studio geometric accent block in the top corner with the second accent shape behind, oversized name, a coloured "Balance due" panel; Prestige framed logo or monogram (initials when there is no logo), serif, second-accent rules).
- [ ] **Step 3:** Extend the PNG helper to all ten designs; run it; list files.
- [ ] **Step 4:** Run the same test classes; save logs.

### Task 4: Web preview for the five free designs, matched to the PDF

**Files:**

- Modify: `invoice-web/src/components/invoice-preview.tsx` (split into shared blocks plus one component per design; `PreviewDoc` gains the letterhead; enlarged name; line under the name; contact row; title; font family; logo sizes; signature block with images; footer line)
- Create: `invoice-web/src/components/invoice-designs/` (one file per design plus `blocks.tsx`) if the split warrants it
- Modify: `invoice-web/src/components/doc-view-parts.tsx` (`previewFromDoc`, `businessForPreview` carry the new fields)
- Modify: `invoice-web/src/lib/templates.ts` (colour helpers: second accent fallback, contrast-safe text and accent text)
- Modify: `invoice-web/src/app/layout.tsx` or a fonts module (the serif and sans families via `next/font`, same families as the PDF)

- [ ] **Step 1:** Rebuild Classic, Modern, Minimal, Bold and Elegant to match their Task 2 PDF composers (read `PdfRenderer.cs` and the PNGs in `.superpowers/sdd/2026-11-10-invoice-designs/pdf-previews/`). Every screen that uses `InvoicePreview` keeps working (editor, invoice and quote detail, public invoice and quote, generator, landing strip).
- [ ] **Step 2:** `npx tsc --noEmit`, `npm run lint` clean. Report which visual differences from the PDF remain and why (if any).

### Task 5: Web preview for the five premium designs, catalogue and thumbnails

**Files:**

- Modify: `invoice-web/src/components/invoice-preview.tsx` / `invoice-designs/*` (Executive, Corporate, Ledger, Studio, Prestige matched to Task 3)
- Modify: `invoice-web/src/lib/templates.ts` (ten entries in the Global Constraints order)
- Modify: `invoice-web/src/components/template-picker.tsx` (thumbnails for ten; the locked note becomes "The five premium templates are part of Pro. Upgrade to use them." with the billing link; the picker lays out ten without crowding on a phone)
- Modify: `invoice-web/src/app/(site)/page.tsx` (templates section: "Ten invoice templates, in your colours"; intro says five come with the free plan and five more with Pro; strip shows all ten with "Free plan" / "Paid plans"; FAQ and plan copy that says 3 or 5 templates updated)
- Modify: any other copy that names template counts (`rg -n "3 templates|5 templates|five templates|Bold and Elegant" invoice-web/src`)

- [ ] **Step 1:** Build the five designs, the catalogue, thumbnails and copy.
- [ ] **Step 2:** `npx tsc --noEmit`, `npm run lint` clean.

### Task 6: Settings "Invoice design" card and the free generator

**Files:**

- Modify: `invoice-web/src/app/app/settings/page.tsx` (new "Invoice design" card with a live `InvoicePreview` of sample lines in the business's default design: design picker; brand colour and second accent; logo, signature and stamp uploads with remove, locked on Free with "Add your logo, signature and stamp with Pro" and a link to `/app/billing`; logo size; font style; document title; line under your name; slogan and registration number fields; show tax ID / registration number / bank details / signature line; signature name and role; footer line. Move the existing logo card and the default template and brand colour inputs here so nothing is duplicated)
- Modify: `invoice-web/src/components/generator.tsx`, `invoice-web/src/lib/draft.ts` (the free letterhead fields; no image inputs; the Pro upsell where the logo would go linking to `/signup`; the draft remembers the choices; the preview PDF request sends the new fields)
- Modify: `invoice-web/src/components/doc-editor.tsx` (the editor's live preview reflects the business's letterhead)

- [ ] **Step 1:** Build the card and the generator changes. Form patterns as elsewhere in the app (labels, field errors, error summary, focus on the first invalid field).
- [ ] **Step 2:** `npx tsc --noEmit`, `npm run lint` clean.

### Task 7: Docs

**Files:**

- Modify: `docs/INVOICES_API.md` (new fields, endpoints, limit key, rendering rule for images)
- Modify: `docs/design/invoicelink.md` (a "Document designs" section: the ten designs, the anatomy, the letterhead rules, colours and fonts)

- [ ] **Step 1:** Write both sections from the code as built.

---

## After the tasks (controller)

- Click-through in the browser: all ten designs in the editor preview and as PDFs; Settings card on Free (locked images) and Pro; the generator; the public invoice page; phone and desktop widths.
- Final whole-branch review, one fix wave, one scoped re-review.
- Production build of `invoice-web`, then ask the owner before committing and deploying.
