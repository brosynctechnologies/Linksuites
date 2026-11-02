"use client";

import { useState, type FormEvent } from "react";
import { api, asList, toApiError, type ApiError } from "@/lib/api";
import { formatDay } from "@/lib/format";
import { formatMoney, toMajorInput, toMinor } from "@/lib/money";
import { useCan } from "@/lib/permissions";
import { useApi } from "@/lib/use-api";
import type { CouponDto, DiscountType, StudioCourseSummaryDto } from "@/lib/types";
import { IconButton } from "@/components/editors";
import { EditIcon, PercentIcon, PlusIcon, TrashIcon } from "@/components/icons";
import {
  ApiErrorView,
  Button,
  DataList,
  EmptyState,
  Input,
  Modal,
  NoAccess,
  PageHeader,
  PageLoader,
  Segmented,
  Select,
  StatusBadge,
  Toggle,
  UpgradePrompt,
  dataRowClass,
} from "@/components/ui";

function discountLabel(c: CouponDto) {
  return c.discountType === "Percent" ? `${c.value}% off` : `${formatMoney(c.value)} off`;
}

export default function CouponsPage() {
  const allowed = useCan("coupons.manage");
  const { data, error, reload, setData } = useApi<CouponDto[]>(allowed ? "/api/studio/coupons" : null);
  const courses = useApi<StudioCourseSummaryDto[]>(allowed ? "/api/studio/courses" : null);
  const courseList = asList(courses.data);
  const list = asList(data);
  const [editing, setEditing] = useState<CouponDto | "new" | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [actionError, setActionError] = useState<ApiError | null>(null);

  if (!allowed) {
    return (
      <div>
        <PageHeader title="Coupons" back="/studio/more" />
        <NoAccess text="Only the owner and admins manage coupons." />
      </div>
    );
  }

  if (error?.isPlanLimit) {
    return (
      <div>
        <PageHeader title="Coupons" back="/studio/more" />
        <UpgradePrompt plan="Pro" message="Coupons come with Pro and Business: give your community a code for a discount, set an expiry date and a limit on uses." />
      </div>
    );
  }

  async function toggle(c: CouponDto) {
    setBusy(c.id);
    setActionError(null);
    try {
      const res = await api<CouponDto>(`/api/studio/coupons/${c.id}`, { method: "PUT", body: { ...c, isActive: !c.isActive } });
      setData((xs) => asList(xs).map((x) => (x.id === c.id ? (res ?? { ...c, isActive: !c.isActive }) : x)));
    } catch (e) {
      setActionError(toApiError(e));
    } finally {
      setBusy(null);
    }
  }

  async function remove(c: CouponDto) {
    if (!window.confirm(`Delete the coupon ${c.code}? Learners can no longer use it.`)) return;
    setBusy(c.id);
    setActionError(null);
    try {
      await api(`/api/studio/coupons/${c.id}`, { method: "DELETE" });
      setData((xs) => asList(xs).filter((x) => x.id !== c.id));
    } catch (e) {
      setActionError(toApiError(e));
    } finally {
      setBusy(null);
    }
  }

  const courseName = (id: string | null) => (id ? (courseList.find((c) => c.id === id)?.title ?? "One course") : "All courses");

  return (
    <div className="space-y-4">
      <PageHeader
        title="Coupons"
        back="/studio/more"
        subtitle="Discount codes learners enter at checkout."
        action={
          <Button onClick={() => setEditing("new")}>
            <PlusIcon className="h-4 w-4" /> New coupon
          </Button>
        }
      />
      {actionError && <ApiErrorView error={actionError} />}
      {!data && error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && <PageLoader />}
      {data && list.length === 0 && (
        <EmptyState
          icon={<PercentIcon />}
          title="No coupons yet"
          text="Create a code for a launch discount, your Telegram group or a scholarship."
          action={<Button onClick={() => setEditing("new")}>New coupon</Button>}
        />
      )}
      {list.length > 0 && (
        <DataList template="minmax(0,1fr) minmax(0,1fr) 7rem 8rem 10rem" head={[{ label: "Code" }, { label: "Applies to" }, { label: "Used" }, { label: "Status" }, { label: "", align: "right" }]}>
          {list.map((c) => (
            <li key={c.id} className={dataRowClass}>
              <div className="min-w-0">
                <p className="font-mono font-semibold text-ink">{c.code}</p>
                <p className="text-xs text-coral-dark">{discountLabel(c)}</p>
              </div>
              <p className="truncate text-right text-sm text-slate-700 md:text-left">{courseName(c.courseId)}</p>
              <p className="tnum text-sm text-slate-700">
                {c.uses}
                {c.maxUses ? ` of ${c.maxUses}` : ""} used
              </p>
              <div className="text-right md:text-left">
                <StatusBadge tone={c.isActive ? "lime" : "neutral"}>{c.isActive ? "Active" : "Paused"}</StatusBadge>
                {c.expiresAt && <p className="text-xs text-muted">Ends {formatDay(c.expiresAt)}</p>}
              </div>
              <div className="col-span-2 flex items-center justify-end gap-1 md:col-span-1">
                <Button variant="ghost" className="min-h-9 px-2.5" disabled={busy === c.id} onClick={() => toggle(c)}>
                  {c.isActive ? "Pause" : "Resume"}
                </Button>
                <IconButton label={`Edit ${c.code}`} onClick={() => setEditing(c)}>
                  <EditIcon className="h-4 w-4" />
                </IconButton>
                <IconButton label={`Delete ${c.code}`} danger disabled={busy === c.id} onClick={() => remove(c)}>
                  <TrashIcon className="h-4 w-4" />
                </IconButton>
              </div>
            </li>
          ))}
        </DataList>
      )}

      <Modal open={editing !== null} title={editing === "new" ? "New coupon" : "Edit coupon"} onClose={() => setEditing(null)}>
        {editing !== null && (
          <CouponForm
            coupon={editing === "new" ? null : editing}
            courses={courseList}
            onSaved={(c) => {
              setEditing(null);
              setData((xs) => {
                const arr = asList(xs);
                return arr.some((x) => x.id === c.id) ? arr.map((x) => (x.id === c.id ? c : x)) : [c, ...arr];
              });
            }}
          />
        )}
      </Modal>
    </div>
  );
}

