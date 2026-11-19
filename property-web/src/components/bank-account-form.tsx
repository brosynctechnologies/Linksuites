"use client";

import { useRef, useState, type FormEvent } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { useApi } from "@/lib/use-api";
import type { BankOption, PayoutAccount, PayoutSettings } from "@/lib/types";
import { Button, ErrorBox, Input, Notice, Select } from "@/components/ui";

/** What the payout-account endpoints take. With a bank code, the API checks the account name with the bank. */
export interface PayoutAccountBody {
  bankName: string;
  bankCode?: string;
  accountNumber: string;
  accountName: string;
}

/** How payouts work on the platform right now; null while loading. */
export function usePayoutSettings(enabled = true) {
  return useApi<PayoutSettings>(enabled ? "/api/payouts/settings" : null);
}

/** One line about how payouts reach the seller. */
export function PayoutModeNote({ settings, account }: { settings: PayoutSettings | undefined; account: PayoutAccount | null }) {
  if (!settings) return null;
  if (account?.directSettlement && settings.directSettlement)
    return <Notice>Direct settlement is on: Paystack pays each sale&apos;s share straight to your bank, so those sales don&apos;t need a payout request.</Notice>;
  if (settings.automaticTransfers)
    return (
      <Notice>
        Payouts are automatic: once approved, Paystack transfers the money to your checked bank account, usually the same day.
        {account && !account.verified && " Pick your bank from the list again so we can check the account name."}
      </Notice>
    );
  return null;
}

/**
 * The bank account form. When the platform can check accounts, the bank is picked from Paystack's list and the account name comes from
 * the bank (it can't be typed); otherwise the three details are typed.
 */
