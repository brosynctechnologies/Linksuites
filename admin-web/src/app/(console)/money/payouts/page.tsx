"use client";

import { useState } from "react";
import { ConfirmDialog } from "@/components/confirm";
import { RequireArea, useQueues } from "@/components/console";
import { FilterBar } from "@/components/filters";
import { useToast } from "@/components/toast";
import { Button, EmptyState, ErrorPanel, Input, Loading, Mono, PageHeader, Panel, ProductChip, StatusBadge, TableScroll, Tabs } from "@/components/ui";
import { api } from "@/lib/api";
import { money, possessive, stamp } from "@/lib/format";
import type { PayoutRow, PayoutSettings, PayoutStatus } from "@/lib/types";
import { qs, useApi } from "@/lib/use-api";
import { useUrlFilters } from "@/lib/use-filters";

const KEYS = ["status"] as const;
const STATUSES: PayoutStatus[] = ["Requested", "Processing", "Paid", "Failed", "Rejected"];
/** Tabs whose rows Finance can act on. */
const ACTIONABLE: PayoutStatus[] = ["Requested", "Failed"];

export default function PayoutsPage() {
  return (
    <RequireArea area="money">
      <Payouts />
    </RequireArea>
  );
}

/** Paystack's transfer status in words. */
function transferLabel(status: string | null | undefined) {
  switch (status) {
    case "otp":
      return "Waiting for OTP";
    case "pending":
    case "received":
      return "Paystack is sending";
    case "success":
      return "Sent by Paystack";
    case "reversed":
      return "Reversed";
    case "failed":
      return "Transfer failed";
    default:
      return status ?? null;
  }
}

