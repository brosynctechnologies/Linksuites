"use client";

import Link from "next/link";
import { createContext, useCallback, useContext, useMemo, useState, type FormEvent, type ReactNode } from "react";
import { api, asList, toApiError, type ApiError } from "@/lib/api";
import { formatMoney } from "@/lib/money";
import { addDays, cityOf, formatDateTime, formatDuration, todayIn, zonedInstant } from "@/lib/time";
import type { BookingCustomerDto, BookingDto, BookingSource, Paged, SlotDto } from "@/lib/types";
import { useApi } from "@/lib/use-api";
import { staffForService, useBusiness, useServices, useStaff } from "@/lib/use-business";
import { useDebounced } from "@/lib/use-debounced";
import { DateSlotPicker } from "./slot-picker";
import { CloseIcon } from "./icons";
import { Button, Checkbox, ErrorBox, Input, Modal, Notice, PageLoader, SearchField, Segmented, Select, Textarea, UpgradePrompt, cx } from "./ui";

export interface NewBookingPrefill {
  date?: string;
  /** "HH:mm" in the business zone: opens on "Custom time". */
  time?: string;
  staffId?: string;
  customer?: { name: string; phone: string | null; email: string | null };
}

interface Ctx {
  open: (prefill?: NewBookingPrefill) => void;
  /** Bumps after every booking created here; pass it as a nonce to refetch lists. */
  version: number;
}

const NewBookingContext = createContext<Ctx>({ open: () => {}, version: 0 });

export function useNewBooking() {
  return useContext(NewBookingContext);
}

export function NewBookingProvider({ children }: { children: ReactNode }) {
  const [prefill, setPrefill] = useState<NewBookingPrefill | null>(null);
  const [session, setSession] = useState(0);
  const [version, setVersion] = useState(0);
  const [created, setCreated] = useState<BookingDto | null>(null);
  const { tz } = useBusiness();

  const open = useCallback((p?: NewBookingPrefill) => {
    setPrefill(p ?? {});
    setSession((s) => s + 1);
    setCreated(null);
  }, []);
  const value = useMemo(() => ({ open, version }), [open, version]);

  return (
    <NewBookingContext.Provider value={value}>
      {children}
      <Modal open={prefill !== null} title="New booking" onClose={() => setPrefill(null)} wide>
        {prefill && (
          <NewBookingForm
            key={session}
            prefill={prefill}
            onCancel={() => setPrefill(null)}
            onCreated={(b) => {
              setPrefill(null);
              setCreated(b);
              setVersion((v) => v + 1);
            }}
          />
        )}
      </Modal>
      {created && (
        <div className="fixed inset-x-4 bottom-[calc(5rem+env(safe-area-inset-bottom))] z-40 mx-auto max-w-md animate-pop-in lg:bottom-6" role="status">
          <div className="flex items-center gap-3 rounded-panel bg-aubergine px-4 py-3 text-sm text-white shadow-float">
            <p className="min-w-0 flex-1">
              Booked {created.customerName} for {formatDateTime(created.startsAt, tz)}.
            </p>
            <Link href={`/app/bookings/${created.id}`} className="shrink-0 font-semibold text-rose-light underline underline-offset-2" onClick={() => setCreated(null)}>
              Open
            </Link>
            <button type="button" onClick={() => setCreated(null)} aria-label="Dismiss" className="-mr-1 shrink-0 rounded-control p-1 text-on-aubergine hover:text-white">
              <CloseIcon className="h-4 w-4" />
            </button>
          </div>
        </div>
      )}
    </NewBookingContext.Provider>
  );
}

type Mode = "slots" | "custom";

