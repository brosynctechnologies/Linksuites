"use client";

import Link from "next/link";
import { useState, type FormEvent } from "react";
import { api } from "@/lib/api";
import { useAction } from "@/lib/mutate";
import { can, useCan, useRole } from "@/lib/roles";
import { qs, useApi } from "@/lib/use-api";
import type { FinanceSettingsDto, PaystackSettingsDto } from "@/lib/types/fees";
import { ApiErrorView, Badge, Button, Card, ErrorBox, Input, Notice, PageLoader, SectionTitle, Toggle, linkClass } from "@/components/ui";

export default function FeeSettingsPage() {
  const isOwner = can(useRole(), "fees.paystack");
  return (
    <div className="space-y-5">
      <FinanceSettings />
      {isOwner ? (
        <PaystackSettings />
      ) : (
        <Card>
          <SectionTitle>Online payments</SectionTitle>
          <p className="max-w-prose text-sm text-slate-700">
            Parents pay online into the school&apos;s own Paystack account. Only the proprietor can connect it or change the key.
          </p>
        </Card>
      )}
    </div>
  );
}

// ---------- Results hold and reminders ----------

function FinanceSettings() {
  const canEdit = useCan("fees.settings");
  const { data, error, reload, setData } = useApi<FinanceSettingsDto>("/api/school/settings/finance");
  const [form, setForm] = useState<{ hold: boolean; before: string; every: string } | null>(null);
  const [saved, setSaved] = useState(false);
  const { busy, error: saveError, run } = useAction();

  if (error) return <ApiErrorView error={error} onRetry={reload} />;
  if (!data) return <PageLoader />;

  const f = form ?? { hold: data.resultsHoldForUnpaidFees, before: String(data.reminderDaysBefore), every: String(data.reminderEveryDays) };
  const change = (patch: Partial<typeof f>) => (setForm({ ...f, ...patch }), setSaved(false));

  async function submit(e: FormEvent) {
    e.preventDefault();
    const body = { resultsHoldForUnpaidFees: f.hold, reminderDaysBefore: Number(f.before), reminderEveryDays: Number(f.every) };
    const res = await run(() => api<FinanceSettingsDto>("/api/school/settings/finance", { method: "PUT", body }));
    if (res.ok) {
      setData(res.data);
      setForm(null);
      setSaved(true);
    }
  }

  return (
    <Card>
      <SectionTitle>Reminders and results</SectionTitle>
      <form onSubmit={submit} className="space-y-5">
        <Toggle
          label="Hold results for unpaid fees"
          description="While a student's invoice for the term has a balance, parents and students see “Results are held. Please see the school office.” instead of results and report cards. Staff still see everything."
          checked={f.hold}
          disabled={!canEdit}
          onChange={(hold) => change({ hold })}
        />
        <div className="grid gap-4 sm:grid-cols-2">
          <Input
            label="First reminder (days before each due date)"
            type="number"
            inputMode="numeric"
            min={0}
            max={30}
            required
            disabled={!canEdit}
            value={f.before}
            onChange={(e) => change({ before: e.target.value })}
            hint="0 to 30. Use 0 to remind on the due date itself."
            error={saveError?.field("reminderDaysBefore")}
          />
          <Input
            label="While overdue, remind every (days)"
            type="number"
            inputMode="numeric"
            min={1}
            max={30}
            required
            disabled={!canEdit}
            value={f.every}
            onChange={(e) => change({ every: e.target.value })}
            hint="1 to 30."
            error={saveError?.field("reminderEveryDays")}
          />
        </div>
        <p className="text-xs text-muted">Reminders are emailed each morning to the primary guardian, up to six per invoice. For a WhatsApp nudge, use the list on the debtors page.</p>
        {saved && <Notice tone="success">Settings saved.</Notice>}
        <ErrorBox error={saveError && !saveError.field("reminderDaysBefore") && !saveError.field("reminderEveryDays") ? saveError : null} />
        {canEdit ? (
          <Button type="submit" loading={busy} disabled={!form}>
            Save settings
          </Button>
        ) : (
          <p className="text-sm text-muted">Only the proprietor or the principal can change these.</p>
        )}
      </form>
    </Card>
  );
}

// ---------- Paystack (proprietor) ----------

