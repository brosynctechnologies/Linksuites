"use client";

import Link from "next/link";
import { useParams, useRouter, useSearchParams } from "next/navigation";
import { useMemo, useRef, useState, type FormEvent, type ReactNode } from "react";
import { api, ApiError, toApiError } from "@/lib/api";
import { initials } from "@/lib/color";
import { STORAGE_PREFIX } from "@/lib/config";
import { formatMoney } from "@/lib/money";
import { addDays, cityOf, formatDate, formatDuration, formatTime, todayIn } from "@/lib/time";
import type { PublicBookingPageDto, PublicBookResponse, PublicServiceDto, PublicStaffDto, SlotDto } from "@/lib/types";
import { useApi } from "@/lib/use-api";
import { DateSlotPicker } from "@/components/slot-picker";
import { BrandingFooter, BusinessHeader, ContactButtons, HoursList, PublicFrame } from "@/components/public-parts";
import { Button, ErrorBox, Input, Notice, PageLoader, Textarea, buttonClass, cx } from "@/components/ui";
import { CheckIcon } from "@/components/icons";

export function BookingPage() {
  const { slug } = useParams<{ slug: string }>();
  const embed = useSearchParams().get("embed") === "1";
  const { data, error, reload } = useApi<PublicBookingPageDto>(`/api/public/book/${encodeURIComponent(slug)}`, { auth: false });

  if (error && !data) {
    return (
      <PublicFrame brand={null}>
        <div className="px-4 py-16 text-center">
          <h1 className="font-expanded text-2xl font-extrabold">{error.status === 404 ? "No booking page here" : "This page didn't load"}</h1>
          <p className="mx-auto mt-2 max-w-sm text-sm text-muted">
            {error.status === 404
              ? "Check the link you were given. The business may have changed its booking address."
              : error.message}
          </p>
          {error.status === 404 ? (
            <Link href="/" className={buttonClass("secondary", "mt-6")}>
              Go to the home page
            </Link>
          ) : (
            <Button className="mt-6" onClick={reload}>
              Try again
            </Button>
          )}
        </div>
      </PublicFrame>
    );
  }
  if (!data) return <PageLoader />;

  return (
    <PublicFrame brand={data.brandColor} className={embed ? "bg-surface" : undefined}>
      {!embed && (
        <BusinessHeader
          name={data.name}
          coverUrl={data.coverUrl}
          logoUrl={data.logoUrl}
          address={data.address}
          rating={data.rating}
          hasPolicies={!!data.policies}
        />
      )}

      {data.bookingEnabled ? (
        <BookingFlow page={data} slug={slug} embed={embed} />
      ) : (
        <div className="px-4 pt-6 sm:px-0">
          <Notice tone="warning">{data.name} isn&apos;t taking online bookings right now. Call or message them to book.</Notice>
          <div className="mt-4">
            <ContactButtons phone={data.phone} whatsapp={data.whatsappNumber} message={`Hello ${data.name}, I'd like to book an appointment.`} />
          </div>
        </div>
      )}

      <AboutSection page={data} embed={embed} />
      {!embed && data.showBranding && <BrandingFooter />}
      {/* Room for the sticky book bar. */}
      {data.bookingEnabled && <div className="h-28" aria-hidden="true" />}
    </PublicFrame>
  );
}

// ---------- The four steps ----------

interface Details {
  name: string;
  phone: string;
  email: string;
  notes: string;
}

const CUSTOMER_KEY = `${STORAGE_PREFIX}customer`;

function storedDetails(): Details {
  const empty = { name: "", phone: "", email: "", notes: "" };
  if (typeof window === "undefined") return empty;
  try {
    const raw = window.localStorage.getItem(CUSTOMER_KEY);
    if (!raw) return empty;
    const v = JSON.parse(raw) as Partial<Details>;
    return { name: v.name ?? "", phone: v.phone ?? "", email: v.email ?? "", notes: "" };
  } catch {
    return empty;
  }
}

/** Where to send the browser after booking: our /b/{token} page when manageUrl points at it. */
function manageTarget(manageUrl: string): { internal: boolean; href: string } {
  try {
    const u = new URL(manageUrl, window.location.origin);
    if (/^\/b\/[^/]+$/.test(u.pathname)) return { internal: true, href: `${u.pathname}?booked=1` };
    return { internal: false, href: u.toString() };
  } catch {
    return { internal: false, href: manageUrl };
  }
}

