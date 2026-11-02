"use client";

import { useState, type FormEvent } from "react";
import { api, asList, toApiError, type ApiError } from "@/lib/api";
import { MARKETPLACE_FEE_PCT, MIN_PAYOUT_MINOR, REFUND_WINDOW_DAYS } from "@/lib/config";
import { formatDay } from "@/lib/format";
import { formatMoney, toMajorInput, toMinor } from "@/lib/money";
import { useCan } from "@/lib/permissions";
import { EARNING_STATUS, PAYOUT_STATUS, statusOf } from "@/lib/status";
import { qs, useApi } from "@/lib/use-api";
import type { AcademyDto, EarningEntry, EarningsDto, PayoutAccount, PayoutDto } from "@/lib/types";
import {
  ApiErrorView,
  Badge,
  Button,
  Card,
  DataList,
  ErrorBox,
  Input,
  Modal,
  NoAccess,
  Notice,
  PageHeader,
  PageLoader,
  Pager,
  SectionTitle,
  StatusBadge,
  SummaryStrip,
  dataRowClass,
} from "@/components/ui";

export default function EarningsPage() {
  const allowed = useCan("earnings.view");
  const canManage = useCan("money.manage");
  const [page, setPage] = useState(1);
  const earnings = useApi<EarningsDto>(allowed ? `/api/studio/earnings${qs({ page })}` : null);
  const payouts = useApi<PayoutDto[]>(allowed ? "/api/studio/payouts" : null);
  const academy = useApi<AcademyDto>(allowed ? "/api/academy" : null);
  const [requestOpen, setRequestOpen] = useState(false);
  const [accountOpen, setAccountOpen] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [actionError, setActionError] = useState<ApiError | null>(null);

  if (!allowed) {
    return (
      <div>
        <PageHeader title="Earnings" back="/studio/more" />
        <NoAccess text="Earnings are visible to the owner, admins and accountants." />
      </div>
    );
  }

  const e = earnings.data;
  const currency = e?.currency || academy.data?.currency || "NGN";
  const account = academy.data?.payoutAccount ?? null;
  const pendingPayout = asList(payouts.data).some((p) => p.status === "Requested");

  async function refund(entry: EarningEntry) {
    const orderId = entry.orderId;
    if (
      !window.confirm(
        `Refund ${entry.studentName} for ${entry.courseTitle}? They lose access to the course, the ${formatMoney(entry.grossMinor, currency)} goes back to them and this earning is removed.`,
      )
    )
      return;
    setBusy(entry.id);
    setActionError(null);
    setNotice(null);
    try {
      await api(`/api/studio/orders/${orderId}/refund`, { method: "POST" });
      setNotice(`Refunded ${entry.studentName}.`);
      earnings.reload();
    } catch (err) {
      setActionError(toApiError(err));
    } finally {
      setBusy(null);
    }
  }

  return (
    <div className="space-y-5">
      <PageHeader
        title="Earnings"
        back="/studio/more"
        action={
          canManage && (
            <Button onClick={() => setRequestOpen(true)} disabled={!e || e.availableMinor < MIN_PAYOUT_MINOR}>
              Request payout
            </Button>
          )
        }
      />

      {!e && earnings.error && <ApiErrorView error={earnings.error} onRetry={earnings.reload} />}
      {!e && !earnings.error && <PageLoader />}
      {e && (
        <SummaryStrip
          cols={3}
          label="Balances"
          items={[
            { label: "Available", value: formatMoney(e.availableMinor, currency), sub: `Payouts from ${formatMoney(MIN_PAYOUT_MINOR, currency)}`, tone: "green" },
            { label: "Pending", value: formatMoney(e.pendingMinor, currency), sub: `Clears ${REFUND_WINDOW_DAYS} days after each sale` },
            { label: "Paid out", value: formatMoney(e.paidOutMinor, currency), sub: "All time" },
          ]}
        />
      )}

      {notice && <Notice tone="success">{notice}</Notice>}
      {actionError && <ApiErrorView error={actionError} />}

      <div className="grid gap-5 lg:grid-cols-2">
        <Card>
          <SectionTitle
            action={
              canManage && (
                <Button variant="secondary" className="min-h-9 px-3 py-1" onClick={() => setAccountOpen(true)}>
                  {account ? "Change" : "Add account"}
                </Button>
              )
            }
          >
            Payout account
          </SectionTitle>
          {academy.data ? (
            account ? (
              <dl className="space-y-1 text-sm">
                <div className="flex justify-between gap-3">
                  <dt className="text-muted">Bank</dt>
                  <dd className="font-medium text-ink">{account.bankName}</dd>
                </div>
                <div className="flex justify-between gap-3">
                  <dt className="text-muted">Account number</dt>
                  <dd className="tnum font-medium text-ink">{account.accountNumber}</dd>
                </div>
                <div className="flex justify-between gap-3">
                  <dt className="text-muted">Account name</dt>
                  <dd className="font-medium text-ink">{account.accountName}</dd>
                </div>
              </dl>
            ) : canManage ? (
              <p className="text-sm text-slate-700">Add the bank account payouts go to. You need one before you can request a payout.</p>
            ) : (
              // The server only sends the account to the owner and admins.
              <p className="text-sm text-slate-700">Only the owner or an admin can see and change the payout account.</p>
            )
          ) : academy.error ? (
            <ErrorBox error={academy.error} onRetry={academy.reload} />
          ) : (
            <PageLoader />
          )}
        </Card>
        <Card>
          <SectionTitle>How fees work</SectionTitle>
          <ul className="space-y-1.5 text-sm text-slate-700">
            <li>Academy sales: the fee depends on your plan (10% on Free, none on Pro or Business).</li>
            <li>Marketplace sales: a {MARKETPLACE_FEE_PCT}% fee on every plan.</li>
            <li>
              Each sale is pending for {REFUND_WINDOW_DAYS} days, the refund window, then becomes available to pay out.
            </li>
          </ul>
        </Card>
      </div>

      <section aria-labelledby="payouts">
        <h2 id="payouts" className="mb-3 font-semibold text-ink">
          Payouts
        </h2>
        {pendingPayout && <Notice className="mb-3">A payout is waiting to be paid. Its bank reference appears here once it&apos;s sent.</Notice>}
        {!payouts.data && payouts.error && <ErrorBox error={payouts.error} onRetry={payouts.reload} />}
        {payouts.data && asList(payouts.data).length === 0 && <p className="text-sm text-muted">No payouts yet.</p>}
        {asList(payouts.data).length > 0 && (
          <DataList template="8rem minmax(0,1fr) minmax(0,1fr) 7rem" head={[{ label: "Requested" }, { label: "To" }, { label: "Reference" }, { label: "Amount", align: "right" }]}>
            {asList(payouts.data).map((p) => {
              const st = statusOf(PAYOUT_STATUS, p.status);
              return (
                <li key={p.id} className={dataRowClass}>
                  <div>
                    <p className="text-sm text-ink">{formatDay(p.requestedAt)}</p>
                    <StatusBadge tone={st.tone}>{st.label}</StatusBadge>
                  </div>
                  <p className="truncate text-right text-sm text-slate-700 md:text-left">
                    {p.bankName}, {p.accountNumber}
                  </p>
                  <p className="truncate text-sm text-muted">{p.status === "Rejected" ? (p.note ?? "Rejected") : (p.reference ?? (p.paidAt ? `Paid ${formatDay(p.paidAt)}` : "—"))}</p>
                  <p className="tnum text-right font-semibold text-ink">{formatMoney(p.amountMinor, currency)}</p>
                </li>
              );
            })}
          </DataList>
        )}
      </section>

      <section aria-labelledby="ledger">
        <h2 id="ledger" className="mb-3 font-semibold text-ink">
          Sales
        </h2>
        {e && e.entries.items.length === 0 && <p className="text-sm text-muted">No sales yet. Free enrolments don&apos;t appear here.</p>}
        {e && e.entries.items.length > 0 && (
          <>
            <DataList
              busy={earnings.loading}
              template="minmax(0,1.5fr) 6.5rem 6.5rem 6.5rem 8rem"
              head={[{ label: "Sale" }, { label: "Price", align: "right" }, { label: "Fee", align: "right" }, { label: "You earn", align: "right" }, { label: "Status", align: "right" }]}
            >
              {e.entries.items.map((x) => {
                const st = statusOf(EARNING_STATUS, x.status);
                const refundable = canManage && x.refundable;
                return (
                  <li key={x.id} className={dataRowClass}>
                    <div className="min-w-0">
                      <p className="truncate font-medium text-ink">{x.courseTitle}</p>
                      <p className="flex flex-wrap items-center gap-1.5 text-xs text-muted">
                        {x.studentName}, {formatDay(x.createdAt)}
                        <Badge className={x.channel === "Academy" ? "bg-lagoon-wash text-lagoon-text" : "bg-mist-2 text-slate-700"}>{x.channel}</Badge>
                      </p>
                    </div>
                    <p className="tnum hidden text-right text-sm text-slate-700 md:block">{formatMoney(x.grossMinor, currency)}</p>
                    <p className="tnum hidden text-right text-sm text-muted md:block">−{formatMoney(x.feeMinor, currency)}</p>
                    <p className="tnum text-right font-semibold text-ink">{formatMoney(x.netMinor, currency)}</p>
                    <div className="col-span-2 flex items-center justify-between gap-2 md:col-span-1 md:flex-col md:items-end md:gap-0.5">
                      <StatusBadge tone={st.tone}>{st.label}</StatusBadge>
                      {x.status === "Pending" && <span className="text-xs text-muted">Available {formatDay(x.availableAt)}</span>}
                      {refundable && (
                        <button type="button" disabled={busy === x.id} onClick={() => refund(x)} className="text-xs font-semibold text-brick hover:underline disabled:opacity-50">
                          Refund
                        </button>
                      )}
                    </div>
                  </li>
                );
              })}
            </DataList>
            <Pager page={e.entries.page} pageSize={e.entries.pageSize} total={e.entries.total} onPage={setPage} />
          </>
        )}
      </section>

      <Modal open={requestOpen} title="Request a payout" onClose={() => setRequestOpen(false)}>
        {requestOpen && e && (
          <PayoutForm
            available={e.availableMinor}
            currency={currency}
            account={account}
            onAddAccount={() => {
              setRequestOpen(false);
              setAccountOpen(true);
            }}
            onDone={(p) => {
              setRequestOpen(false);
              setNotice(`Payout of ${formatMoney(p.amountMinor, currency)} requested.`);
              payouts.reload();
              earnings.reload();
            }}
          />
        )}
      </Modal>
      <Modal open={accountOpen} title="Payout account" onClose={() => setAccountOpen(false)}>
        {accountOpen && (
          <AccountForm
            account={account}
            onSaved={(a) => {
              academy.setData(a);
              setAccountOpen(false);
              setNotice("Payout account saved.");
            }}
          />
        )}
      </Modal>
    </div>
  );
}