export function BankAccountForm({ account, save }: { account: PayoutAccount | null; save: (body: PayoutAccountBody) => Promise<void> }) {
  const settings = usePayoutSettings();
  const verify = settings.data?.canVerify ?? false;
  const banks = useApi<BankOption[]>(verify ? "/api/payouts/banks" : null);
  const [bankCode, setBankCode] = useState(account?.bankCode ?? "");
  const [bankName, setBankName] = useState(account?.bankName ?? "");
  const [number, setNumber] = useState(account?.accountNumber ?? "");
  const [name, setName] = useState(account?.accountName ?? "");
  const [resolved, setResolved] = useState<string | null>(account?.verified ? account.accountName : null);
  const [checking, setChecking] = useState(false);
  const [lookupError, setLookupError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const latest = useRef(0);

  /** Asks the bank for the name on the account once the bank and a 10-digit number are in. */
  async function lookup(code: string, digits: string) {
    const ticket = ++latest.current;
    setResolved(null);
    setLookupError(null);
    if (!code || !/^[0-9]{10}$/.test(digits)) return;
    setChecking(true);
    try {
      const res = await api<{ accountName: string }>("/api/payouts/resolve-account", { method: "POST", body: { bankCode: code, accountNumber: digits } });
      if (ticket === latest.current) setResolved(res.accountName);
    } catch (err) {
      if (ticket === latest.current) setLookupError(toApiError(err).message);
    } finally {
      if (ticket === latest.current) setChecking(false);
    }
  }

  async function submit(ev: FormEvent) {
    ev.preventDefault();
    setBusy(true);
    setError(null);
    const digits = number.replace(/\s/g, "");
    try {
      await save(
        verify
          ? { bankName: bankName.trim(), bankCode, accountNumber: digits, accountName: resolved ?? name.trim() }
          : { bankName: bankName.trim(), accountNumber: digits, accountName: name.trim() },
      );
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  }

  if (settings.loading && !settings.data) return <p className="text-sm text-muted">Loading…</p>;

  if (verify) {
    return (
      <form onSubmit={submit} className="space-y-4">
        {banks.error && <ErrorBox error={banks.error} onRetry={banks.reload} />}
        <Select
          label="Bank"
          required
          value={bankCode}
          disabled={!banks.data}
          onChange={(ev) => {
            const code = ev.target.value;
            setBankCode(code);
            setBankName(banks.data?.find((b) => b.code === code)?.name ?? "");
            void lookup(code, number.replace(/\s/g, ""));
          }}
          error={error?.field("bankCode")}
        >
          <option value="">{banks.data ? "Choose your bank" : "Loading banks…"}</option>
          {(banks.data ?? []).map((b) => (
            <option key={b.code} value={b.code}>
              {b.name}
            </option>
          ))}
        </Select>
        <Input
          label="Account number"
          required
          inputMode="numeric"
          maxLength={10}
          value={number}
          onChange={(ev) => {
            const digits = ev.target.value.replace(/[^0-9]/g, "");
            setNumber(digits);
            void lookup(bankCode, digits);
          }}
          placeholder="10-digit NUBAN"
          error={error?.field("accountNumber") ?? lookupError ?? undefined}
        />
        <div aria-live="polite" className="rounded-control bg-plaster px-3 py-2 text-sm">
          {checking ? (
            <span className="text-muted">Checking the account name with the bank…</span>
          ) : resolved ? (
            <span>
              <span className="text-muted">Account name: </span>
              <span className="font-semibold">{resolved}</span>
            </span>
          ) : (
            <span className="text-muted">The account name appears here once the bank confirms it.</span>
          )}
        </div>
        {error && !error.errors && <ErrorBox error={error} />}
        <Button type="submit" className="w-full" loading={busy} disabled={!resolved || checking}>
          Save account
        </Button>
      </form>
    );
  }

  return (
    <form onSubmit={submit} className="space-y-4">
      <Input label="Bank" required value={bankName} onChange={(ev) => setBankName(ev.target.value)} placeholder="e.g. GTBank" error={error?.field("bankName")} />
      <Input
        label="Account number"
        required
        inputMode="numeric"
        value={number}
        onChange={(ev) => setNumber(ev.target.value)}
        placeholder="10-digit NUBAN"
        error={error?.field("accountNumber")}
      />
      <Input label="Account name" required value={name} onChange={(ev) => setName(ev.target.value)} hint="As your bank shows it." error={error?.field("accountName")} />
      {error && !error.errors && <ErrorBox error={error} />}
      <Button type="submit" className="w-full" loading={busy}>
        Save account
      </Button>
    </form>
  );
}

/** Turns Paystack direct settlement (a subaccount) on or off for a checked account. Shown only when the platform offers it. */
export function DirectSettlement({
  settings,
  account,
  path,
  onChanged,
}: {
  settings: PayoutSettings | undefined;
  account: PayoutAccount | null;
  /** The payout-account path of this product, e.g. /api/academy/payout-account. */
  path: string;
  onChanged: (result: unknown) => void;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  if (!settings?.directSettlement || !account) return null;

  async function toggle(on: boolean) {
    setBusy(true);
    setError(null);
    try {
      onChanged(await api(`${path}/direct-settlement`, { method: on ? "POST" : "DELETE" }));
    } catch (err) {
      setError(toApiError(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="mt-3 space-y-2 border-t border-line pt-3 text-sm">
      <p className="text-slate-700">
        {account.directSettlement
          ? "Direct settlement is on: Paystack pays your share of each sale straight to this account, after the platform fee."
          : "Direct settlement: Paystack can pay your share of each sale straight to this account, after the platform fee, instead of waiting for a payout."}
      </p>
      {!account.verified && !account.directSettlement && <p className="text-muted">Pick your bank from the list first so the account is checked.</p>}
      {error && <ErrorBox error={error} />}
      <Button variant="secondary" className="min-h-9 px-3 py-1" loading={busy} disabled={!account.verified && !account.directSettlement} onClick={() => toggle(!account.directSettlement)}>
        {account.directSettlement ? "Turn off direct settlement" : "Turn on direct settlement"}
      </Button>
    </div>
  );
}
