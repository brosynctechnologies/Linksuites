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
