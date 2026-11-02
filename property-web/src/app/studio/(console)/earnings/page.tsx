"use client";

import { useState, type FormEvent } from "react";
import { api, asList, toApiError, type ApiError } from "@/lib/api";
import { MIN_PAYOUT_MINOR, STAY_COMMISSION_PCT } from "@/lib/config";
import { formatDay } from "@/lib/format";
import { formatMoney, groupDigits, toMajorInput, toMinor } from "@/lib/money";
import { useCan } from "@/lib/permissions";
import { EARNING_STATUS, PAYOUT_STATUS, statusOf } from "@/lib/status";
import { useApi } from "@/lib/use-api";
import type { EarningsDto, PayoutAccount, PayoutDto } from "@/lib/types";
import {
  ApiErrorView,
  Button,
  Card,
  DataList,
  ErrorBox,
  Input,
  Modal,
  Notice,
  PageHeader,
  PageLoader,
  SectionTitle,
  StatusBadge,
  SummaryStrip,
  dataRowClass,
} from "@/components/ui";

export default function EarningsPage() {
  const canPayout = useCan("payouts.manage");
  const earnings = useApi<EarningsDto>("/api/property/earnings");
  const payouts = useApi<PayoutDto[]>("/api/property/payouts");
  const account = useApi<PayoutAccount | undefined>("/api/property/payout-account");
  const [requestOpen, setRequestOpen] = useState(false);
  const [accountOpen, setAccountOpen] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);

  const e = earnings.data;
  const currency = e?.currency || "NGN";
  const acct = account.data && typeof account.data === "object" && account.data.accountNumber ? account.data : null;
  const payoutList = asList(payouts.data);
  const entries = e?.entries ?? [];

  return (
    <div className="space-y-5">
      <PageHeader
        title="Earnings"
        subtitle={`Short-let payments, less the ${STAY_COMMISSION_PCT}% commission. Each stay's earnings become available from the day after check-in (midnight, Lagos time, at the end of the check-in day).`}
        action={
          canPayout && (
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
            { label: "Pending", value: formatMoney(e.pendingMinor, currency), sub: "Until the day after check-in" },
            { label: "Paid out", value: formatMoney(e.paidOutMinor, currency), sub: "All time" },
          ]}
        />
      )}
      {notice && <Notice tone="success">{notice}</Notice>}

      <Card>
        <SectionTitle
          action={
            canPayout && (
              <Button variant="secondary" onClick={() => setAccountOpen(true)}>
                {acct ? "Change" : "Add account"}
              </Button>
            )
          }
        >
          Payout account
        </SectionTitle>
        {account.error ? (
          <ErrorBox error={account.error} onRetry={account.reload} />
        ) : account.loading ? (
          <PageLoader />
        ) : acct ? (
          <dl className="space-y-1 text-sm">
            <div className="flex justify-between gap-3">
              <dt className="text-muted">Bank</dt>
              <dd className="font-medium text-asphalt">{acct.bankName}</dd>
            </div>
            <div className="flex justify-between gap-3">
              <dt className="text-muted">Account number</dt>
              <dd className="tnum font-medium text-asphalt">{acct.accountNumber}</dd>
            </div>
            <div className="flex justify-between gap-3">
              <dt className="text-muted">Account name</dt>
              <dd className="font-medium text-asphalt">{acct.accountName}</dd>
            </div>
          </dl>
        ) : (
          <p className="text-sm text-slate-700">
            No bank account yet. {canPayout ? "Add one before you request a payout." : "The agency owner adds it."}
          </p>
        )}
      </Card>

      <section aria-labelledby="payouts-h">
        <h2 id="payouts-h" className="mb-3 font-semibold text-asphalt">
          Payouts
        </h2>
        {!payouts.data && payouts.error && <ErrorBox error={payouts.error} onRetry={payouts.reload} />}
        {payouts.data && payoutList.length === 0 && <p className="text-sm text-muted">No payouts yet.</p>}
        {payoutList.length > 0 && (
          <DataList template="8rem minmax(0,1fr) minmax(0,1fr) 8rem" head={[{ label: "Requested" }, { label: "To" }, { label: "Reference" }, { label: "Amount", align: "right" }]}>
            {payoutList.map((p) => {
              const st = statusOf(PAYOUT_STATUS, p.status);
              return (
                <li key={p.id} className={dataRowClass}>
                  <div>
                    <p className="text-sm text-asphalt">{formatDay(p.requestedAt)}</p>
                    <StatusBadge tone={st.tone}>{st.label}</StatusBadge>
                  </div>
                  <p className="truncate text-right text-sm text-slate-700 md:text-left">
                    {p.bankName}, {p.accountNumber}
                  </p>
                  <p className="truncate text-sm text-muted">{p.status === "Rejected" ? (p.note ?? "Rejected") : (p.reference ?? (p.paidAt ? `Paid ${formatDay(p.paidAt)}` : "—"))}</p>
                  <p className="price text-right text-asphalt">{formatMoney(p.amountMinor, currency)}</p>
                </li>
              );
            })}
          </DataList>
        )}
      </section>

      <section aria-labelledby="ledger-h">
        <h2 id="ledger-h" className="mb-3 font-semibold text-asphalt">
          Stays
        </h2>
        {e && entries.length === 0 && <p className="text-sm text-muted">No short-let earnings yet.</p>}
        {entries.length > 0 && (
          <DataList
            template="minmax(0,1.5fr) 7rem 7rem 7rem 8rem"
            head={[{ label: "Stay" }, { label: "Paid", align: "right" }, { label: "Commission", align: "right" }, { label: "You earn", align: "right" }, { label: "Status", align: "right" }]}
          >
            {entries.map((x) => {
              const st = statusOf(EARNING_STATUS, x.status);
              return (
                <li key={x.id} className={dataRowClass}>
                  <div className="min-w-0">
                    <p className="truncate font-medium text-asphalt">{x.description}</p>
                    <p className="text-xs text-muted">{formatDay(x.createdAt)}</p>
                  </div>
                  <p className="tnum hidden text-right text-sm text-slate-700 md:block">{formatMoney(x.grossMinor, currency)}</p>
                  <p className="tnum hidden text-right text-sm text-muted md:block">−{formatMoney(x.feeMinor, currency)}</p>
                  <p className="price text-right text-asphalt">{formatMoney(x.netMinor, currency)}</p>
                  <div className="col-span-2 flex items-center justify-between gap-2 md:col-span-1 md:flex-col md:items-end md:gap-0.5">
                    <StatusBadge tone={st.tone}>{st.label}</StatusBadge>
                    {x.status === "Pending" && <span className="text-xs text-muted">Available {formatDay(x.availableAt)}</span>}
                  </div>
                </li>
              );
            })}
          </DataList>
        )}
      </section>

      <Modal open={requestOpen} title="Request a payout" onClose={() => setRequestOpen(false)}>
        {requestOpen && e && (
          <PayoutForm
            available={e.availableMinor}
            currency={currency}
            account={acct}
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
            account={acct}
            onSaved={(a) => {
              account.setData(a);
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
  const [amount, setAmount] = useState(groupDigits(toMajorInput(available)));
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
      onDone(await api<PayoutDto>("/api/property/payouts", { method: "POST", body: { amountMinor: minor } }));
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-4">
      <Input
        label="Amount (₦)"
        inputMode="numeric"
        value={amount}
        onChange={(ev) => setAmount(groupDigits(ev.target.value))}
        hint={`Available: ${formatMoney(available, currency)}. Minimum ${formatMoney(MIN_PAYOUT_MINOR, currency)}.`}
        error={error?.field("amountMinor")}
      />
      <p className="rounded-control bg-plaster px-3 py-2 text-sm text-slate-700">
        To {account.accountName}, {account.bankName} {account.accountNumber}
      </p>
      {error && !error.field("amountMinor") && <ErrorBox error={error} />}
      <Button type="submit" className="w-full" loading={busy}>
        Request payout
      </Button>
    </form>
  );
}

function AccountForm({ account, onSaved }: { account: PayoutAccount | null; onSaved: (a: PayoutAccount) => void }) {
  const [form, setForm] = useState<PayoutAccount>(account ?? { bankName: "", accountNumber: "", accountName: "" });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const set = (k: keyof PayoutAccount) => (ev: { target: { value: string } }) => setForm((f) => ({ ...f, [k]: ev.target.value }));

  async function submit(ev: FormEvent) {
    ev.preventDefault();
    setBusy(true);
    setError(null);
    const body = { bankName: form.bankName.trim(), accountNumber: form.accountNumber.replace(/\s/g, ""), accountName: form.accountName.trim() };
    try {
      const res = await api<PayoutAccount | undefined>("/api/property/payout-account", { method: "PUT", body });
      onSaved(res && typeof res === "object" && res.accountNumber ? res : body);
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-4">
      <Input label="Bank" required value={form.bankName} onChange={set("bankName")} placeholder="e.g. GTBank" error={error?.field("bankName")} />
      <Input label="Account number" required inputMode="numeric" value={form.accountNumber} onChange={set("accountNumber")} placeholder="10-digit NUBAN" error={error?.field("accountNumber")} />
      <Input label="Account name" required value={form.accountName} onChange={set("accountName")} hint="As your bank shows it." error={error?.field("accountName")} />
      {error && !error.errors && <ErrorBox error={error} />}
      <Button type="submit" className="w-full" loading={busy}>
        Save account
      </Button>
    </form>
  );
}
