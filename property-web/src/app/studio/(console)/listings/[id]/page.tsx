"use client";

import Link from "next/link";
import { useParams, useRouter, useSearchParams } from "next/navigation";
import { useState } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { formatCount, formatDay } from "@/lib/format";

import { useCan } from "@/lib/permissions";
import { LISTING_STATUS, renewNotice, statusOf } from "@/lib/status";
import { useApi } from "@/lib/use-api";
import type { StudioListingDto } from "@/lib/types";
import { BoostPicker } from "@/components/boost-picker";
import { BoltIcon, ExternalIcon } from "@/components/icons";
import { ApiErrorView, Button, Card, Modal, Notice, PageHeader, PageLoader, StatusBadge, buttonClass } from "@/components/ui";
import { ListingWizard, STEPS, type StepIndex } from "../wizard";

const STEP_PARAM: Record<string, StepIndex> = { basics: 0, location: 1, costs: 2, photos: 3, review: 4 };

export default function EditListingPage() {
  const { id } = useParams<{ id: string }>();
  const sp = useSearchParams();
  const { data, error, reload, setData } = useApi<StudioListingDto>(`/api/property/listings/${id}`);
  const step = STEP_PARAM[sp.get("step") ?? ""] ?? 0;

  if (!data) {
    return (
      <div>
        <PageHeader title="Listing" back="/studio/listings" />
        {error ? <ApiErrorView error={error} onRetry={reload} /> : <PageLoader />}
      </div>
    );
  }
  return (
    <div className="space-y-6">
      <PageHeader title={data.title || "Untitled listing"} back="/studio/listings" subtitle={`Step through ${STEPS.join(", ").toLowerCase()}.`} />
      <ManagePanel listing={data} onListing={setData} />
      <ListingWizard key={data.id} listing={data} initialStep={step} onListing={setData} />
    </div>
  );
}

/** Status, renew, rented/sold, back to draft, delete and boosts for one listing. */
function ManagePanel({ listing: l, onListing }: { listing: StudioListingDto; onListing: (l: StudioListingDto) => void }) {
  const router = useRouter();
  const canEdit = useCan("listings.edit");
  const canBoost = useCan("money.manage");
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<ApiError | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [boostOpen, setBoostOpen] = useState(false);
  const [nowMs] = useState(() => Date.now());
  const st = statusOf(LISTING_STATUS, l.status);
  const featured = l.featuredUntil && new Date(l.featuredUntil).getTime() > nowMs;
  const top = l.topUntil && new Date(l.topUntil).getTime() > nowMs;

  async function act(key: string, fn: () => Promise<void>) {
    setBusy(key);
    setError(null);
    setNotice(null);
    try {
      await fn();
    } catch (e) {
      setError(toApiError(e));
    } finally {
      setBusy(null);
    }
  }

  const renew = () =>
    act("renew", async () => {
      const res = await api<StudioListingDto | undefined>(`/api/property/listings/${l.id}/renew`, { method: "POST" });
      const fresh = res && typeof res === "object" && "id" in res ? res : await api<StudioListingDto>(`/api/property/listings/${l.id}`);
      onListing(fresh);
      setNotice(renewNotice(fresh));
    });
  const setStatus = (status: "Rented" | "Sold" | "Draft") =>
    act(status, async () => {
      if (status !== "Draft" && !window.confirm(`Mark "${l.title}" as ${status.toLowerCase()}? It comes off the site.`)) return;
      onListing(await api<StudioListingDto>(`/api/property/listings/${l.id}/status`, { method: "POST", body: { status } }));
      setNotice(status === "Draft" ? "Moved back to drafts." : `Marked as ${status.toLowerCase()}.`);
    });
  const remove = () =>
    act("delete", async () => {
      if (!window.confirm(`Delete "${l.title}"? This can't be undone.`)) return;
      await api(`/api/property/listings/${l.id}`, { method: "DELETE" });
      router.replace("/studio/listings");
    });

  return (
    <Card className="space-y-3">
      <div className="flex flex-wrap items-center gap-2">
        <StatusBadge tone={st.tone}>{st.label}</StatusBadge>
        {featured && <span className="rounded-[4px] bg-adire px-1.5 py-0.5 text-xs font-semibold text-white">Featured until {formatDay(l.featuredUntil)}</span>}
        {top && <span className="rounded-[4px] bg-adire-wash px-1.5 py-0.5 text-xs font-semibold text-adire">Top of search until {formatDay(l.topUntil)}</span>}
        <span className="text-sm text-muted">
          <span className="tnum font-semibold text-asphalt">{formatCount(l.viewsCount)}</span> views
          {l.expiresAt && l.status === "Active" ? `. Live until ${formatDay(l.expiresAt)}` : ""}
          {l.status === "Expired" && l.expiresAt ? `. Expired ${formatDay(l.expiresAt)}` : ""}
        </span>
        {l.status === "Active" && l.publicUrl && (
          <a href={l.publicUrl} target="_blank" rel="noopener noreferrer" className="ml-auto inline-flex min-h-11 items-center gap-1.5 text-sm font-semibold text-adire hover:underline">
            <ExternalIcon className="h-4 w-4" /> View on site
          </a>
        )}
      </div>
      {l.moderationNote && (l.status === "Rejected" || l.status === "Hidden" || l.status === "Pending") && (
        <Notice tone={l.status === "Pending" ? "warning" : "error"}>
          <span className="font-semibold">Moderator note:</span> {l.moderationNote}
        </Notice>
      )}
      {l.status === "Hidden" && <Notice tone="error">This listing was reported and is hidden while a moderator looks at it.</Notice>}
      {notice && <Notice tone="success">{notice}</Notice>}
      {error && <ApiErrorView error={error} />}
      {canEdit && (
        <div className="flex flex-wrap gap-2">
          {(l.status === "Active" || l.status === "Expired" || l.status === "Rented" || l.status === "Sold") && (
            <Button variant="secondary" loading={busy === "renew"} onClick={renew}>
              Renew listing
            </Button>
          )}
          {l.status === "Active" && l.purpose === "Rent" && (
            <Button variant="secondary" loading={busy === "Rented"} onClick={() => setStatus("Rented")}>
              Mark rented
            </Button>
          )}
          {l.status === "Active" && l.purpose === "Sale" && (
            <Button variant="secondary" loading={busy === "Sold"} onClick={() => setStatus("Sold")}>
              Mark sold
            </Button>
          )}
          {(l.status === "Active" || l.status === "Pending" || l.status === "Rejected" || l.status === "Expired") && (
            <Button variant="ghost" loading={busy === "Draft"} onClick={() => setStatus("Draft")}>
              Move to drafts
            </Button>
          )}
          {canBoost && l.status === "Active" && (
            <Button onClick={() => setBoostOpen(true)}>
              <BoltIcon className="h-4 w-4" /> Boost
            </Button>
          )}
          {l.purpose === "ShortLet" && (
            <Link href={`/studio/stays?listing=${l.id}`} className={buttonClass("secondary")}>
              Calendar
            </Link>
          )}
          <Button variant="danger" className="sm:ml-auto" loading={busy === "delete"} onClick={remove}>
            Delete
          </Button>
        </div>
      )}
      <Modal open={boostOpen} title="Boost this listing" onClose={() => setBoostOpen(false)}>
        {boostOpen && <BoostPicker listingId={l.id} />}
      </Modal>
    </Card>
  );
}

