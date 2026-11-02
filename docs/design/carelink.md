# CareLink design plan

Product: CareLink, the clinic app for private clinics and small hospitals (Lagos first). App: `care-web`, port 4400. Spec:
`docs/superpowers/specs/2026-10-26-carelink-clinic-core-design.md`.

## Who and what

- **Front desk** registers patients, books and checks them in. Almost always a desktop PC at reception, with a queue in front of
  them and a phone ringing. Speed and certainty matter more than anything: the right patient, the right number.
- **Nurses** take vitals and chart doses, often on a phone or a shared tablet on the ward.
- **Doctors** read the record and write notes, on a laptop in the consulting room.
- **The cashier** prices, bills and receipts at the till. **The medical director** owns the clinic: settings, staff, the plan.

Primary job: **make every screen as certain as a patient's wristband**: one patient, one number, never mixed up, and clinical facts
only in the hands of people allowed to read them.

The objects this world runs on:

- the **patient wristband**: a narrow band with the patient's number and name, put on at registration and checked at every step;
- the **case folder**: a buff card folder with the number on its tab, and inside it the sheets;
- the **allergy sticker**: a red label stuck on the front of the folder so nobody prescribes past it;
- **scrubs**: surgical teal, the colour of theatre and the brand accent `#0F766E`.

## Palette

| Name | Hex | Role |
|---|---|---|
| Ink | `#14211F` | Text. A green-black, so text sits in the same family as the teal |
| Scrub | `#0F766E` | Brand accent: primary buttons, links, the active tab, selection, focus ring (white on it 5.5:1) |
| Theatre | `#0A3A36` (`scrub-deep`) | The app's top bar and desktop rail: the dark green of a theatre wall |
| Gauze | `#EEF3F1` | App background: cool, faintly green white of dressings. Never cream |
| Surface | `#FFFFFF` | Sheets, inputs, dialogs |
| Rule | `#CBD8D4` | Lines between rows and borders |
| Pass | `#2F6B3A` | Done, paid, active, new rows |
| Iodine | `#9A5B0B` | Waiting, plan warnings, update rows (on Iodine wash `#F7EBD9`). The brown-orange of iodine on skin |
| Arterial | `#B42318` | Errors, destructive actions, and the allergy sticker. Allergy is the only "red" that isn't an error |
| WhatsApp | `#1DA851` | Only buttons that open WhatsApp, always with the glyph |

Neutral greys (`slate-*`) are re-tuned toward Ink with a little green, so nothing drifts to Tailwind defaults. Status is always a
word, never colour alone.

## Type

- **Red Hat Display** (600, 700): page titles, the clinic name, the wordmark, big figures. A sturdy humanist grotesque, the kind of
  face on hospital wayfinding signs: it reads from across a reception counter.
- **Red Hat Text** (400, 500, 700): everything else. The same family cut for small sizes, with clear figures for patient numbers,
  phone numbers and naira amounts. Figures are tabular wherever they line up.
- Scale (rem): 0.8125, 0.875, 1, 1.125, 1.375, 1.75, 2.25, 3 (landing hero only). Body 1rem / 1.55.
- Sentence case everywhere. No all-caps labels, no letter-spaced eyebrows, no exclamation marks.

## The one bold thing: the wristband

A patient number is never plain text. It is drawn as a **wristband**: a rounded band with a small punched hole at the left, the
number in tabular Red Hat Text, a Scrub border. It appears on the patient list, the patient page header, the duplicate warning and
the register confirmation, so staff learn to look for it the way they check a real band. Everything around it stays quiet.

The second clinical device is the **allergy sticker**: on the patient page, clinical staff see recorded allergies as a red-bordered
label ("Allergic to …"), the way a folder carries its red sticker. It is never drawn for front desk or the cashier: the component
is not rendered at all for non-clinical roles, even if the data were present.

## Layout

Mobile first; reception is desktop first in practice, so tables get full width from `md`. Content is left aligned; only empty
states centre.

```
Phone (staff)                       Desktop (staff)
+-------------------------------+   +---------+------------------------------+
| Theatre bar: clinic name, bell|   | Theatre | Page title (Red Hat Display) |
+-------------------------------+   | rail    | +-- case sheet ------------+ |
| Title                         |   | clinic  | | (CL-000123) Ada Okafor   | |
| +-- case sheet -------------+ |   | you,    | |-------------------------| |
| | (CL-000123) Ada Okafor    | |   | role    | | (CL-000124) Bayo Ade    | |
| |---------------------------| |   | nav     | +-------------------------+ |
+-------------------------------+   |         |                              |
| Home Patients Prices Staff ...|   +---------+------------------------------+
+-------------------------------+
```

- **Case sheet**: a white panel with a 1px Rule border, rows divided by Rule lines and a Gauze header row. No shadows on panels; the
  float shadow is for dialogs only. Panels 8px radius, controls 6px, the wristband fully round: three radii for three kinds of thing.
- **Clinical sections** (allergies, conditions, blood group, genotype) sit in their own panel with a "Clinical" heading and a note
  that opening them is logged. They exist only in the clinical-role render path.
- **Navigation follows the role** (see `care-web/src/lib/roles.ts`): clinical items only for clinical access; money items (price
  list, later bills and reports) hidden from clinical-only staff; staff, setup and settings for leadership.

## Landing page

The hero is a reception, not a dashboard: the headline on the left, and on the right a large wristband for a sample patient over
the visit path from check-in to paid (a real sequence, so it is numbered). One staggered reveal of the path on load; nothing else
moves.

## Copy

Plain verbs and the clinic's own words: "Register a patient", "Price list", "Medical director", "Front desk", "Cashier". Errors show
the API's sentence unchanged. Health-data notes are factual: "Opening this record is logged."
