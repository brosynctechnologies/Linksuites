"use client";

import Link from "next/link";
import { useState, type FormEvent } from "react";
import { api, ApiError, toApiError } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useApi } from "@/lib/use-api";
import type { StorePaymentsDto } from "@/lib/types";
import {
  ApiErrorView,
  Badge,
  Button,
  ErrorBox,
  FormSection,
  Input,
  Notice,
  PageHeader,
  PageLoader,
  buttonClass,
  inputClass,
} from "@/components/ui";
import { CheckIcon, CopyIcon } from "@/components/icons";

/** The 409 the API sends while customers are part-way through paying with the current key. */
const CHECKOUTS_OPEN = "checkouts_open";

type Pending = { kind: "save"; secretKey: string } | { kind: "remove" };

export default function PaymentsPage() {
  const auth = useAuth();
  const isOwner = auth?.tenant.role === "Owner" && !auth.support;
  const { data, error, reload, setData } = useApi<StorePaymentsDto>("/api/store/payments");
  const [secretKey, setSecretKey] = useState("");
  const [busy, setBusy] = useState<"save" | "remove" | null>(null);
  const [formError, setFormError] = useState<ApiError | string | null>(null);
  const [fieldError, setFieldError] = useState<string | undefined>();
  const [saved, setSaved] = useState<string | null>(null);
  // A change refused with 409 checkouts_open, waiting for "Change anyway".
  const [confirm, setConfirm] = useState<{ pending: Pending; message: string } | null>(null);
  const [copied, setCopied] = useState(false);

  async function run(pending: Pending, force: boolean) {
    setBusy(pending.kind);
    setFormError(null);
    setFieldError(undefined);
    setSaved(null);
    try {
      const res =
        pending.kind === "save"
          ? await api<StorePaymentsDto>("/api/store/payments", { method: "PUT", body: { secretKey: pending.secretKey, force } })
          : await api<StorePaymentsDto>(`/api/store/payments?force=${force}`, { method: "DELETE" });
      setData(res);
      setConfirm(null);
      setSecretKey("");
      setSaved(pending.kind === "save" ? "Paystack is connected. Customers can now pay online." : "Your Paystack key was removed. Customers can't pay online now.");
    } catch (e) {
      const err = toApiError(e);
      if (err.status === 409 && err.code === CHECKOUTS_OPEN) {
        setConfirm({ pending, message: err.message });
      } else {
        setConfirm(null);
        const keyError = err.errors?.secretKey?.[0] ?? err.errors?.SecretKey?.[0];
        if (keyError) setFieldError(keyError);
        else setFormError(err);
      }
    } finally {
      setBusy(null);
    }
  }

  function save(e: FormEvent) {
    e.preventDefault();
    const key = secretKey.trim();
    if (!key) {
      setFieldError("Paste your Paystack secret key.");
      return;
    }
    void run({ kind: "save", secretKey: key }, false);
  }

  function remove() {
    if (!window.confirm("Remove your Paystack key? Customers won't see the pay online button until you add a key again.")) return;
    void run({ kind: "remove" }, false);
  }

  async function copyWebhook(url: string) {
    try {
      await navigator.clipboard.writeText(url);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      setCopied(false);
    }
  }

  if (!data) {
    return (
      <div>
        <PageHeader title="Payments" back="/dashboard/more" />
        {error ? <ApiErrorView error={error} onRetry={reload} /> : <PageLoader rows={2} />}
      </div>
    );
  }

  if (!data.available) {
    return (
      <div className="space-y-6">
        <PageHeader title="Payments" back="/dashboard/more" />
        <div className="rounded-panel border border-marigold/50 bg-marigold-soft p-4 text-sm text-ink sm:p-5">
          <p className="font-display text-lg font-bold">Take payments online with Pro</p>
          <p className="mt-1 max-w-prose text-ink-2">
            On the Pro plan, customers can pay for their order by card or bank transfer through your own Paystack account. The money goes
            straight to you, and orders are marked paid on their own.
          </p>
          {isOwner ? (
            <Link href="/dashboard/billing" className={buttonClass("primary", "mt-3")}>
              See Pro plan
            </Link>
          ) : (
            <p className="mt-2 text-ink-2">Ask the store owner to upgrade.</p>
          )}
        </div>
      </div>
    );
  }

  return (
    <div className="space-y-8">
      <PageHeader
        title="Payments"
        back="/dashboard/more"
        subtitle="Let customers pay for orders online through your own Paystack account."
      />

      {saved && <Notice tone="success">{saved}</Notice>}

      <FormSection
        title="Paystack"
        description="Payments go straight to your Paystack account. We never hold your money."
      >
        <div className="flex flex-wrap items-center gap-3">
          <span className="text-sm text-ink-2">Status</span>
          {data.connected ? (
            <Badge tone="ink" dot>
              Connected
            </Badge>
          ) : (
            <Badge tone="outline" dot>
              Not connected
            </Badge>
          )}
        </div>

        {isOwner ? (
          <form onSubmit={save} className="space-y-4 border-t border-line pt-4" noValidate>
            <Input
              label={data.connected ? "Replace secret key" : "Secret key"}
              type="password"
              autoComplete="off"
              spellCheck={false}
              value={secretKey}
              onChange={(e) => setSecretKey(e.target.value)}
              placeholder="sk_live_…"
              error={fieldError}
              hint="In Paystack, go to Settings, then API Keys & Webhooks. Copy the secret key."
            />
            {formError && (typeof formError === "string" ? <ErrorBox error={formError} /> : <ApiErrorView error={formError} />)}
            {confirm && (
              <div role="alert" className="rounded-control bg-marigold-soft p-3.5 text-sm text-marigold-deep">
                <p className="font-medium">{confirm.message || "Some customers are paying right now with the current key."}</p>
                <p className="mt-1 text-ink-2">
                  If you change it now, those payments may not be matched to their orders. You can check them later from each order.
                </p>
                <div className="mt-3 flex flex-wrap gap-2">
                  <Button variant="danger" loading={busy === confirm.pending.kind} onClick={() => void run(confirm.pending, true)}>
                    Change anyway
                  </Button>
                  <Button variant="ghost" onClick={() => setConfirm(null)}>
                    Keep current key
                  </Button>
                </div>
              </div>
            )}
            <div className="flex flex-wrap gap-2">
              <Button type="submit" loading={busy === "save"} disabled={!!busy}>
                {data.connected ? "Save new key" : "Connect Paystack"}
              </Button>
              {data.connected && (
                <Button variant="danger" loading={busy === "remove"} disabled={!!busy} onClick={remove}>
                  Remove key
                </Button>
              )}
            </div>
          </form>
        ) : (
          <p className="border-t border-line pt-4 text-sm text-ink-2">Only the store owner can add or change the Paystack key.</p>
        )}
      </FormSection>

      <FormSection
        title="Webhook"
        description="Paystack uses this link to tell us when a customer has paid, so orders are marked paid even if the customer closes the page."
      >
        <div>
          <label htmlFor="webhook-url" className="block text-sm font-medium text-ink">
            Webhook URL
          </label>
          <div className="mt-1.5 flex gap-2">
            <input id="webhook-url" readOnly value={data.webhookUrl} className={inputClass} onFocus={(e) => e.target.select()} />
            <Button variant="secondary" onClick={() => void copyWebhook(data.webhookUrl)}>
              {copied ? <CheckIcon width={18} height={18} /> : <CopyIcon width={18} height={18} />}
              {copied ? "Copied" : "Copy"}
            </Button>
          </div>
          <p className="mt-1.5 text-xs text-subtle" aria-live="polite">
            {copied ? "Webhook URL copied." : "In Paystack, paste it into Settings, then API Keys & Webhooks, under Live Webhook URL."}
          </p>
        </div>
      </FormSection>
    </div>
  );
}
