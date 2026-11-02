"use client";

import { DocEditor } from "@/components/doc-editor";
import { PageHeader } from "@/components/ui";

export default function NewQuotePage() {
  return (
    <div>
      <PageHeader title="New quote" back="/app/quotes" />
      <DocEditor kind="quote" />
    </div>
  );
}
