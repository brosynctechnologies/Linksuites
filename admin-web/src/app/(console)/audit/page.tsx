"use client";

import { AuditTable } from "@/components/audit-table";
import { RequireArea } from "@/components/console";
import { FilterBar, FilterInput, FilterSelect } from "@/components/filters";
import { EmptyState, ErrorPanel, Loading, PageHeader, Pager, Panel } from "@/components/ui";
import { lagosDayBound } from "@/lib/format";
import { PRODUCTS } from "@/lib/products";
import type { AuditRowDto, Paged } from "@/lib/types";
import { qs, useApi } from "@/lib/use-api";
import { useUrlFilters } from "@/lib/use-filters";

const PAGE_SIZE = 50;
const KEYS = ["action", "product", "entityType", "tenantId", "actorId", "from", "to"] as const;
const ACTION_PREFIXES = [
  { value: "", label: "Any action" },
  { value: "admin.", label: "admin. (sign-ins, admins)" },
  { value: "business.", label: "business. (suspend, comp)" },
  { value: "support.", label: "support. (view as)" },
  { value: "plan.", label: "plan." },
  { value: "user.", label: "user. (unlock, codes)" },
  { value: "added", label: "added (customer data)" },
  { value: "modified", label: "modified (customer data)" },
  { value: "deleted", label: "deleted (customer data)" },
];

export default function AuditPage() {
  return (
    <RequireArea area="audit">
      <Audit />
    </RequireArea>
  );
}

function Audit() {
  const f = useUrlFilters(KEYS);
  const { action, product, entityType, tenantId, actorId, from, to } = f.values;
  const known = ACTION_PREFIXES.some((p) => p.value === action);
  const { data, error, loading, reload } = useApi<Paged<AuditRowDto>>(
    `/api/platform/audit${qs({
      tenantId,
      product,
      actorId,
      entityType,
      action,
      from: from ? lagosDayBound(from, "start") : "",
      to: to ? lagosDayBound(to, "end") : "",
      page: f.page,
      pageSize: PAGE_SIZE,
    })}`,
  );

  return (
    <>
      <PageHeader title="Audit log" subtitle="Every admin action and every change to audited customer data, newest first. Expand a row to compare before and after." />
      <FilterBar onClear={f.clear} active={f.active}>
        <FilterSelect
          label="Action"
          value={action}
          onChange={(v) => f.set({ action: v })}
          options={known ? ACTION_PREFIXES : [...ACTION_PREFIXES, { value: action, label: action }]}
          className="basis-52"
        />
        <FilterSelect
          label="Product"
          value={product}
          onChange={(v) => f.set({ product: v })}
          options={[{ value: "", label: "All products" }, ...PRODUCTS.map((p) => ({ value: p.key, label: p.name }))]}
        />
        <FilterInput label="Entity type" value={entityType} onCommit={(v) => f.set({ entityType: v })} placeholder="e.g. Plan" className="basis-32" />
        <FilterInput label="Business id" value={tenantId} onCommit={(v) => f.set({ tenantId: v })} mono className="basis-56" />
        <FilterInput label="Actor id" value={actorId} onCommit={(v) => f.set({ actorId: v })} mono className="basis-56" />
        <FilterInput label="From" type="date" value={from} onCommit={(v) => f.set({ from: v })} />
        <FilterInput label="To" type="date" value={to} onCommit={(v) => f.set({ to: v })} />
      </FilterBar>
      <Panel flush>
        {error && <ErrorPanel error={error} onRetry={reload} className="m-3" />}
        {!data && !error && <Loading />}
        {data && data.items.length === 0 && (
          <EmptyState
            title={f.active ? "No entries match these filters" : "No audit entries yet"}
            text="Filter by an action prefix such as business. or plan., a product, an entity type, a business or actor id, or a date range in Lagos time."
          />
        )}
        {data && data.items.length > 0 && (
          <div aria-busy={loading || undefined}>
            <AuditTable rows={data.items} />
            <Pager page={data.page} pageSize={data.pageSize} total={data.total} onPage={(p) => f.set({ page: p })} />
          </div>
        )}
      </Panel>
    </>
  );
}
