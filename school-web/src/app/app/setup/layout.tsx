import { SubNav } from "@/components/sub-nav";

const ITEMS = [
  { href: "/app/setup", label: "Overview", exact: true },
  { href: "/app/setup/levels", label: "Levels" },
  { href: "/app/setup/classes", label: "Classes" },
  { href: "/app/setup/subjects", label: "Subjects" },
  { href: "/app/setup/assessment", label: "Assessment" },
  { href: "/app/setup/grading", label: "Grading" },
  { href: "/app/setup/traits", label: "Traits" },
  { href: "/app/setup/developmental", label: "Developmental" },
  { href: "/app/setup/comment-bank", label: "Comment bank" },
];

export default function SetupLayout({ children }: LayoutProps<"/app/setup">) {
  return (
    <div>
      <h1 className="slab mb-3 text-2xl font-bold text-ink">Setup</h1>
      <SubNav items={ITEMS} label="Setup" />
      {children}
    </div>
  );
}