function NewBookingForm({ prefill, onCancel, onCreated }: { prefill: NewBookingPrefill; onCancel: () => void; onCreated: (b: BookingDto) => void }) {
  const { business, tz, currency, loading: bizLoading } = useBusiness();
  const { services: allServices, data: servicesData, error: servicesError } = useServices();
  const { staff: allStaff } = useStaff();
  const services = [...allServices.filter((s) => s.isActive), ...allServices.filter((s) => !s.isActive)];

  const [serviceId, setServiceId] = useState("");
  const [staffId, setStaffId] = useState(prefill.staffId ?? "");
  const [mode, setMode] = useState<Mode>(prefill.time ? "custom" : "slots");
  const [slot, setSlot] = useState<SlotDto | null>(null);
  const [refreshKey, setRefreshKey] = useState(0);
  const today = todayIn(tz);
  const [date, setDate] = useState(prefill.date ?? "");
  const [time, setTime] = useState(prefill.time ?? "");
  const [force, setForce] = useState(false);
  const [customer, setCustomer] = useState<{ id?: string; name: string; phone: string; email: string }>({
    name: prefill.customer?.name ?? "",
    phone: prefill.customer?.phone ?? "",
    email: prefill.customer?.email ?? "",
  });
  const [source, setSource] = useState<BookingSource>("Phone");
  const [notes, setNotes] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  const service = services.find((s) => s.id === serviceId) ?? null;
  const eligible = staffForService(allStaff, service);

  if (servicesError) return <ErrorBox error={servicesError} />;
  if (!servicesData || bizLoading) return <PageLoader />;
  if (services.length === 0) {
    return (
      <div className="space-y-3 text-sm">
        <p>Add a service first: bookings need to know what is being done and how long it takes.</p>
        <Link href="/app/services" className="font-semibold text-rose underline underline-offset-2" onClick={onCancel}>
          Add a service
        </Link>
      </div>
    );
  }

  const effectiveDate = date || today;
  const startsAt = mode === "slots" ? (slot?.startsAt ?? null) : time ? zonedInstant(effectiveDate, time, tz) : null;
  const chosenStaff = mode === "slots" ? staffId || slot?.staffIds[0] || "" : staffId;

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!service) return setError(toApiError(new Error("Choose a service.")));
    if (!startsAt) return setError(toApiError(new Error(mode === "slots" ? "Pick a free time." : "Enter a date and time.")));
    if (!chosenStaff) return setError(toApiError(new Error("Choose who will do it.")));
    if (!customer.name.trim()) return setError(toApiError(new Error("Add the customer's name.")));
    if (!customer.phone.trim()) return setError(toApiError(new Error("Add a phone number. BookLink keeps customers by their phone.")));
    setBusy(true);
    setError(null);
    try {
      const b = await api<BookingDto>("/api/bookings", {
        method: "POST",
        body: {
          serviceId: service.id,
          staffId: chosenStaff,
          startsAt,
          customer: { name: customer.name.trim(), phone: customer.phone.trim(), email: customer.email.trim() || null },
          source,
          notes: notes.trim() || null,
          force: mode === "custom" && force,
        },
      });
      onCreated(b);
    } catch (err) {
      const apiErr = toApiError(err);
      if (apiErr.status === 409 && mode === "slots") {
        setSlot(null);
        setRefreshKey((k) => k + 1);
      }
      setError(apiErr);
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-5">
      <div className="grid gap-4 sm:grid-cols-2">
        <Select
          label="Service"
          value={serviceId}
          onChange={(e) => {
            setServiceId(e.target.value);
            setSlot(null);
            const next = services.find((s) => s.id === e.target.value);
            if (staffId && next && !staffForService(allStaff, next).some((s) => s.id === staffId)) setStaffId("");
          }}
        >
          <option value="">Choose a service</option>
          {services.map((s) => (
            <option key={s.id} value={s.id}>
              {s.name} ({formatDuration(s.durationMinutes)}, {formatMoney(s.priceMinor, currency)}){s.isActive ? "" : ", hidden online"}
            </option>
          ))}
        </Select>
        <Select
          label="With"
          value={staffId}
          onChange={(e) => {
            setStaffId(e.target.value);
            setSlot(null);
          }}
          disabled={!service}
        >
          <option value="">{mode === "slots" ? "Anyone free" : "Choose someone"}</option>
          {(service ? eligible : allStaff).map((s) => (
            <option key={s.id} value={s.id}>
              {s.displayName}
            </option>
          ))}
        </Select>
      </div>

      {service && (
        <div className="space-y-4">
          <Segmented<Mode>
            label="How to pick the time"
            className="max-w-xs"
            items={[
              { value: "slots", label: "Free times" },
              { value: "custom", label: "Custom time" },
            ]}
            value={mode}
            onChange={(m) => {
              setMode(m);
              setSlot(null);
            }}
          />
          {mode === "slots" ? (
            <DateSlotPicker
              key={`${service.id}:${staffId}`}
              source={{ kind: "app", serviceId: service.id, staffId: staffId || null }}
              timezone={tz}
              today={today}
              maxDate={addDays(today, Math.max(business?.maxDaysAhead ?? 60, 365))}
              initialDate={prefill.date}
              value={slot}
              onChange={setSlot}
              refreshKey={refreshKey}
            />
          ) : (
            <div className="space-y-3">
              <div className="grid grid-cols-2 gap-3 sm:max-w-sm">
                <Input label="Date" type="date" value={effectiveDate} onChange={(e) => setDate(e.target.value)} required />
                <Input label="Time" type="time" value={time} step={300} onChange={(e) => setTime(e.target.value)} required />
              </div>
              <Checkbox
                checked={force}
                onChange={setForce}
                label={
                  <>
                    Book outside working hours
                    <span className="block text-xs text-muted">It still won&apos;t overlap another booking.</span>
                  </>
                }
              />
            </div>
          )}
          <p className="text-xs text-muted">Times are in {cityOf(tz)} time.</p>
        </div>
      )}

      <CustomerFields value={customer} onChange={setCustomer} />

      <div className="grid gap-4 sm:grid-cols-2">
        <div>
          <p className="mb-1.5 text-sm font-medium text-slate-700">How they booked</p>
          <Segmented<BookingSource>
            label="How they booked"
            items={[
              { value: "Phone", label: "Phone" },
              { value: "WalkIn", label: "Walk-in" },
            ]}
            value={source}
            onChange={setSource}
          />
        </div>
        <Textarea label="Notes (optional)" rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} />
      </div>

      {error && (error.isPlanLimit ? <UpgradePrompt message={error.detail} /> : <ErrorBox error={error} />)}
      {error?.status === 409 && mode === "slots" && <Notice tone="warning">The free times above are refreshed.</Notice>}

      <div className="flex justify-end gap-2 border-t border-line-soft pt-4">
        <Button variant="secondary" onClick={onCancel}>
          Cancel
        </Button>
        <Button type="submit" loading={busy}>
          {startsAt ? `Book ${formatDateTime(startsAt, tz)}` : "Book"}
        </Button>
      </div>
    </form>
  );
}

