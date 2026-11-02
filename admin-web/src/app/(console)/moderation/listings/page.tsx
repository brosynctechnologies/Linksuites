"use client";

import { useEffect, useState } from "react";
import { RequireArea, useQueues } from "@/components/console";
import { ExternalIcon } from "@/components/icons";
import { Button, Dialog, PageHeader, StatusBadge, cx } from "@/components/ui";
import { Queue } from "@/components/queue";
import { api } from "@/lib/api";
import { money, plural, stamp } from "@/lib/format";
import type { ModerationItemDto } from "@/lib/types";
import { useApi } from "@/lib/use-api";

const PERIOD: Record<string, string> = { Year: " a year", Month: " a month", Night: " a night", Total: "" };

export default function ListingsQueuePage() {
  return (
    <RequireArea area="moderation">
      <ListingsQueue />
    </RequireArea>
  );
}

function ListingsQueue() {
  const { data, error, reload, setData } = useApi<ModerationItemDto[]>("/api/admin/property/moderation");
  const { refresh } = useQueues();

  return (
    <>
      <PageHeader
        title="Listings"
        subtitle="Pending listings (unverified agency or duplicate photos) and listings hidden after reports. Approving publishes; rejecting sends your note to the agency."
      />
      <Queue
        items={data}
        error={error}
        reload={reload}
        config={{
          label: "Listings queue",
          getId: (m) => m.listingId,
          getTitle: (m) => m.title,
          approveLabel: "Approve",
          rejectLabel: "Reject",
          rejectNote: { label: "Note to the agency", prompt: (m) => `Tell ${m.agencyName} what to fix in "${m.title}". They see this note in the studio.`, required: true },
          decide: async (m, approve, note) => {
            await api(`/api/admin/property/listings/${m.listingId}/decision`, { method: "POST", body: { approve, note } });
            setData((xs) => (xs ?? []).filter((x) => x.listingId !== m.listingId));
            refresh();
            return approve ? "Listing approved" : "Listing rejected";
          },
          emptyTitle: "The listings queue is clear",
          emptyText: "New pending and hidden listings appear here.",
          renderRow: (m) => (
            <>
              <span className="flex items-center gap-2">
                <StatusBadge status={m.status} />
                {m.duplicates.length > 0 && <span className="text-xs font-semibold text-stop">{plural(m.duplicates.length, "duplicate")}</span>}
                {m.openReports > 0 && <span className="text-xs font-semibold text-stop">{plural(m.openReports, "report")}</span>}
              </span>
              <span className="mt-1 block truncate font-medium">{m.title}</span>
              <span className="block truncate text-xs text-muted">
                {m.agencyName} · {stamp(m.submittedAt)}
              </span>
            </>
          ),
          renderDetail: (m) => <ListingDetail m={m} />,
        }}
      />
    </>
  );
}

