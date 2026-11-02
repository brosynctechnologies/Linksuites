"use client";

import Link from "next/link";
import { useState, type FormEvent } from "react";
import { api } from "@/lib/api";
import { patchAuth, useAuth } from "@/lib/auth";
import { useCare } from "@/lib/care-me";
import { formatDay, koboInput, money, naira, toKobo } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { qs, useApi } from "@/lib/use-api";
import type { BillingDto, CarePaystackSettingsDto, CareSetupDto, ClinicDto, PriceItemDto, SaveClinicProfileRequest } from "@/lib/types";
import {
  ApiErrorView,
  Badge,
  Button,
  Card,
  ErrorBox,
  Input,
  NoAccess,
  Notice,
  PageHeader,
  PageLoader,
  SectionTitle,
  Select,
  Toggle,
  UpgradePrompt,
  UsageMeter,
  linkClass,
} from "@/components/ui";

/** Clinic settings: the profile (leadership), the plan, and the clinic's own Paystack key (the medical director only). */
export default function SettingsPage() {
  const { access } = useCare();
  const setup = useApi<CareSetupDto>(access.isLeadership ? "/api/care/setup" : null);
  if (!access.isLeadership) return <NoAccess />;

  return (
    <div className="space-y-5">
      <PageHeader title="Settings" subtitle="The clinic's details, its plan and online payments." />
      {setup.error && <ApiErrorView error={setup.error} onRetry={setup.reload} />}
      {!setup.data && !setup.error && <PageLoader />}
      {setup.data && <ProfileForm clinic={setup.data.clinic} readOnly={access.readOnly} onSaved={setup.reload} />}
      <PlanCard />
      {access.isDirector && <PaystackSettings />}
      {!access.isDirector && (
        <p className="text-sm text-muted">Online payments with Paystack and plan changes are set by the medical director.</p>
      )}
    </div>
  );
}

// ---------- Profile ----------

