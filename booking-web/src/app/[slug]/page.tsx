import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { JsonLd } from "@/components/suite/sections";
import { getPublicBookingPage } from "@/lib/server-api";
import { SITE_URL, baseOpenGraph, baseTwitter } from "@/lib/site";
import type { BusinessCategory, PublicBookingPageDto } from "@/lib/types";
import { ClientBookingPage } from "./booking-fallback";
import { BookingPageView } from "./booking-view";

/**
 * The page a business shares in its Instagram bio. It blocks on the page data instead of streaming a shell, so an
 * unknown address answers 404 before any HTML is sent, and every business gets its own title and share preview.
 */
export const instant = false;

const SCHEMA_TYPE: Record<BusinessCategory, string> = {
  Salon: "HairSalon",
  Barber: "HairSalon",
  Spa: "DaySpa",
  MakeUp: "BeautySalon",
  Photography: "ProfessionalService",
  Clinic: "MedicalClinic",
  Fitness: "HealthClub",
  Other: "LocalBusiness",
};

const SCHEMA_DAYS = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

function summary(p: PublicBookingPageDto): string {
  const about = p.description?.replace(/\s+/g, " ").trim();
  if (about) return about.length > 160 ? `${about.slice(0, 157).trimEnd()}…` : about;
  const where = p.address ? ` in ${p.address}` : "";
  const what = p.services.slice(0, 3).map((s) => s.name);
  return `Book an appointment at ${p.name}${where}${what.length ? `: ${what.join(", ")}` : ""}. Choose a free time online.`;
}

function businessJsonLd(p: PublicBookingPageDto) {
  const url = `${SITE_URL}/${p.slug}`;
  return {
    "@context": "https://schema.org",
    "@type": SCHEMA_TYPE[p.category] ?? "LocalBusiness",
    name: p.name,
    url,
    description: p.description ?? undefined,
    image: [p.coverUrl, p.logoUrl].filter(Boolean),
    logo: p.logoUrl ?? undefined,
    address: p.address ?? undefined,
    telephone: p.phone ?? undefined,
    currenciesAccepted: p.currency,
    openingHoursSpecification: p.hours
      .filter((h) => !h.closed)
      .map((h) => ({ "@type": "OpeningHoursSpecification", dayOfWeek: `https://schema.org/${SCHEMA_DAYS[h.weekday]}`, opens: h.open, closes: h.close })),
    aggregateRating:
      p.rating && p.rating.count > 0
        ? { "@type": "AggregateRating", ratingValue: p.rating.average.toFixed(1), reviewCount: p.rating.count, bestRating: 5 }
        : undefined,
    makesOffer: p.services.map((s) => ({
      "@type": "Offer",
      itemOffered: { "@type": "Service", name: s.name },
      price: (s.priceMinor / 100).toFixed(2),
      priceCurrency: p.currency,
    })),
    potentialAction: { "@type": "ReserveAction", target: url },
  };
}

export async function generateMetadata({ params, searchParams }: PageProps<"/[slug]">): Promise<Metadata> {
  const { slug } = await params;
  const embed = (await searchParams).embed === "1";
  const res = await getPublicBookingPage(slug);
  if (!res.ok) {
    return {
      title: res.status === 404 ? "Booking page not found" : "Book an appointment",
      robots: { index: false, follow: true },
      alternates: {},
    };
  }
  const p = res.data;
  const title = `${p.name}: book online`;
  const description = summary(p);
  const url = `/${p.slug}`;
  const image = p.coverUrl ?? p.logoUrl;
  return {
    title: { absolute: title },
    description,
    alternates: { canonical: url },
    // The embedded widget is the same page inside another site: keep only the full page in search.
    robots: embed ? { index: false, follow: true } : { index: true, follow: true },
    openGraph: { ...baseOpenGraph, url, title, description, ...(image ? { images: [{ url: image, alt: p.name }] } : {}) },
    twitter: { ...baseTwitter, title, description, ...(image ? { images: [image] } : {}) },
  };
}

export default async function PublicBookingRoute({ params, searchParams }: PageProps<"/[slug]">) {
  const { slug } = await params;
  const embed = (await searchParams).embed === "1";
  const res = await getPublicBookingPage(slug);
  if (!res.ok && res.status === 404) notFound();
  // The API didn't answer the server: let the browser load the page, with the old error and retry screens.
  if (!res.ok) return <ClientBookingPage slug={slug} embed={embed} />;
  return (
    <>
      {!embed && <JsonLd data={businessJsonLd(res.data)} />}
      <BookingPageView page={res.data} slug={slug} embed={embed} />
    </>
  );
}
