"use client";

import { useParams } from "next/navigation";
import { useApi } from "@/lib/use-api";
import type { QuoteDto } from "@/lib/types";
import { DocEditor } from "@/components/doc-editor";
import { ApiErrorView, Notice, PageHeader, PageLoader } from "@/components/ui";

export default function EditQuotePage() {
  const { id } = useParams<{ id: string }>();
  const { data, error, reload } = useApi<QuoteDto>(`/api/quotes/${id}`);

  return (
    <div>
      <PageHeader title={data ? `Edit ${data.number}` : "Edit quote"} back={`/app/quotes/${id}`} />
      {error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && <PageLoader />}
      {data && data.status === "Converted" && <Notice tone="warning">This quote was converted to an invoice and can&apos;t be edited.</Notice>}
      {data && data.status !== "Converted" && <DocEditor key={data.id} kind="quote" existing={data} />}
    </div>
  );
}
