"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useState, type FormEvent } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { formatMoney } from "@/lib/money";
import { useCan } from "@/lib/permissions";
import { isLive } from "@/lib/status";
import { dateKey, formatPlainDate, formatTime } from "@/lib/time";
import type { BookingCustomerDetailDto, BookingCustomerDto } from "@/lib/types";
import { useApi } from "@/lib/use-api";
import { useBusiness } from "@/lib/use-business";
import { useNewBooking } from "@/components/new-booking";
import { telHref, whatsappHref } from "@/components/public-parts";
import { ApiErrorView, BookingStatusBadge, Button, Card, ErrorBox, Input, Notice, PageHeader, PageLoader, SectionTitle, SummaryStrip, Textarea, buttonClass, cx } from "@/components/ui";
import { PhoneIcon, PlusIcon, WhatsAppIcon } from "@/components/icons";

export default function CustomerPage() {
  const { id } = useParams<{ id: string }>();
  const { tz, currency } = useBusiness();
  const canEdit = useCan("bookings.edit");
  const { open, version } = useNewBooking();
  const { data: c, error, reload, setData } = useApi<BookingCustomerDetailDto>(`/api/booking-customers/${id}`, { nonce: version });
  const [banner, setBanner] = useState<string | null>(null);

  if (!c) {
    return (
      <div>
        <PageHeader title="Customer" back="/app/customers" />
        {error ? <ApiErrorView error={error} onRetry={reload} /> : <PageLoader />}
      </div>
    );
  }

  const bookings = [...c.bookings].sort((a, b) => b.startsAt.localeCompare(a.startsAt));

  return (
    <div className="space-y-5">
      <PageHeader
        title={c.name}
        back="/app/customers"
        subtitle={[c.phone, c.email].filter(Boolean).join(", ")}
        action={
          canEdit && (
            <Button onClick={() => open({ customer: { name: c.name, phone: c.phone, email: c.email } })}>
              <PlusIcon className="h-4 w-4" /> Book again
            </Button>
          )
        }
      />
      {banner && <Notice tone="success">{banner}</Notice>}
      {c.phone && (
        <div className="flex flex-wrap gap-2">
          <a href={telHref(c.phone)} className={buttonClass("secondary")}>
            <PhoneIcon className="h-4 w-4" /> Call
          </a>
          <a href={whatsappHref(c.phone)} target="_blank" rel="noopener noreferrer" className={buttonClass("secondary")}>
            <WhatsAppIcon className="h-4 w-4 text-mint" /> WhatsApp
          </a>
        </div>
      )}
      <SummaryStrip
        label="Customer figures"
        cols={4}
        items={[
          { label: "Visits", value: String(c.bookingsCount), sub: `${c.completedCount} completed` },
          { label: "No-shows", value: String(c.noShowCount), tone: c.noShowCount > 0 ? "red" : undefined, sub: c.noShowCount > 0 ? "Ask for a deposit next time" : "None" },
          { label: "Total spent", value: formatMoney(c.totalSpentMinor, currency) },
          { label: "Last visit", value: c.lastVisitAt ? formatPlainDate(dateKey(c.lastVisitAt, tz)) : "None yet" },
        ]}
      />

      <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_minmax(0,22rem)] lg:items-start">
        <section aria-labelledby="history">
          <h2 id="history" className="mb-3 text-lg font-bold">
            Bookings
          </h2>
          {bookings.length === 0 ? (
            <p className="rounded-panel border border-dashed border-slate-300 px-4 py-8 text-center text-sm text-muted">No bookings yet.</p>
          ) : (
            <ul className="divide-y divide-line-soft overflow-hidden rounded-panel border border-line bg-surface">
              {bookings.map((b) => (
                <li key={b.id}>
                  <Link href={`/app/bookings/${b.id}`} className="grid grid-cols-[5.5rem_minmax(0,1fr)_auto] items-center gap-x-3 px-4 py-3 hover:bg-mist">
                    <span className={cx(!isLive(b.status) && "text-muted")}>
                      <span className="block font-expanded text-lg leading-none font-extrabold tnum">{formatTime(b.startsAt, tz)}</span>
                      <span className="mt-1 block text-xs text-muted">{formatPlainDate(dateKey(b.startsAt, tz), "medium")}</span>
                    </span>
                    <span className="min-w-0">
                      <span className="block truncate font-medium">{b.serviceName}</span>
                      <span className="block truncate text-sm text-muted">with {b.staffName}</span>
                    </span>
                    <span className="text-right">
                      <span className="block text-sm font-semibold tnum">{formatMoney(b.priceMinor, currency)}</span>
                      <BookingStatusBadge status={b.status} />
                    </span>
                  </Link>
                </li>
              ))}
            </ul>
          )}
        </section>
        <CustomerForm
          key={`${c.id}-${c.name}-${c.notes}`}
          customer={c}
          canEdit={canEdit}
          onSaved={(u) => {
            setData((d) => (d ? { ...d, ...u } : d!));
            setBanner("Customer saved.");
          }}
        />
      </div>
    </div>
  );
}

function CustomerForm({ customer, canEdit, onSaved }: { customer: BookingCustomerDto; canEdit: boolean; onSaved: (c: BookingCustomerDto) => void }) {
  const [form, setForm] = useState({ name: customer.name, phone: customer.phone, email: customer.email ?? "", notes: customer.notes ?? "" });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const set = (k: keyof typeof form) => (e: { target: { value: string } }) => setForm((f) => ({ ...f, [k]: e.target.value }));

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!form.name.trim()) return setError(toApiError(new Error("Add their name.")));
    if (!form.phone.trim()) return setError(toApiError(new Error("Add their phone number. BookLink keeps customers by their phone.")));
    setBusy(true);
    setError(null);
    try {
      onSaved(
        await api<BookingCustomerDto>(`/api/booking-customers/${customer.id}`, {
          method: "PUT",
          body: { name: form.name.trim(), phone: form.phone.trim(), email: form.email.trim() || null, notes: form.notes.trim() || null },
        }),
      );
    } catch (err) {
      setError(toApiError(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card>
      <form onSubmit={submit} className="space-y-4">
        <SectionTitle>Details and notes</SectionTitle>
        <fieldset disabled={!canEdit} className="space-y-4">
          <Input label="Name" value={form.name} onChange={set("name")} required error={error?.field("name")} />
          <Input label="Phone" type="tel" value={form.phone} onChange={set("phone")} required error={error?.field("phone")} />
          <Input label="Email" type="email" value={form.email} onChange={set("email")} error={error?.field("email")} />
          <Textarea label="Notes" rows={4} value={form.notes} onChange={set("notes")} hint="Only your team sees these: preferences, allergies, usual stylist." />
        </fieldset>
        <ErrorBox error={error} />
        {canEdit && (
          <Button type="submit" loading={busy}>
            Save customer
          </Button>
        )}
      </form>
    </Card>
  );
}
