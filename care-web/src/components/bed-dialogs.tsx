"use client";

// Bed logistics shared by the bed board, the ward list and the admission page (Task 19): assigning the first bed with the deposit, and
// transfers. Demographics, bed and money only: nothing clinical is read or shown, so front desk and the cashier use them too.

import { useMemo, useState, type FormEvent } from "react";
import { api, downloadFile } from "@/lib/api";
import { useCare } from "@/lib/care-me";
import { money, toKobo } from "@/lib/format";
import { DESK_METHODS } from "@/lib/inpatient";
import { useAction } from "@/lib/mutate";
import { useApi } from "@/lib/use-api";
import type { CareSetupDto } from "@/lib/types";
import type { AdmissionSummaryDto, AssignBedRequest, BedAssignedDto, BedBoardDto, PayMethod, WardDto } from "@/lib/types/inpatient";
import { ApiErrorView, Button, Checkbox, Input, Modal, Notice, Select, Wristband } from "./ui";

/** Free beds on the board, grouped by ward, leaving out `exceptBedId`. */
function useFreeBeds(exceptBedId?: string | null) {
  const board = useApi<BedBoardDto>("/api/care/bed-board");
  const wards = useApi<WardDto[]>("/api/care/wards");
  const free = useMemo(
    () =>
      (board.data?.wards ?? [])
        .map((w) => ({ ward: w, beds: w.beds.filter((b) => b.status === "Free" && b.id !== exceptBedId) }))
        .filter((g) => g.beds.length > 0),
    [board.data, exceptBedId],
  );
  return { free, wards: wards.data ?? [], error: board.error ?? wards.error, loading: !board.data && !board.error, reload: board.reload };
}

function BedSelect({ value, onChange, groups, error }: { value: string; onChange: (v: string) => void; groups: ReturnType<typeof useFreeBeds>["free"]; error?: string }) {
  return (
    <Select label="Bed" value={value} onChange={(e) => onChange(e.target.value)} error={error}>
      <option value="">Choose a free bed</option>
      {groups.map((g) => (
        <optgroup key={g.ward.id} label={g.ward.name}>
          {g.beds.map((b) => (
            <option key={b.id} value={b.id}>
              {g.ward.name}, {b.label}
            </option>
          ))}
        </optgroup>
      ))}
    </Select>
  );
}

/**
 * Assigns the admission's first bed and records the deposit (a desk payment with a receipt). Any member but the support view (Task 8 ruling).
 * A deposit below the ward's minimum (or the clinic's) is refused with the API's sentence; the medical director may override it (audited).
 */
