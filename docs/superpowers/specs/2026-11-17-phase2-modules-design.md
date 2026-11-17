# Phase 2 modules (roadmap wave 4): designs for approval

Status: **approved by the owner on 17 November 2026**, as written and in the suggested order. Owner's choices: telemedicine on **Daily.co**, paid SMS on **Termii**, HMO claims as **one generic file** for any HMO (specific formats when a clinic asks).
It follows `docs/superpowers/roadmap-2026-11-17.md` wave 4: the order is by how many paying customers each module reaches.

Shared rules for every module:
- It lives inside the existing app and its API area, with the same roles, tenant isolation, audit and plan limits.
- Features that cost us money to run, or that sell well, go on a paid plan. The free plan stays useful.
- Each module ships with its tests, docs and an admin console view where it adds money or moderation.
- Nothing here needs a new paid service unless it says so under "Needs from the owner".

---

## 4a. OrderLink (WhatsApp commerce)

1. **Inventory**
   - Each product and variant gets an optional stock count.
   - A paid order takes stock; a cancelled order puts it back.
   - Customers see "Only 3 left" and "Sold out"; sold-out items can't be ordered.
   - A low-stock alert goes to the owner, by email and in the app.
   - An adjustments log records who changed stock, by how much and why.
   - Plan: free = no tracking; Pro = tracking and alerts.
2. **Staff seats**
   - Invite staff with the roles Admin (everything but billing) and Staff (orders and products, no settings).
   - Uses the existing team and invite system. Pro gets 3 seats; a higher plan gets more.
3. **Online order payments**
   - Customers can pay for an order by card or bank transfer through the store's own Paystack account, as BookLink deposits already do.
   - The order becomes Paid when Paystack confirms, with the webhook, a return check and a background re-check.
   - The WhatsApp message carries the pay link. Pro only.
4. **Automatic reminders**
   - **To customers:** a reminder for an unpaid order after 24 hours, by email when the customer gave one, plus a WhatsApp link the store owner can tap.
   - **To the owner:** a daily summary email.
5. **WhatsApp Cloud API inbox**
   - The store's WhatsApp Business number is connected through Meta's Cloud API: messages are received in the app, replies go out from the app, and orders are sent as templates.
   - It is the largest piece and comes last.

**Needs from the owner:** for item 5, a Meta Business account (verified), a phone number for the platform app and Meta's app review. Items 1 to 4 need nothing.

## 4b. SchoolLink

1. **Attendance per subject**
   - Optional, set per school.
   - A teacher marks a register for each lesson on the timetable.
   - Reports show attendance per student per subject. Daily attendance stays as it is.
2. **Homework submissions**
   - Students (or parents, for young children) upload work or type an answer to an assignment.
   - The teacher marks it with a score and comment.
   - Late work is flagged.
   - The scores can count toward an assessment component, as CBT scores do.
3. **Library**
   - A catalogue of books and copies, kept as a list, with no barcode hardware needed.
   - Loans to students or staff, due dates, overdue reminders to parents, and fines as optional fee items.
4. **Transport**
   - Routes, stops and buses, with students assigned to a route.
   - A transport fee per route goes onto the fee invoice.
   - Day manifests show who to pick up.
5. **Hostel**
   - Hostels, rooms and beds, with students allocated to a bed per term.
   - A hostel fee goes onto the invoice.
   - Exeat (leave) requests are approved by the house master, and parents are told.
6. **Payroll**
   - Staff salary structure (basic plus allowances minus deductions) and a monthly run.
   - Payslips as PDF, and the payroll total in the expenses report.
   - No bank transfer at first: we export a bank schedule file.
7. **Multi-branch**
   - One proprietor runs several campuses under one account.
   - Each branch has its own students, staff and fees. The proprietor's dashboard adds them all up.
   - Built on the existing "several businesses per user" support, plus a group view.
8. **Theory (essay) CBT**
   - Written-answer questions in a CBT test.
   - The teacher marks them on a screen that shows one answer at a time.
   - The total combines automatic and teacher marks.
9. **Nursery logins:** a simple picture-based sign-in for nursery pupils, set up by the class teacher, used only on the school's own devices.
10. **Play:**
    - more games (spelling bee, times-tables race, a matching game);
    - high scores per class, opt-in by the school, showing first names only.

## 4c. CareLink

