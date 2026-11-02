"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { asList } from "@/lib/api";
import { formatMoney } from "@/lib/money";
import { useApi } from "@/lib/use-api";
import { useCan } from "@/lib/permissions";
import { useDebounced } from "@/lib/use-debounced";
import type { BusinessDto, ClientDto } from "@/lib/types";
import { ClientModal } from "@/components/client-form";
import { ApiErrorView, Button, DataList, EmptyState, PageHeader, PageLoader, SearchField, cx, dataRowClass } from "@/components/ui";
import { ChevronRightIcon, PlusIcon, UsersIcon } from "@/components/icons";

export default function ClientsPage() {
  const router = useRouter();
  const [q, setQ] = useState("");
  const [adding, setAdding] = useState(false);
  const canEdit = useCan("docs.edit");
  const dq = useDebounced(q.trim());
  const { data, error, loading, reload } = useApi<ClientDto[]>(`/api/clients${dq ? `?q=${encodeURIComponent(dq)}` : ""}`);
  const { data: business } = useApi<BusinessDto>("/api/business");
  const clients = asList(data);
  const currency = business?.currency ?? "NGN";

  return (
    <div>
      <PageHeader
        title="Clients"
        action={
          canEdit && (
            <Button onClick={() => setAdding(true)}>
              <PlusIcon className="h-4 w-4" /> Add client
            </Button>
          )
        }
      />
      <SearchField
        id="client-search"
        label="Search clients"
        className="mb-4 max-w-md"
        placeholder="Search by name, company or email"
        value={q}
        onChange={setQ}
      />
      {error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && <PageLoader />}
      {data && clients.length === 0 && (
        <EmptyState
          icon={<UsersIcon width={24} height={24} />}
          title={dq ? "No matching clients" : "No clients yet"}
          text={dq ? "Nobody matches that search. Try part of a name, company or email." : "Add the people and companies you bill. You can also add them while creating an invoice."}
          action={!dq && canEdit ? <Button onClick={() => setAdding(true)}>Add client</Button> : undefined}
        />
      )}
      {clients.length > 0 && (
        <DataList
          busy={loading}
          template="minmax(0,1.2fr) minmax(0,1fr) 6rem minmax(8rem,auto) 1rem"
          head={[{ label: "Client" }, { label: "Contact" }, { label: "Invoices", align: "right" }, { label: "Outstanding", align: "right" }, { label: "" }]}
        >
          {clients.map((c) => (
            <li key={c.id}>
              <Link href={`/app/clients/${c.id}`} className={cx(dataRowClass, "hover:bg-slate-50")}>
                <span className="row-span-2 flex min-w-0 items-center gap-3 md:row-span-1">
                  <span
                    className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-ink-wash font-display text-sm font-semibold text-ink-dark"
                    aria-hidden="true"
                  >
                    {c.name.slice(0, 1).toUpperCase()}
                  </span>
                  <span className="min-w-0">
                    <span className="block truncate font-medium text-graphite">{c.name}</span>
                    <span className="block truncate text-xs text-muted md:hidden">
                      {[c.company, c.email].filter(Boolean).join(", ") || "No contact details"}
                    </span>
                  </span>
                </span>
                <span className="hidden min-w-0 text-sm text-slate-600 md:block">
                  <span className="block truncate">{c.company || c.email || "No contact details"}</span>
                  {c.company && c.email && <span className="block truncate text-xs text-muted">{c.email}</span>}
                </span>
                <span className="hidden text-right text-sm text-slate-600 tabular-nums md:block">{c.invoiceCount}</span>
                <span className={cx("amount text-right text-sm font-semibold", c.outstandingMinor > 0 ? "text-graphite" : "text-muted")}>
                  {formatMoney(c.outstandingMinor, currency)}
                </span>
                <span className="col-start-2 text-right text-xs text-muted md:hidden">
                  {c.invoiceCount} invoice{c.invoiceCount === 1 ? "" : "s"}
                </span>
                <ChevronRightIcon className="hidden h-4 w-4 text-faint md:block" />
              </Link>
            </li>
          ))}
        </DataList>
      )}
      <ClientModal open={adding} initialName={q.trim()} onClose={() => setAdding(false)} onSaved={(c) => router.push(`/app/clients/${c.id}`)} />
    </div>
  );
}
