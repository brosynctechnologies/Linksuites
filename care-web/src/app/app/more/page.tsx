"use client";

import { PageHeader } from "@/components/ui";
import { MoreMenu } from "../app-shell";

export default function MorePage() {
  return (
    <div>
      <PageHeader title="More" />
      <MoreMenu />
    </div>
  );
}
