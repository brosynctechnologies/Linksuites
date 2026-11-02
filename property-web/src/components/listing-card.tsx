import Link from "next/link";
import { formatPrice } from "@/lib/money";
import { listingPath, placeLine } from "@/lib/format";
import type { ListingCardDto } from "@/lib/types";
import { FeaturedTag, VerifiedSeal } from "./badges";
import { BathIcon, BedIcon, ImageIcon, RulerIcon } from "./icons";
import { cx } from "./styles";

// Server-safe (no hooks): used by server pages and by the client search view.

function Facts({ l }: { l: ListingCardDto }) {
  const isLand = l.bedrooms === 0 && l.bathrooms === 0;
  return (
    <p className="flex flex-wrap items-center gap-x-3 gap-y-1 text-sm text-slate-700">
      {!isLand && (
        <>
          <span className="inline-flex items-center gap-1">
            <BedIcon className="h-4 w-4 text-muted" />
            <span className="tnum">{l.bedrooms}</span>
            <span className="sr-only">bedroom{l.bedrooms === 1 ? "" : "s"}</span>
          </span>
          <span className="inline-flex items-center gap-1">
            <BathIcon className="h-4 w-4 text-muted" />
            <span className="tnum">{l.bathrooms}</span>
            <span className="sr-only">bathroom{l.bathrooms === 1 ? "" : "s"}</span>
          </span>
        </>
      )}
      {l.sizeSqm ? (
        <span className="inline-flex items-center gap-1">
          <RulerIcon className="h-4 w-4 text-muted" />
          <span className="tnum">{l.sizeSqm} m²</span>
        </span>
      ) : null}
      <span className="text-muted">{l.typeName}</span>
    </p>
  );
}

function Cover({ l, className, row }: { l: ListingCardDto; className?: string; row?: boolean }) {
  return (
    <div className={cx("relative overflow-hidden bg-plaster-2", row ? "h-full min-h-32" : "aspect-[4/3]", className)}>
      {l.coverUrl ? (
        <img src={l.coverUrl} alt={l.title} loading="lazy" decoding="async" className="absolute inset-0 h-full w-full object-cover" />
      ) : (
        <span className="absolute inset-0 flex items-center justify-center text-faint">
          <ImageIcon className="h-8 w-8" />
          <span className="sr-only">No photo yet</span>
        </span>
      )}
      {l.featured && <FeaturedTag className="absolute top-2 left-2" />}
      {l.photoCount > 1 && (
        <span className="absolute right-2 bottom-2 inline-flex items-center gap-1 rounded-[4px] bg-asphalt/75 px-1.5 py-0.5 text-xs font-medium text-white">
          <ImageIcon className="h-3.5 w-3.5" aria-hidden="true" />
          <span className="tnum">{l.photoCount}</span>
          <span className="sr-only">photos</span>
        </span>
      )}
    </div>
  );
}

/**
 * Listing card: photo (4:3, lazy, running to the card edge), price in Archivo with its period, title, beds·baths,
 * area and city, the agency line with the verified seal. The whole card is one link.
 * `row` puts the photo on the left (search results); `grid` stacks it (home, area and agent pages).
 */
export function ListingCard({ listing: l, layout = "grid", className }: { listing: ListingCardDto; layout?: "grid" | "row"; className?: string }) {
  const href = listingPath(l.agencySlug, l.slug);
  if (layout === "row") {
    return (
      <Link
        href={href}
        className={cx(
          "group grid grid-cols-[minmax(0,40%)_minmax(0,1fr)] overflow-hidden rounded-panel border border-line bg-surface hover:border-slate-300 sm:grid-cols-[minmax(0,15rem)_minmax(0,1fr)]",
          className,
        )}
      >
        <Cover l={l} row />
        <div className="flex min-w-0 flex-col gap-1 p-3 sm:p-4">
          <p className="text-adire">
            <span className="price text-lg leading-tight sm:text-xl">{formatPrice(l.priceMinor, l.pricePeriod, l.currency)}</span>
          </p>
          <h3 className="line-clamp-2 font-semibold text-asphalt group-hover:underline">{l.title}</h3>
          <Facts l={l} />
          <p className="truncate text-sm text-muted">{placeLine(l.areaName, l.cityName)}</p>
          <p className="mt-auto flex min-w-0 items-center gap-1.5 pt-1 text-xs text-slate-600">
            <span className="truncate">{l.agencyName}</span>
            <VerifiedSeal verified={l.verified} />
          </p>
        </div>
      </Link>
    );
  }
  return (
    <Link href={href} className={cx("group flex flex-col overflow-hidden rounded-panel border border-line bg-surface hover:border-slate-300", className)}>
      <Cover l={l} />
      <div className="flex flex-1 flex-col gap-1 p-3.5">
        <p className="text-adire">
          <span className="price text-xl leading-tight">{formatPrice(l.priceMinor, l.pricePeriod, l.currency)}</span>
        </p>
        <h3 className="line-clamp-2 font-semibold text-asphalt group-hover:underline">{l.title}</h3>
        <Facts l={l} />
        <p className="truncate text-sm text-muted">{placeLine(l.areaName, l.cityName)}</p>
        <p className="mt-auto flex min-w-0 items-center gap-1.5 pt-1 text-xs text-slate-600">
          <span className="truncate">{l.agencyName}</span>
          <VerifiedSeal verified={l.verified} />
        </p>
      </div>
    </Link>
  );
}

export function ListingGrid({ listings, className }: { listings: ListingCardDto[]; className?: string }) {
  return (
    <ul className={cx("grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4", className)}>
      {listings.map((l) => (
        <li key={l.id} className="flex">
          <ListingCard listing={l} className="w-full" />
        </li>
      ))}
    </ul>
  );
}