function BookingFlow({ page, slug, embed }: { page: PublicBookingPageDto; slug: string; embed: boolean }) {
  const router = useRouter();
  const tz = page.timezone;
  const today = todayIn(tz);
  const maxDate = addDays(today, Math.max(0, page.maxDaysAhead));

  const [serviceId, setServiceId] = useState<string | null>(page.services.length === 1 ? page.services[0].id : null);
  const [staffId, setStaffId] = useState<string | null>(null);
  const [slot, setSlot] = useState<SlotDto | null>(null);
  const [details, setDetails] = useState<Details>(storedDetails);
  const [refreshKey, setRefreshKey] = useState(0);
  const [conflict, setConflict] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [tried, setTried] = useState(false);

  const whenRef = useRef<HTMLElement>(null);
  const detailsRef = useRef<HTMLElement>(null);
  const serviceRef = useRef<HTMLElement>(null);

  const service = page.services.find((s) => s.id === serviceId) ?? null;
  const eligibleStaff = useMemo(
    () => (service ? page.staff.filter((st) => service.staffIds.includes(st.id) || st.serviceIds.includes(service.id)) : []),
    [service, page.staff],
  );
  const staff = eligibleStaff.find((s) => s.id === staffId) ?? null;
  // With "Anyone available", name the person the slot belongs to when only one person can take it.
  const slotStaff = !staff && slot?.staffIds.length === 1 ? page.staff.find((s) => s.id === slot.staffIds[0]) : null;

  const set = (k: keyof Details) => (e: { target: { value: string } }) => setDetails((d) => ({ ...d, [k]: e.target.value }));
  const deposit = service?.depositMinor ?? 0;
  const missingName = !details.name.trim();
  const missingPhone = details.phone.replace(/\D/g, "").length < 7;

  function scrollTo(el: HTMLElement | null) {
    el?.scrollIntoView({ behavior: "smooth", block: "start" });
  }

  function chooseService(s: PublicServiceDto) {
    setServiceId(s.id);
    setStaffId(null);
    setSlot(null);
    setConflict(null);
    setError(null);
  }

  function chooseStaff(id: string | null) {
    setStaffId(id);
    setSlot(null);
    setConflict(null);
  }

  function chooseSlot(s: SlotDto | null) {
    setSlot(s);
    setConflict(null);
    setError(null);
    if (s) setTimeout(() => scrollTo(detailsRef.current), 120);
  }

  async function submit(e?: FormEvent) {
    e?.preventDefault();
    if (!service) return scrollTo(serviceRef.current);
    if (!slot) return scrollTo(whenRef.current);
    if (missingName || missingPhone) {
      setTried(true);
      return scrollTo(detailsRef.current);
    }
    setBusy(true);
    setError(null);
    setConflict(null);
    try {
      const res = await api<PublicBookResponse>(`/api/public/book/${encodeURIComponent(slug)}/bookings`, {
        method: "POST",
        auth: false,
        body: {
          serviceId: service.id,
          staffId,
          startsAt: slot.startsAt,
          customer: { name: details.name.trim(), phone: details.phone.trim(), email: details.email.trim() || null },
          notes: details.notes.trim() || null,
        },
      });
      try {
        window.localStorage.setItem(CUSTOMER_KEY, JSON.stringify({ name: details.name.trim(), phone: details.phone.trim(), email: details.email.trim() }));
      } catch {
        /* storage unavailable */
      }
      if (res.authorizationUrl) {
        // Paystack can't run inside an iframe: leave the embed for the payment page.
        if (embed && window.top && window.top !== window) {
          try {
            window.top.location.href = res.authorizationUrl;
            return;
          } catch {
            /* cross-origin top: fall through */
          }
        }
        window.location.assign(res.authorizationUrl);
        return;
      }
      const target = manageTarget(res.manageUrl);
      if (target.internal) router.push(target.href);
      else window.location.assign(target.href);
    } catch (err) {
      const apiErr = toApiError(err);
      setBusy(false);
      if (apiErr.status === 409) {
        setConflict(`Someone has just taken ${formatTime(slot.startsAt, tz)}. The times below are up to date: pick another one.`);
        setSlot(null);
        setRefreshKey((k) => k + 1);
        scrollTo(whenRef.current);
        return;
      }
      if (apiErr.status === 402) {
        setError(new ApiError(402, { detail: `${page.name} can't take more online bookings this month. Call or message them and they'll fit you in.` }));
        return;
      }
      setError(apiErr);
    }
  }

  let barLabel: string;
  if (!service) barLabel = "Choose a service";
  else if (!slot) barLabel = "Choose a time";
  else if (deposit > 0) barLabel = `Pay ${formatMoney(deposit, page.currency)} deposit and book`;
  else barLabel = `Book ${formatDate(slot.startsAt, tz)}, ${formatTime(slot.startsAt, tz)}`;

  const who = staff?.displayName ?? slotStaff?.displayName ?? null;

  return (
    <form onSubmit={submit} className="space-y-3 px-4 pt-6 sm:px-0" noValidate>
      {/* 1 Service */}
      <Step
        n={1}
        title="Service"
        sectionRef={serviceRef}
        done={!!service}
        summary={
          service && (
            <span>
              {service.name}
              <span className="block text-sm font-normal text-muted">
                {formatDuration(service.durationMinutes)}, {formatMoney(service.priceMinor, page.currency)}
              </span>
            </span>
          )
        }
        onChange={page.services.length > 1 ? () => setServiceId(null) : undefined}
      >
        {page.services.length === 0 ? (
          <p className="text-sm text-muted">No services are listed yet. Call or message {page.name} to book.</p>
        ) : (
          <ul className="-mx-4 divide-y divide-line-soft sm:-mx-5">
            {page.services.map((s) => (
              <li key={s.id}>
                <button
                  type="button"
                  onClick={() => chooseService(s)}
                  className="grid w-full grid-cols-[1fr_auto] gap-x-4 px-4 py-3.5 text-left hover:bg-brand-wash sm:px-5"
                >
                  <span className="min-w-0">
                    <span className="block font-semibold">{s.name}</span>
                    {s.description && <span className="mt-0.5 line-clamp-2 block text-sm text-muted">{s.description}</span>}
                    <span className="mt-1 block text-sm text-slate-700">{formatDuration(s.durationMinutes)}</span>
                  </span>
                  <span className="text-right">
                    <span className="block font-semibold tnum">{formatMoney(s.priceMinor, page.currency)}</span>
                    {s.depositMinor > 0 && (
                      <span className="block text-xs text-muted tnum">{formatMoney(s.depositMinor, page.currency)} deposit</span>
                    )}
                  </span>
                </button>
              </li>
            ))}
          </ul>
        )}
      </Step>

      {/* 2 Who */}
      {service && (
        <Step n={2} title="Who" done={eligibleStaff.length <= 1} open summary={eligibleStaff.length === 1 ? `With ${eligibleStaff[0].displayName}` : eligibleStaff.length === 0 ? "Whoever is free" : undefined}>
          {eligibleStaff.length > 1 && (
            <div className="no-scrollbar -mx-4 flex gap-2 overflow-x-auto px-4 pb-1 sm:-mx-5 sm:px-5" role="radiogroup" aria-label="Who would you like?">
              <StaffChoice selected={staffId === null} onClick={() => chooseStaff(null)} name="Anyone available" />
              {eligibleStaff.map((st) => (
                <StaffChoice key={st.id} selected={staffId === st.id} onClick={() => chooseStaff(st.id)} name={st.displayName} staff={st} />
              ))}
            </div>
          )}
        </Step>
      )}

      {/* 3 When */}
      {service && (
        <Step n={3} title="When" sectionRef={whenRef} open>
          {conflict && (
            <div className="mb-4">
              <Notice tone="warning">{conflict}</Notice>
            </div>
          )}
          <DateSlotPicker
            key={`${service.id}:${staffId ?? "any"}`}
            source={{ kind: "public", slug, serviceId: service.id, staffId }}
            timezone={tz}
            today={today}
            maxDate={maxDate}
            value={slot}
            onChange={chooseSlot}
            refreshKey={refreshKey}
            showZoneNote
          />
        </Step>
      )}

      {/* 4 Your details */}
      {service && slot && (
        <Step n={4} title="Your details" sectionRef={detailsRef} open>
          <div className="space-y-4">
            <Input
              label="Your name"
              autoComplete="name"
              required
              value={details.name}
              onChange={set("name")}
              error={tried && missingName ? "Add your name so they know who's coming." : undefined}
            />
            <Input
              label="Phone number"
              type="tel"
              autoComplete="tel"
              inputMode="tel"
              required
              value={details.phone}
              onChange={set("phone")}
              placeholder="0803 123 4567"
              error={tried && missingPhone ? "Add a phone number they can reach you on." : undefined}
            />
            <Input
              label="Email (optional)"
              type="email"
              autoComplete="email"
              value={details.email}
              onChange={set("email")}
              hint="Add it to get your confirmation and a reminder before the appointment."
            />
            <Textarea
              label="Anything they should know? (optional)"
              value={details.notes}
              onChange={set("notes")}
              rows={2}
              placeholder="e.g. hair length, allergies, parking"
            />
            {deposit > 0 && (
              <p className="text-sm text-slate-700">
                A {formatMoney(deposit, page.currency)} deposit holds your time. You pay the rest,{" "}
                {formatMoney(Math.max(0, service.priceMinor - deposit), page.currency)}, at {page.name}.
              </p>
            )}
            <ErrorBox error={error} />
          </div>
        </Step>
      )}

      {/* Sticky book bar */}
      <div className="fixed inset-x-0 bottom-0 z-30 bg-surface/95 pb-[env(safe-area-inset-bottom)] shadow-bar backdrop-blur">
        <div className="mx-auto max-w-[560px] px-4 py-3">
          {service && slot && (
            <p className="mb-2 truncate text-sm text-slate-700">
              {service.name}
              {who ? ` with ${who}` : ""}
            </p>
          )}
          <button
            type="submit"
            disabled={busy}
            aria-busy={busy || undefined}
            className="flex min-h-12 w-full items-center justify-center rounded-control bg-brand px-4 text-base font-semibold text-on-brand transition-opacity hover:opacity-95 disabled:opacity-60"
          >
            {busy ? "Booking…" : barLabel}
          </button>
          {service && slot && (
            <p className="mt-1.5 text-center text-xs text-muted">Times are in {cityOf(tz)} time.</p>
          )}
        </div>
      </div>
    </form>
  );
}

