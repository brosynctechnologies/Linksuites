"use client";

import { useState, type FormEvent } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { depositFor, formatMoney, toMajorInput, toMinor } from "@/lib/money";
import { useCan } from "@/lib/permissions";
import { formatDuration } from "@/lib/time";
import type { DepositType, SaveServiceRequest, ServiceDto, StaffDto } from "@/lib/types";
import { depositsOffReason, useBusiness, useServices, useStaff } from "@/lib/use-business";
import {
  ApiErrorView,
  Badge,
  Button,
  Checkbox,
  EmptyState,
  ErrorBox,
  Input,
  Modal,
  Notice,
  PageHeader,
  PageLoader,
  Select,
  Textarea,
  Toggle,
  cx,
} from "@/components/ui";
import { ChevronDownIcon, PlusIcon, ScissorsIcon } from "@/components/icons";

function toRequest(s: ServiceDto, position = s.position): SaveServiceRequest {
  return {
    name: s.name,
    description: s.description,
    durationMinutes: s.durationMinutes,
    bufferBeforeMinutes: s.bufferBeforeMinutes,
    bufferAfterMinutes: s.bufferAfterMinutes,
    priceMinor: s.priceMinor,
    depositType: s.depositType,
    depositValue: s.depositValue,
    isActive: s.isActive,
    position,
    staffIds: s.staffIds,
  };
}

function depositText(s: ServiceDto, currency: string) {
  if (s.depositType === "None" || s.depositMinor <= 0) return "No deposit";
  return s.depositType === "Percent" ? `${s.depositValue}% deposit (${formatMoney(s.depositMinor, currency)})` : `${formatMoney(s.depositMinor, currency)} deposit`;
}

export default function ServicesPage() {
  const canEdit = useCan("catalog.edit");
  const { currency, business } = useBusiness();
  const [nonce, setNonce] = useState(0);
  const { services, data, error, reload, loading } = useServices(nonce);
  const { staff } = useStaff();
  const [editing, setEditing] = useState<ServiceDto | "new" | null>(null);
  const [moving, setMoving] = useState(false);
  const [moveError, setMoveError] = useState<ApiError | null>(null);

  async function move(index: number, dir: -1 | 1) {
    const order = [...services];
    const j = index + dir;
    if (j < 0 || j >= order.length) return;
    [order[index], order[j]] = [order[j], order[index]];
    setMoving(true);
    setMoveError(null);
    try {
      await Promise.all(
        order.map((s, i) => (s.position === i ? null : api<ServiceDto>(`/api/services/${s.id}`, { method: "PUT", body: toRequest(s, i) }))).filter(Boolean),
      );
    } catch (e) {
      setMoveError(toApiError(e));
    } finally {
      setMoving(false);
      setNonce((n) => n + 1);
    }
  }

  return (
    <div className="space-y-4">
      <PageHeader
        title="Services"
        subtitle="What customers can book, how long it takes and what it costs."
        action={
          canEdit && (
            <Button onClick={() => setEditing("new")}>
              <PlusIcon className="h-4 w-4" /> Add service
            </Button>
          )
        }
      />
      {business && !business.depositsEnabled && services.some((s) => s.depositType !== "None") && (
        <Notice tone="warning">{depositsOffReason(business)} Until then, customers aren&apos;t asked for a deposit and bookings are confirmed without one.</Notice>
      )}
      {moveError && <ApiErrorView error={moveError} />}
      {error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && <PageLoader />}
      {data && services.length === 0 && (
        <EmptyState
          icon={<ScissorsIcon />}
          title="No services yet"
          text="Add what you offer, for example “Knotless braids, medium, 4 hours”. Customers pick from this list on your booking page."
          action={canEdit ? <Button onClick={() => setEditing("new")}>Add your first service</Button> : undefined}
        />
      )}
      {services.length > 0 && (
        <ol className={cx("divide-y divide-line-soft overflow-hidden rounded-panel border border-line bg-surface", (loading || moving) && "opacity-60")}>
          {services.map((s, i) => (
            <li key={s.id} className="flex items-center gap-2 px-2 py-1 sm:px-3">
              {canEdit && (
                <div className="flex shrink-0 flex-col">
                  <button
                    type="button"
                    disabled={i === 0 || moving}
                    onClick={() => move(i, -1)}
                    className="flex h-7 w-8 items-center justify-center rounded-control text-muted hover:bg-mist disabled:opacity-25"
                    aria-label={`Move ${s.name} up`}
                  >
                    <ChevronDownIcon className="h-4 w-4 rotate-180" />
                  </button>
                  <button
                    type="button"
                    disabled={i === services.length - 1 || moving}
                    onClick={() => move(i, 1)}
                    className="flex h-7 w-8 items-center justify-center rounded-control text-muted hover:bg-mist disabled:opacity-25"
                    aria-label={`Move ${s.name} down`}
                  >
                    <ChevronDownIcon className="h-4 w-4" />
                  </button>
                </div>
              )}
              <button
                type="button"
                disabled={!canEdit}
                onClick={() => setEditing(s)}
                className="grid min-w-0 flex-1 grid-cols-[minmax(0,1fr)_auto] items-center gap-x-4 rounded-control px-2 py-2.5 text-left enabled:hover:bg-mist sm:grid-cols-[minmax(0,1.4fr)_minmax(0,1fr)_auto]"
              >
                <span className="min-w-0">
                  <span className="flex items-center gap-2">
                    <span className={cx("truncate font-semibold", !s.isActive && "text-muted")}>{s.name}</span>
                    {!s.isActive && <Badge className="bg-mist-2 text-muted">Hidden</Badge>}
                  </span>
                  <span className="block text-sm text-muted">
                    {formatDuration(s.durationMinutes)}
                    {s.bufferAfterMinutes > 0 && `, then ${s.bufferAfterMinutes} min clean-up`}
                  </span>
                </span>
                <span className="hidden text-sm text-slate-700 sm:block">
                  {s.staffIds.length === 0 ? <span className="text-brick">Nobody can do this yet</span> : `${s.staffIds.length} staff`}
                </span>
                <span className="text-right">
                  <span className="block font-semibold tnum">{formatMoney(s.priceMinor, currency)}</span>
                  <span className="block text-xs text-muted">{depositText(s, currency)}</span>
                </span>
              </button>
            </li>
          ))}
        </ol>
      )}

      <Modal open={editing !== null} title={editing === "new" ? "Add a service" : "Edit service"} onClose={() => setEditing(null)} wide>
        {editing !== null && (
          <ServiceForm
            key={editing === "new" ? "new" : editing.id}
            existing={editing === "new" ? null : editing}
            nextPosition={services.length}
            staff={staff}
            currency={currency}
            onCancel={() => setEditing(null)}
            onSaved={() => {
              setEditing(null);
              setNonce((n) => n + 1);
            }}
          />
        )}
      </Modal>
    </div>
  );
}

