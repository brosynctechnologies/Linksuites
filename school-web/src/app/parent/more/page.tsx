"use client";

import { PageHeader } from "@/components/ui";
import { ParentMoreMenu } from "../parent-shell";

export default function ParentMorePage() {
  return (
    <div>
      <PageHeader title="More" />
      <ParentMoreMenu />
    </div>
  );
}
