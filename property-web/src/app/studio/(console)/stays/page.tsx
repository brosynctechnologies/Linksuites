"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useState } from "react";
import { api, asList, toApiError, type ApiError } from "@/lib/api";
import { addDays, formatDay, lagosToday, phoneDigits } from "@/lib/format";
import { formatMoney } from "@/lib/money";
import { useCan } from "@/lib/permissions";
import { STAY_STATUS, statusOf } from "@/lib/status";
import { qs, useApi } from "@/lib/use-api";
import type { BlockDto, StudioListingSummaryDto, StudioStayDto } from "@/lib/types";
import { MonthCalendar, nextMonth } from "@/components/month-calendar";
import { ChevronLeftIcon, ChevronRightIcon, KeyIcon, TrashIcon } from "@/components/icons";
import { ApiErrorView, Button, Card, EmptyState, ErrorBox, Input, Notice, PageHeader, PageLoader, SectionTitle, Select, StatusBadge, buttonClass } from "@/components/ui";

const inRange = (day: string, start: string, end: string) => day >= start.slice(0, 10) && day < end.slice(0, 10);

export default function StaysPage() {
  const sp = useSearchParams();
  const listings = useApi<StudioListingSummaryDto[]>("/api/property/listings");
  const shortLets = asList(listings.data).filter((l) => l.purpose === "ShortLet");
  const [picked, setPicked] = useState<string | null>(sp.get("listing"));
  const selected = shortLets.find((l) => l.id === picked) ?? shortLets[0] ?? null;

  if (!listings.data) {
    return (
      <div>
        <PageHeader title="Short-lets" />
        {listings.error ? <ApiErrorView error={listings.error} onRetry={listings.reload} /> : <PageLoader />}
      </div>
    );
  }
  if (shortLets.length === 0) {
    return (
      <div>
        <PageHeader title="Short-lets" />
        <EmptyState
          icon={<KeyIcon />}
          title="No short-let listings"
          text="Create a listing for Short-let to take bookings and payments here."
          action={
            <Link href="/studio/listings/new" className={buttonClass("primary")}>
              New listing
            </Link>
          }
        />
      </div>
    );
  }

  return (
    <div className="space-y-5">
      <PageHeader title="Short-lets" subtitle="Close dates you can't host, and see who's staying." />
      <Select label="Listing" value={selected?.id ?? ""} onChange={(e) => setPicked(e.target.value)} className="max-w-md">
        {shortLets.map((l) => (
          <option key={l.id} value={l.id}>
            {l.title} ({l.areaName})
          </option>
        ))}
      </Select>
      {selected && <ListingCalendar key={selected.id} listing={selected} />}
    </div>
  );
}

