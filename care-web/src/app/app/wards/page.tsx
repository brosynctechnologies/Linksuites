"use client";

import Link from "next/link";
import { useState, type FormEvent } from "react";
import { api } from "@/lib/api";
import { useCare } from "@/lib/care-me";
import { koboInput, money, plural, toKobo } from "@/lib/format";
import { BED_STATUS, WARD_KINDS, wardKindLabel } from "@/lib/inpatient";
import { useAction } from "@/lib/mutate";
import { qs, useApi } from "@/lib/use-api";
import type { CareSetupDto, CareStaffDto, PriceItemDto } from "@/lib/types";
import type { BedDto, SaveWardRequest, WardDto, WardKind } from "@/lib/types/inpatient";
import { BedIcon } from "@/components/icons";
import {
  ApiErrorView,
  Badge,
  Button,
  Card,
  Checkbox,
  EmptyState,
  ErrorBox,
  Input,
  Modal,
  NoAccess,
  Notice,
  PageHeader,
  PageLoader,
  Select,
  buttonClass,
  cx,
} from "@/components/ui";

/**
 * Wards and beds setup (leadership; the support view reads it). Each ward has a kind, a daily bed charge (a Bed item from the price list), an
 * optional minimum deposit (else the clinic's) and its nurses ("All nurses" is an explicit choice, Task 8 ruling). Active beds count against
 * the plan (402 shows the upgrade prompt). A used bed is made inactive rather than deleted.
 */
export default function WardsPage() {
  const { access } = useCare();
  const allowed = access.isLeadership;
  const wards = useApi<WardDto[]>(allowed ? "/api/care/wards" : null);
  const setup = useApi<CareSetupDto>(allowed ? "/api/care/setup" : null);
  // The daily charge in naira for each ward card (WardDto names the bed item but not its price).
  const bedItems = useApi<PriceItemDto[]>(allowed ? `/api/care/price-list${qs({ category: "Bed" })}` : null);
  const [editing, setEditing] = useState<WardDto | "new" | null>(null);
  const [open, setOpen] = useState<string | null>(null);

  if (!allowed) return <NoAccess text="Wards and beds are set up by the medical director and admins." />;
  const canEdit = !access.readOnly;
  const clinicMin = setup.data?.clinic.minDepositMinor ?? 0;

  return (
    <div className="space-y-5">
      <PageHeader
        title="Wards and beds"
        subtitle="Each ward's daily bed charge, minimum deposit, nurses and beds."
        action={
          <>
            <Link href="/app/beds" className={buttonClass("secondary")}>
              Bed board
            </Link>
            {canEdit && <Button onClick={() => setEditing("new")}>Add ward</Button>}
          </>
        }
      />
      {wards.error && <ApiErrorView error={wards.error} onRetry={wards.reload} />}
      {!wards.data && !wards.error && <PageLoader />}
      {wards.data && wards.data.length === 0 && (
        <EmptyState
          icon={<BedIcon />}
          title="No wards yet"
          text="Add a ward, then its beds. You need a Bed item on the price list for the daily charge."
          action={canEdit && <Button onClick={() => setEditing("new")}>Add ward</Button>}
        />
      )}
      {wards.data && wards.data.length > 0 && (
        <ul className="space-y-3">
          {wards.data.map((w) => (
            <li key={w.id}>
              <Card className="space-y-3">
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div className="min-w-0">
                    <h2 className="display text-lg font-bold text-ink">{w.name}</h2>
                    <p className="text-sm text-muted">
                      {wardKindLabel(w.kind)}. <BedCharge ward={w} items={bedItems.data} /> Minimum deposit{" "}
                      <span className="tnum">{money(w.minDepositMinor ?? clinicMin)}</span>
                      {w.minDepositMinor === null && " (the clinic's)"}.{" "}
                      {w.nurseUserIds.length === 0 ? "All nurses." : `${plural(w.nurseUserIds.length, "nurse")}.`}
                    </p>
                    <p className="tnum mt-1 text-sm text-slate-700">
                      {/* Final fix pass (Task 19 carry): a bed in use is free, occupied or being cleaned, so the three add up. */}
                      {plural(w.activeBeds, "bed")} in use: {w.freeBeds} free, {w.occupiedBeds} occupied, {Math.max(0, w.activeBeds - w.freeBeds - w.occupiedBeds)} being cleaned
                    </p>
                  </div>
                  <div className="flex flex-wrap gap-2">
                    <Button variant="secondary" aria-expanded={open === w.id} onClick={() => setOpen(open === w.id ? null : w.id)}>
                      {open === w.id ? "Hide beds" : "Beds"}
                    </Button>
                    {canEdit && (
                      <Button variant="ghost" onClick={() => setEditing(w)}>
                        Edit
                      </Button>
                    )}
                  </div>
                </div>
                {open === w.id && <Beds ward={w} canEdit={canEdit} onChanged={wards.reload} />}
              </Card>
            </li>
          ))}
        </ul>
      )}
      {editing && (
        <WardDialog
          ward={editing === "new" ? null : editing}
          clinicMinMinor={clinicMin}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            wards.reload();
          }}
        />
      )}
    </div>
  );
}

