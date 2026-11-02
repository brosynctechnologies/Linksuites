"use client";

import { useState } from "react";
import { ConfirmDialog } from "@/components/confirm";
import { RequireArea, useQueues } from "@/components/console";
import { FilterBar } from "@/components/filters";
import { useToast } from "@/components/toast";
import { Button, EmptyState, ErrorPanel, Input, Loading, Mono, PageHeader, Panel, ProductChip, StatusBadge, TableScroll, Tabs } from "@/components/ui";
import { api } from "@/lib/api";
import { money, possessive, stamp } from "@/lib/format";
import type { PayoutRow, PayoutStatus } from "@/lib/types";
import { qs, useApi } from "@/lib/use-api";
import { useUrlFilters } from "@/lib/use-filters";

const KEYS = ["status"] as const;
const STATUSES: PayoutStatus[] = ["Requested", "Paid", "Rejected"];

export default function PayoutsPage() {
  return (
    <RequireArea area="money">
      <Payouts />
    </RequireArea>
  );
}

function Payouts() {
  const toast = useToast();
  const { refresh } = useQueues();
  const f = useUrlFilters(KEYS);
  const status = (STATUSES.includes(f.values.status as PayoutStatus) ? f.values.status : "Requested") as PayoutStatus;
  const { data, error, reload, setData } = useApi<PayoutRow[]>(`/api/admin/payouts${qs({ status })}`);
  const [paying, setPaying] = useState<PayoutRow | null>(null);
  const [rejecting, setRejecting] = useState<PayoutRow | null>(null);
  const [reference, setReference] = useState("");
  const [note, setNote] = useState("");
  const list = data ?? [];
  const total = list.reduce((s, p) => s + p.amountMinor, 0);

  const remove = (id: string) => setData((xs) => (xs ?? []).filter((x) => x.id !== id));

  return (
    <>
      <PageHeader title="Payouts" subtitle="Course and short-let earnings businesses asked to withdraw. Pay them from the bank, then record the transfer reference here." />
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
                  {status === "Requested" && (
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
                    </td>
                    <td>
                      <StatusBadge status={p.status} />
                      {p.paidAt && <span className="tnum block text-xs text-muted">{stamp(p.paidAt)}</span>}
                    </td>
                    <td className="max-w-64">{status === "Rejected" ? p.note || "—" : p.reference ? <Mono>{p.reference}</Mono> : "—"}</td>
                    <td className="num font-medium">{money(p.amountMinor, p.currency)}</td>
                    {status === "Requested" && (
                      <td className="whitespace-nowrap">
                        <div className="flex gap-2">
                          <Button
                            onClick={() => {
                              setReference("");
                              setPaying(p);
                            }}
                          >
                            Mark paid
                          </Button>
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
                  {status === "Requested" && <td className="border-t border-line" />}
                </tr>
              </tfoot>
            </table>
          </TableScroll>
        )}
      </Panel>

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
