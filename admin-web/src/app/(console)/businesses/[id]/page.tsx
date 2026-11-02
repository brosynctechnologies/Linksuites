"use client";

import Link from "next/link";
import { useParams, usePathname, useRouter, useSearchParams } from "next/navigation";
import { useState } from "react";
import { AuditTable } from "@/components/audit-table";
import { ConfirmDialog } from "@/components/confirm";
import { RequireArea, useAdmin } from "@/components/console";
import { ChevronLeftIcon, ExternalIcon, EyeIcon, LayersIcon, LockIcon, UnlockIcon } from "@/components/icons";
import { useToast } from "@/components/toast";
import {
  Button,
  EmptyState,
  ErrorPanel,
  Facts,
  HiddenValue,
  Input,
  Loading,
  Mono,
  PageHeader,
  Panel,
  ProductChip,
  Select,
  StatusBadge,
  TableScroll,
  Tabs,
  cx,
} from "@/components/ui";
import { api, toApiError, type ApiError } from "@/lib/api";
import { day, lagosDayBound, money, num, plural, possessive, stamp, todayLagos } from "@/lib/format";
import { productName } from "@/lib/products";
import { can } from "@/lib/roles";
import type { BusinessDetailDto, PlanDto, ViewAsResponse } from "@/lib/types";
import { useApi } from "@/lib/use-api";
import { useNow } from "@/lib/use-now";
import { addSupportView, removeSupportView, useSupportViews, type SupportView } from "@/lib/view-as";

type Tab = "overview" | "members" | "payments" | "payouts" | "audit";
const TABS: Tab[] = ["overview", "members", "payments", "payouts", "audit"];
type Action = "suspend" | "reactivate" | "view-as" | "comp" | null;

export default function BusinessPage() {
  return (
    <RequireArea area="businesses">
      <Business />
    </RequireArea>
  );
}

