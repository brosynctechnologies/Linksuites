// schema.org JSON-LD for the public marketplace pages. These describe the agency's own homes and prices, which stay
// public: only PropertyLink's plan prices are kept off the site. Undefined fields drop out when serialised.

import { APP_NAME, SITE_URL } from "./config";
import { listingPath } from "./format";
import { purposePath } from "./search";
import type { AgencyCardDto, AgentCardDto, ListingDetailDto, LocationDto } from "./types";

const abs = (path: string) => `${SITE_URL}${path}`;
const naira = (minor: number) => String(minor / 100);

const UNIT: Record<string, string> = { Year: "year", Month: "month", Night: "night" };

function postalAddress(breadcrumb: LocationDto[], street?: string | null) {
  const area = breadcrumb.findLast((b) => b.kind === "Area") ?? breadcrumb[breadcrumb.length - 1];
  const city = breadcrumb.find((b) => b.kind === "City");
  const state = breadcrumb.find((b) => b.kind === "State");
  return {
    "@type": "PostalAddress",
    streetAddress: street || undefined,
    addressLocality: [area?.name, city && city.slug !== area?.slug ? city.name : undefined].filter(Boolean).join(", ") || undefined,
    addressRegion: state?.name,
    addressCountry: "NG",
  };
}

function agencyRef(agency: AgencyCardDto) {
  return { "@type": "RealEstateAgent", name: agency.name, url: abs(`/agencies/${agency.slug}`), logo: agency.logoUrl ?? undefined };
}

/** A listing page: the listing with its home and the agency's offer, plus the breadcrumb trail. */
export function listingJsonLd(l: ListingDetailDto) {
  const url = abs(listingPath(l.agency.slug, l.slug));
  const base = purposePath(l.purpose);
  const purposeName = l.purpose === "Sale" ? "Buy" : l.purpose === "ShortLet" ? "Short-let" : "Rent";
  const price = naira(l.priceMinor);
  const unit = UNIT[l.pricePeriod];
  return {
    "@context": "https://schema.org",
    "@graph": [
      {
        "@type": "RealEstateListing",
        "@id": `${url}#listing`,
        url,
        name: l.title,
        description: l.description ? l.description.slice(0, 500) : undefined,
        datePosted: l.publishedAt ?? undefined,
        image: l.photos.slice(0, 6).map((p) => p.fullUrl),
        provider: { "@type": "Organization", name: APP_NAME, url: abs("/") },
        mainEntity: {
          "@type": "Accommodation",
          name: l.title,
          numberOfBedrooms: l.bedrooms || undefined,
          numberOfBathroomsTotal: l.bathrooms || undefined,
          floorSize: l.sizeSqm ? { "@type": "QuantitativeValue", value: l.sizeSqm, unitCode: "MTK" } : undefined,
          amenityFeature: l.amenities.length ? l.amenities.map((a) => ({ "@type": "LocationFeatureSpecification", name: a.name, value: true })) : undefined,
          address: postalAddress(l.breadcrumb, l.address),
          geo: { "@type": "GeoCoordinates", latitude: l.lat, longitude: l.lng },
        },
        offers: {
          "@type": "Offer",
          url,
          price,
          priceCurrency: l.currency,
          priceSpecification: unit ? { "@type": "UnitPriceSpecification", price, priceCurrency: l.currency, unitText: unit } : undefined,
          businessFunction: l.purpose === "Sale" ? "http://purl.org/goodrelations/v1#Sell" : "http://purl.org/goodrelations/v1#LeaseOut",
          availability: "https://schema.org/InStock",
          offeredBy: agencyRef(l.agency),
        },
      },
      {
        "@type": "BreadcrumbList",
        itemListElement: [
          { "@type": "ListItem", position: 1, name: purposeName, item: abs(base) },
          ...l.breadcrumb.map((b, i) => ({ "@type": "ListItem", position: i + 2, name: b.name, item: abs(`${base}/${b.slug}`) })),
          { "@type": "ListItem", position: l.breadcrumb.length + 2, name: l.title, item: url },
        ],
      },
    ],
  };
}

/** An agency's page: the business, its address and phone, and its rating from seekers' reviews when it has one. */
export function agencyJsonLd(agency: AgencyCardDto, extra: { bio: string | null; address: string | null; ratingAverage: number; ratingCount: number }) {
  return {
    "@context": "https://schema.org",
    ...agencyRef(agency),
    description: extra.bio ?? undefined,
    image: agency.logoUrl ?? undefined,
    telephone: agency.phone ?? undefined,
    address: extra.address ? { "@type": "PostalAddress", streetAddress: extra.address, addressCountry: "NG" } : undefined,
    aggregateRating:
      extra.ratingCount > 0 ? { "@type": "AggregateRating", ratingValue: extra.ratingAverage.toFixed(1), reviewCount: extra.ratingCount, bestRating: 5 } : undefined,
  };
}

/** An agent's page: the agent as an estate agent working for their agency. */
export function agentJsonLd(agent: AgentCardDto, agency: AgencyCardDto, bio: string | null) {
  return {
    "@context": "https://schema.org",
    "@type": "RealEstateAgent",
    name: agent.displayName,
    url: abs(`/agents/${agent.slug}`),
    image: agent.photoUrl ?? undefined,
    description: bio ?? undefined,
    telephone: agent.phone ?? undefined,
    parentOrganization: agencyRef(agency),
    aggregateRating:
      agent.ratingCount > 0 ? { "@type": "AggregateRating", ratingValue: agent.ratingAverage.toFixed(1), reviewCount: agent.ratingCount, bestRating: 5 } : undefined,
  };
}
