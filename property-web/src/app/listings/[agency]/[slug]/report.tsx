"use client";

import { useState, type FormEvent } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { REPORT_REASONS } from "@/lib/config";
import type { ReportReason } from "@/lib/types";
import { FlagIcon } from "@/components/icons";
import { Button, ErrorBox, Modal, Notice, Textarea, cx } from "@/components/ui";

/** "Report this listing": fake, taken, wrong price, duplicate, scam. 409 = this visitor already reported it. */
export function ReportLink({ listingId }: { listingId: string }) {
  const [open, setOpen] = useState(false);
  const [done, setDone] = useState<string | null>(null);
  return (
    <>
      <button type="button" onClick={() => setOpen(true)} className="inline-flex min-h-11 items-center gap-1.5 text-sm font-medium text-brick hover:underline">
        <FlagIcon className="h-4 w-4" /> Report this listing
      </button>
      {done && <Notice tone="success">{done}</Notice>}
      <Modal open={open} title="Report this listing" onClose={() => setOpen(false)}>
        {open && (
          <ReportForm
            listingId={listingId}
            onDone={(msg) => {
              setDone(msg);
              setOpen(false);
            }}
          />
        )}
      </Modal>
    </>
  );
}

function ReportForm({ listingId, onDone }: { listingId: string; onDone: (msg: string) => void }) {
  const [reason, setReason] = useState<ReportReason | "">("");
  const [details, setDetails] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!reason) return;
    setBusy(true);
    setError(null);
    try {
      await api(`/api/listings/${listingId}/report`, { method: "POST", body: { reason, details: details.trim() || null } });
      onDone("Thanks. Our moderators will look at this listing.");
    } catch (err) {
      const ae = toApiError(err);
      if (ae.status === 409) return onDone("You have already reported this listing. Our moderators are on it.");
      setError(ae);
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-4">
      <fieldset className="space-y-2">
        <legend className="text-sm font-medium text-slate-700">What&apos;s wrong?</legend>
        {REPORT_REASONS.map((r) => (
          <label
            key={r.value}
            className={cx("flex min-h-11 cursor-pointer items-center gap-3 rounded-control border px-3 py-2 text-sm", reason === r.value ? "border-adire bg-adire-wash" : "border-line")}
          >
            <input type="radio" name="reason" checked={reason === r.value} onChange={() => setReason(r.value)} className="accent-[#33309a]" />
            {r.label}
          </label>
        ))}
      </fieldset>
      <Textarea label="Details (optional)" rows={3} value={details} onChange={(e) => setDetails(e.target.value)} error={error?.field("details")} />
      <ErrorBox error={error} />
      <Button type="submit" variant="danger" className="w-full" loading={busy} disabled={!reason}>
        Send report
      </Button>
    </form>
  );
}
