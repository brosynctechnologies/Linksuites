"use client";

import { useParams } from "next/navigation";
import { useApi } from "@/lib/use-api";
import type { InvoiceDto } from "@/lib/types";
import { DocEditor } from "@/components/doc-editor";
import { ApiErrorView, Notice, PageHeader, PageLoader } from "@/components/ui";

export default function EditInvoicePage() {
  const { id } = useParams<{ id: string }>();
  const { data, error, reload } = useApi<InvoiceDto>(`/api/invoices/${id}`);

  return (
    <div>
      <PageHeader title={data ? `Edit ${data.number}` : "Edit invoice"} back={`/app/invoices/${id}`} />
      {error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && <PageLoader />}
      {data && data.status === "Void" && <Notice tone="warning">Void invoices can&apos;t be edited. Duplicate it instead.</Notice>}
      {data && data.status !== "Void" && <DocEditor key={data.id} kind="invoice" existing={data} />}
    </div>
  );
}
