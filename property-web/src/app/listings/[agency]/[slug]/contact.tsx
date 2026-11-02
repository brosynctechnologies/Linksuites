"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { APP_NAME, SITE_URL } from "@/lib/config";
import { formatShortDay, formatTime, lagosDay, phoneDigits } from "@/lib/format";
import { trackEvent } from "@/lib/track";
import type { AgencyCardDto, AgentCardDto, InspectionDto } from "@/lib/types";
import { Avatar, Rating, VerifiedSeal } from "@/components/badges";
import { PhoneIcon, WhatsAppIcon } from "@/components/icons";
import { Button, ErrorBox, Input, Modal, Notice, Tabs, Textarea, buttonClass, cx, linkClass } from "@/components/ui";

export interface ContactListing {
  id: string;
  title: string;
  path: string;
  purpose: string;
}

/** Remember the View once per tab session so reloads and React's dev double-mount don't inflate counts. */
export function ViewTracker({ listingId }: { listingId: string }) {
  const done = useRef(false);
  useEffect(() => {
    if (done.current) return;
    done.current = true;
    const key = `propertylink.viewed.${listingId}`;
    try {
      if (sessionStorage.getItem(key)) return;
      sessionStorage.setItem(key, "1");
    } catch {
      /* storage blocked: still count it */
    }
    trackEvent(listingId, "View");
  }, [listingId]);
  return null;
}

function whatsAppHref(agent: AgentCardDto, listing: ContactListing) {
  const digits = phoneDigits(agent.whatsApp);
  const text = `Hello ${agent.displayName}, I saw "${listing.title}" on ${APP_NAME} (${SITE_URL}${listing.path}). Is it still available?`;
  return `https://wa.me/${digits}?text=${encodeURIComponent(text)}`;
}

/** WhatsApp and Call buttons; each tap also records the lead event. */
export function ContactButtons({ agent, listing, className }: { agent: AgentCardDto; listing: ContactListing; className?: string }) {
  const phone = agent.phone;
  if (!agent.whatsApp && !phone) return null;
  return (
    <div className={cx("grid gap-2", agent.whatsApp && phone ? "grid-cols-2" : "grid-cols-1", className)}>
      {agent.whatsApp && (
        <a href={whatsAppHref(agent, listing)} target="_blank" rel="noopener noreferrer" onClick={() => trackEvent(listing.id, "WhatsApp")} className={buttonClass("whatsapp")}>
          <WhatsAppIcon className="h-5 w-5" /> Chat on WhatsApp
        </a>
      )}
      {phone && (
        <a href={`tel:+${phoneDigits(phone)}`} onClick={() => trackEvent(listing.id, "Call")} className={buttonClass("secondary")}>
          <PhoneIcon className="h-4 w-4" /> Call {phone}
        </a>
      )}
    </div>
  );
}

/** The agent card: who you'll deal with, the seal, rating, WhatsApp/call and the enquiry or viewing forms. */
export function AgentPanel({ agent, agency, listing, showViewing }: { agent: AgentCardDto; agency: AgencyCardDto; listing: ContactListing; showViewing: boolean }) {
  const [tab, setTab] = useState<"enquiry" | "viewing">("enquiry");
  return (
    <section id="contact" aria-labelledby="agent-name" className="scroll-mt-4 rounded-panel border border-line bg-surface p-5">
      <div className="flex items-start gap-3">
        <Avatar src={agent.photoUrl} name={agent.displayName} size={52} />
        <div className="min-w-0">
          <p id="agent-name" className="flex flex-wrap items-center gap-1.5 font-semibold text-asphalt">
            <Link href={`/agents/${agent.slug}`} className="hover:underline">
              {agent.displayName}
            </Link>
            <VerifiedSeal verified={agent.verified} />
          </p>
          <p className="flex flex-wrap items-center gap-1.5 text-sm text-slate-600">
            <Link href={`/agencies/${agency.slug}`} className="hover:underline">
              {agency.name}
            </Link>
            <VerifiedSeal verified={agency.verified} />
          </p>
          <Rating average={agent.ratingAverage} count={agent.ratingCount} className="mt-0.5" />
        </div>
      </div>
      <ContactButtons agent={agent} listing={listing} className="mt-4" />
      <div className="mt-4">
        {showViewing ? (
          <>
            <Tabs
              label="Contact the agent"
              value={tab}
              onChange={setTab}
              items={[
                { value: "enquiry", label: "Send enquiry" },
                { value: "viewing", label: "Book a viewing" },
              ]}
              className="mx-0 px-0"
            />
            <div className="pt-4">{tab === "enquiry" ? <EnquiryForm listing={listing} /> : <ViewingPicker listing={listing} />}</div>
          </>
        ) : (
          <>
            <h3 className="text-sm font-semibold text-asphalt">Ask the host a question</h3>
            <div className="pt-3">
              <EnquiryForm listing={listing} />
            </div>
          </>
        )}
      </div>
    </section>
  );
}