1. **Lab module**
   - Test catalogue with reference ranges; sample collection and labels (printable).
   - Results entered against the ranges; abnormal values are flagged.
   - Turnaround times are tracked.
   - Builds on the existing tests and results flow.
2. **Pharmacy stock**
   - Drug items with batches and expiry dates.
   - Dispensing a prescription takes stock.
   - Low-stock and near-expiry alerts; purchase receipts and a stock count.
3. **HMO claims**
   - Patients get HMO cover details: provider, plan and member number.
   - A bill splits into an HMO part and a patient part.
   - A monthly claim file for each HMO, as PDF or CSV, with the claim status tracked by hand.
   - No HMO API at first: most Nigerian HMOs take files or portals.
4. **Maternity**
   - Antenatal visits with gestation, fundal height, foetal heart rate and blood pressure.
   - Delivery record, and a postnatal check.
5. **Theatre scheduling**
   - Theatre rooms, booked procedures and the team on each one.
   - Pre-op checklist and an operation note.
6. **Telemedicine**
   - Video consultations booked through the portal.
   - The video runs in the browser through a hosted provider; the clinic gets a link.
   - Notes, prescriptions and billing work as in a normal visit.
7. **Multi-branch:** like SchoolLink's: one clinic group, several branches, a combined dashboard.
8. **National health ID:** a stored and validated field on the patient (NIN or NHIA number). A lookup only if a licensed API becomes available.

**Needs from the owner:**
- **Item 3:** which HMOs to support first.
- **Item 6:** a choice of video provider: Daily (pay as you go) or Jitsi (free, self-hosted on another server).

## 4d. PropertyLink

1. **Video and 3D tours:** today a tour is only a link. YouTube and Matterport tours will play on the listing page instead.
2. **Developer project pages**
   - A developer has one page per estate, with phases, house types, prices, payment plans and a progress gallery.
   - Leads go to the developer.
   - Paid plan.
3. **Partner leads**
   - Seekers opt in to offers from partners (mortgage, movers, interior).
   - Leads are sent to approved partners.
   - Admin console page for partners.
4. **Banner ads**
   - Self-serve banner slots on search and city pages, paid per week (boost-style, through the platform payment provider).
   - Ads are moderated in the admin console.
5. **Blog / guides**
   - Admin-written articles: area guides and renting tips.
   - SEO pages with structured data; listings are linked from articles.

## 4e. Admin console

1. **Plan refunds:** a full or partial refund of a plan payment through the provider that took it, with a reason. The plan is shortened or ended.
2. **Feature flags:** turn a feature on for all businesses, a percentage of them, or named businesses, without a deploy.
3. **Email campaigns**
   - Send an announcement to a chosen group (product, plan, active in the last N days).
   - Unsubscribe is honoured; it uses the existing email sender with a daily cap.
4. **Report builder:** saved reports over a fixed set of measures (signups, payments, active businesses, by product and period), with CSV export.
5. **Editing customer content**
   - Support can fix a listing's or store's text and images when asked.
   - Every edit is audited, and the business owner is told.

## 4f. Paid SMS

- One SMS provider is used by every product, for:
  - sign-in codes (fallback);
  - appointment and booking reminders;
  - absence alerts to parents;
  - order updates.
- Each business buys SMS credit, prepaid in naira, and every text uses some up.
- Opt-in per message type.

**Needs from the owner:** an account with a Nigerian SMS provider (for example Termii) and a registered sender ID. The provider's per-SMS price decides what we charge.

## 4g. Apps

1. **Installable web apps (PWA)**
   - In all 7 apps: an app icon, a home-screen install prompt, an offline shell and push notifications on Android.
   - SchoolLink parents, CareLink patients and OrderLink owners come first.
2. **Native apps** come only after PWA use shows demand, and need Apple and Google developer accounts.

---

## Suggested build order inside wave 4

1. OrderLink inventory, seats and online payments.
2. PWAs.
3. SchoolLink homework and per-subject attendance.
4. CareLink lab and pharmacy.
5. Paid SMS.
6. PropertyLink tours and developer pages.
7. SchoolLink library, transport, hostel and payroll.
8. CareLink HMO and maternity.
9. Admin refunds and flags.
10. Theatre, multi-branch, telemedicine, campaigns and the report builder.
11. The WhatsApp Cloud API inbox, once Meta approval is in place.
12. Theory CBT, nursery logins, Play extras, banners, partner leads and the blog.
