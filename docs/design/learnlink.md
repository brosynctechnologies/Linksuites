# LearnLink design plan

**Subject.** Online courses for African learners: exam prep (WAEC, JAMB), professional certificates, digital skills,
taught by tutors who already have an audience on YouTube, Instagram or Telegram. Two sides:
- **Learners** browse the marketplace or a tutor's academy, pay in naira, and study on phones over patchy mobile data.
- **Tutors** build courses in a studio, watch enrolments and earnings, and get paid out.
**Primary jobs.** Learner: find a course they trust, then keep going until they finish. Tutor: publish a course quickly and see money arrive.

The thing this world runs on is **progress**: lesson 7 of 23, 30% done, quiz passed, certificate earned.

## Palette

| Name | Hex | Role |
|---|---|---|
| Deep ink | `#10202B` | Text, studio sidebar, player chrome |
| Mist | `#F3F6F6` | App background (cool, never cream) |
| Surface | `#FFFFFF` | Panels, cards for courses only |
| Lagoon | `#0B6E6E` | Brand accent: primary actions, links, current lesson, focus ring (white text on it) |
| Lime | `#5E9E2F` | Progress and completion only: progress bars, finished lessons, passed quizzes, certificates |
| Coral | `#E8624A` | Price highlights for sales/discounts and "new" markers (sparingly) |
| Brick | `#B3261E` | Errors, failed, destructive |
| Line | `#DCE3E3` | Borders and dividers |

An academy's `brandColor` replaces Lagoon on its own academy page and course pages reached from it.

## Type

- **Literata** (serif, optical sizes): course titles, lesson titles, and the body of text lessons. Reading is the core
  activity; a book face makes long lessons comfortable. Body 1.0625rem / 1.7 for lesson text, max 68ch.
- **Figtree** (sans): all UI, numbers, the studio. `tabular-nums` for prices, earnings and stats.
- Scale (rem): 0.8125, 0.875, 1, 1.125, 1.375, 1.75, 2.375, 3.25. Sentence case; no all-caps labels.

## Layout

```
Marketplace (/)                         Course page (/courses/{academy}/{course})
┌───────────────────────────────────┐   ┌──────────────────────────┬─────────────┐
│ LearnLink   [search courses   ]   │   │ Title (Literata)         │ sticky buy  │
│ What do you want to learn?        │   │ by Tutor · rating        │ price       │
│ category chips                    │   │ What you'll learn        │ [Enrol]     │
│ rows of course cards (thumb,      │   │ Curriculum = the path    │ preview     │
│  title, tutor, rating, price)     │   │ Reviews                  │             │
└───────────────────────────────────┘   └──────────────────────────┴─────────────┘

Lesson player (/learn/{course}/{lesson})             mobile: video on top, path in a sheet
┌──────────────────────────────┬────────────────┐
│ video / text / quiz           │ the path       │
│                               │ ● done         │
│ [Mark complete] [Next lesson] │ ◉ current      │
│ tabs: Overview, Resources,    │ ○ next         │
│       Q&A, Notes              │                │
└──────────────────────────────┴────────────────┘

Studio (tutor), desktop: deep-ink sidebar; course builder = sections with lesson rows, drag or move up/down.
```

Course cards are the one place card grids are right (people scan thumbnails). Everything else is ruled lists.

## The one memorable thing

**The learning path.** A course's curriculum is drawn as a vertical route: a line connecting lesson nodes, filled in
lime up to where the learner has reached, the current lesson as a ringed node, quizzes as diamond nodes, the
certificate as the final node. It appears on the course page (empty, as a preview of the journey), in the player
sidebar (live progress), and in "My learning" as a slim horizontal version on each course. Everything else stays calm.

## Principles

- Low data first: thumbnails lazy-loaded and small, video adaptive, nothing heavy on the marketplace page.
- Progress always visible and honest (lessons done / total, %).
- Status colour paired with words (Draft, In review, Published, Rejected; Processing, Ready, Failed).
- Radius by hierarchy: 14px course cards and panels, 10px inputs and buttons, 999px chips/badges; the player is square-edged.
- Motion only in answer to actions: a lesson node fills when marked complete; certificate appears on completion.
- Visible focus: 3px lagoon ring, 2px offset. Respect `prefers-reduced-motion`.
- Avoid: all-caps eyebrows, "→" in buttons, decorative gradients, middle-dot meta strings where layout can separate facts,
  identical shadowed cards outside the course grid.
