"use client";

import { useState, type FormEvent, type ReactNode } from "react";
import { ConfirmDialog } from "@/components/confirm";
import { RequireArea } from "@/components/console";
import { ArrowDownIcon, ArrowUpIcon, EditIcon, PlusIcon, TrashIcon } from "@/components/icons";
import { useToast } from "@/components/toast";
import { Button, Checkbox, EmptyState, ErrorPanel, FormErrors, Input, Loading, Mono, PageHeader, Panel, SidePanel, StatusBadge, TableScroll, cx } from "@/components/ui";
import { api, toApiError, type ApiError } from "@/lib/api";
import { money, num } from "@/lib/format";
import { limitLabel, PRODUCT_LIMIT_KEYS, PRODUCTS, productName } from "@/lib/products";
import type { PlanDto, SavePlanRequest } from "@/lib/types";
import { useApi } from "@/lib/use-api";

const PRICE_NOTE = "A new price or new limits apply to new subscriptions, and to current subscribers when they next pay (nothing renews automatically). Current paid periods keep what they paid for. The default free plan is different: its businesses have no subscription, so edits to it apply to all of them at once.";
/** The free plan businesses fall back to has no subscriptions to protect: a change reaches every business on it straight away. */
const DEFAULT_PLAN_NOTE = "This is the default free plan. Every business without a running subscription is on it, so new limits and features apply to all of them immediately. Lowering a limit can block their next publish or upload.";
type Editing = { plan: PlanDto | null; product: string };

export default function PlansPage() {
  return (
    <RequireArea area="money">
      <Plans />
    </RequireArea>
  );
}