function ProfileForm({ clinic, readOnly, onSaved }: { clinic: ClinicDto; readOnly: boolean; onSaved: () => void }) {
  const auth = useAuth();
  const prices = useApi<PriceItemDto[]>("/api/care/price-list");
  const [name, setName] = useState(clinic.name);
  const [address, setAddress] = useState(clinic.address ?? "");
  const [phone, setPhone] = useState(clinic.phone ?? "");
  const [prefix, setPrefix] = useState(clinic.patientPrefix);
  const [dispenses, setDispenses] = useState(clinic.dispensesDrugs);
  const [minDeposit, setMinDeposit] = useState(koboInput(clinic.minDepositMinor));
  const [lowDeposit, setLowDeposit] = useState(koboInput(clinic.lowDepositThresholdMinor));
  const [registrationId, setRegistrationId] = useState(clinic.registrationPriceItemId ?? "");
  const [consultationId, setConsultationId] = useState(clinic.consultationPriceItemId ?? "");
  const [moneyErrors, setMoneyErrors] = useState<{ min?: string; low?: string }>({});
  const [saved, setSaved] = useState(false);
  const { busy, error, run } = useAction();

  async function submit(e: FormEvent) {
    e.preventDefault();
    setSaved(false);
    const min = toKobo(minDeposit || "0");
    const low = toKobo(lowDeposit || "0");
    const errs = {
      min: min === null ? "Enter an amount in naira, such as 20000." : undefined,
      low: low === null ? "Enter an amount in naira, such as 5000." : undefined,
    };
    setMoneyErrors(errs);
    if (min === null || low === null) return;
    // The profile PUT is a full replace: both price items are always sent (an id left out would be cleared).
    const body: SaveClinicProfileRequest = {
      name: name.trim(),
      address: address.trim() || null,
      phone: phone.trim() || null,
      patientPrefix: prefix.trim().toUpperCase(),
      dispensesDrugs: dispenses,
      minDepositMinor: min,
      lowDepositThresholdMinor: low,
      registrationPriceItemId: registrationId || null,
      consultationPriceItemId: consultationId || null,
    };
    const res = await run(() => api<ClinicDto>("/api/care/settings/profile", { method: "PUT", body }));
    if (res.ok) {
      setSaved(true);
      if (auth?.tenant && res.data.name !== auth.tenant.name) patchAuth({ tenant: { ...auth.tenant, name: res.data.name } });
      onSaved();
    }
  }

  /** Active items of one category, plus the saved one even if it was since taken out of use (so it isn't silently dropped). */
  function options(category: "Registration" | "Consultation", saved: string | null) {
    const list = (prices.data ?? []).filter((i) => i.category === category && (i.active || i.id === saved));
    return list.map((i) => (
      <option key={i.id} value={i.id}>
        {i.name}, {money(i.priceMinor)}
        {i.active ? "" : " (not in use)"}
      </option>
    ));
  }

  const fields = ["name", "address", "phone", "patientPrefix", "minDepositMinor", "lowDepositThresholdMinor", "registrationPriceItemId", "consultationPriceItemId"];
  return (
    <Card>
      <SectionTitle>Clinic profile</SectionTitle>
      <form onSubmit={submit} className="space-y-4" noValidate>
        <fieldset disabled={readOnly} className="space-y-4">
          <Input label="Clinic name" required value={name} onChange={(e) => setName(e.target.value)} error={error?.field("name")} />
          <div className="grid gap-4 sm:grid-cols-2">
            <Input label="Address" value={address} onChange={(e) => setAddress(e.target.value)} error={error?.field("address")} autoComplete="street-address" />
            <Input label="Phone" type="tel" value={phone} onChange={(e) => setPhone(e.target.value)} error={error?.field("phone")} autoComplete="tel" />
          </div>
          <Input
            label="Patient number prefix"
            value={prefix}
            maxLength={6}
            onChange={(e) => setPrefix(e.target.value.toUpperCase())}
            error={error?.field("patientPrefix")}
            hint={`New patients are numbered ${prefix.trim().toUpperCase() || "CL"}-000001, ${prefix.trim().toUpperCase() || "CL"}-000002 and so on. Existing numbers don't change.`}
            className="tnum max-w-40"
          />

          <div className="grid gap-4 sm:grid-cols-2">
            <Select
              label="Registration charge"
              value={registrationId}
              onChange={(e) => setRegistrationId(e.target.value)}
              error={error?.field("registrationPriceItemId")}
              hint="Added to a new patient's first visit."
            >
              <option value="">No registration charge</option>
              {options("Registration", clinic.registrationPriceItemId)}
            </Select>
            <Select
              label="Consultation charge"
              value={consultationId}
              onChange={(e) => setConsultationId(e.target.value)}
              error={error?.field("consultationPriceItemId")}
              hint="Added to every visit at check-in."
            >
              <option value="">No consultation charge</option>
              {options("Consultation", clinic.consultationPriceItemId)}
            </Select>
          </div>
          {prices.data && !prices.data.some((i) => i.active && (i.category === "Registration" || i.category === "Consultation")) && (
            <p className="text-sm text-muted">
              Add Registration and Consultation items to the{" "}
              <Link href="/app/price-list" className={linkClass}>
                price list
              </Link>{" "}
              to charge them automatically.
            </p>
          )}

          <div className="grid gap-4 sm:grid-cols-2">
            <Input
              label="Minimum deposit on admission (₦)"
              inputMode="decimal"
              value={minDeposit}
              onChange={(e) => setMinDeposit(e.target.value)}
              error={moneyErrors.min ?? error?.field("minDepositMinor")}
              hint="0 means no minimum. A ward can set its own."
              className="tnum"
            />
            <Input
              label="Low deposit alert below (₦)"
              inputMode="decimal"
              value={lowDeposit}
              onChange={(e) => setLowDeposit(e.target.value)}
              error={moneyErrors.low ?? error?.field("lowDepositThresholdMinor")}
              hint="The front desk and cashier are told when an admitted patient's deposit falls below this. 0 means once they owe money."
              className="tnum"
            />
          </div>
          <Toggle
            checked={dispenses}
            onChange={setDispenses}
            label="The clinic dispenses drugs"
            description="Prescriptions are charged on the bill from the price list. Turn off if patients buy drugs elsewhere."
          />
        </fieldset>
        <ErrorBox error={error && !fields.some((f) => error.field(f)) ? error : null} />
        {prices.error && <ErrorBox error={prices.error} onRetry={prices.reload} />}
        {saved && <Notice tone="success">Clinic profile saved.</Notice>}
        {!readOnly && (
          <Button type="submit" loading={busy}>
            Save profile
          </Button>
        )}
      </form>
    </Card>
  );
}

// ---------- Plan ----------