function Step({
  n,
  title,
  done,
  open,
  summary,
  onChange,
  sectionRef,
  children,
}: {
  n: number;
  title: string;
  done?: boolean;
  open?: boolean;
  summary?: ReactNode;
  onChange?: () => void;
  sectionRef?: React.Ref<HTMLElement>;
  children: ReactNode;
}) {
  const collapsed = done && !open;
  return (
    <section ref={sectionRef} className="scroll-mt-4 rounded-panel border border-line bg-surface px-4 py-4 sm:px-5" aria-labelledby={`step-${n}`}>
      <div className="flex items-start gap-3">
        <span
          className={cx(
            "mt-0.5 flex h-7 w-7 shrink-0 items-center justify-center rounded-full text-sm font-bold",
            done ? "bg-brand text-on-brand" : "border-2 border-line text-slate-700",
          )}
          aria-hidden="true"
        >
          {done ? <CheckIcon className="h-4 w-4" /> : n}
        </span>
        <div className="min-w-0 flex-1">
          <h2 id={`step-${n}`} className="text-lg font-bold">
            {title}
          </h2>
          {summary && <div className="mt-1 font-semibold">{summary}</div>}
        </div>
        {collapsed && onChange && (
          <button type="button" onClick={onChange} className="text-sm font-semibold text-brand underline underline-offset-2">
            Change
          </button>
        )}
      </div>
      {!collapsed && <div className={cx(summary || title ? "mt-4" : "")}>{children}</div>}
    </section>
  );
}

function StaffChoice({ selected, onClick, name, staff }: { selected: boolean; onClick: () => void; name: string; staff?: PublicStaffDto }) {
  return (
    <button
      type="button"
      role="radio"
      aria-checked={selected}
      onClick={onClick}
      className={cx(
        "flex w-24 shrink-0 flex-col items-center gap-1.5 rounded-control border px-2 py-3 text-center text-sm transition-colors",
        selected ? "border-brand bg-brand-wash font-semibold" : "border-line hover:bg-brand-wash",
      )}
    >
      {staff?.photoUrl ? (
        <img src={staff.photoUrl} alt="" className={cx("h-12 w-12 rounded-full object-cover", selected && "ring-2 ring-brand ring-offset-2")} />
      ) : (
        <span
          className={cx(
            "flex h-12 w-12 items-center justify-center rounded-full text-sm font-bold",
            selected ? "bg-brand text-on-brand" : "bg-mist-2 text-slate-700",
          )}
          aria-hidden="true"
        >
          {staff ? initials(name) : "Any"}
        </span>
      )}
      <span className="leading-tight">{name}</span>
    </button>
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
