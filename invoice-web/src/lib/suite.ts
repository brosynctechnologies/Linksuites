/**
 * The seven LinkSuite products, in the order the hub lists them. This file is the single source of product data for the
 * LinkSuite sites: the hub imports it directly, and each app keeps a verbatim copy at `src/lib/suite.ts` (see the porting
 * guide in docs/design/linksuite.md). Each app's address comes from its env var.
 *
 * No prices here, ever: public pages say "free to start" and list what plans include. The free and paid lines mirror the
 * plans seeded in api/src/Saas.Infrastructure/Persistence/Support.cs; update them here if those plans change.
 */
import type { CSSProperties } from "react";

export type AppKey = "orders" | "invoices" | "bookings" | "courses" | "property" | "school" | "care";

export interface Product {
  key: AppKey;
  name: string;
  /** The picker's signboard: the visitor's kind of business, in their words. */
  business: string;
  /** Three or four words under the signboard: what the app does. */
  tagline: string;
  /** What the product does, in one sentence. */
  job: string;
  /** Who it is for. */
  audience: string;
  /** Three things it does, in plain words. */
  points: [string, string, string];
  /** What the free plan includes, and what paid plans add. No amounts. */
  free: [string, string, string];
  paid: [string, string, string];
  /** The app's site, and the paths a business owner uses there. */
  url: string;
  landingPath: string;
  startPath: string;
  signInPath: string;
  /** schema.org applicationCategory for structured data. */
  category: "BusinessApplication" | "EducationalApplication" | "HealthApplication";
  /** Signature colour (from the app's globals.css), the text colour on it, and the colour for accent text on light grounds. */
  accent: string;
  onAccent: string;
  accentText: string;
}

const url = (value: string | undefined, fallback: string) => (value || fallback).replace(/\/+$/, "");

