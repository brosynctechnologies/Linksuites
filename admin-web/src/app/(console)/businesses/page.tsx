"use client";

import Link from "next/link";
import { RequireArea, useAdmin } from "@/components/console";
import { FilterBar, FilterInput, FilterSelect, SearchFilter } from "@/components/filters";
import { EmptyState, ErrorPanel, HiddenValue, Loading, Mono, PageHeader, Pager, Panel, ProductChip, StatusBadge, TableScroll } from "@/components/ui";
import { day } from "@/lib/format";
import { PRODUCTS } from "@/lib/products";
import { can } from "@/lib/roles";
import type { BusinessRow, Paged } from "@/lib/types";
import { qs, useApi } from "@/lib/use-api";
import { useUrlFilters } from "@/lib/use-filters";

const PAGE_SIZE = 25;
const KEYS = ["q", "product", "status", "plan"] as const;

export default function BusinessesPage() {
  return (
    <RequireArea area="businesses">
      <Businesses />
    </RequireArea>
  );
}

function Businesses() {
  const f = useUrlFilters(KEYS);
  const { user } = useAdmin();
  // Roles without contact access (Finance) search by name and slug only; the API ignores email and phone for them.
  const contact = can(user.platformRole, "contact");
  const { q, product, status, plan } = f.values;
  const { data, error, loading, reload } = useApi<Paged<BusinessRow>>(`/api/platform/businesses${qs({ q, product, status, plan, page: f.page, pageSize: PAGE_SIZE })}`);

  return (
    <>
      <PageHeader title="Businesses" subtitle="Every business on every product." />
      <FilterBar onClear={f.clear} active={f.active}>
        <SearchFilter label="Search" placeholder={contact ? "Name, slug, owner email, name or phone" : "Name, slug or owner name"} value={q} onCommit={(v) => f.set({ q: v })} />
        <FilterSelect
          label="Product"
          value={product}
          onChange={(v) => f.set({ product: v })}
          options={[{ value: "", label: "All products" }, ...PRODUCTS.map((p) => ({ value: p.key, label: p.name }))]}
        />
        <FilterSelect
          label="Status"
          value={status}
          onChange={(v) => f.set({ status: v })}
          options={[
            { value: "", label: "Any status" },
            { value: "Active", label: "Active" },
            { value: "Suspended", label: "Suspended" },
            { value: "Closed", label: "Closed" },
          ]}
        />
        <FilterInput label="Plan code" value={plan} onCommit={(v) => f.set({ plan: v })} placeholder="free, pro…" mono className="basis-28" />
      </FilterBar>

      <Panel flush>
        {error && <ErrorPanel error={error} onRetry={reload} className="m-3" />}
        {!data && !error && <Loading />}
        {data && data.items.length === 0 && (
          <EmptyState
            title={f.active ? "No businesses match these filters" : "No businesses yet"}
            text={
              contact
                ? "Search by business name, slug, or the owner's email, name or phone. Plan takes a plan code such as free or pro."
                : "Search by business name, slug or the owner's name. Plan takes a plan code such as free or pro."
            }
          />
        )}
        {data && data.items.length > 0 && (
          <>
            <TableScroll label="Businesses">
              <table className="data-table min-w-[860px]" aria-busy={loading || undefined}>
                <thead>
                  <tr>
                    <th>Name</th>
                    <th>Slug</th>
                    <th>Product</th>
                    <th>Status</th>
                    <th>Plan</th>
                    <th>Owner</th>
                    <th className="num">Created</th>
                  </tr>
                </thead>
                <tbody>
                  {data.items.map((b) => (
                    <tr key={b.id} className="row-link">
                      <td>
                        <Link href={`/businesses/${b.id}`} className="font-medium underline decoration-line underline-offset-2 hover:decoration-graphite">
                          {b.name}
                        </Link>
                      </td>
                      <td>
                        <Mono>{b.slug}</Mono>
                      </td>
                      <td>
                        <ProductChip product={b.product} />
                      </td>
                      <td>
                        <StatusBadge status={b.status} />
                      </td>
                      <td>{b.planName ?? <span className="text-muted">Free</span>}</td>
                      <td className="max-w-64 truncate">{b.ownerEmail ?? (contact ? <span className="text-muted">—</span> : <HiddenValue />)}</td>
                      <td className="num">{day(b.createdAt)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </TableScroll>
            <Pager page={data.page} pageSize={data.pageSize} total={data.total} onPage={(p) => f.set({ page: p })} />
          </>
        )}
      </Panel>
    </>
  );
}
