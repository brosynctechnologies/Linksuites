# OrderLink design plan

**Subject.** Market sellers in Lagos (food vendors, fashion and beauty sellers) who take orders on WhatsApp.
Two audiences: the seller running a busy day from a phone, and the customer arriving from an Instagram bio on mobile data.
**Primary job.** Seller: see new orders and act on them fast. Customer: pick items and send the order in seconds.

## Palette

| Name | Hex | Role |
|---|---|---|
| Indigo ink | `#1B1F3B` | Text, nav, the seller app's dark rail |
| Cool paper | `#F6F7FA` | App background (cool, never cream) |
| Surface | `#FFFFFF` | Panels, inputs |
| Marigold | `#F2A93B` | Brand accent: primary actions, focus ring, "new order" highlights (market-umbrella yellow) |
| Leaf | `#1F7A4D` | Reserved for anything that opens WhatsApp. Green means "this goes to WhatsApp", nothing else |
| Hibiscus | `#C8372D` | Destructive actions, cancelled, out of stock |
| Line | `#E3E6EE` | Borders and dividers |

Text on marigold is indigo ink (contrast OK); never white on marigold. Storefronts still use each store's own `themeColor` for their accent; marigold is OrderLink's brand only.

## Type

- **Bricolage Grotesque** (display): page titles, prices, totals, order numbers, store name. Prices are the most-read thing in this product, so they get the character. Weights 600-800, tight tracking (-0.02em) at large sizes.
- **Onest** (UI and body): everything else, 400/500/600. `font-variant-numeric: tabular-nums` on amounts in tables and lists.
- Scale (rem): 0.8125, 0.875, 1, 1.125, 1.375, 1.75, 2.25, 3.25. Body 1rem / 1.55. Labels in sentence case, never all caps.

## Layout

```
Seller, desktop ≥1024                      Seller, mobile
┌────────┬─────────────────────────────┐   ┌──────────────────┐
│ indigo │  Page title         [action]│   │ Store ▾      ●3  │  ● = new orders
│  rail  │                             │   │ Today            │
│ store  │  New orders strip (tickets) │   │ ┌ ticket ┐┌ tic ┐│  horizontal scroll
│ nav    │  ──────────────────────────  │   │ list rows        │
│        │  list rows / form sections  │   ├──────────────────┤
└────────┴─────────────────────────────┘   │ Home Orders Prod More│ bottom bar
                                           └──────────────────┘
Storefront, mobile-first (max 560px column, centred on desktop)
┌──────────────────┐
│ cover            │
│ (logo) Name  Open│
│ [search        ] │
│ chips: categories│
│ ┌────┐ ┌────┐    │  2-col grid; price in Bricolage
│ └────┘ └────┘    │
│ [ 3 items · ₦9,000  Send order ] sticky bar (store colour)
└──────────────────┘
```

Left-aligned throughout; the storefront header is the only centred moment on desktop. Lists are rows with dividers, not a stack of identical cards. Panels only where grouping helps (forms, the order ticket).

## The one memorable thing

**The order ticket.** Orders look like a kitchen/market docket: a white slip with a notched (perforated) top edge, the order number big in Bricolage, items as lines, total at the foot. It appears in the dashboard's "New orders" strip, the order detail page, and the customer's order status page. Everything else stays quiet.

On the landing page the hero shows the product's characteristic moment: a phone with a storefront cart on the left and the generated WhatsApp message on the right (real copy from the product: "New order #1042 / 2 x Jollof rice (large) - ₦6,000 ..."). Static, no gimmicks; one gentle reveal on load at most.

## Principles

- Speed over decoration. Big tap targets (min 44px), sticky primary actions on mobile.
- Status uses colour + words, never colour alone: New (marigold), Confirmed (indigo), Paid (leaf-tinted), Delivered (neutral), Cancelled (hibiscus).
- Radius by hierarchy: 14px panels/sheets, 10px inputs and buttons, 999px chips and badges. Shadow only on floating things (bottom bar, sticky cart, modals, the ticket).
- Motion only in answer to an action (sheet opens, item added to cart: count bumps). Respect `prefers-reduced-motion`.
- Visible focus ring: 3px marigold outline with 2px offset.
- Avoid: all-caps eyebrows, "→" in buttons, gradients as decoration, middle-dot meta strings where a layout can separate the facts.