export const products: Product[] = [
  {
    key: "orders",
    name: "OrderLink",
    business: "A food business or a shop",
    tagline: "Orders on WhatsApp",
    job: "Take orders on WhatsApp from your own online store.",
    audience: "Food vendors, boutiques and small shops",
    points: ["A store link to share anywhere", "Orders arrive priced and ready on WhatsApp", "Delivery areas, discounts and stock"],
    free: ["Up to 20 products", "Orders sent to your WhatsApp", "One delivery zone"],
    paid: ["Unlimited products and delivery zones", "Discount codes", "No OrderLink branding"],
    url: url(process.env.NEXT_PUBLIC_ORDERLINK_URL, "http://localhost:3700"),
    landingPath: "/",
    startPath: "/signup",
    signInPath: "/login",
    category: "BusinessApplication",
    accent: "#F2A93B",
    onAccent: "#1B1F3B",
    accentText: "#87560C",
  },
  {
    key: "invoices",
    name: "InvoiceLink",
    business: "Freelance work or an agency",
    tagline: "Invoices that get paid",
    job: "Send invoices and quotes clients take seriously, and get paid online.",
    audience: "Freelancers, agencies and service businesses",
    points: ["Invoices, quotes and receipts as PDFs", "Clients pay by card or transfer", "Reminders, recurring invoices and expenses"],
    free: ["10 invoices a month", "Quotes and a client list", "5 invoice templates"],
    paid: ["Unlimited invoices and all 10 templates", "Pay-now links, reminders and recurring invoices", "Expenses, a profit report and team seats"],
    url: url(process.env.NEXT_PUBLIC_INVOICELINK_URL, "http://localhost:3800"),
    landingPath: "/",
    startPath: "/signup",
    signInPath: "/login",
    category: "BusinessApplication",
    accent: "#2B47C9",
    onAccent: "#FFFFFF",
    accentText: "#2B47C9",
  },
  {
    key: "bookings",
    name: "BookLink",
    business: "A salon, barber or studio",
    tagline: "Bookings and deposits",
    job: "Let customers book and pay a deposit without calling you.",
    audience: "Salons, barbers, braiders, make-up artists and studios",
    points: ["A booking page with live time slots", "Deposits that cut no-shows", "Staff calendars and reminders"],
    free: ["50 bookings a month", "Your own booking page", "A calendar and customer list"],
    paid: ["Unlimited bookings", "Deposits paid to your Paystack", "More staff, each with a calendar"],
    url: url(process.env.NEXT_PUBLIC_BOOKLINK_URL, "http://localhost:3900"),
    landingPath: "/",
    startPath: "/signup",
    signInPath: "/login",
    category: "BusinessApplication",
    accent: "#C2255C",
    onAccent: "#FFFFFF",
    accentText: "#A01C4B",
  },
  {
    key: "courses",
    name: "LearnLink",
    business: "Classes, tutoring or coaching",
    tagline: "Courses people pay for",
    job: "Sell online courses and keep learners going until they finish.",
    audience: "Tutors, coaches and training academies",
    points: ["Video lessons, quizzes and certificates", "Your own academy page and coupons", "Earnings and payouts in one place"],
    free: ["2 courses and 2 GB of video", "Your own academy page", "A listing on the LearnLink marketplace"],
    paid: ["Unlimited courses and more video", "No fee on sales from your academy", "Coupons, certificates and team seats"],
    url: url(process.env.NEXT_PUBLIC_LEARNLINK_URL, "http://localhost:4000"),
    landingPath: "/teach",
    startPath: "/teach/signup",
    signInPath: "/teach/login",
    category: "EducationalApplication",
    accent: "#0B6E6E",
    onAccent: "#FFFFFF",
    accentText: "#0B6E6E",
  },
  {
    key: "property",
    name: "PropertyLink",
    business: "Homes to let or sell",
    tagline: "Listings and inspections",
    job: "List homes to rent, buy or book, with the full cost to move in.",
    audience: "Agents, agencies and landlords",
    points: ["Listings with the full cost to move in", "Inspections and short-let bookings", "A Verified badge once we check your papers"],
    free: ["3 live listings", "Enquiries and WhatsApp leads", "The Verified badge after checks"],
    paid: ["More live listings, up for longer", "Views and lead stats", "Team seats for your agents"],
    url: url(process.env.NEXT_PUBLIC_PROPERTYLINK_URL, "http://localhost:4100"),
    landingPath: "/for-agents",
    startPath: "/studio/signup",
    signInPath: "/studio/login",
    category: "BusinessApplication",
    accent: "#33309A",
    onAccent: "#FFFFFF",
    accentText: "#33309A",
  },
  {
    key: "school",
    name: "SchoolLink",
    business: "A nursery, primary or secondary school",
    tagline: "Fees, results and parents",
    job: "Run fees, results and parent updates for your school.",
    audience: "Nursery, primary and secondary schools",
    points: ["Fee invoices, online payment and reminders", "Results and printed report cards", "A portal for parents and students"],
    free: ["Up to 50 students", "Results, report cards and attendance", "3 staff logins"],
    paid: ["Room for more students", "Online fee payments and reminders", "More staff logins"],
    url: url(process.env.NEXT_PUBLIC_SCHOOLLINK_URL, "http://localhost:4200"),
    landingPath: "/",
    startPath: "/signup",
    signInPath: "/login",
    category: "EducationalApplication",
    accent: "#6B1D33",
    onAccent: "#FFFFFF",
    accentText: "#6B1D33",
  },
  {
    key: "care",
    name: "CareLink",
    business: "A clinic or small hospital",
    tagline: "Patients, wards and bills",
    job: "Run your clinic's front desk, patient records, wards and bills.",
    audience: "Private clinics and small hospitals",
    points: ["Queue, consultations and prescriptions", "Admissions, wards and nursing care", "Patient bills, deposits and a patient portal"],
    free: ["Patient records, visits and bills", "An outpatient clinic", "3 staff logins"],
    paid: ["Wards and beds", "Online payments and the patient portal", "More staff logins"],
    url: url(process.env.NEXT_PUBLIC_CARELINK_URL, "http://localhost:4400"),
    landingPath: "/",
    startPath: "/signup",
    signInPath: "/login",
    category: "HealthApplication",
    accent: "#0F766E",
    onAccent: "#FFFFFF",
    accentText: "#0F766E",
  },
];

/** The LinkSuite hub, and the platform team's console. */
export const hubUrl = url(process.env.NEXT_PUBLIC_HUB_URL, "http://localhost:3600");
export const adminUrl = url(process.env.NEXT_PUBLIC_ADMIN_URL, "http://localhost:4300");

export const productByKey = (key: AppKey) => products.find((p) => p.key === key)!;

/** Full links into an app. */
export const appHref = (p: Product, path: string) => `${p.url}${path === "/" ? "" : path}`;

/** CSS custom properties that turn a wrapper into that app's colour context (see --app in suite.css). */
export const appStyle = (p: Product) =>
  ({ "--app": p.accent, "--app-ink": p.onAccent, "--app-text": p.accentText }) as CSSProperties;

/** schema.org SoftwareApplication for one app, published by LinkSuite. Free to start, so the only offer is the free plan. */
export const appJsonLd = (p: Product) => ({
  "@type": "SoftwareApplication",
  name: p.name,
  url: p.url,
  description: p.job,
  applicationCategory: p.category,
  operatingSystem: "Web",
  audience: { "@type": "BusinessAudience", audienceType: p.audience },
  offers: { "@type": "Offer", price: "0", priceCurrency: "NGN", description: "Free plan" },
  publisher: { "@type": "Organization", name: "LinkSuite", url: hubUrl },
});