function CouponForm({ coupon, courses, onSaved }: { coupon: CouponDto | null; courses: StudioCourseSummaryDto[]; onSaved: (c: CouponDto) => void }) {
  const [code, setCode] = useState(coupon?.code ?? "");
  const [courseId, setCourseId] = useState(coupon?.courseId ?? "");
  const [type, setType] = useState<DiscountType>(coupon?.discountType ?? "Percent");
  const [value, setValue] = useState(coupon ? (coupon.discountType === "Fixed" ? toMajorInput(coupon.value) : String(coupon.value)) : "");
  const [maxUses, setMaxUses] = useState(coupon?.maxUses ? String(coupon.maxUses) : "");
  const [expiresAt, setExpiresAt] = useState(coupon?.expiresAt ? coupon.expiresAt.slice(0, 10) : "");
  const [isActive, setIsActive] = useState(coupon?.isActive ?? true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    const v = type === "Percent" ? Math.round(Number(value)) : toMinor(value);
    if (!v || v <= 0 || (type === "Percent" && v > 100)) {
      setError(toApiError(new Error(type === "Percent" ? "Enter a percentage between 1 and 100." : "Enter the amount off in naira.")));
      return;
    }
    const body = {
      code: code.trim().toUpperCase(),
      courseId: courseId || null,
      discountType: type,
      value: v,
      maxUses: maxUses ? Math.max(1, Number(maxUses)) : null,
      expiresAt: expiresAt || null,
      isActive,
    };
    setBusy(true);
    setError(null);
    try {
      const res = coupon
        ? await api<CouponDto>(`/api/studio/coupons/${coupon.id}`, { method: "PUT", body })
        : await api<CouponDto>("/api/studio/coupons", { method: "POST", body });
      onSaved(res);
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-4">
      <Input label="Code" required value={code} onChange={(e) => setCode(e.target.value.toUpperCase().replace(/\s/g, ""))} placeholder="e.g. JAMB2026" className="font-mono uppercase" error={error?.field("code")} />
      <Select label="Applies to" value={courseId} onChange={(e) => setCourseId(e.target.value)}>
        <option value="">All courses</option>
        {courses.map((c) => (
          <option key={c.id} value={c.id}>
            {c.title}
          </option>
        ))}
      </Select>
      <div>
        <p className="mb-1.5 text-sm font-medium text-slate-700">Discount</p>
        <Segmented
          label="Discount type"
          value={type}
          onChange={setType}
          items={[
            { value: "Percent", label: "Percentage" },
            { value: "Fixed", label: "Fixed amount" },
          ]}
        />
      </div>
      <Input label={type === "Percent" ? "Percentage off" : "Amount off (₦)"} inputMode="decimal" required value={value} onChange={(e) => setValue(e.target.value)} hint={type === "Percent" ? "100% makes the course free with this code." : undefined} />
      <div className="grid gap-4 sm:grid-cols-2">
        <Input label="Limit on uses" type="number" min={1} value={maxUses} onChange={(e) => setMaxUses(e.target.value)} placeholder="No limit" />
        <Input
          label="Ends on"
          type="date"
          value={expiresAt}
          onChange={(e) => setExpiresAt(e.target.value)}
          hint="Works until the end of that day, Lagos time. Leave empty for no end date."
        />
      </div>
      <Toggle checked={isActive} onChange={setIsActive} label="Active" description="Paused codes are rejected at checkout." />
      {error && <ApiErrorView error={error} plan="Pro" />}
      <Button type="submit" className="w-full" loading={busy} disabled={!code.trim()}>
        {coupon ? "Save coupon" : "Create coupon"}
      </Button>
    </form>
  );
}
