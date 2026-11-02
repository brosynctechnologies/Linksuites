"use client";

import { useParams } from "next/navigation";
import { formatDay } from "@/lib/dates";
import { useApi } from "@/lib/use-api";
import { useQueryParam } from "@/lib/use-debounced";
import type { ExpenseDto } from "@/lib/types";
import { ExpenseEditor } from "@/components/expense-form";
import { ApiErrorView, Notice, PageHeader, PageLoader } from "@/components/ui";

export default function ExpensePage() {
  const { id } = useParams<{ id: string }>();
  const { data, error, reload, setData } = useApi<ExpenseDto>(`/api/expenses/${id}`);
  const receiptFailed = useQueryParam("receipt") === "failed";

  return (
    <div className="max-w-3xl space-y-4">
      <PageHeader
        title={data ? data.vendor || data.description || data.category : "Expense"}
        subtitle={data ? `${data.category}, ${formatDay(data.date)}` : undefined}
        back="/app/expenses"
      />
      {receiptFailed && <Notice tone="warning">The expense was saved, but the receipt didn&apos;t upload. Attach it again below.</Notice>}
      {error && <ApiErrorView error={error} onRetry={reload} plan="Business" />}
      {!data && !error && <PageLoader />}
      {data && <ExpenseEditor key={data.id} existing={data} onChange={setData} />}
    </div>
  );
}
