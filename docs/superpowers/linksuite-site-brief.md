# LinkSuite website: redesign brief

Rewritten 7 November 2026 to replace the earlier "link-in-bio" brief. That brief described a different product: LinkSuite is not a link aggregator for creators. It is seven business apps for Nigerian small businesses under one account.

## What LinkSuite is

One account, seven apps. Each app runs one kind of business end to end, takes payments in naira through the business's own Paystack account, and works on a phone.

| App | For | What it does | Plans (as seeded; editable in the admin console) |
|---|---|---|---|
| OrderLink | Food vendors, boutiques, small shops | A storefront link customers order from, with orders landing on WhatsApp and the dashboard | Free, Pro ₦3,000/month |
| InvoiceLink | Freelancers, agencies, service businesses | Invoices, quotes, reminders and online payment links | Free, Pro ₦4,000, Business ₦10,000 /month |
| BookLink | Salons, barbers, studios | Online booking by staff and service, deposits, reminders | Free, Solo ₦5,000, Team ₦12,000, Business ₦25,000 /month |
| LearnLink | Tutors, coaches, academies | Sell and deliver courses, with certificates and a marketplace | Free, Pro ₦15,000, Business ₦40,000 /month |
| PropertyLink | Agents, agencies, landlords | Listings, enquiries, viewings and short stays | Free, Pro ₦10,000, Agency ₦35,000 /month |
| SchoolLink | Nursery, primary and secondary schools | Students, results, attendance, fees and a parent portal | Free, Standard ₦60,000, Premium ₦150,000 /term |
| CareLink | Private clinics and small hospitals | Patients, consultations, wards, billing and a patient portal | Free, Clinic ₦25,000, Hospital ₦75,000 /month |

## Who the site is for

Owners and managers of small Nigerian businesses. Most arrive on a phone, often on mobile data, from a WhatsApp link, a search, or a friend's recommendation. They have one question: "does this run *my* kind of business, and what will it cost?" They are not developers, and they distrust software that looks foreign or hides its price.

## The one job of the site

Get each visitor to the right app's free sign-up in as few taps as possible.

- The hub's main action is **choosing your business type**. Each app's own site has one main action: **start free**.
- Every page answers price up front, in naira. No "contact us for pricing" except where a plan truly needs it.

## Site structure

- **LinkSuite home (hub-web):** "Run your business on one account." A business-type picker that shows the seven apps by who they serve, not by product name. A short section on what's shared (one login, naira payments through your own Paystack, works on a phone, data stays yours). A pricing overview pointing to each app's pricing. FAQ. Footer with contact and legal pages.
- **Each app's site (already exists in each web app):** what it does in the owner's words, three or four real screenshots, its plans in naira, FAQ, start free. Each app links back to the hub ("Part of LinkSuite") and to sibling apps where they connect (BookLink to InvoiceLink, for example).
- **Shared pages:** About, Contact, Privacy, Terms. Privacy matters more than usual because SchoolLink and CareLink hold children's and patients' data.

## Message and copy

- Lead with the job, in plain words: "Take orders on WhatsApp", "Get paid for every invoice", "Your clinic, from front desk to ward".
- Trust signals that matter here: naira pricing, Paystack, free plan with no card, works on any phone, your customers' data is never sold. Use real customer names or numbers only once they exist; leave social proof out until then rather than inventing it.
- Sentence case, active verbs, buttons that say what happens ("Start free", "See CareLink plans").

## Visual direction

- Each app already has its own identity (CareLink's teal and wristband patient number, for instance). The hub ties them together: each app keeps its colour as its signature, and the hub's own palette stays neutral so the seven read as a family.
- Real product screenshots, not stock photos or abstract shapes. The apps are built, so capture them at phone and desktop sizes.
- Avoid the default SaaS template: no all-caps eyebrow labels, no fade-in on every section, no generic purple gradient.
- Motion in one deliberate place only, such as a single sequence showing an order becoming an invoice and then a receipt. Everything respects reduced-motion settings.

## Non-negotiables

- **Mobile and data:** the home page must work well on a mid-range Android phone on a slow connection. Server-rendered pages, `next/image` with sizes, `next/font`, no heavy third-party scripts. Targets: LCP under 2.5 s and CLS under 0.1 on a mid-range phone.
- **Accessibility:** WCAG 2.2 AA. One H1 per page, landmarks, labelled controls, visible focus, 4.5:1 text contrast, announced errors.
- **SEO:** a unique title and description per page, Open Graph images per app, `sitemap.xml` and `robots.txt` per site, `lang="en"`, Organization and SoftwareApplication structured data. Target searches like "invoice app Nigeria", "school management software Nigeria", "clinic software Lagos".

## Plan

The pages, apps and pricing already exist, so this is a repositioning and polish job, not a rebuild.

1. **Week 1:** fix the audit's high-severity findings across all eight sites (see `site-audit-2026-11-07.md`); agree the hub's new structure and copy.
2. **Week 2:** rebuild the hub home and pricing overview; capture screenshots of all seven apps.
3. **Week 3:** refresh each app's landing page around its screenshots and naira plans; shared About, Contact, Privacy and Terms.
4. **Days 1–3 of week 4:** performance, accessibility and SEO checks on real phones; launch.

## Decisions needed from you

- The domain for the hub and each app (for example linksuite.ng, with apps on subdomains or their own domains).
- Whether any early customers can be named on the site.
- Who owns privacy and terms text. SchoolLink and CareLink need it reviewed before launch.
