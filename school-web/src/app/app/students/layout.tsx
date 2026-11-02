"use client";

import { usePathname } from "next/navigation";
import type { ReactNode } from "react";
import { useCan } from "@/lib/roles";
import { SubNav } from "@/components/sub-nav";

/** Student tools. A single student's page has its own header, so the tabs show on the list and the tools only. */
export default function StudentsLayout({ children }: { children: ReactNode }) {
  const pathname = usePathname();
  const canManage = useCan("school.manage");
  const canPrint = useCan("idcards.print");
  const tools = ["/app/students", "/app/students/import", "/app/students/invites", "/app/students/id-cards"];
  const items = [
    { href: "/app/students", label: "Students", exact: true },
    ...(canManage ? [{ href: "/app/students/import", label: "Import" }] : []),
    ...(canManage ? [{ href: "/app/students/invites", label: "Parent invites" }] : []),
    ...(canPrint ? [{ href: "/app/students/id-cards", label: "ID cards" }] : []),
  ];
  return (
    <div>
      {tools.includes(pathname) && (
        <>
          <h1 className="slab mb-3 text-2xl font-bold text-ink">Students</h1>
          {items.length > 1 && <SubNav items={items} label="Students" />}
        </>
      )}
      {children}
    </div>
  );
}