/** Search existing customers (by name or phone) or type a new one. */
export function CustomerFields({
  value,
  onChange,
}: {
  value: { id?: string; name: string; phone: string; email: string };
  onChange: (v: { id?: string; name: string; phone: string; email: string }) => void;
}) {
  const [q, setQ] = useState("");
  const dq = useDebounced(q.trim());
  const { data, loading } = useApi<Paged<BookingCustomerDto>>(dq.length >= 2 ? `/api/booking-customers?q=${encodeURIComponent(dq)}&pageSize=6` : null);
  const results = asList(data);

  if (value.id) {
    return (
      <div>
        <p className="mb-1.5 text-sm font-medium text-slate-700">Customer</p>
        <div className="flex items-center justify-between gap-3 rounded-control border border-line bg-mist px-3 py-2.5">
          <div className="min-w-0">
            <p className="truncate font-semibold">{value.name}</p>
            <p className="truncate text-xs text-muted">{[value.phone, value.email].filter(Boolean).join(", ")}</p>
          </div>
          <Button variant="ghost" className="min-h-8 px-2 py-1" onClick={() => onChange({ name: "", phone: "", email: "" })}>
            Change
          </Button>
        </div>
      </div>
    );
  }

  return (
    <fieldset className="space-y-3">
      <legend className="mb-1.5 text-sm font-medium text-slate-700">Customer</legend>
      <div className="relative">
        <SearchField id="customer-search" label="Find a customer" placeholder="Find a returning customer by name or phone" value={q} onChange={setQ} />
        {dq.length >= 2 && (
          <ul className={cx("mt-1 divide-y divide-line-soft overflow-hidden rounded-control border border-line bg-surface", loading && "opacity-60")}>
            {results.length === 0 && !loading && <li className="px-3 py-2.5 text-sm text-muted">No match. Fill in their details below.</li>}
            {results.map((c) => (
              <li key={c.id}>
                <button
                  type="button"
                  className="flex w-full items-center justify-between gap-3 px-3 py-2.5 text-left hover:bg-mist"
                  onClick={() => {
                    onChange({ id: c.id, name: c.name, phone: c.phone, email: c.email ?? "" });
                    setQ("");
                  }}
                >
                  <span className="min-w-0">
                    <span className="block truncate font-medium">{c.name}</span>
                    <span className="block truncate text-xs text-muted">{c.phone}</span>
                  </span>
                  <span className="shrink-0 text-xs text-muted">
                    {c.bookingsCount} visit{c.bookingsCount === 1 ? "" : "s"}
                    {c.noShowCount > 0 && <span className="text-brick">, {c.noShowCount} no-show</span>}
                  </span>
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>
      <div className="grid gap-3 sm:grid-cols-3">
        <Input label="Name" autoComplete="off" value={value.name} onChange={(e) => onChange({ ...value, name: e.target.value })} required />
        <Input label="Phone" type="tel" autoComplete="off" value={value.phone} onChange={(e) => onChange({ ...value, phone: e.target.value })} required />
        <Input
          label="Email"
          type="email"
          autoComplete="off"
          value={value.email}
          onChange={(e) => onChange({ ...value, email: e.target.value })}
          hint="For email reminders."
        />
      </div>
    </fieldset>
  );
}
