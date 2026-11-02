"use client";

import { DocEditor } from "@/components/doc-editor";
import { PageHeader } from "@/components/ui";

export default function NewInvoicePage() {
  return (
    <div>
      <PageHeader title="New invoice" back="/app/invoices" />
      <DocEditor kind="invoice" />
    </div>
  );
}
