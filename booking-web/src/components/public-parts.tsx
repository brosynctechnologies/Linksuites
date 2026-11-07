import Link from "next/link";
import type { CSSProperties, ReactNode } from "react";
import { brandVars, initials } from "@/lib/color";
import { APP_NAME } from "@/lib/config";
import { hubUrl } from "@/lib/suite";
import { WEEK_ORDER, WEEKDAYS } from "@/lib/time";
import type { OpeningHours } from "@/lib/types";
import { MapPinIcon, PhoneIcon, StarIcon, WhatsAppIcon } from "./icons";
import { buttonClass, cx } from "./styles";

/** The 560px public column, with the business brand colour as --brand. */
export function PublicFrame({ brand, children, className }: { brand: string | null | undefined; children: ReactNode; className?: string }) {
  return (
    <div className={cx("min-h-screen bg-mist", className)} style={brandVars(brand) as CSSProperties}>
      <div className="mx-auto w-full max-w-[560px]">{children}</div>
    </div>
  );
}

export function telHref(phone: string) {
  return `tel:${phone.replace(/[^\d+]/g, "")}`;
}

export function whatsappHref(number: string, text?: string) {
  const digits = number.replace(/\D/g, "");
  return `https://wa.me/${digits}${text ? `?text=${encodeURIComponent(text)}` : ""}`;
}

/** Call and WhatsApp buttons for a business. */
export function ContactButtons({ phone, whatsapp, message }: { phone?: string | null; whatsapp?: string | null; message?: string }) {
  if (!phone && !whatsapp) return null;
  return (
    <div className="flex flex-wrap gap-2">
      {whatsapp && (
        <a href={whatsappHref(whatsapp, message)} target="_blank" rel="noopener noreferrer" className={buttonClass("secondary")}>
          <WhatsAppIcon className="h-4 w-4 text-mint" /> WhatsApp
        </a>
      )}
      {phone && (
        <a href={telHref(phone)} className={buttonClass("secondary")}>
          <PhoneIcon className="h-4 w-4" /> Call {phone}
        </a>
      )}
    </div>
  );
}

/** Cover, logo, name in expanded type, address and rating. */
export function BusinessHeader({
  name,
  coverUrl,
  logoUrl,
  address,
  rating,
  hasPolicies,
}: {
  name: string;
  coverUrl?: string | null;
  logoUrl?: string | null;
  address?: string | null;
  rating?: { average: number; count: number } | null;
  hasPolicies?: boolean;
}) {
  return (
    <header>
      {coverUrl ? (
        <div className="aspect-[16/7] w-full overflow-hidden bg-brand sm:mt-4 sm:rounded-panel">
          <img src={coverUrl} alt="" className="h-full w-full object-cover" />
        </div>
      ) : (
        <div className="h-20 w-full bg-brand sm:mt-4 sm:rounded-panel" aria-hidden="true" />
      )}
      <div className="px-4 sm:px-0">
        <div className="-mt-9 flex items-end gap-3">
          {logoUrl ? (
            <img src={logoUrl} alt="" width={72} height={72} className="h-[4.5rem] w-[4.5rem] shrink-0 rounded-full border-4 border-mist bg-surface object-cover" />
          ) : (
            <span
              className="flex h-[4.5rem] w-[4.5rem] shrink-0 items-center justify-center rounded-full border-4 border-mist bg-surface font-expanded text-xl font-extrabold text-brand"
              aria-hidden="true"
            >
              {initials(name)}
            </span>
          )}
        </div>
        <h1 className="mt-3 font-expanded text-[1.75rem] leading-[1.1] font-extrabold break-words">{name}</h1>
        <div className="mt-2 flex flex-wrap items-center gap-x-4 gap-y-1.5 text-sm text-slate-700">
          {address && (
            <span className="flex items-center gap-1.5">
              <MapPinIcon className="h-4 w-4 shrink-0 text-muted" />
              {address}
            </span>
          )}
          {rating && rating.count > 0 && (
            <span className="flex items-center gap-1.5">
              <StarIcon filled className="h-4 w-4 text-amber" />
              <span className="font-semibold tnum">{rating.average.toFixed(1)}</span>
              <span className="text-muted">
                ({rating.count} review{rating.count === 1 ? "" : "s"})
              </span>
            </span>
          )}
          {hasPolicies && (
            <a href="#policies" className="font-medium text-brand underline underline-offset-2">
              Booking policies
            </a>
          )}
        </div>
      </div>
    </header>
  );
}

/** Opening hours, Monday first. */
export function HoursList({ hours }: { hours: OpeningHours[] }) {
  if (!hours.length) return null;
  return (
    <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-1 text-sm">
      {WEEK_ORDER.map((w) => {
        const h = hours.find((x) => x.weekday === w);
        return (
          <div key={w} className="contents">
            <dt className="text-muted">{WEEKDAYS[w]}</dt>
            <dd className="tnum">{!h || h.closed ? "Closed" : `${h.open}–${h.close}`}</dd>
          </div>
        );
      })}
    </dl>
  );
}

export function BrandingFooter() {
  return (
    <footer className="px-4 py-8 text-center text-xs text-muted">
      Bookings by{" "}
      <Link href="/" className="font-expanded font-bold text-aubergine hover:underline">
        {APP_NAME}
      </Link>
      , part of{" "}
      <a href={hubUrl} className="font-semibold text-aubergine hover:underline">
        LinkSuite
      </a>
    </footer>
  );
}