export function AssignBedDialog({ admission, onClose, onDone }: { admission: AdmissionSummaryDto; onClose: () => void; onDone: (r: BedAssignedDto) => void }) {
  const { access } = useCare();
  const beds = useFreeBeds();
  const setup = useApi<CareSetupDto>("/api/care/setup");
  const [bedId, setBedId] = useState("");
  const [deposit, setDeposit] = useState("");
  const [method, setMethod] = useState<PayMethod>("Cash");
  const [reference, setReference] = useState("");
  const [override, setOverride] = useState(false);
  const [amountError, setAmountError] = useState<string | null>(null);
  const [done, setDone] = useState<BedAssignedDto | null>(null);
  const { busy, error, run } = useAction();
  const receipt = useAction();

  const wardId = beds.free.find((g) => g.beds.some((b) => b.id === bedId))?.ward.id;
  const ward = beds.wards.find((w) => w.id === wardId);
  const minimum = ward ? (ward.minDepositMinor ?? setup.data?.clinic.minDepositMinor ?? 0) : null;
  const kobo = deposit.trim() ? toKobo(deposit) : 0;
  const below = minimum !== null && kobo !== null && kobo < minimum;

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (kobo === null) {
      setAmountError("Enter the deposit in naira, such as 50000.");
      return;
    }
    setAmountError(null);
    const body: AssignBedRequest = {
      bedId,
      depositMinor: kobo,
      method: kobo > 0 ? method : null,
      reference: reference.trim() || null,
      overrideByOwner: access.isDirector && override,
    };
    const res = await run(() => api<BedAssignedDto>(`/api/care/admissions/${admission.id}/bed`, { method: "POST", body }));
    if (res.ok) {
      setDone(res.data);
      onDone(res.data);
    } else if (res.error.code === "bed_occupied" || res.error.code === "bed_cleaning") beds.reload();
  }

  if (done)
    return (
      <Modal open title="Bed assigned" onClose={onClose}>
        <div className="space-y-4">
          <Notice tone="success">
            {done.admission.patientName} is in {done.admission.wardName}, {done.admission.bedLabel}.
            {done.deposit && ` Deposit of ${money(done.deposit.amountMinor)} received, receipt ${done.deposit.receiptNumber}.`}
          </Notice>
          <ApiErrorView error={receipt.error} />
          <div className="flex flex-wrap justify-end gap-2">
            {done.deposit && (
              <Button
                variant="secondary"
                loading={receipt.busy}
                onClick={() => receipt.run(() => downloadFile(`/api/care/payments/${done.deposit!.id}/receipt.pdf`, `${done.deposit!.receiptNumber ?? "receipt"}.pdf`))}
              >
                Print the receipt
              </Button>
            )}
            <Button onClick={onClose}>Done</Button>
          </div>
        </div>
      </Modal>
    );

  return (
    <Modal open title="Assign a bed" onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        <p className="flex flex-wrap items-center gap-2 text-sm">
          <Wristband number={admission.patientNumber} />
          <span className="font-bold text-ink">{admission.patientName}</span>
        </p>
        <ApiErrorView error={beds.error} onRetry={beds.reload} />
        {!beds.loading && beds.free.length === 0 && <Notice tone="warning">No bed is free. Mark a cleaned bed ready on the bed board, or add beds.</Notice>}
        <BedSelect value={bedId} onChange={setBedId} groups={beds.free} error={error?.field("bedId")} />
        <Input
          label="Deposit (naira)"
          inputMode="decimal"
          value={deposit}
          onChange={(e) => setDeposit(e.target.value)}
          error={amountError ?? error?.field("depositMinor")}
          hint={minimum !== null ? `This ward's minimum deposit is ${money(minimum)}.` : "Choose a bed to see the ward's minimum deposit."}
        />
        {kobo !== null && kobo > 0 && (
          <div className="grid gap-3 sm:grid-cols-2">
            <Select label="Paid by" value={method} onChange={(e) => setMethod(e.target.value as PayMethod)} error={error?.field("method")}>
              {DESK_METHODS.map((m) => (
                <option key={m.value} value={m.value}>
                  {m.label}
                </option>
              ))}
            </Select>
            <Input label="Reference" value={reference} maxLength={100} onChange={(e) => setReference(e.target.value)} error={error?.field("reference")} hint="POS slip or transfer reference." />
          </div>
        )}
        {below && access.isDirector && !access.readOnly && (
          <Checkbox checked={override} onChange={setOverride} label={`Admit with less than the minimum of ${money(minimum!)} (medical director's override, recorded).`} />
        )}
        {below && !access.isDirector && <p className="text-sm text-iodine">Below the ward&apos;s minimum: only the medical director can accept a smaller deposit.</p>}
        <ApiErrorView error={error && !["bedId", "depositMinor", "method", "reference"].some((f) => error.field(f)) ? error : null} />
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" loading={busy} disabled={!bedId || access.readOnly}>
            Assign the bed
          </Button>
        </div>
      </form>
    </Modal>
  );
}

/** Moves an admitted patient to another free bed (the old one goes to cleaning). Front desk, clinical staff and leadership. */
export function TransferDialog({ admission, onClose, onDone }: { admission: AdmissionSummaryDto; onClose: () => void; onDone: (a: AdmissionSummaryDto) => void }) {
  const beds = useFreeBeds(admission.bedId);
  const [bedId, setBedId] = useState("");
  const { busy, error, run } = useAction();

  async function submit(e: FormEvent) {
    e.preventDefault();
    const res = await run(() => api<AdmissionSummaryDto>(`/api/care/admissions/${admission.id}/transfer`, { method: "POST", body: { bedId } }));
    if (res.ok) onDone(res.data);
    else if (res.error.code === "bed_occupied" || res.error.code === "bed_cleaning") beds.reload();
  }

  return (
    <Modal open title="Transfer to another bed" onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        <p className="flex flex-wrap items-center gap-2 text-sm">
          <Wristband number={admission.patientNumber} />
          <span className="font-bold text-ink">{admission.patientName}</span>
          <span className="text-muted">
            now in {admission.wardName}, {admission.bedLabel}
          </span>
        </p>
        <ApiErrorView error={beds.error} onRetry={beds.reload} />
        {!beds.loading && beds.free.length === 0 && <Notice tone="warning">No other bed is free.</Notice>}
        <BedSelect value={bedId} onChange={setBedId} groups={beds.free} error={error?.field("bedId")} />
        <p className="text-xs text-muted">The old bed goes to cleaning. From tomorrow the daily charge follows the new ward, and its nurses take over.</p>
        <ApiErrorView error={error && !error.field("bedId") ? error : null} />
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" loading={busy} disabled={!bedId}>
            Transfer
          </Button>
        </div>
      </form>
    </Modal>
  );
}