function ListingDetail({ m }: { m: ModerationItemDto }) {
  const photos = m.photos?.length ? m.photos : m.coverUrl ? [m.coverUrl] : [];
  const [open, setOpen] = useState<number | null>(null);
  return (
    <div className="space-y-4">
      <div className="grid gap-4 xl:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
        <div className="min-w-0 space-y-2">
          {photos.length > 0 ? (
            <button type="button" onClick={() => setOpen(0)} className="block w-full rounded-control" aria-label={`Open photo 1 of ${photos.length} of ${m.title}`}>
              <img src={photos[0]} alt="" className="aspect-[4/3] w-full rounded-control bg-concrete object-cover" />
            </button>
          ) : (
            <div className="flex aspect-[4/3] w-full items-center justify-center rounded-control bg-concrete text-sm text-muted">No photo</div>
          )}
          {photos.length > 1 && (
            <ul className="grid grid-cols-5 gap-1.5 sm:grid-cols-6" aria-label={`${photos.length} photos`}>
              {photos.map((src, i) => (
                <li key={`${i}-${src}`}>
                  <button type="button" onClick={() => setOpen(i)} className="block w-full rounded-control" aria-label={`Open photo ${i + 1} of ${photos.length}`}>
                    <img src={src} alt="" loading="lazy" className="aspect-square w-full rounded-control bg-concrete object-cover hover:opacity-85" />
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
        <div className="min-w-0 space-y-2 text-sm">
          <h2 className="text-lg font-semibold">{m.title}</h2>
          <p className="tnum text-base font-semibold">
            {money(m.priceMinor)}
            <span className="font-normal text-muted">{PERIOD[m.pricePeriod] ?? ""}</span>
          </p>
          <p className="text-muted">{m.areaName ?? "—"}</p>
          <dl className="grid grid-cols-[max-content_1fr] gap-x-4 gap-y-1.5 border-t border-line-soft pt-2">
            <dt className="text-muted">Agency</dt>
            <dd>
              {m.agencyName} <span className="font-mono text-xs text-muted">{m.agencySlug}</span>
            </dd>
            <dt className="text-muted">Verified</dt>
            <dd>{m.agencyVerified ? <StatusBadge tone="ok">Verified</StatusBadge> : <StatusBadge tone="wait">Not verified</StatusBadge>}</dd>
            <dt className="text-muted">Open reports</dt>
            <dd className={cx("tnum", m.openReports > 0 && "font-semibold text-stop")}>{m.openReports}</dd>
            <dt className="text-muted">Host cancellations</dt>
            <dd className={cx("tnum", m.hostCancellations > 0 && "font-semibold text-stop")}>{m.hostCancellations}</dd>
            <dt className="text-muted">Submitted</dt>
            <dd>{stamp(m.submittedAt)}</dd>
            <dt className="text-muted">Listing id</dt>
            <dd className="font-mono text-xs break-all">{m.listingId}</dd>
          </dl>
          {m.note && <p className="rounded-control bg-concrete-2 px-3 py-2">Note: {m.note}</p>}
          {m.publicUrl && (
            <div className="pt-1">
              <a
                href={m.publicUrl}
                target="_blank"
                rel="noopener noreferrer"
                className="inline-flex min-h-10 items-center gap-1.5 rounded-control border border-line px-3 text-sm font-medium hover:bg-concrete-2"
              >
                View public page <ExternalIcon className="h-4 w-4" />
              </a>
              <p className="mt-1 text-xs text-muted">Only works once the listing is live.</p>
            </div>
          )}
        </div>
      </div>
      <PhotoViewer title={m.title} photos={photos} index={open} onIndex={setOpen} />

      {m.duplicates.length > 0 && (
        <section className="space-y-3 border-t border-line pt-4">
          <h3 className="text-sm font-semibold">Duplicate photos ({m.duplicates.length})</h3>
          {m.duplicates.map((d, i) => (
            <div key={i} className="grid gap-3 sm:grid-cols-2">
              <figure className="min-w-0">
                <img src={d.myPhotoUrl} alt={`Photo on ${m.title}`} className="aspect-[4/3] w-full rounded-control bg-concrete object-cover" />
                <figcaption className="mt-1 text-xs text-muted">This listing ({m.agencyName})</figcaption>
              </figure>
              <figure className="min-w-0">
                <img src={d.theirPhotoUrl} alt={`Matching photo on ${d.theirListingTitle}`} className="aspect-[4/3] w-full rounded-control bg-concrete object-cover" />
                <figcaption className="mt-1 text-xs text-muted">
                  {d.theirListingTitle} ({d.theirAgency}) <span className="font-mono">{d.theirListingId.slice(0, 8)}</span>
                </figcaption>
              </figure>
            </div>
          ))}
        </section>
      )}
    </div>
  );
}

/** A simple lightbox over the queue: one photo at a time, with Previous/Next buttons and the arrow keys. */
function PhotoViewer({ title, photos, index, onIndex }: { title: string; photos: string[]; index: number | null; onIndex: (i: number | null) => void }) {
  const i = index === null ? null : Math.min(Math.max(index, 0), photos.length - 1);
  const go = (step: number) => i !== null && onIndex((i + step + photos.length) % photos.length);
  const count = photos.length;
  // Arrow keys step through the photos while the viewer is open (focus starts on the close button).
  useEffect(() => {
    if (i === null || count < 2) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "ArrowRight") onIndex((i + 1) % count);
      else if (e.key === "ArrowLeft") onIndex((i - 1 + count) % count);
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [i, count, onIndex]);
  return (
    <Dialog open={i !== null && photos.length > 0} onClose={() => onIndex(null)} title={i === null ? title : `Photo ${i + 1} of ${photos.length}`} wide>
      {i !== null && (
        <div className="space-y-3">
          <img src={photos[i]} alt={`Photo ${i + 1} of ${title}`} className="mx-auto max-h-[70dvh] w-auto max-w-full rounded-control bg-concrete object-contain" />
          <div className="flex flex-wrap items-center justify-between gap-2">
            <a href={photos[i]} target="_blank" rel="noopener noreferrer" className="inline-flex min-h-10 items-center gap-1.5 text-sm font-medium underline underline-offset-2">
              Open full size <ExternalIcon className="h-4 w-4" />
            </a>
            {photos.length > 1 && (
              <div className="flex gap-2">
                <Button variant="secondary" onClick={() => go(-1)} aria-keyshortcuts="ArrowLeft">
                  Previous
                </Button>
                <Button variant="secondary" onClick={() => go(1)} aria-keyshortcuts="ArrowRight">
                  Next
                </Button>
              </div>
            )}
          </div>
        </div>
      )}
    </Dialog>
  );
}
