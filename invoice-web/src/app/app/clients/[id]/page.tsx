"use client";

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useState } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { formatMoney } from "@/lib/money";
import { useApi } from "@/lib/use-api";
import { useCan } from "@/lib/permissions";
import type { BusinessDto, ClientDto, InvoiceSummaryDto, Paged } from "@/lib/types";
import { ClientForm } from "@/components/client-form";
import { InvoiceList } from "@/components/doc-rows";
import { ApiErrorView, Button, Card, EmptyState, Notice, PageHeader, PageLoader, Pager, SectionTitle, SummaryStrip, buttonClass } from "@/components/ui";
import { PlusIcon } from "@/components/icons";

export default function ClientPage() {
  const { id } = useParams<{ id: string }>();
  const router = useRouter();
  const { data: client, error, reload, setData } = useApi<ClientDto>(`/api/clients/${id}`);
  const { data: business } = useApi<BusinessDto>("/api/business");
  const [page, setPage] = useState(1);
  const invoices = useApi<Paged<InvoiceSummaryDto>>(`/api/invoices?clientId=${encodeURIComponent(id)}&page=${page}&pageSize=10`);
  const [editing, setEditing] = useState(false);
  const canEdit = useCan("docs.edit");
  const [saved, setSaved] = useState(false);
  const [busy, setBusy] = useState(false);
  const [actionError, setActionError] = useState<ApiError | null>(null);

  if (!client) {
    return (
      <div>
        <PageHeader title="Client" back="/app/clients" />
        {error ? <ApiErrorView error={error} onRetry={reload} /> : <PageLoader />}
      </div>
    );
  }

  async function remove() {
    if (!client || !window.confirm(`Delete ${client.name}? Their invoices are kept.`)) return;
    setBusy(true);
    setActionError(null);
    try {
      await api(`/api/clients/${client.id}`, { method: "DELETE" });
      router.replace("/app/clients");
    } catch (e) {
      setActionError(toApiError(e));
      setBusy(false);
    }
  }

  const currency = business?.currency ?? "NGN";

  return (
    <div className="space-y-4">
      <PageHeader
        title={client.name}
        back="/app/clients"
        subtitle={client.company ?? undefined}
        action={
          canEdit && (
            <Link href={`/app/invoices/new?clientId=${client.id}`} className={buttonClass("primary")}>
              <PlusIcon className="h-4 w-4" /> New invoice
            </Link>
          )
        }
      />
      {saved && <Notice tone="success">Client saved.</Notice>}

      <SummaryStrip
        cols={2}
        label="Client totals"
        items={[
          { label: "Outstanding", value: formatMoney(client.outstandingMinor, currency) },
          { label: "Invoices", value: String(client.invoiceCount) },
        ]}
      />

      <Card>
        <SectionTitle
          action={
            !editing &&
            canEdit && (
              <Button variant="ghost" className="min-h-8 px-2 py-1" onClick={() => setEditing(true)}>
                Edit
              </Button>
            )
          }
        >
          Details
        </SectionTitle>
        {editing ? (
          <ClientForm
            client={client}
            onCancel={() => setEditing(false)}
            onSaved={(c) => {
              setData(c);
              setEditing(false);
              setSaved(true);
            }}
          />
        ) : (
          <dl className="grid gap-3 text-sm sm:grid-cols-2">
            {(
              [
                ["Email", client.email],
                ["Phone", client.phone],
                ["Address", client.address],
                ["Tax ID", client.taxId],
                ["Notes", client.notes],
              ] as const
            ).map(([k, v]) => (
              <div key={k}>
                <dt className="text-xs text-muted">{k}</dt>
                <dd className="mt-0.5 break-words whitespace-pre-line text-graphite">{v || "—"}</dd>
              </div>
            ))}
          </dl>
        )}
      </Card>

      <section className="pt-2">
        <h2 className="mb-3 font-display text-xl font-[560] text-graphite">Invoices</h2>
        {invoices.error && <ApiErrorView error={invoices.error} onRetry={invoices.reload} />}
        {!invoices.data && !invoices.error && <PageLoader />}
        {invoices.data && invoices.data.items.length === 0 && (
          <EmptyState
            title="No invoices for this client yet"
            text="Their invoices will be listed here once you create one."
            action={
              canEdit && (
                <Link href={`/app/invoices/new?clientId=${client.id}`} className={buttonClass("primary")}>
                  Create invoice
                </Link>
              )
            }
          />
        )}
        {invoices.data && invoices.data.items.length > 0 && (
          <>
            <InvoiceList invoices={invoices.data.items} baseCurrency={business?.currency} busy={invoices.loading} />
            <Pager page={invoices.data.page} pageSize={invoices.data.pageSize || 10} total={invoices.data.total} onPage={setPage} />
          </>
        )}
      </section>

      {canEdit && (
      <div className="border-t border-rule pt-5">
        {actionError && (
          <div className="mb-2">
            <ApiErrorView error={actionError} />
          </div>
        )}
        <Button variant="danger" onClick={remove} loading={busy}>
          Delete client
        </Button>
      </div>
      )}
    </div>
  );
}