const DURATIONS = [15, 20, 30, 45, 60, 75, 90, 120, 150, 180, 240, 300, 360, 480];

function ServiceForm({
  existing,
  nextPosition,
  staff,
  currency,
  onCancel,
  onSaved,
}: {
  existing: ServiceDto | null;
  nextPosition: number;
  staff: StaffDto[];
  currency: string;
  onCancel: () => void;
  onSaved: () => void;
}) {
  const [name, setName] = useState(existing?.name ?? "");
  const [description, setDescription] = useState(existing?.description ?? "");
  const [duration, setDuration] = useState(String(existing?.durationMinutes ?? 60));
  const [before, setBefore] = useState(String(existing?.bufferBeforeMinutes ?? 0));
  const [after, setAfter] = useState(String(existing?.bufferAfterMinutes ?? 0));
  const [price, setPrice] = useState(toMajorInput(existing?.priceMinor ?? null));
  const [depositType, setDepositType] = useState<DepositType>(existing?.depositType ?? "None");
  const [depositValue, setDepositValue] = useState(
    existing ? (existing.depositType === "Fixed" ? toMajorInput(existing.depositValue) : existing.depositType === "Percent" ? String(existing.depositValue) : "") : "",
  );
  const [isActive, setIsActive] = useState(existing?.isActive ?? true);
  const [staffIds, setStaffIds] = useState<string[]>(existing?.staffIds ?? (staff.length === 1 ? [staff[0].id] : staff.map((s) => s.id)));
  const [busy, setBusy] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  const priceMinor = toMinor(price) ?? 0;
  const percent = Number(depositValue);
  const wholePercent = Number.isInteger(percent) ? percent : 0;
  // The same rule the server saves and charges with: whole percentages only, rounded down to the kobo.
  const depositPreview = depositFor(priceMinor, depositType, depositType === "Fixed" ? toMinor(depositValue) ?? 0 : wholePercent);

  async function submit(e: FormEvent) {
    e.preventDefault();
    const durationMinutes = Number(duration);
    if (!name.trim()) return setError(toApiError(new Error("Give the service a name.")));
    if (!durationMinutes || durationMinutes < 5) return setError(toApiError(new Error("Duration must be at least 5 minutes.")));
    let value = 0;
    if (depositType === "Fixed") {
      value = toMinor(depositValue) ?? 0;
      if (value <= 0) return setError(toApiError(new Error("Enter the deposit amount.")));
      if (value > priceMinor) return setError(toApiError(new Error("The deposit can't be more than the price.")));
    } else if (depositType === "Percent") {
      value = Number(depositValue);
      if (!Number.isInteger(value) || value < 1 || value > 100)
        return setError(toApiError(new Error("The deposit percentage must be a whole number between 1 and 100.")));
    }
    const body: SaveServiceRequest = {
      name: name.trim(),
      description: description.trim() || null,
      durationMinutes,
      bufferBeforeMinutes: Math.max(0, Number(before) || 0),
      bufferAfterMinutes: Math.max(0, Number(after) || 0),
      priceMinor,
      depositType,
      depositValue: value,
      isActive,
      position: existing?.position ?? nextPosition,
      staffIds,
    };
    setBusy(true);
    setError(null);
    try {
      await api<ServiceDto>(existing ? `/api/services/${existing.id}` : "/api/services", { method: existing ? "PUT" : "POST", body });
      onSaved();
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  }

  async function remove() {
    if (!existing || !window.confirm(`Delete ${existing.name}? Existing bookings keep their details; customers can no longer book it.`)) return;
    setDeleting(true);
    setError(null);
    try {
      await api(`/api/services/${existing.id}`, { method: "DELETE" });
      onSaved();
    } catch (err) {
      setError(toApiError(err));
      setDeleting(false);
    }
  }

  const toggleStaff = (id: string, on: boolean) => setStaffIds((ids) => (on ? [...new Set([...ids, id])] : ids.filter((x) => x !== id)));

  return (
    <form onSubmit={submit} className="space-y-5">
      <Input label="Name" value={name} onChange={(e) => setName(e.target.value)} required placeholder="e.g. Knotless braids, medium" error={error?.field("name")} />
      <Textarea label="Description (optional)" rows={2} value={description} onChange={(e) => setDescription(e.target.value)} hint="Shown under the name on your booking page." />
      <div className="grid gap-4 sm:grid-cols-3">
        <Select label="Duration" value={duration} onChange={(e) => setDuration(e.target.value)}>
          {[...new Set([...DURATIONS, Number(duration)])]
            .sort((a, b) => a - b)
            .map((d) => (
              <option key={d} value={d}>
                {formatDuration(d)}
              </option>
            ))}
        </Select>
        <Input label="Prep before (min)" type="number" min={0} max={240} value={before} onChange={(e) => setBefore(e.target.value)} />
        <Input label="Clean-up after (min)" type="number" min={0} max={240} value={after} onChange={(e) => setAfter(e.target.value)} />
      </div>
      <div className="grid gap-4 sm:grid-cols-3">
        <Input label={`Price (${currency})`} inputMode="decimal" value={price} onChange={(e) => setPrice(e.target.value)} placeholder="0" />
        <Select label="Deposit" value={depositType} onChange={(e) => setDepositType(e.target.value as DepositType)}>
          <option value="None">No deposit</option>
          <option value="Fixed">Fixed amount</option>
          <option value="Percent">Percentage of price</option>
        </Select>
        {depositType !== "None" && (
          <Input
            label={depositType === "Fixed" ? `Deposit (${currency})` : "Deposit (%)"}
            inputMode={depositType === "Fixed" ? "decimal" : "numeric"}
            value={depositValue}
            onChange={(e) => setDepositValue(e.target.value)}
            hint={
              depositType !== "Percent"
                ? undefined
                : depositPreview > 0
                  ? `${formatMoney(depositPreview, currency)} at this price`
                  : "A whole number, such as 25."
            }
          />
        )}
      </div>
      {depositType !== "None" && (
        <p className="text-sm text-slate-700">
          Customers pay the deposit online to hold the time; the rest is paid at your place. A deposit is the strongest way to stop no-shows.
        </p>
      )}
      <fieldset>
        <legend className="mb-2 text-sm font-medium text-slate-700">Who can do it</legend>
        {staff.length === 0 ? (
          <p className="text-sm text-muted">Add staff first, then choose who does this service.</p>
        ) : (
          <div className="grid gap-2 sm:grid-cols-2">
            {staff.map((s) => (
              <Checkbox key={s.id} checked={staffIds.includes(s.id)} onChange={(on) => toggleStaff(s.id, on)} label={s.displayName} />
            ))}
          </div>
        )}
      </fieldset>
      <Toggle checked={isActive} onChange={setIsActive} label="Show on the booking page" description="Hidden services can still be booked by your team." />
      <ErrorBox error={error} />
      <div className="flex flex-wrap items-center justify-between gap-2 border-t border-line-soft pt-4">
        {existing ? (
          <Button variant="danger" loading={deleting} onClick={remove}>
            Delete service
          </Button>
        ) : (
          <span />
        )}
        <div className="flex gap-2">
          <Button variant="secondary" onClick={onCancel}>
            Cancel
          </Button>
          <Button type="submit" loading={busy}>
            {existing ? "Save service" : "Add service"}
          </Button>
        </div>
      </div>
    </form>
  );
}
