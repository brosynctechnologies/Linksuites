"use client";

import { PageHeader } from "@/components/ui";
import { StudentMoreMenu } from "../student-shell";

export default function StudentMorePage() {
  return (
    <div>
      <PageHeader title="More" />
      <StudentMoreMenu />
    </div>
  );
}
