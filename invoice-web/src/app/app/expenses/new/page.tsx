"use client";

import { useCan } from "@/lib/permissions";
import { usePlanFeature } from "@/lib/use-plan";
import { ExpenseEditor } from "@/components/expense-form";
import { NoAccess, PageHeader, UpgradePrompt } from "@/components/ui";

export default function NewExpensePage() {
  const canEdit = useCan("expenses.edit");
  const hasFeature = usePlanFeature("expenses");

  return (
    <div className="max-w-3xl">
      <PageHeader title="Add expense" back="/app/expenses" />
      {!canEdit ? (
        <NoAccess text="Your role can view expenses but not add them." />
      ) : hasFeature === false ? (
        <UpgradePrompt plan="Business" />
      ) : (
        <ExpenseEditor />
      )}
    </div>
  );
}