function Business() {
  const { id } = useParams<{ id: string }>();
  const { user } = useAdmin();
  const toast = useToast();
  const sp = useSearchParams();
  const pathname = usePathname();
  const router = useRouter();
  const tabParam = sp.get("tab") as Tab | null;
  const tab: Tab = tabParam && TABS.includes(tabParam) ? tabParam : "overview";
  const { data: b, error, reload } = useApi<BusinessDetailDto>(`/api/platform/businesses/${id}`);
  const [action, setAction] = useState<Action>(null);
  const views = useSupportViews();
  const now = useNow(15_000);
  const activeViews = views.filter((v) => v.businessId === id && v.adminId === user.id && now > 0 && new Date(v.expiresAt).getTime() > now);

  const canSupport = can(user.platformRole, "business.support");
  const canComp = can(user.platformRole, "business.comp");

  if (!b) {
    return (
      <>
        <BackLink />
        {error ? <ErrorPanel error={error} onRetry={reload} /> : <Loading />}
      </>
    );
  }

  const memberCount = b.members.length;
  const membersText = plural(memberCount, "member");
  const setTab = (t: Tab) => router.replace(t === "overview" ? pathname : `${pathname}?tab=${t}`, { scroll: false });

  return (
    <>
      <BackLink />
      <PageHeader
        title={b.name}
        action={
          <>
            {canSupport && (
              <Button variant="secondary" onClick={() => setAction("view-as")}>
                <EyeIcon className="h-4 w-4" /> View as
              </Button>
            )}
            {canComp && (
              <Button variant="secondary" onClick={() => setAction("comp")}>
                <LayersIcon className="h-4 w-4" /> Comp plan
              </Button>
            )}
            {canSupport && b.status === "Active" && (
              <Button variant="danger" onClick={() => setAction("suspend")}>
                <LockIcon className="h-4 w-4" /> Suspend
              </Button>
            )}
            {canSupport && b.status === "Suspended" && (
              <Button onClick={() => setAction("reactivate")}>
                <UnlockIcon className="h-4 w-4" /> Reactivate
              </Button>
            )}
          </>
        }
      >
        <div className="mt-2 flex flex-wrap items-center gap-2 text-sm">
          <ProductChip product={b.product} />
          <StatusBadge status={b.status} />
          <span className="text-muted">Plan:</span>
          <span className="font-medium">{b.planName ?? "Free"}</span>
          {b.subscription?.provider === "comp" && <StatusBadge tone="wait">Comped</StatusBadge>}
          <Mono className="text-muted">{b.slug}</Mono>
        </div>
      </PageHeader>

      {activeViews.map((v) => (
        <SupportViewNotice key={v.sessionId} view={v} />
      ))}

      <div className="mb-4">
        <Tabs<Tab>
          label="Business sections"
          value={tab}
          onChange={setTab}
          items={[
            { value: "overview", label: "Overview" },
            { value: "members", label: `Members (${memberCount})` },
            { value: "payments", label: "Payments" },
            { value: "payouts", label: "Payouts" },
            { value: "audit", label: "Audit" },
          ]}
        />
      </div>

      {tab === "overview" && <OverviewTab b={b} />}
      {tab === "members" && <MembersTab b={b} />}
      {tab === "payments" && <PaymentsTab b={b} />}
      {tab === "payouts" && <PayoutsTab b={b} />}
      {tab === "audit" && (
        <Panel
          title="Recent activity"
          flush
          action={
            can(user.platformRole, "audit") && (
              <Link href={`/audit?tenantId=${b.id}`} className="text-sm font-medium underline decoration-line underline-offset-2 hover:decoration-graphite">
                Full audit log
              </Link>
            )
          }
        >
          {b.recentAudit.length === 0 ? <EmptyState title="No audit entries yet" /> : <AuditTable rows={b.recentAudit} showBusiness={false} />}
        </Panel>
      )}

      <ConfirmDialog
        open={action === "suspend"}
        onClose={() => setAction(null)}
        title={`Suspend ${b.name}`}
        effect={
          <>
            Hides {possessive(b.name)} public pages in every product and signs its {membersText} out at their next request. The owner is emailed.
          </>
        }
        confirmLabel="Suspend business"
        danger
        reason
        reasonHint="The owner sees this reason. It is also saved in the audit log with your name."
        onConfirm={async (reason) => {
          await api(`/api/platform/businesses/${b.id}/suspend`, { method: "POST", body: { reason } });
          setAction(null);
          toast("Business suspended");
          reload();
        }}
      />
      <ConfirmDialog
        open={action === "reactivate"}
        onClose={() => setAction(null)}
        title={`Reactivate ${b.name}`}
        effect={
          <>
            Shows {possessive(b.name)} public pages again and lets its {membersText} sign in.
          </>
        }
        confirmLabel="Reactivate business"
        reason
        onConfirm={async (reason) => {
          await api(`/api/platform/businesses/${b.id}/reactivate`, { method: "POST", body: { reason } });
          setAction(null);
          toast("Business reactivated");
          reload();
        }}
      />
      <ConfirmDialog
        open={action === "view-as"}
        onClose={() => setAction(null)}
        title={`View as ${b.name}`}
        effect={
          <>
            Opens {b.name} in {productName(b.product)} in a new tab, read only, for 30 minutes. The business sees this view in its account activity.
          </>
        }
        confirmLabel="View as business"
        reason
        reasonHint="The business sees this reason in its account activity."
        onConfirm={async (reason) => {
          const res = await api<ViewAsResponse>(`/api/platform/businesses/${b.id}/view-as`, { method: "POST", body: { reason } });
          window.open(res.webUrl, "_blank", "noopener");
          addSupportView({ sessionId: res.sessionId, adminId: user.id, businessId: b.id, url: res.webUrl, expiresAt: res.expiresAt });
          setAction(null);
          toast("Support view opened");
        }}
      />
      {action === "comp" && <CompDialog b={b} onClose={() => setAction(null)} onDone={reload} />}
    </>
  );
}

/** An open support view: link to reopen it, and End view to close it on the server before its 30 minutes are up. */
function SupportViewNotice({ view }: { view: SupportView }) {
  const toast = useToast();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function end() {
    setBusy(true);
    setError(null);
    try {
      await api(`/api/platform/view-as/${view.sessionId}/end`, { method: "POST" });
      removeSupportView(view.sessionId);
      toast("Support view ended");
    } catch (err) {
      const e = toApiError(err);
      // Already gone on the server (ended elsewhere or ran out): nothing left to end.
      if (e.status === 404) {
        removeSupportView(view.sessionId);
        return;
      }
      setError(e);
      setBusy(false);
    }
  }

  return (
    <div className="mb-4 rounded-panel border border-line bg-surface px-4 py-3 text-sm" role="status">
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
        <p className="min-w-0 flex-1">
          Support view open until {stamp(view.expiresAt)}. If the tab did not open,{" "}
          <a href={view.url} target="_blank" rel="noopener noreferrer" className="font-medium underline underline-offset-2">
            open the support view
          </a>
          .
        </p>
        <Button variant="secondary" onClick={end} loading={busy}>
          End view
        </Button>
      </div>
      {error && <ErrorPanel error={error} className="mt-3" />}
    </div>
  );
}

