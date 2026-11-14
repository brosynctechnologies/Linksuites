"use client";

import { useState, type FormEvent } from "react";
import { ConfirmDialog } from "@/components/confirm";
import { RequireArea, useAdmin } from "@/components/console";
import { useToast } from "@/components/toast";
import { Button, Checkbox, ErrorPanel, Facts, FormErrors, Input, Loading, Mono, PageHeader, Panel, Select, StatusBadge } from "@/components/ui";
import { api, toApiError, type ApiError } from "@/lib/api";
import { stamp } from "@/lib/format";
import { can } from "@/lib/roles";
import type { PaymentSettingsDto, PlanProvider, SavePaymentSettingsRequest } from "@/lib/types";
import { useApi } from "@/lib/use-api";

const PROVIDER_NAME: Record<PlanProvider, string> = { paystack: "Paystack", opay: "OPay" };
const FIELDS = ["onlinePaymentsEnabled", "planProvider", "bankName", "bankAccountNumber", "bankAccountName"];

export default function PaymentSettingsPage() {
  return (
    <RequireArea area="money">
      <PaymentSettings />
    </RequireArea>
  );
}

function PaymentSettings() {
  const { data, error, reload, setData } = useApi<PaymentSettingsDto>("/api/platform/payment-settings");
  const { user } = useAdmin();
  const canEdit = can(user.platformRole, "payment.settings");

  return (
    <>
      <PageHeader
        title="Payment settings"
        subtitle="Whether businesses can pay online, which provider takes plan payments, and the company account they pay into by bank transfer while online payment is off. Provider keys stay in the server config."
      />
      {error && <ErrorPanel error={error} onRetry={reload} className="mb-4" />}
      {!data && !error && <Loading />}
      {data && (
        <SettingsForm
          key={data.updatedAt ?? "initial"}
          settings={data}
          canEdit={canEdit}
          onSaved={(saved) => setData(() => saved)}
        />
      )}
    </>
  );
}