function WardDialog({ ward, clinicMinMinor, onClose, onSaved }: { ward: WardDto | null; clinicMinMinor: number; onClose: () => void; onSaved: () => void }) {
  const items = useApi<PriceItemDto[]>(`/api/care/price-list${qs({ category: "Bed" })}`);
  const staff = useApi<CareStaffDto[]>("/api/care/staff");
  const [name, setName] = useState(ward?.name ?? "");
  const [kind, setKind] = useState<WardKind>(ward?.kind ?? "General");
  const [itemId, setItemId] = useState(ward?.bedPriceItemId ?? "");
  const [min, setMin] = useState(ward?.minDepositMinor != null ? koboInput(ward.minDepositMinor) : "");
  const [allNurses, setAllNurses] = useState((ward?.nurseUserIds.length ?? 0) === 0);
  const [nurses, setNurses] = useState<string[]>(ward?.nurseUserIds ?? []);
  const [minError, setMinError] = useState<string | null>(null);
  const save = useAction();
  const remove = useAction();
  const nurseStaff = (staff.data ?? []).filter((s) => s.clinicalRole === "Nurse" && s.memberRole !== "Accountant");
  // A ward keeps its current bed item selectable even if it was taken out of use.
  const bedItems = (items.data ?? []).filter((i) => i.active || i.id === ward?.bedPriceItemId);

  async function submit(e: FormEvent) {
    e.preventDefault();
    let minDepositMinor: number | null = null;
    if (min.trim()) {
      minDepositMinor = toKobo(min);
      if (minDepositMinor === null) {
        setMinError("Enter an amount in naira, such as 50000.");
        return;
      }
    }
    setMinError(null);
    const body: SaveWardRequest = { name: name.trim(), kind, bedPriceItemId: itemId || null, minDepositMinor, nurseUserIds: allNurses ? [] : nurses };
    const res = await save.run(() =>
      ward ? api<WardDto>(`/api/care/wards/${ward.id}`, { method: "PUT", body }) : api<WardDto>("/api/care/wards", { method: "POST", body }),
    );
    if (res.ok) onSaved();
  }

  async function del() {
    if (!ward || !window.confirm(`Delete ${ward.name}? Only a ward with no beds can be deleted.`)) return;
    const res = await remove.run(() => api(`/api/care/wards/${ward.id}`, { method: "DELETE" }));
    if (res.ok) onSaved();
  }

  const err = save.error;
  const fields = ["name", "kind", "bedPriceItemId", "minDepositMinor", "nurseUserIds"];
  return (
    <Modal open title={ward ? `Edit ${ward.name}` : "Add a ward"} onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        <Input label="Name" value={name} maxLength={80} required onChange={(e) => setName(e.target.value)} error={err?.field("name")} />
        <Select label="Kind" value={kind} onChange={(e) => setKind(e.target.value as WardKind)} error={err?.field("kind")}>
          {WARD_KINDS.map((k) => (
            <option key={k.value} value={k.value}>
              {k.label}
            </option>
          ))}
        </Select>
        <Select
          label="Daily bed charge"
          value={itemId}
          onChange={(e) => setItemId(e.target.value)}
          error={err?.field("bedPriceItemId")}
          hint={items.data && bedItems.length === 0 ? "Add a Bed item to the price list first." : "Charged once per Lagos day, from the ward the patient is in at midnight."}
        >
          <option value="">Choose a Bed item</option>
          {bedItems.map((i) => (
            <option key={i.id} value={i.id}>
              {i.name} ({money(i.priceMinor)}){i.active ? "" : ", not in use"}
            </option>
          ))}
        </Select>
        <Input
          label="Minimum deposit (naira)"
          inputMode="decimal"
          value={min}
          onChange={(e) => setMin(e.target.value)}
          error={minError ?? err?.field("minDepositMinor")}
          hint={`Leave blank to use the clinic's minimum (${money(clinicMinMinor)}). Only the medical director can admit below it.`}
        />
        <fieldset className="space-y-2">
          <legend className="text-sm font-medium text-slate-700">Nurses</legend>
          <label className="flex items-center gap-2 text-sm text-slate-800">
            <input type="radio" name="nurses" checked={allNurses} onChange={() => setAllNurses(true)} className="accent-[#0f766e]" />
            All nurses
          </label>
          <label className="flex items-center gap-2 text-sm text-slate-800">
            <input type="radio" name="nurses" checked={!allNurses} onChange={() => setAllNurses(false)} className="accent-[#0f766e]" />
            Only these nurses
          </label>
          {!allNurses && (
            <div className="space-y-1.5 rounded-control border border-rule p-3">
              {staff.error && <ErrorBox error={staff.error} onRetry={staff.reload} />}
              {staff.data && nurseStaff.length === 0 && <p className="text-sm text-muted">No nurses on the staff yet.</p>}
              {nurseStaff.map((n) => (
                <Checkbox
                  key={n.userId}
                  checked={nurses.includes(n.userId)}
                  onChange={(on) => setNurses((list) => (on ? [...list, n.userId] : list.filter((x) => x !== n.userId)))}
                  label={n.fullName}
                />
              ))}
            </div>
          )}
          {err?.field("nurseUserIds") && <p className="text-xs font-medium text-arterial">{err.field("nurseUserIds")}</p>}
          <p className="text-xs text-muted">A ward&apos;s nurses chart its patients without giving a reason. Changing the list moves them at once.</p>
        </fieldset>
        <ErrorBox error={err && !fields.some((f) => err.field(f)) ? err : null} />
        <ErrorBox error={remove.error} />
        <div className="flex flex-wrap justify-between gap-2">
          {ward ? (
            <Button variant="danger" loading={remove.busy} onClick={del}>
              Delete ward
            </Button>
          ) : (
            <span />
          )}
          <div className="flex gap-2">
            <Button variant="secondary" onClick={onClose}>
              Cancel
            </Button>
            <Button type="submit" loading={save.busy}>
              Save
            </Button>
          </div>
        </div>
      </form>
    </Modal>
  );
}