function Payouts() {
  const toast = useToast();
  const { refresh } = useQueues();
  const f = useUrlFilters(KEYS);
  const status = (STATUSES.includes(f.values.status as PayoutStatus) ? f.values.status : "Requested") as PayoutStatus;
  const { data, error, reload, setData } = useApi<PayoutRow[]>(`/api/admin/payouts${qs({ status })}`);
  const settings = useApi<PayoutSettings>("/api/admin/payouts/settings");
  const automatic = settings.data?.automaticTransfers ?? false;
  const [paying, setPaying] = useState<PayoutRow | null>(null);
  const [rejecting, setRejecting] = useState<PayoutRow | null>(null);
  const [sending, setSending] = useState<PayoutRow | null>(null);
  const [reference, setReference] = useState("");
  const [note, setNote] = useState("");
  const list = data ?? [];
  const total = list.reduce((s, p) => s + p.amountMinor, 0);
  const actions = ACTIONABLE.includes(status);

  const remove = (id: string) => setData((xs) => (xs ?? []).filter((x) => x.id !== id));

  return (
    <>
      <PageHeader
        title="Payouts"
        subtitle={
          automatic
            ? "Course and short-let earnings businesses asked to withdraw. Approve a payout to send it by Paystack Transfer; Paystack confirms it here."
            : "Course and short-let earnings businesses asked to withdraw. Pay them from the bank, then record the transfer reference here."
        }
      />
      {settings.data && (
        <p className="mb-3 text-sm text-muted">
          {automatic
            ? "Automatic transfers are on: “Send” pays from the platform's Paystack balance. Failed or reversed transfers come back here under Failed, and Finance is emailed."
            : "Automatic transfers are off, so payouts are sent by hand."}
          {settings.data.directSettlement && " Sellers with direct settlement are paid by Paystack at checkout and don't request payouts for those sales."}
        </p>
      )}
      <FilterBar>
        <Tabs<PayoutStatus>
          label="Payout status"
          value={status}
          onChange={(v) => f.set({ status: v === "Requested" ? "" : v })}
          items={STATUSES.map((s) => ({ value: s, label: s }))}
        />
      </FilterBar>
      <Panel flush>
        {error && <ErrorPanel error={error} onRetry={reload} className="m-3" />}
        {!data && !error && <Loading />}
        {data && list.length === 0 && (
          <EmptyState
            title={status === "Requested" ? "No payouts waiting" : `No ${status.toLowerCase()} payouts`}
            text={status === "Requested" ? "Requests from tutors and hosts appear here." : undefined}
          />
        )}
        {data && list.length > 0 && (
          <TableScroll label="Payouts">
            <table className="data-table min-w-[1000px]">
              <thead>
                <tr>
                  <th>Requested</th>
                  <th>Business</th>
                  <th>Bank account</th>
                  <th>Status</th>
                  <th>{status === "Rejected" ? "Note" : "Reference"}</th>
                  <th className="num">Amount</th>
                  {actions && (
                    <th>
                      <span className="sr-only">Actions</span>
                    </th>
                  )}
                </tr>
              </thead>
              <tbody>
                {list.map((p) => (
                  <tr key={p.id}>
                    <td className="tnum">{stamp(p.requestedAt)}</td>
                    <td>
                      <span className="block font-medium">{p.businessName || p.academyName || "—"}</span>
                      <ProductChip product={p.product} />
                    </td>
                    <td>
                      <span className="block">{p.accountName ?? "—"}</span>
                      <span className="text-xs text-muted">
                        {p.bankName ?? "—"} <Mono>{p.accountNumber ?? ""}</Mono>
                      </span>
                      {automatic && !p.accountVerified && <span className="block text-xs font-medium text-stop">Not checked with the bank</span>}
                    </td>
                    <td>
                      <StatusBadge status={p.status} />
                      {transferLabel(p.transferStatus) && <span className="block text-xs text-muted">{transferLabel(p.transferStatus)}</span>}
                      {p.paidAt && <span className="tnum block text-xs text-muted">{stamp(p.paidAt)}</span>}
                      {p.failureReason && <span className="block max-w-64 text-xs text-stop">{p.failureReason}</span>}
                    </td>
                    <td className="max-w-64">
                      {status === "Rejected" ? (
                        p.note || "—"
                      ) : p.reference || p.transferReference ? (
                        <Mono>{p.reference ?? p.transferReference}</Mono>
                      ) : (
                        "—"
                      )}
                    </td>
                    <td className="num font-medium">{money(p.amountMinor, p.currency)}</td>
                    {actions && (
                      <td className="whitespace-nowrap">
                        <div className="flex gap-2">
                          {automatic && (
                            <Button onClick={() => setSending(p)} disabled={!p.accountVerified}>
                              {p.status === "Failed" ? "Retry" : "Send"}
                            </Button>
                          )}
                          {!automatic && p.status === "Requested" && (
                            <Button
                              onClick={() => {
                                setReference("");
                                setPaying(p);
                              }}
                            >
                              Mark paid
                            </Button>
                          )}
                          <Button
                            variant="secondary"
                            onClick={() => {
                              setNote("");
                              setRejecting(p);
                            }}
                          >
                            Reject
                          </Button>
                        </div>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr>
                  <td colSpan={5} className="border-t border-line text-right text-sm text-muted">
                    Total of {list.length} {list.length === 1 ? "payout" : "payouts"}
                  </td>
                  <td className="num border-t border-line font-semibold">{money(total, list[0]?.currency)}</td>
                  {actions && <td className="border-t border-line" />}
                </tr>
              </tfoot>
            </table>
          </TableScroll>
        )}
      </Panel>

      <ConfirmDialog
        open={!!sending}
        onClose={() => setSending(null)}
        title={sending?.status === "Failed" ? "Retry payout transfer" : "Send payout"}
        effect={
          sending && (
            <>
              Sends {money(sending.amountMinor, sending.currency)} from the platform&apos;s Paystack balance to {sending.accountName ?? "the account"} ({sending.bankName}{" "}
              {sending.accountNumber}) for {sending.businessName || sending.academyName}. Paystack confirms it within minutes. It can&apos;t be called back from here.
            </>
          )
        }
        confirmLabel={sending?.status === "Failed" ? "Retry transfer" : "Send payout"}
        onConfirm={async () => {
          if (!sending) return;
          const updated = await api<PayoutRow>(`/api/admin/payouts/${sending.id}/send`, { method: "POST" });
          remove(sending.id);
          setSending(null);
          refresh();
          toast(updated.status === "Failed" ? `Paystack refused the transfer: ${updated.failureReason ?? "see the Failed tab"}` : "Payout sent to Paystack");
        }}
      />

      <ConfirmDialog
        open={!!paying}
        onClose={() => setPaying(null)}
        title="Mark payout paid"
        effect={
          paying && (
            <>
              Records {money(paying.amountMinor, paying.currency)} to {paying.accountName ?? "the account"} ({paying.bankName} {paying.accountNumber}) as paid to{" "}
              {paying.businessName || paying.academyName}. They see the reference.
            </>
          )
        }
        confirmLabel="Mark paid"
        fields={["reference"]}
        canConfirm={!!reference.trim()}
        onConfirm={async () => {
          if (!paying) return;
          await api(`/api/admin/payouts/${paying.id}/paid`, { method: "POST", body: { reference: reference.trim() } });
          remove(paying.id);
          setPaying(null);
          refresh();
          toast("Payout marked paid");
        }}
      >
        {(err) => (
          <Input
            label="Bank transfer reference"
            value={reference}
            onChange={(e) => setReference(e.target.value)}
            error={err?.field("reference")}
            className="font-mono"
            hint="From the bank's confirmation. Make the transfer before marking it paid."
            autoFocus
          />
        )}
      </ConfirmDialog>

      <ConfirmDialog
        open={!!rejecting}
        onClose={() => setRejecting(null)}
        title="Reject payout"
        effect={
          rejecting && (
            <>
              Rejects {possessive(rejecting.businessName || rejecting.academyName || "the business")} request for {money(rejecting.amountMinor, rejecting.currency)}. The money returns to their available
              balance.
            </>
          )
        }
        confirmLabel="Reject payout"
        danger
        fields={["note"]}
        canConfirm={!!note.trim()}
        onConfirm={async () => {
          if (!rejecting) return;
          await api(`/api/admin/payouts/${rejecting.id}/reject`, { method: "POST", body: { note: note.trim() } });
          remove(rejecting.id);
          setRejecting(null);
          refresh();
          toast("Payout rejected");
        }}
      >
        {(err) => (
          <div className="space-y-1">
            <label htmlFor="payout-note" className="block text-sm font-medium">
              Note to the business
            </label>
            <textarea
              id="payout-note"
              rows={3}
              value={note}
              onChange={(e) => setNote(e.target.value)}
              className="block w-full rounded-control border border-line bg-surface px-3 py-2 text-base"
              aria-invalid={!!err?.field("note") || undefined}
              aria-describedby="payout-note-hint"
              autoFocus
            />
            <p id="payout-note-hint" className={err?.field("note") ? "text-xs font-medium text-stop" : "text-xs text-muted"}>
              {err?.field("note") ?? "Say why, e.g. the account name doesn't match the business."}
            </p>
          </div>
        )}
      </ConfirmDialog>
    </>
  );
}