function BackLink() {
  return (
    <Link href="/businesses" className="mb-2 inline-flex min-h-10 items-center gap-1 text-sm text-muted hover:text-graphite">
      <ChevronLeftIcon className="h-4 w-4" /> Businesses
    </Link>
  );
}

function OverviewTab({ b }: { b: BusinessDetailDto }) {
  const s = b.subscription;
  return (
    <div className="grid gap-4 lg:grid-cols-2">
      <Panel title="Usage">
        {b.usage.length === 0 ? (
          <p className="text-sm text-muted">No usage reported for this product.</p>
        ) : (
          <ul className="space-y-3">
            {b.usage.map((u) => (
              <UsageBar key={u.key} label={u.label} used={u.used} limit={u.limit} />
            ))}
          </ul>
        )}
      </Panel>
      {/* The current subscription by the shared rule (running now, latest period end first): the same one behind the header and the list. */}
      <Panel title="Current subscription">
        {s ? (
          <Facts
            items={[
              { label: "Plan", value: `${s.planName} (${s.planCode})` },
              { label: "Price", value: <span className="tnum">{money(s.priceMinor, b.currency)}</span> },
              { label: "Status", value: <StatusBadge status={s.status} /> },
              { label: "Provider", value: s.provider === "comp" ? "Comp (free)" : s.provider },
              { label: "Period", value: s.currentPeriodEnd ? `${day(s.currentPeriodStart)} to ${day(s.currentPeriodEnd)} (ends then; nothing renews automatically)` : `From ${day(s.currentPeriodStart)}, no end date` },
              { label: "Subscription id", value: <Mono>{s.id}</Mono> },
            ]}
          />
        ) : (
          <p className="text-sm text-muted">No subscription is running now, so the business is on the free plan. Ended periods are under Payments.</p>
        )}
      </Panel>
      <Panel title="Business">
        <Facts
          items={[
            { label: "Product", value: productName(b.product) },
            { label: "Country", value: b.country },
            { label: "Currency", value: b.currency },
            { label: "Created", value: stamp(b.createdAt) },
            { label: "Business id", value: <Mono>{b.id}</Mono> },
            {
              label: "Public link",
              value: b.webUrl ? (
                <a href={b.webUrl} target="_blank" rel="noopener noreferrer" className="inline-flex items-center gap-1 font-medium underline decoration-line underline-offset-2 hover:decoration-graphite">
                  <Mono>{b.webUrl}</Mono> <ExternalIcon className="h-3.5 w-3.5" />
                </a>
              ) : (
                <span className="text-muted">None</span>
              ),
            },
          ]}
        />
      </Panel>
    </div>
  );
}

function UsageBar({ label, used, limit }: { label: string; used: number; limit: number }) {
  const unlimited = limit < 0;
  const pct = unlimited ? 0 : limit === 0 ? (used > 0 ? 100 : 0) : Math.min(100, (used / limit) * 100);
  const tone = !unlimited && pct >= 100 ? "bg-stop" : !unlimited && pct >= 80 ? "bg-wait" : "bg-graphite";
  return (
    <li>
      <div className="flex justify-between gap-2 text-sm">
        <span>{label}</span>
        <span className="tnum text-muted">
          {num(used)} / {unlimited ? "Unlimited" : num(limit)}
        </span>
      </div>
      <div
        className="mt-1 h-2 overflow-hidden rounded-full bg-concrete"
        role="meter"
        aria-label={label}
        aria-valuemin={0}
        aria-valuemax={unlimited ? undefined : limit}
        aria-valuenow={used}
        aria-valuetext={unlimited ? `${used} of unlimited` : `${used} of ${limit}`}
      >
        {!unlimited && <div className={cx("h-full rounded-full", tone)} style={{ width: `${pct}%` }} />}
      </div>
    </li>
  );
}