function ListingCalendar({ listing }: { listing: StudioListingSummaryDto }) {
  const canEdit = useCan("listings.edit");
  const canCancel = useCan("money.manage");
  const [today] = useState(() => lagosToday());
  const [month, setMonth] = useState(today.slice(0, 7));
  const blocks = useApi<BlockDto[]>(`/api/property/listings/${listing.id}/blocks`);
  const stays = useApi<StudioStayDto[]>(`/api/property/stays${qs({ from: addDays(today, -60), to: addDays(today, 365) })}`);
  const [start, setStart] = useState<string | null>(null);
  const [end, setEnd] = useState<string | null>(null);
  const [note, setNote] = useState("");
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<ApiError | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const myStays = asList(stays.data)
    .filter((s) => s.listingId === listing.id)
    .sort((a, b) => a.checkIn.localeCompare(b.checkIn));
  const live = myStays.filter((s) => s.status === "Confirmed" || s.status === "Pending");
  const blockList = asList(blocks.data);
  const isBlocked = (d: string) => blockList.some((b) => inRange(d, b.start, b.end));
  const isBooked = (d: string) => live.some((s) => inRange(d, s.checkIn, s.checkOut));

  function pick(day: string) {
    setNotice(null);
    if (!start || end || day < start) {
      setStart(day);
      setEnd(null);
    } else setEnd(day);
  }

  async function close() {
    if (!start) return;
    setBusy("close");
    setError(null);
    try {
      // The range is inclusive on screen; the API's end is exclusive.
      await api<BlockDto>(`/api/property/listings/${listing.id}/blocks`, { method: "POST", body: { start, end: addDays(end ?? start, 1), note: note.trim() || null } });
      setNotice(`Closed ${formatDay(start)}${end && end !== start ? ` to ${formatDay(end)}` : ""}.`);
      setStart(null);
      setEnd(null);
      setNote("");
      blocks.reload();
    } catch (e) {
      setError(toApiError(e));
    } finally {
      setBusy(null);
    }
  }

  async function reopen(b: BlockDto) {
    setBusy(b.id);
    setError(null);
    try {
      await api(`/api/property/listings/${listing.id}/blocks/${b.id}`, { method: "DELETE" });
      blocks.reload();
    } catch (e) {
      setError(toApiError(e));
    } finally {
      setBusy(null);
    }
  }

  async function hostCancel(s: StudioStayDto) {
    if (!window.confirm(`Cancel ${s.guestName}'s stay (${formatDay(s.checkIn)} to ${formatDay(s.checkOut)})? They get a full refund and it counts against your agency.`)) return;
    setBusy(s.id);
    setError(null);
    try {
      await api(`/api/property/stays/${s.id}/cancel`, { method: "POST" });
      setNotice(`Cancelled ${s.guestName}'s stay. They get a full refund.`);
      stays.reload();
    } catch (e) {
      setError(toApiError(e));
    } finally {
      setBusy(null);
    }
  }

  const look = (day: string) => {
    const booked = isBooked(day);
    const blocked = isBlocked(day);
    const sel = day === start || day === end;
    const between = !!start && !!end && day > start && day < end;
    return {
      disabled: !canEdit || day < today,
      struck: booked || blocked,
      selected: sel,
      inRange: between,
      dot: booked ? "bg-seal" : blocked ? "bg-brick" : undefined,
      note: booked ? "booked" : blocked ? "closed" : undefined,
    };
  };

  return (
    <div className="space-y-5">
      <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_20rem]">
        <Card>
          <div className="flex items-center justify-between gap-2">
            <h2 className="font-semibold text-asphalt">Calendar</h2>
            <div className="flex gap-1">
              <button type="button" onClick={() => setMonth((m) => nextMonth(m, -1))} className="flex h-11 w-11 items-center justify-center rounded-control hover:bg-plaster-2" aria-label="Earlier months">
                <ChevronLeftIcon />
              </button>
              <button type="button" onClick={() => setMonth((m) => nextMonth(m, 1))} className="flex h-11 w-11 items-center justify-center rounded-control hover:bg-plaster-2" aria-label="Later months">
                <ChevronRightIcon />
              </button>
            </div>
          </div>
          <p className="mt-1 flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted">
            <span className="inline-flex items-center gap-1.5">
              <span className="h-1.5 w-1.5 rounded-full bg-seal" aria-hidden="true" /> Booked
            </span>
            <span className="inline-flex items-center gap-1.5">
              <span className="h-1.5 w-1.5 rounded-full bg-brick" aria-hidden="true" /> Closed by you
            </span>
          </p>
          <div className="mt-3 grid gap-5 sm:grid-cols-2">
            <MonthCalendar month={month} look={look} onPick={canEdit ? pick : undefined} />
            <MonthCalendar month={nextMonth(month)} look={look} onPick={canEdit ? pick : undefined} />
          </div>
        </Card>
        <Card className="space-y-3">
          <SectionTitle>Close dates</SectionTitle>
          {!canEdit ? (
            <p className="text-sm text-muted">Your role can see the calendar but not change it.</p>
          ) : (
            <>
              <p className="text-sm text-slate-700">
                {start ? (
                  <>
                    Closing <strong>{formatDay(start)}</strong>
                    {end && end !== start ? (
                      <>
                        {" "}
                        to <strong>{formatDay(end)}</strong>
                      </>
                    ) : (
                      " (tap another day to close a range)"
                    )}
                    .
                  </>
                ) : (
                  "Tap the first night you can't host, then the last."
                )}
              </p>
              <Input label="Note (optional)" value={note} onChange={(e) => setNote(e.target.value)} placeholder="e.g. Repairs" />
              <Button className="w-full" disabled={!start} loading={busy === "close"} onClick={close}>
                Close these nights
              </Button>
            </>
          )}
          {blockList.length > 0 && (
            <ul className="divide-y divide-line-soft border-t border-line-soft pt-1">
              {blockList.map((b) => (
                <li key={b.id} className="flex items-center justify-between gap-2 py-2 text-sm">
                  <span>
                    {formatDay(b.start)} to {formatDay(addDays(b.end.slice(0, 10), -1))}
                    {b.note && <span className="block text-xs text-muted">{b.note}</span>}
                  </span>
                  {canEdit && (
                    <button type="button" disabled={busy === b.id} onClick={() => reopen(b)} className="flex h-11 w-11 items-center justify-center rounded-control text-brick hover:bg-brick-wash" aria-label="Reopen these dates">
                      <TrashIcon className="h-4 w-4" />
                    </button>
                  )}
                </li>
              ))}
            </ul>
          )}
        </Card>
      </div>

      {notice && <Notice tone="success">{notice}</Notice>}
      {error && <ApiErrorView error={error} />}
      {blocks.error && <ErrorBox error={blocks.error} onRetry={blocks.reload} />}

      <section aria-labelledby="stays-h">
        <h2 id="stays-h" className="mb-2 font-semibold text-asphalt">
          Stays
        </h2>
        {!stays.data && stays.error && <ErrorBox error={stays.error} onRetry={stays.reload} />}
        {stays.data && myStays.length === 0 && <p className="text-sm text-muted">No stays booked for this listing yet.</p>}
        {myStays.length > 0 && (
          <ul className="divide-y divide-line-soft overflow-hidden rounded-panel border border-line bg-surface">
            {myStays.map((s) => {
              const st = statusOf(STAY_STATUS, s.status);
              return (
                <li key={s.id} className="flex flex-col gap-2 p-4 sm:flex-row sm:items-center">
                  <div className="min-w-0 flex-1">
                    <p className="font-semibold text-asphalt">
                      {s.guestName} <span className="font-normal text-muted">· {s.guests} guest{s.guests === 1 ? "" : "s"}</span>
                    </p>
                    <p className="text-sm text-slate-700">
                      {formatDay(s.checkIn)} to {formatDay(s.checkOut)}, {s.nights} night{s.nights === 1 ? "" : "s"}
                    </p>
                    <div className="mt-1 flex flex-wrap items-center gap-2 text-xs text-muted">
                      <StatusBadge tone={st.tone}>{st.label}</StatusBadge>
                      {s.cancelledBy && <span>Cancelled by {s.cancelledBy === "Host" ? "you" : "the guest"}</span>}
                      {s.refundMinor > 0 && <span>Refunded {formatMoney(s.refundMinor)}</span>}
                    </div>
                  </div>
                  <div className="text-sm sm:text-right">
                    <p className="price text-asphalt">{formatMoney(s.totalMinor)}</p>
                    {/* The server's earning after commission and any refund, so it matches the Earnings page. */}
                    <p className="text-xs text-muted">
                      {s.status === "Pending" ? "You'd earn" : "You earn"} {formatMoney(s.hostNetMinor)}
                    </p>
                  </div>
                  <div className="flex gap-2">
                    {s.guestPhone && (
                      <a href={`tel:+${phoneDigits(s.guestPhone)}`} className={buttonClass("secondary")}>
                        Call
                      </a>
                    )}
                    {/* The server refuses once the stay has started, which is from the check-in day itself. */}
                    {canCancel && s.status === "Confirmed" && s.checkIn > today && (
                      <Button variant="danger" loading={busy === s.id} onClick={() => hostCancel(s)}>
                        Cancel stay
                      </Button>
                    )}
                  </div>
                </li>
              );
            })}
          </ul>
        )}
      </section>
    </div>
  );
}