function PayoutForm({
  available,
  currency,
  account,
  onAddAccount,
  onDone,
}: {
  available: number;
  currency: string;
  account: PayoutAccount | null;
  onAddAccount: () => void;
  onDone: (p: PayoutDto) => void;
}) {
  const [amount, setAmount] = useState(toMajorInput(available));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  if (!account) {
    return (
      <div className="space-y-4">
        <p className="text-sm text-slate-700">Add the bank account to pay into first.</p>
        <Button className="w-full" onClick={onAddAccount}>
          Add payout account
        </Button>
      </div>
    );
  }

  async function submit(ev: FormEvent) {
    ev.preventDefault();
    const minor = toMinor(amount) ?? 0;
    if (minor < MIN_PAYOUT_MINOR) return setError(toApiError(new Error(`The smallest payout is ${formatMoney(MIN_PAYOUT_MINOR, currency)}.`)));
    if (minor > available) return setError(toApiError(new Error(`You can pay out up to ${formatMoney(available, currency)}.`)));
    setBusy(true);
    setError(null);
    try {
      onDone(await api<PayoutDto>("/api/studio/payouts", { method: "POST", body: { amountMinor: minor } }));
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-4">
      <Input
        label="Amount (₦)"
        inputMode="decimal"
        value={amount}
        onChange={(ev) => setAmount(ev.target.value)}
        hint={`Available: ${formatMoney(available, currency)}. Minimum ${formatMoney(MIN_PAYOUT_MINOR, currency)}.`}
      />
      <p className="rounded-control bg-mist px-3 py-2 text-sm text-slate-700">
        To {account.accountName}, {account.bankName} {account.accountNumber}
      </p>
      {error && <ErrorBox error={error} />}
      <Button type="submit" className="w-full" loading={busy}>
        Request payout
      </Button>
    </form>
  );
}

function AccountForm({ account, onSaved }: { account: PayoutAccount | null; onSaved: (a: AcademyDto) => void }) {
  const [form, setForm] = useState<PayoutAccount>(account ?? { bankName: "", accountNumber: "", accountName: "" });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const set = (k: keyof PayoutAccount) => (ev: { target: { value: string } }) => setForm((f) => ({ ...f, [k]: ev.target.value }));

  async function submit(ev: FormEvent) {
    ev.preventDefault();
    setBusy(true);
    setError(null);
    try {
      onSaved(
        await api<AcademyDto>("/api/academy/payout-account", {
          method: "PUT",
          body: { bankName: form.bankName.trim(), accountNumber: form.accountNumber.replace(/\s/g, ""), accountName: form.accountName.trim() },
        }),
      );
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-4">
      <Input label="Bank" required value={form.bankName} onChange={set("bankName")} placeholder="e.g. GTBank" error={error?.field("bankName")} />
      <Input
        label="Account number"
        required
        inputMode="numeric"
        value={form.accountNumber}
        onChange={set("accountNumber")}
        placeholder="10-digit NUBAN"
        error={error?.field("accountNumber")}
      />
      <Input label="Account name" required value={form.accountName} onChange={set("accountName")} hint="As your bank shows it." error={error?.field("accountName")} />
      {error && <ErrorBox error={error} />}
      <Button type="submit" className="w-full" loading={busy}>
        Save account
      </Button>
    </form>
  );
}
