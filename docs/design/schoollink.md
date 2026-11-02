# SchoolLink design plan

Product: SchoolLink, the school management app for private nursery, primary and secondary schools (Lagos first). App: `school-web`,
port 4200. Spec: `docs/superpowers/specs/2026-10-13-school-management-design.md`.

## Who and what

- **Proprietors and principals** set up the school, import students from Excel, watch fees and results. Often on a laptop in the
  office, just as often on a phone between assemblies.
- **Bursars** chase fees and print receipts. **Teachers** take the register and enter scores, almost always on a phone.
- **Parents** open a WhatsApp link from the school, sign in with an emailed code, and check results and fees on a phone over
  mobile data. Many are not confident with web apps.

Primary job: **make a school's records feel as dependable as its exercise books and report cards**, and let a new user find the
next thing to do without help.

The objects this world runs on are paper: the **class register** (ruled columns, ticks, a red margin), the **exercise book** (pale
blue rules, the margin line) and the **report card** (subjects down the side, CA1, CA2, Exam, Total, Grade). The school's identity
is its **uniform**: wine and maroon blazers, navy skirts, a gold crest.

## Palette

| Name | Hex | Role |
|---|---|---|
| Ink | `#1F1B24` | Text; the school name in the top bar |
| Blazer | `#6B1D33` | Brand accent: primary buttons, links, the active tab, selection, focus ring (white text on it, 10.6:1) |
| Blazer deep | `#40111F` | The app's top bar and desktop rail (the uniform) |
| Ruled | `#EDF1F6` | App background: the pale blue-grey of exercise-book paper (cool, never cream) |
| Surface | `#FFFFFF` | Sheets, inputs, dialogs |
| Rule | `#C9D5E3` | Ruled lines between rows, borders. Blue, like the lines in an exercise book |
| Margin | `#D9828F` | The margin line only (2px, vertical, left of a ruled sheet). Decorative, never a state colour |
| Pass | `#1D6E45` | Done, new rows, active students, paid |
| Crest gold | `#9A6A0E` | Waiting, update rows, plan warnings (on Crest wash `#F6EED9`) |
| Brick | `#B3261E` | Errors, error rows, destructive actions |
| WhatsApp | `#1DA851` | Only buttons that open WhatsApp, always with the glyph |

Neutral greys are re-tuned toward Ink with a slight blue (the `slate-*` scale is overridden), so nothing drifts to Tailwind defaults.

## Type

- **Zilla Slab** (600, 700): page titles, the school name, the wordmark, grades and score figures. A slab serif reads like a
  textbook cover and a typed report card; it is sturdy at small sizes on cheap phones.
- **Atkinson Hyperlegible Next** (400, 500, 700): all UI and body text. Built for low-vision readers: unambiguous 1/l/I and 0/O,
  which matters for admission numbers, codes and phone numbers that parents and teachers copy by hand.
- Scale (rem): 0.8125, 0.875, 1, 1.125, 1.375, 1.75, 2.25, 3 (landing hero only). Body 1rem / 1.55. Figures use tabular numbers.
- No all-caps labels, no letter-spaced eyebrows. Section titles are sentence case in Zilla Slab.

## Layout

Mobile first. Content is left aligned throughout; only the empty states centre.

```
Phone (staff)                      Desktop (staff)
+------------------------------+   +--------+-------------------------------+
| Blazer deep bar: school name |   | Blazer | Page title (Zilla Slab)       |
+------------------------------+   | deep   |  | ruled sheet ---------------|
| Title                        |   | rail   |  | row ----------------------|
| |  ruled sheet ------------- |   | nav    |  | row ----------------------|
| |  row --------------------- |   |        |                               |
| |  row --------------------- |   |        |                               |
+------------------------------+   +--------+-------------------------------+
| Home  Students  Setup  More  |   (bottom tab bar on phones only)
+------------------------------+
```

- **The ruled sheet** is the one memorable device: lists (students, staff, levels, import preview rows) are white sheets with pale
  blue rules between rows and a 2px Margin line inset on the left, like a register page. Everything else stays quiet: panels are
  plain white with a 1px Rule border and a 8px radius; inputs and buttons are 6px; there are no shadows except dialogs.
- **The setup checklist** on the dashboard is drawn as a register column: each item has a tick box square that fills Pass green
  when done, and the next undone item carries the only primary button on the page.
- **Landing hero** shows the most characteristic object in the product, a report card fragment (subjects, CA1, CA2, Exam, Total,
  Grade, with WAEC-style grades) on a ruled sheet, beside a plain headline. No stat blocks, no gradients.
- **The school sign-in page** `/{slug}` is the school's own front door: the school name large in Zilla Slab on Blazer deep, then a
  three-tab sheet (Staff, Parent, Student). Parents are the most common visitors, so the Parent tab is the default.
- Status words (New, Update, Error, Done) are short text badges with a tinted wash, always with the word, never colour alone.

## Principles

1. **Paper you already trust.** Borrow the register and the report card, not dashboard chrome. One device (the ruled sheet), used
   consistently.
2. **One next step.** Each screen has at most one primary (Blazer) button: the thing to do next.
3. **Phone first, thumb first.** 44px targets, bottom tab bar, bottom-sheet dialogs, fields at 16px so phones don't zoom.
4. **Say it like the school office would.** Sentence case, plain verbs, the school's own words: Proprietor, Principal, Bursar,
   Teacher, admission number, class, term, session.
5. **Errors tell you what to do**, next to the field or row they concern ("Row 14: class 'JSS 4B' doesn't exist").

## Review against generic defaults

- First draft used a cream "paper" background for the exercise-book idea. That is the generic warm-cream default, so it became
  the cool **Ruled** blue-grey that real exercise-book paper has, with blue rules.
- First draft used a red margin line in the same red as errors. That made decoration look like an error, so the Margin line is a
  muted rose that is never used for states, and errors stay Brick.
- First draft put the hero as a big number ("2,000 students imported in minutes") with small labels: the default stat treatment.
  Replaced with the report-card fragment, which is the product's real output.
- Checked against the other five apps: OrderLink (indigo and marigold, Bricolage), BookLink (aubergine and rose, Archivo),
  InvoiceLink (graphite and ink blue, Fraunces), LearnLink (deep ink and lagoon, Literata), PropertyLink (asphalt and adire indigo,
  Archivo). Wine, slab serif and blue-ruled paper overlap with none of them.