function PaystackSettings() {
  const canEdit = useCan("fees.paystack");
  const { data, error, reload, setData } = useApi<PaystackSettingsDto>("/api/school/settings/paystack");
  const [key, setKey] = useState("");
  const [busyNotice, setBusyNotice] = useState<"save" | "remove" | null>(null);
  const [done, setDone] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);
  const save = useAction();
  const remove = useAction();

  if (error) return <ApiErrorView error={error} onRetry={reload} />;
  if (!data) return <PageLoader />;

  async function saveKey(force: boolean) {
    setDone(null);
    const res = await save.run(() => api<PaystackSettingsDto>("/api/school/settings/paystack", { method: "PUT", body: { secretKey: key.trim(), force } }));
    if (res.ok) {
      const changed = data?.connected;
      setData(res.data);
      setKey("");
      setBusyNotice(null);
      setDone(changed ? "Key changed. Payments started with the previous key are still confirmed for 7 days." : "Paystack connected.");
    } else if (res.error.code === "checkouts_open") setBusyNotice("save");
  }

  async function removeKey(force: boolean) {
    if (!force && !window.confirm("Disconnect Paystack? Parents won't be able to pay online until you add a key again.")) return;
    setDone(null);
    const res = await remove.run(() => api(`/api/school/settings/paystack${qs({ force: force || undefined })}`, { method: "DELETE" }));
    if (res.ok) {
      setData({ ...data!, connected: false });
      setBusyNotice(null);
      setDone("Paystack disconnected. Payments started before now are still confirmed for 7 days.");
    } else if (res.error.code === "checkouts_open") setBusyNotice("remove");
  }

  async function copy() {
    try {
      await navigator.clipboard.writeText(data!.webhookUrl);
      setCopied(true);
    } catch {
      setCopied(false);
    }
  }

  return (
    <Card>
      <SectionTitle action={data.connected ? <Badge tone="pass">Connected</Badge> : <Badge>Not connected</Badge>}>Online payments with Paystack</SectionTitle>
      <div className="space-y-4">
        <p className="max-w-prose text-sm text-slate-700">
          Parents pay fees online straight into the school&apos;s own Paystack account. SchoolLink never holds the money. Parents see Pay now
          once a key is saved and the school&apos;s plan includes online fees.{" "}
          <Link href="/app/billing" className={linkClass}>
            See plans
          </Link>
        </p>

        <div className="space-y-1.5">
          <p className="text-sm font-medium text-slate-700">Webhook URL</p>
          <div className="flex flex-wrap items-center gap-2">
            <code className="tnum min-w-0 flex-1 rounded-control border border-rule bg-slate-50 px-3 py-2.5 text-sm break-all text-ink">{data.webhookUrl}</code>
            <Button variant="secondary" onClick={copy}>
              {copied ? "Copied" : "Copy"}
            </Button>
          </div>
          <p className="text-xs text-muted">In your Paystack dashboard, open Settings, then API keys and webhooks, and paste this as the webhook URL.</p>
        </div>

        {canEdit && (
          <form
            onSubmit={(e) => {
              e.preventDefault();
              void saveKey(false);
            }}
            className="space-y-3"
          >
            <Input
              label={data.connected ? "New secret key" : "Secret key"}
              type="password"
              autoComplete="off"
              spellCheck={false}
              required
              placeholder="sk_live_…"
              value={key}
              onChange={(e) => setKey(e.target.value)}
              hint="It starts with sk_live_ (or sk_test_ while you try it out). The key is stored encrypted and never shown again."
              error={save.error?.field("secretKey")}
            />
            {data.connected && (
              <p className="text-sm text-slate-700">
                When you change the key, payments parents started with the previous key are still confirmed for 7 days, so nobody&apos;s payment is lost.
              </p>
            )}
            {busyNotice === "save" && (
              <Notice tone="warning">
                <p>Some parents are in the middle of paying. Wait a few minutes and try again, or change the key now: their payments are still confirmed with the previous key for 7 days.</p>
                <Button variant="secondary" className="mt-2" loading={save.busy} onClick={() => saveKey(true)}>
                  Change the key now
                </Button>
              </Notice>
            )}
            <ErrorBox error={save.error && !save.error.field("secretKey") && save.error.code !== "checkouts_open" ? save.error : null} />
            <div className="flex flex-wrap gap-2">
              <Button type="submit" loading={save.busy}>
                {data.connected ? "Change key" : "Connect Paystack"}
              </Button>
              {data.connected && (
                <Button variant="danger" loading={remove.busy} onClick={() => removeKey(false)}>
                  Disconnect
                </Button>
              )}
            </div>
          </form>
        )}
        {busyNotice === "remove" && (
          <Notice tone="warning">
            <p>Some parents are in the middle of paying. Wait a few minutes and try again, or disconnect now: their payments are still confirmed with the old key for 7 days.</p>
            <Button variant="danger" className="mt-2" loading={remove.busy} onClick={() => removeKey(true)}>
              Disconnect now
            </Button>
          </Notice>
        )}
        <ErrorBox error={remove.error && remove.error.code !== "checkouts_open" ? remove.error : null} />
        {done && <Notice tone="success">{done}</Notice>}
      </div>
    </Card>
  );
}