function Plans() {
  const { data, error, reload } = useApi<PlanDto[]>("/api/platform/plans");
  const [editing, setEditing] = useState<Editing | null>(null);
  const plans = data ?? [];
  const known = PRODUCTS.map((p) => p.key as string);
  const products = [...known, ...[...new Set(plans.map((p) => p.product))].filter((p) => !known.includes(p))];

  return (
    <>
      <PageHeader title="Plans" subtitle={`Plans per product. Plans are never deleted: hide one to stop new sign-ups. ${PRICE_NOTE}`} />
      {error && <ErrorPanel error={error} onRetry={reload} className="mb-4" />}
      {!data && !error && <Loading />}
      {data && (
        <div className="space-y-4">
          {products.map((product) => {
            const group = plans.filter((p) => p.product === product).sort((a, b) => a.sortOrder - b.sortOrder);
            return (
              <Panel
                key={product}
                title={productName(product)}
                flush
                action={
                  <Button variant="secondary" onClick={() => setEditing({ plan: null, product })}>
                    <PlusIcon className="h-4 w-4" /> New plan
                  </Button>
                }
              >
                {group.length === 0 ? (
                  <EmptyState title="No plans for this product" />
                ) : (
                  <TableScroll label={`${productName(product)} plans`}>
                    <table className="data-table min-w-[720px]">
                      <thead>
                        <tr>
                          <th>Name</th>
                          <th>Code</th>
                          <th className="num">Price</th>
                          <th>Interval</th>
                          <th className="num">Paying</th>
                          <th className="num">Comped</th>
                          <th>Flags</th>
                          <th className="num">Order</th>
                          <th className="w-px">
                            <span className="sr-only">Actions</span>
                          </th>
                        </tr>
                      </thead>
                      <tbody>
                        {group.map((p) => (
                          <tr key={p.id}>
                            <td className="font-medium">{p.name}</td>
                            <td>
                              <Mono>{p.code}</Mono>
                            </td>
                            <td className="num">{money(p.priceMinor, p.currency)}</td>
                            <td>{p.interval}</td>
                            <td className="num">{num(p.activeSubscribers)}</td>
                            <td className="num">{num(p.compSubscribers)}</td>
                            <td>
                              <span className="flex flex-wrap gap-1">
                                {p.isDefault && <StatusBadge tone="neutral">Default</StatusBadge>}
                                {p.isHidden && <StatusBadge tone="wait">Hidden</StatusBadge>}
                              </span>
                            </td>
                            <td className="num">{p.sortOrder}</td>
                            <td className="!py-1">
                              <Button variant="ghost" className="px-2" onClick={() => setEditing({ plan: p, product })} aria-label={`Edit ${p.name}`}>
                                <EditIcon className="h-4 w-4" /> <span className="hidden sm:inline">Edit</span>
                              </Button>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </TableScroll>
                )}
              </Panel>
            );
          })}
        </div>
      )}

      <SidePanel open={!!editing} onClose={() => setEditing(null)} title={editing?.plan ? `Edit ${productName(editing.product)} ${editing.plan.name}` : `New ${editing ? productName(editing.product) : ""} plan`}>
        {editing && (
          <PlanForm
            key={editing.plan?.id ?? `new-${editing.product}`}
            editing={editing}
            siblings={plans.filter((p) => p.product === editing.product)}
            onSaved={() => {
              setEditing(null);
              reload();
            }}
          />
        )}
      </SidePanel>
    </>
  );
}

const FIELDS = ["product", "code", "name", "priceMinor", "isHidden", "sortOrder", "limits", "features"];

function PlanForm({ editing, siblings, onSaved }: { editing: Editing; siblings: PlanDto[]; onSaved: () => void }) {
  const toast = useToast();
  const plan = editing.plan;
  const currency = plan?.currency ?? siblings[0]?.currency ?? "NGN";
  // Limit inputs: the plan's own keys, plus every key its product's plans or the docs use.
  const keySet = new Set<string>([...Object.keys(plan?.limits ?? {}), ...siblings.flatMap((s) => Object.keys(s.limits ?? {})), ...(PRODUCT_LIMIT_KEYS[editing.product] ?? [])]);
  const [code, setCode] = useState(plan?.code ?? "");
  const [name, setName] = useState(plan?.name ?? "");
  const [price, setPrice] = useState(plan ? String(plan.priceMinor / 100) : "");
  const [isHidden, setIsHidden] = useState(plan?.isHidden ?? false);
  const [sortOrder, setSortOrder] = useState(String(plan?.sortOrder ?? (siblings.length ? Math.max(...siblings.map((s) => s.sortOrder)) + 1 : 0)));
  const [limits, setLimits] = useState<Record<string, string>>(() => Object.fromEntries([...keySet].map((k) => [k, plan?.limits?.[k] !== undefined ? String(plan.limits[k]) : ""])));
  const [newKey, setNewKey] = useState("");
  const [features, setFeatures] = useState<string[]>(plan?.features?.length ? [...plan.features] : [""]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [localErrors, setLocalErrors] = useState<Record<string, string>>({});
  const [confirmPrice, setConfirmPrice] = useState<SavePlanRequest | null>(null);

  function build(): SavePlanRequest | null {
    const errs: Record<string, string> = {};
    const priceNum = Number(price.replace(/,/g, ""));
    if (!name.trim()) errs.name = "Give the plan a name.";
    if (!plan && !/^[a-z0-9-]{2,30}$/.test(code.trim())) errs.code = "Use 2-30 lowercase letters, numbers and dashes.";
    if (price.trim() === "" || !Number.isFinite(priceNum) || priceNum < 0) errs.priceMinor = "Enter a price of 0 or more.";
    const limitValues: Record<string, number> = {};
    for (const [k, v] of Object.entries(limits)) {
      if (v.trim() === "") continue;
      const n = Number(v);
      if (!Number.isInteger(n) || n < -1) errs[`limits.${k}`] = "Whole number, -1 for unlimited.";
      else limitValues[k] = n;
    }
    setLocalErrors(errs);
    if (Object.keys(errs).length) return null;
    return {
      product: editing.product,
      code: plan?.code ?? code.trim(),
      name: name.trim(),
      priceMinor: Math.round(priceNum * 100),
      isHidden,
      sortOrder: Number(sortOrder) || 0,
      limits: limitValues,
      features: features.map((f) => f.trim()).filter(Boolean),
    };
  }

  async function save(body: SavePlanRequest) {
    setBusy(true);
    setError(null);
    try {
      if (plan) await api(`/api/platform/plans/${plan.id}`, { method: "PUT", body });
      else await api("/api/platform/plans", { method: "POST", body });
      toast(plan ? "Plan saved" : "Plan created");
      onSaved();
    } catch (err) {
      const e = toApiError(err);
      setError(e);
      throw e;
    } finally {
      setBusy(false);
    }
  }

  async function submit(e: FormEvent) {
    e.preventDefault();
    const body = build();
    if (!body) return;
    if (plan && body.priceMinor !== plan.priceMinor) {
      setConfirmPrice(body);
      return;
    }
    await save(body).catch(() => {});
  }

  const fieldError = (k: string) => localErrors[k] ?? error?.field(k);

  return (
    <>
    <form onSubmit={submit} className="space-y-5" noValidate>
      <div className="grid gap-3 sm:grid-cols-2">
        <Input label="Name" value={name} onChange={(e) => setName(e.target.value)} error={fieldError("name")} required />
        <Input
          label="Code"
          value={plan?.code ?? code}
          onChange={(e) => setCode(e.target.value)}
          disabled={!!plan}
          className="font-mono"
          error={fieldError("code")}
          hint={plan ? "Codes can't change." : "Lowercase, e.g. pro or business."}
        />
        <Input
          label={`Price (${currency === "NGN" ? "₦" : currency}) per ${(plan?.interval ?? siblings[0]?.interval ?? "Month").toLowerCase()}`}
          inputMode="decimal"
          value={price}
          onChange={(e) => setPrice(e.target.value)}
          error={fieldError("priceMinor")}
          className="tnum"
          hint={price.trim() && Number.isFinite(Number(price.replace(/,/g, ""))) ? `Shown as ${money(Math.round(Number(price.replace(/,/g, "")) * 100), currency)}` : "In naira, not kobo."}
        />
        <Input label="Sort order" type="number" value={sortOrder} onChange={(e) => setSortOrder(e.target.value)} error={fieldError("sortOrder")} className="tnum" hint="Lower shows first." />
      </div>
      <p className="rounded-control border border-line bg-concrete-2 px-3 py-2 text-sm">{plan?.isDefault ? DEFAULT_PLAN_NOTE : PRICE_NOTE}</p>
      <Checkbox
        checked={isHidden}
        onChange={setIsHidden}
        disabled={plan?.isDefault}
        label="Hidden"
        hint={plan?.isDefault ? "The default free plan new businesses start on can't be hidden." : "Hidden plans can't be chosen by new subscribers; businesses already on them can keep renewing."}
      />
      {error?.field("isHidden") && <p className="text-xs font-medium text-stop">{error.field("isHidden")}</p>}

      <fieldset className="space-y-3">
        <legend className="text-sm font-semibold">Limits</legend>
        <p className="text-xs text-muted">-1 = unlimited. For on/off features, 0 = off and 1 = on. Leave empty to leave the limit out.</p>
        <div className="grid gap-3 sm:grid-cols-2">
          {Object.keys(limits).map((k) => (
            <Input
              key={k}
              label={limitLabel(k)}
              type="number"
              min={-1}
              step={1}
              value={limits[k]}
              onChange={(e) => setLimits((l) => ({ ...l, [k]: e.target.value }))}
              error={fieldError(`limits.${k}`) ?? fieldError(`limits[${k}]`)}
              hint={<Mono>{k}</Mono>}
              className="tnum"
            />
          ))}
        </div>
        <div className="flex items-end gap-2">
          <Input label="Add a limit key" value={newKey} onChange={(e) => setNewKey(e.target.value)} className="font-mono" placeholder="e.g. team_seats" />
          <Button
            variant="secondary"
            disabled={!/^[a-z0-9_]+$/.test(newKey.trim()) || newKey.trim() in limits}
            onClick={() => {
              setLimits((l) => ({ ...l, [newKey.trim()]: "" }));
              setNewKey("");
            }}
          >
            Add
          </Button>
        </div>
        {error?.field("limits") && <p className="text-xs font-medium text-stop">{error.field("limits")}</p>}
      </fieldset>

      <fieldset className="space-y-2">
        <legend className="text-sm font-semibold">Features</legend>
        <p className="text-xs text-muted">One line each, shown on the pricing page in this order.</p>
        <ol className="space-y-2">
          {features.map((f, i) => (
            <li key={i} className="flex items-center gap-1">
              <span className="tnum w-5 text-right text-xs text-muted">{i + 1}</span>
              <label className="sr-only" htmlFor={`feature-${i}`}>
                Feature {i + 1}
              </label>
              <input
                id={`feature-${i}`}
                value={f}
                onChange={(e) => setFeatures((xs) => xs.map((x, j) => (j === i ? e.target.value : x)))}
                className="block min-h-10 w-full min-w-0 rounded-control border border-line bg-surface px-3 text-base"
              />
              <IconButton label={`Move feature ${i + 1} up`} disabled={i === 0} onClick={() => setFeatures((xs) => move(xs, i, i - 1))}>
                <ArrowUpIcon className="h-4 w-4" />
              </IconButton>
              <IconButton label={`Move feature ${i + 1} down`} disabled={i === features.length - 1} onClick={() => setFeatures((xs) => move(xs, i, i + 1))}>
                <ArrowDownIcon className="h-4 w-4" />
              </IconButton>
              <IconButton label={`Remove feature ${i + 1}`} onClick={() => setFeatures((xs) => (xs.length === 1 ? [""] : xs.filter((_, j) => j !== i)))}>
                <TrashIcon className="h-4 w-4" />
              </IconButton>
            </li>
          ))}
        </ol>
        <Button variant="ghost" onClick={() => setFeatures((xs) => [...xs, ""])}>
          <PlusIcon className="h-4 w-4" /> Add feature
        </Button>
        {error?.field("features") && <p className="text-xs font-medium text-stop">{error.field("features")}</p>}
      </fieldset>

      <FormErrors error={error} fields={[...FIELDS, ...Object.keys(limits).map((k) => `limits.${k}`)]} />
      <div className="flex justify-end gap-2 border-t border-line pt-4">
        <Button type="submit" loading={busy && !confirmPrice}>
          {plan ? "Save plan" : "Create plan"}
        </Button>
      </div>
    </form>

      {plan && (
        <ConfirmDialog
          open={!!confirmPrice}
          onClose={() => setConfirmPrice(null)}
          title={`Change the price of ${plan.name}`}
          customer={plan.activeSubscribers > 0}
          effect={
            confirmPrice && (
              <>
                Changes {productName(plan.product)} {plan.name} from {money(plan.priceMinor, plan.currency)} to {money(confirmPrice.priceMinor, plan.currency)} a{" "}
                {plan.interval.toLowerCase()} for new subscriptions and the next payment of current ones. {num(plan.activeSubscribers)} paying{" "}
                {plan.activeSubscribers === 1 ? "subscriber keeps" : "subscribers keep"} their price until their period ends.
                {plan.compSubscribers > 0 && ` ${num(plan.compSubscribers)} comped ${plan.compSubscribers === 1 ? "business pays" : "businesses pay"} nothing either way.`}
              </>
            )
          }
          confirmLabel="Change price"
          onConfirm={async () => {
            if (!confirmPrice) return;
            await save(confirmPrice);
            setConfirmPrice(null);
          }}
        />
      )}
    </>
  );
}

function move<T>(xs: T[], from: number, to: number): T[] {
  const next = [...xs];
  const [item] = next.splice(from, 1);
  next.splice(to, 0, item);
  return next;
}

function IconButton({ label, onClick, disabled, children }: { label: string; onClick: () => void; disabled?: boolean; children: ReactNode }) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      aria-label={label}
      className={cx("inline-flex h-10 w-9 shrink-0 items-center justify-center rounded-control text-muted hover:bg-concrete disabled:opacity-30")}
    >
      {children}
    </button>
  );
}