function MembersTab({ b }: { b: BusinessDetailDto }) {
  const { user } = useAdmin();
  const linkUsers = can(user.platformRole, "users");
  return (
    <Panel flush>
      {b.members.length === 0 ? (
        <EmptyState title="No members" />
      ) : (
        <TableScroll label="Members">
          <table className="data-table min-w-[640px]">
            <thead>
              <tr>
                <th>Name</th>
                <th>Email</th>
                <th>Role</th>
                <th>Status</th>
                <th className="num">Last sign-in</th>
              </tr>
            </thead>
            <tbody>
              {b.members.map((m) => (
                <tr key={m.userId}>
                  <td>
                    {linkUsers ? (
                      <Link href={`/users/${m.userId}`} className="font-medium underline decoration-line underline-offset-2 hover:decoration-graphite">
                        {m.name}
                      </Link>
                    ) : (
                      <span className="font-medium">{m.name}</span>
                    )}
                  </td>
                  <td>{m.email ?? <HiddenValue />}</td>
                  <td>{m.role}</td>
                  <td>
                    <StatusBadge status={m.status} />
                  </td>
                  <td className="num">{stamp(m.lastLoginAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </TableScroll>
      )}
    </Panel>
  );
}

function PaymentsTab({ b }: { b: BusinessDetailDto }) {
  return (
    <Panel flush>
      {b.payments.length === 0 ? (
        <EmptyState title="No payments" text="Subscription payments for this business appear here." />
      ) : (
        <TableScroll label="Payments">
          <table className="data-table min-w-[600px]">
            <thead>
              <tr>
                <th>Date</th>
                <th>Reference</th>
                <th>Status</th>
                <th className="num">Amount</th>
              </tr>
            </thead>
            <tbody>
              {b.payments.map((p) => (
                <tr key={p.id}>
                  <td className="tnum">{stamp(p.createdAt)}</td>
                  <td>{p.reference ? <Mono>{p.reference}</Mono> : "—"}</td>
                  <td>
                    <StatusBadge status={p.status} />
                  </td>
                  <td className="num">{money(p.amountMinor, p.currency)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </TableScroll>
      )}
    </Panel>
  );
}

function PayoutsTab({ b }: { b: BusinessDetailDto }) {
  return (
    <Panel flush>
      {b.payouts.length === 0 ? (
        <EmptyState title="No payouts" text="Course and short-let payouts for this business appear here." />
      ) : (
        <TableScroll label="Payouts">
          <table className="data-table min-w-[640px]">
            <thead>
              <tr>
                <th>Requested</th>
                <th>Status</th>
                <th>Reference</th>
                <th>Paid</th>
                <th className="num">Amount</th>
              </tr>
            </thead>
            <tbody>
              {b.payouts.map((p) => (
                <tr key={p.id}>
                  <td className="tnum">{stamp(p.createdAt)}</td>
                  <td>
                    <StatusBadge status={p.status} />
                  </td>
                  <td>{p.reference ? <Mono>{p.reference}</Mono> : "—"}</td>
                  <td className="tnum">{stamp(p.paidAt)}</td>
                  <td className="num">{money(p.amountMinor, b.currency)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </TableScroll>
      )}
    </Panel>
  );
}

/** Comp: give a plan free until a future date. The plans list is Finance-only, like this action. */
function CompDialog({ b, onClose, onDone }: { b: BusinessDetailDto; onClose: () => void; onDone: () => void }) {
  const toast = useToast();
  const { data: plans, error } = useApi<PlanDto[]>("/api/platform/plans");
  const options = (plans ?? []).filter((p) => p.product === b.product && p.priceMinor > 0);
  const [planCode, setPlanCode] = useState("");
  const [until, setUntil] = useState("");
  const chosen = options.find((p) => p.code === planCode);
  const minDate = todayLagos();

  return (
    <ConfirmDialog
      open
      onClose={onClose}
      title={`Comp a plan for ${b.name}`}
      effect={
        chosen && until ? (
          <>
            Gives {b.name} the {chosen.name} plan free until {day(lagosDayBound(until, "end"))}. No payment is taken; it doesn&apos;t count towards revenue.
          </>
        ) : (
          <>Gives {b.name} a paid plan free until a date you choose. No payment is taken.</>
        )
      }
      confirmLabel="Comp plan"
      reason
      fields={["planCode", "until"]}
      canConfirm={!!planCode && !!until}
      onConfirm={async (reason) => {
        await api(`/api/platform/businesses/${b.id}/comp`, { method: "POST", body: { planCode, until: lagosDayBound(until, "end"), reason } });
        onClose();
        toast("Plan comped");
        onDone();
      }}
    >
      {(err) => (
        <div className="grid gap-3 sm:grid-cols-2">
          {error && <ErrorPanel error={error} className="sm:col-span-2" />}
          <Select label="Plan" value={planCode} onChange={(e) => setPlanCode(e.target.value)} error={err?.field("planCode")} disabled={!plans}>
            <option value="">{plans ? "Choose a plan" : "Loading plans…"}</option>
            {options.map((p) => (
              <option key={p.id} value={p.code}>
                {p.name} ({money(p.priceMinor, p.currency)} a {p.interval.toLowerCase()})
              </option>
            ))}
          </Select>
          <Input label="Free until" type="date" min={minDate} value={until} onChange={(e) => setUntil(e.target.value)} error={err?.field("until")} hint="End of that day, Lagos time." />
        </div>
      )}
    </ConfirmDialog>
  );
}
