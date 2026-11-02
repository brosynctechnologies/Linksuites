/** The seven LinkSuite products, in the order the home page lists them. Each app's address comes from its env var. */
export interface Product {
  key: string;
  name: string;
  /** What the product does, in one sentence. */
  job: string;
  /** Who it is for. */
  audience: string;
  /** Three things it does, in plain words. */
  points: [string, string, string];
  url: string;
  /** Brand accent, and the colour text takes on top of it. */
  accent: string;
  onAccent: string;
}

const url = (value: string | undefined, fallback: string) => (value || fallback).replace(/\/+$/, "");

export const products: Product[] = [
  {
    key: "orders",
    name: "OrderLink",
    job: "Take orders on WhatsApp from your own online store.",
    audience: "Food vendors, boutiques and small shops",
    points: ["A store link to share anywhere", "Orders arrive priced and ready on WhatsApp", "Delivery areas, discounts and stock"],
    url: url(process.env.NEXT_PUBLIC_ORDERLINK_URL, "http://localhost:3700"),
    accent: "#F2A93B",
    onAccent: "#1B1F3B",
  },
  {
    key: "invoices",
    name: "InvoiceLink",
    job: "Send invoices and quotes clients take seriously, and get paid online.",
    audience: "Freelancers, agencies and service businesses",
    points: ["Invoices, quotes and receipts as PDFs", "Clients pay by card or transfer", "Reminders, recurring invoices and expenses"],
    url: url(process.env.NEXT_PUBLIC_INVOICELINK_URL, "http://localhost:3800"),
    accent: "#2B47C9",
    onAccent: "#FFFFFF",
  },
  {
    key: "bookings",
    name: "BookLink",
    job: "Let customers book and pay a deposit without calling you.",
    audience: "Salons, barbers, clinics and studios",
    points: ["A booking page with live time slots", "Deposits that cut no-shows", "Staff calendars and reminders"],
    url: url(process.env.NEXT_PUBLIC_BOOKLINK_URL, "http://localhost:3900"),
    accent: "#C2255C",
    onAccent: "#FFFFFF",
  },
  {
    key: "courses",
    name: "LearnLink",
    job: "Sell online courses and keep learners going until they finish.",
    audience: "Tutors, coaches and training academies",
    points: ["Video lessons, quizzes and certificates", "Your own academy page and coupons", "Earnings and payouts in one place"],
    url: url(process.env.NEXT_PUBLIC_LEARNLINK_URL, "http://localhost:4000"),
    accent: "#0B6E6E",
    onAccent: "#FFFFFF",
  },
  {
    key: "property",
    name: "PropertyLink",
    job: "List homes to rent, buy or book, with verified agents.",
    audience: "Agents, agencies, landlords and home seekers",
    points: ["Listings with the full cost to move in", "Inspections and short-let bookings", "Verified agent badges"],
    url: url(process.env.NEXT_PUBLIC_PROPERTYLINK_URL, "http://localhost:4100"),
    accent: "#33309A",
    onAccent: "#FFFFFF",
  },
  {
    key: "school",
    name: "SchoolLink",
    job: "Run fees, results and parent updates for your school.",
    audience: "Nursery, primary and secondary schools",
    points: ["Fee invoices, online payment and reminders", "Results and printed report cards", "A portal for parents and students"],
    url: url(process.env.NEXT_PUBLIC_SCHOOLLINK_URL, "http://localhost:4200"),
    accent: "#6B1D33",
    onAccent: "#FFFFFF",
  },
  {
    key: "care",
    name: "CareLink",
    job: "Run your clinic's front desk, patient records, wards and bills.",
    audience: "Private clinics and small hospitals",
    points: ["Queue, consultations and prescriptions", "Admissions, wards and nursing care", "Patient bills, deposits and a patient portal"],
    url: url(process.env.NEXT_PUBLIC_CARELINK_URL, "http://localhost:4400"),
    accent: "#0F766E",
    onAccent: "#FFFFFF",
  },
];

export const adminUrl = url(process.env.NEXT_PUBLIC_ADMIN_URL, "http://localhost:4300");
