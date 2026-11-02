"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { STAFF_COLORS } from "@/lib/config";
import { useCan } from "@/lib/permissions";
import { WEEK_ORDER, WEEKDAYS_SHORT } from "@/lib/time";
import type { SaveStaffRequest, StaffDto } from "@/lib/types";
import { useServices, useStaff } from "@/lib/use-business";
import { ApiErrorView, Badge, Button, EmptyState, ErrorBox, Input, Modal, PageHeader, PageLoader, UpgradePrompt } from "@/components/ui";
import { ChevronRightIcon, PlusIcon, UserIcon } from "@/components/icons";
import { ColorSwatches } from "./color-swatches";
import { StaffAvatar } from "@/components/staff-avatar";

/** "Mon to Sat" style summary of the days someone works. */
function workingDays(s: StaffDto): string {
  const days = WEEK_ORDER.filter((w) => s.schedule.some((i) => i.weekday === w));
  if (days.length === 0) return "No working hours set";
  if (days.length === 7) return "Every day";
  // Collapse consecutive runs (in Monday-first order).
  const idx = days.map((d) => WEEK_ORDER.indexOf(d as (typeof WEEK_ORDER)[number]));
  const runs: string[] = [];
  let start = idx[0];
  let prev = idx[0];
  for (const i of [...idx.slice(1), -1]) {
    if (i === prev + 1) {
      prev = i;
      continue;
    }
    runs.push(start === prev ? WEEKDAYS_SHORT[WEEK_ORDER[start]] : `${WEEKDAYS_SHORT[WEEK_ORDER[start]]} to ${WEEKDAYS_SHORT[WEEK_ORDER[prev]]}`);
    start = prev = i;
  }
  return runs.join(", ");
}

export default function StaffPage() {
  const canEdit = useCan("catalog.edit");
  const { staff, data, error, reload } = useStaff();
  const { services } = useServices();
  const [adding, setAdding] = useState(false);

  return (
    <div className="space-y-4">
      <PageHeader
        title="Staff"
        subtitle="The people customers can book, their hours and their time off."
        action={
          canEdit && (
            <Button onClick={() => setAdding(true)}>
              <PlusIcon className="h-4 w-4" /> Add staff member
            </Button>
          )
        }
      />
      {error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && <PageLoader />}
      {data && staff.length === 0 && (
        <EmptyState
          icon={<UserIcon />}
          title="No staff yet"
          text="Add each stylist or barber so customers can choose who they see, and so their hours shape your free times."
          action={canEdit ? <Button onClick={() => setAdding(true)}>Add staff member</Button> : undefined}
        />
      )}
      {staff.length > 0 && (
        <ul className="divide-y divide-line-soft overflow-hidden rounded-panel border border-line bg-surface">
          {staff.map((s) => {
            const count = services.filter((sv) => sv.staffIds.includes(s.id) || s.serviceIds.includes(sv.id)).length;
            return (
              <li key={s.id}>
                <Link href={`/app/staff/${s.id}`} className="flex items-center gap-3 px-4 py-3 hover:bg-mist sm:px-5">
                  <StaffAvatar staff={s} />
                  <span className="min-w-0 flex-1">
                    <span className="flex flex-wrap items-center gap-2">
                      <span className="truncate font-semibold">{s.displayName}</span>
                      {!s.isBookable && <Badge className="bg-mist-2 text-muted">Not bookable</Badge>}
                      {s.userId && <Badge className="bg-rose-wash text-rose-dark">Has a login</Badge>}
                    </span>
                    <span className="block truncate text-sm text-muted">
                      {workingDays(s)}. {count} service{count === 1 ? "" : "s"}.
                    </span>
                  </span>
                  <ChevronRightIcon className="h-4 w-4 shrink-0 text-faint" />
                </Link>
              </li>
            );
          })}
        </ul>
      )}
      <Modal open={adding} title="Add a staff member" onClose={() => setAdding(false)}>
        {adding && <AddStaffForm position={staff.length} usedColors={staff.map((s) => s.color)} onCancel={() => setAdding(false)} />}
      </Modal>
    </div>
  );
}

function AddStaffForm({ position, usedColors, onCancel }: { position: number; usedColors: string[]; onCancel: () => void }) {
  const router = useRouter();
  const [displayName, setDisplayName] = useState("");
  const [email, setEmail] = useState("");
  const [color, setColor] = useState<string>(STAFF_COLORS.find((c) => !usedColors.map((u) => u?.toUpperCase()).includes(c)) ?? STAFF_COLORS[0]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    const body: SaveStaffRequest = {
      displayName: displayName.trim(),
      bio: null,
      email: email.trim() || null,
      color,
      isBookable: true,
      position,
      serviceIds: [],
      userId: null,
    };
    try {
      const s = await api<StaffDto>("/api/staff", { method: "POST", body });
      router.push(`/app/staff/${s.id}?new=1`);
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-4">
      <Input label="Name customers see" value={displayName} onChange={(e) => setDisplayName(e.target.value)} required autoFocus placeholder="e.g. Tunde" />
      <Input label="Email (optional)" type="email" value={email} onChange={(e) => setEmail(e.target.value)} hint="For their own booking notifications." />
      <ColorSwatches value={color} onChange={setColor} />
      <p className="text-sm text-muted">Next you&apos;ll set their working hours and the services they do.</p>
      {error && (error.isPlanLimit ? <UpgradePrompt message={error.detail || "Your plan's staff limit is reached. Team allows 5 staff and Business has no limit."} /> : <ErrorBox error={error} />)}
      <div className="flex justify-end gap-2">
        <Button variant="secondary" onClick={onCancel}>
          Cancel
        </Button>
        <Button type="submit" loading={busy} disabled={!displayName.trim()}>
          Add staff member
        </Button>
      </div>
    </form>
  );
}
