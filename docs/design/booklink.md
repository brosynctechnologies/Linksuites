# BookLink design plan

**Subject.** Salons, barbers, braiders, make-up artists, photographers and small clinics, one neighbourhood at a time.
Two audiences: the owner or receptionist running the day's chair schedule, and the customer booking from an Instagram bio on a phone.
**Primary job.** Customer: pick a service and a time in under a minute, pay the deposit, and know exactly when to show up.
Business: see who is in which chair today and stop no-shows.

The thing this world runs on is **time**: "Saturday, 10:30, with Tunde." Times are the hero.

## Palette

| Name | Hex | Role |
|---|---|---|
| Aubergine | `#2B1838` | Text, dark sidebar, calendar headers |
| Mist | `#F5F3F7` | App background (cool lilac-grey, never cream) |
| Surface | `#FFFFFF` | Panels, inputs, the appointment card |
| Rose | `#C2255C` | Brand accent: primary actions, selected slot, focus ring (white text on it) |
| Mint | `#2F8F6E` | Confirmed, available, completed |
| Amber | `#A86A12` | Pending deposit / held slot |
| Brick | `#B3261E` | Cancelled, no-show, destructive |
| Line | `#E6E1EC` | Borders, calendar grid lines |

A business's own `brandColor` colours its public booking page header and selected states; Rose is BookLink's chrome.
Calendar booking blocks use each staff member's colour (a fixed set of 8 muted tints derived from the palette), so the day reads by person.

## Type

One family, two widths: **Archivo** (variable, `wdth` axis).
- **Archivo Expanded, 700-800** (`font-stretch: 125%`): times, dates and the business name on the booking page. "10:30" set wide and heavy is the visual signature.
- **Archivo normal width, 400-600**: all UI and body text. `tabular-nums` on times in lists and the calendar so columns line up.
- Scale (rem): 0.8125, 0.875, 1, 1.125, 1.375, 1.75, 2.5, 3.5. Body 1rem / 1.55. Sentence case; no all-caps labels.

## Layout

```
Public booking page (mobile-first, 560px column, centred on desktop)
┌──────────────────┐
│ cover / brand    │
│ Kemi's Studio    │  expanded type
│ Lekki · ★ 4.9    │
│ 1 Service ──────── │  steps are a real sequence, so numbering is right here
│   rows: name, 45 min, ₦15,000, deposit ₦5,000
│ 2 Who            │  avatars + "Anyone available"
│ 3 When           │  week strip of dates, then slot chips grouped Morning / Afternoon / Evening
│ 4 Your details   │
│ [ Book Sat 14 Oct, 10:30 · Pay ₦5,000 deposit ] sticky
└──────────────────┘

Business app, desktop ≥1024
┌──────────┬──────────────────────────────────────────────┐
│aubergine │ Today, Sat 14 Oct            [New booking]   │
│ sidebar  │ ┌ Tunde ──┬ Ada ────┬ Bisi ───┐  day view   │
│          │ │10:00 ██ │         │ ██      │  columns =  │
│          │ │10:30 ██ │ ███     │         │  staff      │
│          │ └─────────┴─────────┴─────────┘              │
└──────────┴──────────────────────────────────────────────┘
Mobile: agenda list for the day (time big on the left, customer + service right), staff filter chips; bottom bar Today, Calendar, Customers, More.
```

Left-aligned app. Times always lead a row. Lists are ruled rows, not card grids.

## The one memorable thing

**The appointment card.** The card salons hand you at the counter: a white card with the business name, then the time huge in Archivo Expanded ("10:30"), the date beneath, then the service and the stylist. It is the confirmation screen, the top of the manage-booking page, and what the confirmation email describes. Pending deposit shows a "Held for 9:41" countdown on the card in amber.

## Principles

- Fewest possible taps to book. Big slot chips (min 44px), sticky book button.
- Status colour always paired with a word: Pending deposit, Confirmed, Completed, Cancelled, No-show.
- Radius by hierarchy: 16px the appointment card, 12px panels, 10px inputs/buttons/slot chips, 999px avatars/badges. Shadow only on the card, sticky bars and modals.
- Motion only in answer to actions: chosen slot fills, card appears on confirmation. Respect `prefers-reduced-motion`.
- Visible focus: 3px rose ring, 2px offset.
- Show every time in the business's time zone and say so once on the page ("Times are in Lagos time").
- Avoid: all-caps eyebrows, "→" in buttons, decorative gradients, middle-dot meta strings where layout can separate facts, identical shadowed cards everywhere.
