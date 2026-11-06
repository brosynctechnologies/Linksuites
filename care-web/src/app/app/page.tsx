"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { firstName } from "@/lib/auth";
import { useCare } from "@/lib/care-me";
import { plural } from "@/lib/format";
import { canTakeMoney, careRoleLabel } from "@/lib/roles";
import { useApi } from "@/lib/use-api";
import type { CareSetupDto } from "@/lib/types";
import { BedIcon, BillIcon, CalendarIcon, NoteIcon, PatientsIcon, PlusIcon, PriceIcon, QueueIcon, ReportIcon, StaffIcon, WardIcon } from "@/components/icons";
import { ApiErrorView, SearchField, SectionTitle, buttonClass, cx } from "@/components/ui";
import { SetupChecklist } from "./setup/checklist";

/** Home: find a patient first (the front desk's most common job), the setup checklist for leadership until it is done, then shortcuts. */
export default function HomePage() {
  const { me, access } = useCare();
  const router = useRouter();
  const [q, setQ] = useState("");
  const setup = useApi<CareSetupDto>(access.isLeadership ? "/api/care/setup" : null);
  const left = setup.data?.checklist.filter((i) => !i.done) ?? [];

  const shortcuts = [
    { href: "/app/queue", label: "Queue", text: "Today's patients by stage, with check-in.", icon: QueueIcon, show: !access.isCashier },
    { href: "/app/patients", label: "Patients", text: "Search by name, number or phone.", icon: PatientsIcon, show: true },
    { href: "/app/appointments", label: "Appointments", text: "Each doctor's day, booking and weekly hours.", icon: CalendarIcon, show: !access.isCashier },
    { href: "/app/beds", label: "Beds", text: "Every ward's beds, patients waiting for a bed, deposits and transfers.", icon: BedIcon, show: true },
    { href: "/app/admissions", label: "Inpatients", text: "Patients on the wards now and those discharged recently.", icon: WardIcon, show: !access.isCashier },
    { href: "/app/bills", label: "Bills", text: "Take payments, print receipts and close paid visits.", icon: BillIcon, show: canTakeMoney(access) },
    { href: "/app/reports", label: "Reports", text: "Takings, revenue, deposits held and debtors, with CSV.", icon: ReportIcon, show: canTakeMoney(access) },
    { href: "/app/unsigned-notes", label: "Unsigned notes", text: "Drafts waiting for a doctor's signature.", icon: NoteIcon, show: access.isDirector },
    { href: "/app/patients/new", label: "Register a patient", text: "With a check for anyone already registered.", icon: PlusIcon, show: access.canRegister },
    { href: "/app/price-list", label: "Price list", text: "What the clinic charges for.", icon: PriceIcon, show: access.canSeeMoney },
    { href: "/app/staff", label: "Staff", text: "Invite staff and set their clinic roles.", icon: StaffIcon, show: access.isLeadership },
  ].filter((s) => s.show);

  return (
    <div className="space-y-8">
      <div>
        <h1 className="display text-2xl font-bold text-ink sm:text-3xl">Good to see you, {firstName(me.fullName)}</h1>
        <p className="mt-1 text-sm text-muted">{careRoleLabel(me.memberRole, me.clinicalRole)}</p>
      </div>

      <form
        role="search"
        onSubmit={(e) => {
          e.preventDefault();
          router.push(q.trim() ? `/app/patients?q=${encodeURIComponent(q.trim())}` : "/app/patients");
        }}
        className="flex max-w-xl gap-2"
      >
        <SearchField id="home-search" label="Find a patient" placeholder="Name, patient number or phone" value={q} onChange={setQ} className="flex-1" />
        <button type="submit" className={buttonClass("primary")}>
          Find
        </button>
      </form>

      {access.isLeadership && setup.error && <ApiErrorView error={setup.error} onRetry={setup.reload} />}
      {access.isLeadership && setup.data && left.length > 0 && (
        <section aria-labelledby="setup-title">
          <SectionTitle id="setup-title" action={<Link href="/app/setup" className="text-sm font-bold text-scrub hover:underline">See setup</Link>}>
            Set up your clinic: {plural(left.length, "step")} left
          </SectionTitle>
          <SetupChecklist items={setup.data.checklist} />
        </section>
      )}

      <section aria-labelledby="shortcuts">
        <h2 id="shortcuts" className="sr-only">
          Shortcuts
        </h2>
        <ul className="grid gap-3 sm:grid-cols-2">
          {shortcuts.map((s) => (
            <li key={s.href}>
              <Link
                href={s.href}
                className={cx("flex min-h-20 items-start gap-3 rounded-panel border border-rule bg-surface p-4 hover:border-scrub-line hover:bg-scrub-wash/40")}
              >
                <s.icon className="mt-0.5 h-5 w-5 text-scrub" />
                <span>
                  <span className="block font-bold text-ink">{s.label}</span>
                  <span className="block text-sm text-muted">{s.text}</span>
                </span>
              </Link>
            </li>
          ))}
        </ul>
      </section>
    </div>
  );
}