function EnquiryForm({ listing }: { listing: ContactListing }) {
  const auth = useAuth();
  const [form, setForm] = useState({
    name: null as string | null,
    phone: "",
    email: null as string | null,
    message: `Hello, is "${listing.title}" still available? I'd like to know more.`,
  });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [sent, setSent] = useState(false);
  const set = (k: keyof typeof form) => (e: { target: { value: string } }) => setForm((f) => ({ ...f, [k]: e.target.value }));
  const name = form.name ?? auth?.user.fullName ?? "";
  const email = form.email ?? auth?.user.email ?? "";

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api(`/api/listings/${listing.id}/enquiries`, {
        method: "POST",
        body: { name: name.trim(), phone: form.phone.trim(), email: email.trim() || null, message: form.message.trim() || null },
      });
      setSent(true);
    } catch (err) {
      setError(toApiError(err));
    } finally {
      setBusy(false);
    }
  }

  if (sent) {
    return (
      <Notice tone="success">
        Enquiry sent. The agent will reply by phone, WhatsApp or email.{" "}
        {auth && (
          <Link href="/me/enquiries" className={linkClass}>
            See your enquiries
          </Link>
        )}
      </Notice>
    );
  }

  return (
    <form onSubmit={submit} className="space-y-3">
      <Input label="Your name" autoComplete="name" required value={name} onChange={set("name")} error={error?.field("name")} />
      <Input label="Phone" type="tel" autoComplete="tel" required value={form.phone} onChange={set("phone")} placeholder="0803 123 4567" error={error?.field("phone")} />
      <Input label="Email (optional)" type="email" autoComplete="email" value={email} onChange={set("email")} error={error?.field("email")} />
      <Textarea label="Message" rows={3} value={form.message} onChange={set("message")} error={error?.field("message")} />
      <ErrorBox error={error && !error.errors ? error : null} />
      <Button type="submit" loading={busy} className="w-full">
        Send enquiry
      </Button>
    </form>
  );
}

