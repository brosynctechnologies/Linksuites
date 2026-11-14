"use client";

import Link from "next/link";
import { useState } from "react";
import { ConfirmDialog } from "@/components/confirm";
import { RequireArea } from "@/components/console";
import { FilterBar } from "@/components/filters";
import { useToast } from "@/components/toast";
import { Button, EmptyState, ErrorPanel, Loading, Mono, PageHeader, Panel, ProductChip, StatusBadge, TableScroll, Tabs } from "@/components/ui";
import { api } from "@/lib/api";
import { money, possessive, stamp } from "@/lib/format";
import type { TransferClaimRow, TransferStatus } from "@/lib/types";
import { qs, useApi } from "@/lib/use-api";
import { useUrlFilters } from "@/lib/use-filters";

const KEYS = ["status"] as const;
const STATUSES: TransferStatus[] = ["Pending", "Succeeded", "Failed", "Abandoned"];
const STATUS_TAB: Record<TransferStatus, string> = { Pending: "Waiting", Succeeded: "Confirmed", Failed: "Rejected", Abandoned: "Withdrawn" };

export default function TransfersPage() {
  return (
    <RequireArea area="money">
      <Transfers />
    </RequireArea>
  );
}

function Transfers() {
  const toast = useToast();
  const f = useUrlFilters(KEYS);
  const status = (STATUSES.includes(f.values.status as TransferStatus) ? f.values.status : "Pending") as TransferStatus;
  const { data, error, reload } = useApi<TransferClaimRow[]>(`/api/platform/transfers${qs({ status })}`);
  const [confirming, setConfirming] = useState<TransferClaimRow | null>(null);
  const [rejecting, setRejecting] = useState<TransferClaimRow | null>(null);
  // Money that arrived for a claim the business withdrew, or that was rejected: confirmed only on purpose, with a note.
  const [confirmingAnyway, setConfirmingAnyway] = useState<TransferClaimRow | null>(null);
  const actions = status !== "Succeeded";
  const list = data ?? [];

  /** A claim that changed elsewhere (withdrawn, or decided by another admin) reports its state instead. */
  function outcome(row: TransferClaimRow, wanted: TransferStatus, done: string) {
    if (row.status === wanted) toast(done);
    else toast(`Nothing changed: this claim is already ${STATUS_TAB[row.status].toLowerCase()}.`);
    reload();
  }

  return (
    <>
      <PageHeader
        title="Bank transfers"
        subtitle="Plan payments businesses sent by bank transfer while online payment is off. Find the transfer in the company account (amount and reference), then confirm it to start the plan, or reject it."
      />
      <FilterBar>
        <Tabs<TransferStatus>
          label="Transfer status"
          value={status}
          onChange={(v) => f.set({ status: v === "Pending" ? "" : v })}
          items={STATUSES.map((s) => ({ value: s, label: STATUS_TAB[s] }))}
        />
      </FilterBar>
      <Panel flush>
        {error && <ErrorPanel error={error} onRetry={reload} className="m-3" />}
        {!data && !error && <Loading />}
        {data && list.length === 0 && (
          <EmptyState
            title={status === "Pending" ? "No transfers waiting" : `No ${STATUS_TAB[status].toLowerCase()} transfers`}
            text={status === "Pending" ? "When a business says it has sent a transfer, it appears here and finance admins get an email." : undefined}
          />
        )}
        {data && list.length > 0 && (
          <TableScroll label="Bank transfers">
            <table className="data-table min-w-[1100px]">
              <thead>
                <tr>
                  <th>Claimed</th>
                  <th>Business</th>
                  <th>Plan</th>
                  <th>Reference</th>
                  <th>Claimed by</th>
                  <th>Status</th>
                  <th className="num">Amount</th>
                  {actions && (
                    <th>
                      <span className="sr-only">Actions</span>
                    </th>
                  )}
                </tr>
              </thead>
              <tbody>
                {list.map((t) => (
                  <tr key={t.id}>
                    <td className="tnum">{stamp(t.createdAt)}</td>
                    <td className="max-w-56">
                      <Link href={`/businesses/${t.businessId}`} className="block font-medium underline decoration-line underline-offset-2 hover:decoration-graphite">
                        {t.businessName || "(unnamed)"}
                      </Link>
                      {t.product && <ProductChip product={t.product} />}
                    </td>
                    <td>{t.planName || t.planCode}</td>
                    <td>
                      <Mono>{t.reference}</Mono>
                    </td>
                    <td>
                      <span className="block">{t.claimedByName ?? "—"}</span>
                      {t.claimedByEmail && <span className="text-xs text-muted">{t.claimedByEmail}</span>}
                    </td>
                    <td className="max-w-64">
                      <StatusBadge status={t.status}>{STATUS_TAB[t.status]}</StatusBadge>
                      {t.decidedAt && <span className="tnum block text-xs text-muted">{stamp(t.decidedAt)}</span>}
                      {t.rejectReason && <span className="block text-xs text-muted">{t.rejectReason}</span>}
                    </td>
                    <td className="num font-medium">{money(t.amountMinor, t.currency)}</td>
                    {actions && (
                      <td className="whitespace-nowrap">
                        {t.status === "Pending" ? (
                          <div className="flex gap-2">
                            <Button onClick={() => setConfirming(t)}>Confirm</Button>
                            <Button variant="secondary" onClick={() => setRejecting(t)}>
                              Reject
                            </Button>
                          </div>
                        ) : (
                          t.status !== "Succeeded" && (
                            <Button variant="secondary" onClick={() => setConfirmingAnyway(t)}>
                              Confirm anyway
                            </Button>
                          )
                        )}
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </TableScroll>
        )}
      </Panel>

      <ConfirmDialog
        open={!!confirming}
        onClose={() => setConfirming(null)}
        title="Confirm bank transfer"
        effect={
          confirming && (
            <>
              Starts {possessive(confirming.businessName || "the business")} {confirming.planName} plan as a paid period and records{" "}
              {money(confirming.amountMinor, confirming.currency)} as received. Confirm only after you&apos;ve seen this amount with reference{" "}
              <Mono>{confirming.reference}</Mono> in the company account. They get an email.
            </>
          )
        }
        confirmLabel="Confirm transfer"
        onConfirm={async () => {
          if (!confirming) return;
          const row = await api<TransferClaimRow>(`/api/platform/transfers/${confirming.id}/confirm`, { method: "POST" });
          setConfirming(null);
          outcome(row, "Succeeded", "Transfer confirmed, plan started");
        }}
      />

      <ConfirmDialog
        open={!!confirmingAnyway}
        onClose={() => setConfirmingAnyway(null)}
        title="Confirm a closed transfer claim"
        effect={
          confirmingAnyway && (
            <>
              This claim was {confirmingAnyway.status === "Failed" ? "rejected" : "withdrawn by the business"}. Confirming it anyway starts{" "}
              {possessive(confirmingAnyway.businessName || "the business")} {confirmingAnyway.planName} plan as a paid period and records{" "}
              {money(confirmingAnyway.amountMinor, confirmingAnyway.currency)} as received. Do this only when that amount with reference{" "}
              <Mono>{confirmingAnyway.reference}</Mono> is in the company account and hasn&apos;t been booked against another claim. They get an email.
            </>
          )
        }
        confirmLabel="Confirm anyway"
        danger
        reason
        reasonLabel="Note"
        reasonHint="What you found and where. Saved in the audit log with your name."
        fields={["note"]}
        onConfirm={async (note) => {
          if (!confirmingAnyway) return;
          const row = await api<TransferClaimRow>(`/api/platform/transfers/${confirmingAnyway.id}/confirm`, {
            method: "POST",
            body: { anyway: true, note: note.trim() },
          });
          setConfirmingAnyway(null);
          outcome(row, "Succeeded", "Transfer confirmed, plan started");
        }}
      />

      <ConfirmDialog
        open={!!rejecting}
        onClose={() => setRejecting(null)}
        title="Reject bank transfer"
        effect={
          rejecting && (
            <>
              Marks {possessive(rejecting.businessName || "the business")} transfer of {money(rejecting.amountMinor, rejecting.currency)} (reference{" "}
              <Mono>{rejecting.reference}</Mono>) as not received. Their plan doesn&apos;t change. They get an email with your reason.
            </>
          )
        }
        confirmLabel="Reject transfer"
        danger
        reason
        reasonHint="Sent to the business and saved in the audit log with your name."
        onConfirm={async (reason) => {
          if (!rejecting) return;
          const row = await api<TransferClaimRow>(`/api/platform/transfers/${rejecting.id}/reject`, { method: "POST", body: { reason: reason.trim() } });
          setRejecting(null);
          outcome(row, "Failed", "Transfer rejected");
        }}
      />
    </>
  );
}
