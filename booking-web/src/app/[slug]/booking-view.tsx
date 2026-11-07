import { BrandingFooter, BusinessHeader, ContactButtons, HoursList, PublicFrame } from "@/components/public-parts";
import { Notice } from "@/components/ui";
import { cityOf } from "@/lib/time";
import type { PublicBookingPageDto } from "@/lib/types";
import { BookingFlow } from "./booking-page";

/**
 * A business's booking page. Rendered on the server from the page data, so the header, hours and policies are in the
 * first HTML; only the booking steps (BookingFlow) run in the browser. The client fallback renders the same view.
 */
export function BookingPageView({ page, slug, embed }: { page: PublicBookingPageDto; slug: string; embed: boolean }) {
  return (
    <PublicFrame brand={page.brandColor} className={embed ? "bg-surface" : undefined}>
      {embed ? (
        <h1 className="sr-only">Book at {page.name}</h1>
      ) : (
        <BusinessHeader
          name={page.name}
          coverUrl={page.coverUrl}
          logoUrl={page.logoUrl}
          address={page.address}
          rating={page.rating}
          hasPolicies={!!page.policies}
        />
      )}

      <main id="main" tabIndex={-1} className="outline-none">
        {page.bookingEnabled ? (
          <BookingFlow page={page} slug={slug} embed={embed} />
        ) : (
          <div className="px-4 pt-6 sm:px-0">
            <Notice tone="warning">{page.name} isn&apos;t taking online bookings right now. Call or message them to book.</Notice>
            <div className="mt-4">
              <ContactButtons phone={page.phone} whatsapp={page.whatsappNumber} message={`Hello ${page.name}, I'd like to book an appointment.`} />
            </div>
          </div>
        )}

        <AboutSection page={page} embed={embed} />
      </main>
      {!embed && page.showBranding && <BrandingFooter />}
      {/* Room for the sticky book bar. */}
      {page.bookingEnabled && <div className="h-28" aria-hidden="true" />}
    </PublicFrame>
  );
}

function AboutSection({ page, embed }: { page: PublicBookingPageDto; embed: boolean }) {
  const hasHours = page.hours.length > 0;
  if (embed && !page.policies) return null;
  return (
    <div className="mt-8 space-y-6 px-4 sm:px-0">
      {!embed && page.description && (
        <section>
          <h2 className="mb-1.5 font-bold">About {page.name}</h2>
          <p className="text-sm whitespace-pre-line text-slate-700">{page.description}</p>
        </section>
      )}
      {!embed && hasHours && (
        <section>
          <h2 className="mb-2 font-bold">Opening hours</h2>
          <HoursList hours={page.hours} />
          <p className="mt-2 text-xs text-muted">Times are in {cityOf(page.timezone)} time.</p>
        </section>
      )}
      {page.policies && (
        <section id="policies" className="scroll-mt-4">
          <h2 className="mb-1.5 font-bold">Booking policies</h2>
          <p className="text-sm whitespace-pre-line text-slate-700">{page.policies}</p>
          {page.cancellationHours > 0 && (
            <p className="mt-2 text-sm text-slate-700">
              You can cancel or move your booking up to {page.cancellationHours} hour{page.cancellationHours === 1 ? "" : "s"} before it starts.
            </p>
          )}
        </section>
      )}
      {!embed && (page.phone || page.whatsappNumber) && (
        <section>
          <h2 className="mb-2 font-bold">Questions first?</h2>
          <ContactButtons phone={page.phone} whatsapp={page.whatsappNumber} message={`Hello ${page.name}, I have a question about booking.`} />
        </section>
      )}
    </div>
  );
}