/** Next 14 days as a horizontal day strip, the offered times as chips. Booking needs a session. */
function ViewingPicker({ listing }: { listing: ContactListing }) {
  const auth = useAuth();
  const pathname = usePathname();
  const [times, setTimes] = useState<string[] | null>(null);
  const [loadError, setLoadError] = useState<ApiError | null>(null);
  const [nonce, setNonce] = useState(0);
  const [day, setDay] = useState<string | null>(null);
  const [at, setAt] = useState<string | null>(null);
  const [phone, setPhone] = useState("");
  const [notes, setNotes] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [booked, setBooked] = useState<InspectionDto | null>(null);
  const [askLogin, setAskLogin] = useState(false);

  useEffect(() => {
    const ctrl = new AbortController();
    api<string[]>(`/api/listings/${listing.id}/inspection-times`, { auth: false, signal: ctrl.signal }).then(
      (t) => setTimes(Array.isArray(t) ? t : []),
      (e) => !ctrl.signal.aborted && setLoadError(toApiError(e)),
    );
    return () => ctrl.abort();
  }, [listing.id, nonce]);

  const byDay = useMemo(() => {
    const m = new Map<string, string[]>();
    for (const t of times ?? []) {
      const d = lagosDay(t);
      m.set(d, [...(m.get(d) ?? []), t]);
    }
    return m;
  }, [times]);
  const days = [...byDay.keys()];
  const activeDay = day && byDay.has(day) ? day : (days[0] ?? null);

  async function book(e: FormEvent) {
    e.preventDefault();
    if (!at) return;
    if (!auth) return setAskLogin(true);
    setBusy(true);
    setError(null);
    try {
      setBooked(await api<InspectionDto>(`/api/listings/${listing.id}/inspections`, { method: "POST", body: { at, phone: phone.trim() || null, notes: notes.trim() || null } }));
    } catch (err) {
      const ae = toApiError(err);
      setError(ae);
      if (ae.status === 409 || ae.status === 400) {
        setAt(null);
        setNonce((n) => n + 1);
      }
    } finally {
      setBusy(false);
    }
  }

  if (booked) {
    return (
      <Notice tone="success">
        Viewing requested for {formatShortDay(booked.scheduledAt)} at {formatTime(booked.scheduledAt)}. The agent will confirm it.{" "}
        <Link href="/me/viewings" className={linkClass}>
          Your viewings
        </Link>
      </Notice>
    );
  }
  if (loadError) return <ErrorBox error={loadError} onRetry={() => {
    setLoadError(null);
    setNonce((n) => n + 1);
  }} />;
  if (!times) return <p className="text-sm text-muted">Loading viewing times…</p>;
  if (days.length === 0) return <p className="text-sm text-slate-700">No viewing times in the next 14 days. Send an enquiry or message the agent to arrange one.</p>;

  return (
    <form onSubmit={book} className="space-y-3">
      <fieldset>
        <legend className="text-sm font-medium text-slate-700">Day</legend>
        <div className="no-scrollbar -mx-1 mt-1.5 flex gap-1.5 overflow-x-auto px-1 pb-1">
          {days.map((d) => (
            <button
              key={d}
              type="button"
              onClick={() => {
                setDay(d);
                setAt(null);
              }}
              aria-pressed={d === activeDay}
              className={cx(
                "flex min-h-14 min-w-16 shrink-0 flex-col items-center justify-center rounded-control border px-2 text-sm",
                d === activeDay ? "border-adire bg-adire text-white" : "border-line bg-surface text-asphalt hover:border-adire-line",
              )}
            >
              <span className="text-xs">{formatShortDay(d).split(",")[0]}</span>
              <span className="font-semibold">{formatShortDay(d).split(", ")[1] ?? formatShortDay(d)}</span>
            </button>
          ))}
        </div>
      </fieldset>
      {activeDay && (
        <fieldset>
          <legend className="text-sm font-medium text-slate-700">Time</legend>
          <div className="mt-1.5 flex flex-wrap gap-1.5">
            {(byDay.get(activeDay) ?? []).map((t) => (
              <button
                key={t}
                type="button"
                onClick={() => setAt(t)}
                aria-pressed={t === at}
                className={cx(
                  "min-h-11 rounded-full border px-3.5 text-sm font-medium tabular-nums",
                  t === at ? "border-adire bg-adire text-white" : "border-line bg-surface text-asphalt hover:border-adire-line",
                )}
              >
                {formatTime(t)}
              </button>
            ))}
          </div>
        </fieldset>
      )}
      {at && (
        <>
          <Input label="Phone for the agent (optional)" type="tel" autoComplete="tel" value={phone} onChange={(e) => setPhone(e.target.value)} />
          <Textarea label="Notes (optional)" rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} />
        </>
      )}
      <ErrorBox error={error} />
      <Button type="submit" className="w-full" disabled={!at} loading={busy}>
        Book viewing
      </Button>
      <p className="text-xs text-muted">Viewings are free. Never pay an &quot;inspection fee&quot;.</p>
      <Modal open={askLogin} title="Log in to book a viewing" onClose={() => setAskLogin(false)}>
        <p className="text-sm text-slate-700">We&apos;ll keep the time you picked. Log in or create a free account, then book it.</p>
        <div className="mt-4 grid gap-2">
          <Link href={`/login?next=${encodeURIComponent(pathname)}`} className={buttonClass("primary")}>
            Log in
          </Link>
          <Link href={`/signup?next=${encodeURIComponent(pathname)}`} className={buttonClass("secondary")}>
            Create an account
          </Link>
        </div>
      </Modal>
    </form>
  );
}

/** Phones: a bottom bar with the price and "Contact". */
export function MobileContactBar({ price, period }: { price: string; period: string }) {
  return (
    <div className="fixed inset-x-0 bottom-0 z-30 flex items-center justify-between gap-3 border-t border-line bg-surface px-4 pt-2 pb-[max(0.5rem,env(safe-area-inset-bottom))] lg:hidden">
      <p className="min-w-0 truncate text-adire">
        <span className="price text-xl">{price}</span>
        <span className="text-sm text-muted">{period}</span>
      </p>
      <a href="#contact" className={buttonClass("primary", "shrink-0 px-6")}>
        Contact
      </a>
    </div>
  );
}