function PlanCard() {
  const { access } = useCare();
  const billing = useApi<BillingDto>("/api/billing");
  const checkout = useAction();
  if (billing.error) return <ApiErrorView error={billing.error} onRetry={billing.reload} />;
  if (!billing.data) return null;
  const { currentPlan, plans, usage, subscription } = billing.data;
  const upgrades = plans.filter((p) => p.priceMinor > currentPlan.priceMinor);

  async function upgrade(code: string) {
    const res = await checkout.run(() => api<{ authorizationUrl: string }>("/api/billing/checkout", { method: "POST", body: { planCode: code } }));
    if (res.ok) window.location.assign(res.data.authorizationUrl);
  }

  return (
    <Card>
      <SectionTitle id="plan" action={<Badge tone="pass">{currentPlan.name}</Badge>}>Plan</SectionTitle>
      <p className="-mt-1 mb-4 text-sm text-slate-700">
        {currentPlan.priceMinor === 0 ? "Free" : `${naira(currentPlan.priceMinor)} a month`}
        {subscription?.currentPeriodEnd ? `, paid to ${formatDay(subscription.currentPeriodEnd.slice(0, 10))}` : ""}
        {subscription?.complimentary ? " (complimentary)" : ""}.
      </p>
      {usage.items.length > 0 && (
        <div className="space-y-3">
          {usage.items.map((u) => (
            <UsageMeter key={u.key} label={u.label} used={u.used} limit={u.limit} />
          ))}
        </div>
      )}
      {upgrades.length > 0 && access.isDirector && !access.readOnly && (
        <div className="mt-5 flex flex-wrap gap-2">
          {upgrades.map((p) => (
            <Button key={p.code} variant={p.code === "clinic" ? "primary" : "secondary"} loading={checkout.busy} onClick={() => upgrade(p.code)}>
              Upgrade to {p.name}, {naira(p.priceMinor)} a month
            </Button>
          ))}
        </div>
      )}
      {upgrades.length > 0 && !access.isDirector && <p className="mt-4 text-sm text-muted">The medical director can upgrade the plan.</p>}
      <div className="mt-3">
        <ErrorBox error={checkout.error} />
      </div>
    </Card>
  );
}

// ---------- Paystack (the medical director) ----------

function PaystackSettings() {
  const { access } = useCare();
  const { data, error, reload, setData } = useApi<CarePaystackSettingsDto>("/api/care/settings/paystack");
  const [key, setKey] = useState("");
  const [busyNotice, setBusyNotice] = useState<"save" | "remove" | null>(null);
  const [done, setDone] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);
  const save = useAction();
  const remove = useAction();

  if (error) return <ApiErrorView error={error} onRetry={reload} />;
  if (!data) return <PageLoader />;
  const canEdit = !access.readOnly;

  async function saveKey(force: boolean) {
    setDone(null);
    const res = await save.run(() => api<CarePaystackSettingsDto>("/api/care/settings/paystack", { method: "PUT", body: { secretKey: key.trim(), force } }));
    if (res.ok) {
      const changed = data?.connected;
      setData(res.data);
      setKey("");
      setBusyNotice(null);
      setDone(changed ? "Key changed. Payments started with the previous key are still confirmed for 7 days." : "Paystack connected.");
    } else if (res.error.code === "checkouts_open") setBusyNotice("save");
  }

  async function removeKey(force: boolean) {
    if (!force && !window.confirm("Disconnect Paystack? Patients won't be able to pay online until you add a key again.")) return;
    setDone(null);
    const res = await remove.run(() => api(`/api/care/settings/paystack${qs({ force: force || undefined })}`, { method: "DELETE" }));
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
          Patients pay bills online straight into the clinic&apos;s own Paystack account. CareLink never holds the money. Only the medical director
          connects it or changes the key.
        </p>

        {!data.available && !data.connected ? (
          // The plan has no online payments: the API answers 402 to a key, so offer the upgrade instead of the form.
          <UpgradePrompt plan="Clinic" message="Online payments aren't included in your plan. Upgrade to the Clinic plan to take payments online." />
        ) : (
          <>
            {!data.available && (
              <Notice tone="warning">Your plan no longer includes online payments, so patients can&apos;t pay online. Upgrade to take payments again.</Notice>
            )}
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
                {busyNotice === "save" && (
                  <Notice tone="warning">
                    <p>
                      Some patients are in the middle of paying. Wait a few minutes and try again, or change the key now: their payments are still confirmed
                      with the previous key for 7 days.
                    </p>
                    <Button variant="secondary" className="mt-2" loading={save.busy} onClick={() => saveKey(true)}>
                      Change the key now
                    </Button>
                  </Notice>
                )}
                {save.error?.isPlanLimit ? (
                  <UpgradePrompt plan="Clinic" message={save.error.detail} />
                ) : (
                  <ErrorBox error={save.error && !save.error.field("secretKey") && save.error.code !== "checkouts_open" ? save.error : null} />
                )}
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
          </>
        )}
        {busyNotice === "remove" && (
          <Notice tone="warning">
            <p>Some patients are in the middle of paying. Wait a few minutes and try again, or disconnect now: their payments are still confirmed with the old key for 7 days.</p>
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