function Beds({ ward, canEdit, onChanged }: { ward: WardDto; canEdit: boolean; onChanged: () => void }) {
  const beds = useApi<BedDto[]>(`/api/care/wards/${ward.id}/beds`);
  const [label, setLabel] = useState("");
  const add = useAction();
  const change = useAction();

  async function addBed(e: FormEvent) {
    e.preventDefault();
    const res = await add.run(() => api<BedDto>(`/api/care/wards/${ward.id}/beds`, { method: "POST", body: { label: label.trim(), active: true } }));
    if (res.ok) {
      setLabel("");
      beds.reload();
      onChanged();
    }
  }

  async function update(bed: BedDto, patch: { label?: string; active?: boolean }) {
    const res = await change.run(() =>
      api<BedDto>(`/api/care/wards/${ward.id}/beds/${bed.id}`, { method: "PUT", body: { label: patch.label ?? bed.label, active: patch.active ?? bed.active } }),
    );
    if (res.ok) {
      beds.reload();
      onChanged();
    }
  }

  async function rename(bed: BedDto) {
    const next = window.prompt("The bed's new label", bed.label);
    if (next === null || !next.trim() || next.trim() === bed.label) return;
    await update(bed, { label: next.trim() });
  }

  async function remove(bed: BedDto) {
    if (!window.confirm(`Delete bed ${bed.label}? A bed that has been used can only be made inactive.`)) return;
    const res = await change.run(() => api(`/api/care/wards/${ward.id}/beds/${bed.id}`, { method: "DELETE" }));
    if (res.ok) {
      beds.reload();
      onChanged();
    }
  }

  return (
    <div className="space-y-3 border-t border-rule-soft pt-3">
      {beds.error && <ApiErrorView error={beds.error} onRetry={beds.reload} />}
      {beds.data && beds.data.length === 0 && <p className="text-sm text-muted">No beds in this ward yet.</p>}
      {beds.data && beds.data.length > 0 && (
        <ul className="divide-y divide-rule-soft rounded-control border border-rule">
          {beds.data.map((b) => (
            <li key={b.id} className={cx("flex flex-wrap items-center gap-x-3 gap-y-2 px-3 py-2", !b.active && "bg-slate-50")}>
              <span className="tnum min-w-16 font-bold text-ink">{b.label}</span>
              <Badge tone={BED_STATUS[b.status].tone}>{BED_STATUS[b.status].label}</Badge>
              {!b.active && <Badge>Not in use</Badge>}
              {canEdit && (
                <span className="ml-auto flex flex-wrap gap-1">
                  <Button variant="ghost" className="min-h-9 px-2.5" onClick={() => rename(b)}>
                    Rename
                  </Button>
                  <Button variant="ghost" className="min-h-9 px-2.5" onClick={() => update(b, { active: !b.active })}>
                    {b.active ? "Make inactive" : "Make active"}
                  </Button>
                  <Button variant="ghost" className="min-h-9 px-2.5 text-arterial" onClick={() => remove(b)}>
                    Delete
                  </Button>
                </span>
              )}
            </li>
          ))}
        </ul>
      )}
      <ApiErrorView error={change.error} />
      {canEdit && (
        <form onSubmit={addBed} className="flex flex-wrap items-end gap-2">
          <div className="min-w-40 flex-1">
            <Input label="New bed label" placeholder="Bed 1" value={label} maxLength={40} onChange={(e) => setLabel(e.target.value)} error={add.error?.field("label")} />
          </div>
          <Button type="submit" loading={add.busy} disabled={!label.trim()}>
            Add bed
          </Button>
        </form>
      )}
      {add.error && !add.error.field("label") && <ApiErrorView error={add.error} />}
      {canEdit && <Notice>Active beds count towards your plan. A bed that has been used keeps its history: make it inactive instead of deleting it.</Notice>}
    </div>
  );
}

/** "₦10,000 a day (General ward bed day)": the bed item's current price, which is what each new bed day is charged. */
function BedCharge({ ward, items }: { ward: WardDto; items: PriceItemDto[] | undefined }) {
  const item = items?.find((i) => i.id === ward.bedPriceItemId);
  const name = ward.bedPriceItemName ?? item?.name;
  if (!item) return <>{name ? `${name}, charged daily.` : "Charged daily."}</>;
  return (
    <>
      <span className="tnum font-bold text-ink">{money(item.priceMinor)} a day</span>
      {name && ` (${name})`}.
    </>
  );
}