function SettingsForm({ settings, canEdit, onSaved }: { settings: PaymentSettingsDto; canEdit: boolean; onSaved: (s: PaymentSettingsDto) => void }) {
  const toast = useToast();
  const [online, setOnline] = useState(settings.onlinePaymentsEnabled);
  const [provider, setProvider] = useState<PlanProvider>(settings.planProvider);
  const [bankName, setBankName] = useState(settings.bankName ?? "");
  const [accountNumber, setAccountNumber] = useState(settings.bankAccountNumber ?? "");
  const [accountName, setAccountName] = useState(settings.bankAccountName ?? "");
  const [localErrors, setLocalErrors] = useState<Record<string, string>>({});
  const [error, setError] = useState<ApiError | null>(null);
  const [pending, setPending] = useState<SavePaymentSettingsRequest | null>(null);
  const configured = settings.providers[provider];

  function build(): SavePaymentSettingsRequest | null {
    const errs: Record<string, string> = {};
    if (online && !configured) errs.onlinePaymentsEnabled = `${PROVIDER_NAME[provider]} keys aren't set on the server, so online payment can't be switched on with it.`;
    if (!bankName.trim()) errs.bankName = "Enter the bank name.";
    if (!/^[0-9]{10}$/.test(accountNumber.trim())) errs.bankAccountNumber = "Enter the 10-digit account number.";
    if (!accountName.trim()) errs.bankAccountName = "Enter the account name.";
    setLocalErrors(errs);
    if (Object.keys(errs).length) return null;
    return { onlinePaymentsEnabled: online, planProvider: provider, bankName: bankName.trim(), bankAccountNumber: accountNumber.trim(), bankAccountName: accountName.trim() };
  }

  function submit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    const body = build();
    if (body) setPending(body);
  }

  async function save(body: SavePaymentSettingsRequest) {
    try {
      const saved = await api<PaymentSettingsDto>("/api/platform/payment-settings", { method: "PUT", body });
      setPending(null);
      toast("Payment settings saved");
      onSaved(saved);
    } catch (err) {
      const e = toApiError(err);
      setError(e);
      throw e;
    }
  }

  const fieldError = (k: string) => localErrors[k] ?? error?.field(k);

  return (
    <div className="space-y-4">
      <Panel title="Now">
        <Facts
          items={[
            {
              label: "Online payment",
              value: settings.onlinePaymentsEnabled ? <StatusBadge tone="ok">On</StatusBadge> : <StatusBadge tone="wait">Off: plans by bank transfer</StatusBadge>,
            },
            { label: "Plan provider", value: PROVIDER_NAME[settings.planProvider] },
            {
              label: "Keys on the server",
              value: (["paystack", "opay"] as PlanProvider[]).map((p) => `${PROVIDER_NAME[p]}: ${settings.providers[p] ? "set" : "not set"}`).join(", "),
            },
            {
              label: "Company account",
              value: settings.bankAccountNumber ? (
                <>
                  {settings.bankName}, <Mono>{settings.bankAccountNumber}</Mono>, {settings.bankAccountName}
                </>
              ) : (
                "Not set"
              ),
            },
            { label: "Last changed", value: settings.updatedAt ? `${stamp(settings.updatedAt)}${settings.updatedByName ? ` by ${settings.updatedByName}` : ""}` : "Not changed since setup" },
          ]}
        />
      </Panel>

      <Panel title="Change settings">
        {!canEdit && <p className="mb-4 text-sm text-muted">Only a super admin can change these settings.</p>}
        <form onSubmit={submit} className="space-y-5" noValidate>
          <fieldset disabled={!canEdit} className="space-y-5">
            <div>
              <Checkbox
                checked={online}
                onChange={setOnline}
                label="Take payments online"
                hint="Off: businesses pay for plans by bank transfer to the account below, and course sales, short-let stays and listing boosts are unavailable. Payments already made are still confirmed and refunded either way."
              />
              {fieldError("onlinePaymentsEnabled") && <p className="text-xs font-medium text-stop">{fieldError("onlinePaymentsEnabled")}</p>}
            </div>
            <Select
              label="Plan provider"
              value={provider}
              onChange={(e) => setProvider(e.target.value as PlanProvider)}
              error={fieldError("planProvider")}
              hint="Takes new plan payments while online payment is on. Course sales, stays and boosts always use Paystack."
            >
              {(["paystack", "opay"] as PlanProvider[]).map((p) => (
                <option key={p} value={p}>
                  {PROVIDER_NAME[p]}
                  {settings.providers[p] ? "" : " (keys not set)"}
                </option>
              ))}
            </Select>
            <div className="grid gap-3 sm:grid-cols-3">
              <Input label="Bank" value={bankName} onChange={(e) => setBankName(e.target.value)} maxLength={60} error={fieldError("bankName")} />
              <Input
                label="Account number"
                inputMode="numeric"
                value={accountNumber}
                onChange={(e) => setAccountNumber(e.target.value)}
                maxLength={10}
                className="font-mono tnum"
                error={fieldError("bankAccountNumber")}
              />
              <Input label="Account name" value={accountName} onChange={(e) => setAccountName(e.target.value)} maxLength={100} error={fieldError("bankAccountName")} />
            </div>
            <FormErrors error={error} fields={FIELDS} />
            <div className="flex justify-end border-t border-line pt-4">
              <Button type="submit">Save settings</Button>
            </div>
          </fieldset>
        </form>
      </Panel>

      <ConfirmDialog
        open={!!pending}
        onClose={() => setPending(null)}
        title="Save payment settings"
        customer
        effect={
          pending && (
            <>
              Online payment will be <strong>{pending.onlinePaymentsEnabled ? "on" : "off"}</strong>
              {pending.onlinePaymentsEnabled
                ? `, with new plan payments through ${PROVIDER_NAME[pending.planProvider]}.`
                : ". Businesses pay for plans by bank transfer to "}
              {!pending.onlinePaymentsEnabled && (
                <>
                  {pending.bankName}, <Mono>{pending.bankAccountNumber}</Mono>, {pending.bankAccountName}.
                </>
              )}{" "}
              Every business sees this at once.
            </>
          )
        }
        confirmLabel="Save settings"
        onConfirm={async () => {
          if (pending) await save(pending);
        }}
      />
    </div>
  );
}
