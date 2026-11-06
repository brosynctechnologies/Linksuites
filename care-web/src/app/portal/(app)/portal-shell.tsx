"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import type { ComponentType, ReactNode, SVGProps } from "react";
import { AccountIcon, BillIcon, CalendarIcon, HomeIcon, LogoutIcon, MoreIcon, NoteIcon, ShieldIcon } from "@/components/icons";
import { BellIcon } from "@/components/notification-bell";
import { PortalNotificationBell } from "@/components/portal-bell";
import { Wordmark } from "@/components/site-chrome";
import { EmptyState, ErrorBox, PageLoader, Wristband, cx, panelClass } from "@/components/ui";
import { logout } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { forgetPortalPatient, isPortalUnavailable, portalHref, relationshipLabel, usePortal } from "@/lib/portal";

interface NavItem {
  href: string;
  label: string;
  icon: ComponentType<SVGProps<SVGSVGElement>>;
  exact?: boolean;
}

const NAV: NavItem[] = [
  { href: "/portal", label: "Home", icon: HomeIcon, exact: true },
  { href: "/portal/record", label: "Record", icon: NoteIcon },
  { href: "/portal/appointments", label: "Appointments", icon: CalendarIcon },
  { href: "/portal/bills", label: "Bills", icon: BillIcon },
  { href: "/portal/access-log", label: "Who opened it", icon: ShieldIcon },
  { href: "/portal/contact", label: "Contact details", icon: AccountIcon },
  { href: "/portal/notifications", label: "Notifications", icon: BellIcon },
];
/** On phones the tab bar holds these four and More. */
const BOTTOM = ["/portal", "/portal/record", "/portal/appointments", "/portal/bills"];

function isActive(pathname: string, item: NavItem) {
  if (item.href === "/portal/record" && pathname === "/portal/results") return true;
  return item.exact ? pathname === item.href : pathname === item.href || pathname.startsWith(item.href + "/");
}

export function usePortalSignOut() {
  const router = useRouter();
  return async () => {
    await logout();
    forgetPortalPatient();
    router.replace("/portal/sign-in");
  };
}

/**
 * The portal's frame: a plain white header (the patient-facing side is calm: no theatre colours), the patient switcher, the bell and
 * the page tabs. Everything below the header is keyed by the chosen patient, so switching never shows one patient's data under another.
 */
export function PortalShell({ children }: { children: ReactNode }) {
  const pathname = usePathname();
  const portal = usePortal();
  const signOut = usePortalSignOut();
  const id = portal.patient?.id;
  const bottom = NAV.filter((n) => BOTTOM.includes(n.href));
  const moreActive = pathname === "/portal/more" || NAV.filter((n) => !BOTTOM.includes(n.href)).some((n) => isActive(pathname, n));
  // The payment return verifies by reference alone, so it opens even while the list is loading or empty.
  const ungated = pathname.startsWith("/portal/pay/");

  return (
    <div className="min-h-screen bg-gauze">
      <header className="sticky top-0 z-30 border-b border-rule bg-surface">
        <div className="mx-auto flex max-w-4xl items-center gap-2 px-4 py-2 sm:px-6">
          <Link href={portalHref("/portal", id)} className="flex min-h-11 shrink-0 items-center rounded-control" aria-label="Patient portal home">
            <Wordmark className="text-lg" />
          </Link>
          <div className="ml-auto flex min-w-0 items-center gap-1">
            <PatientSwitcher />
            {id && <PortalNotificationBell key={id} />}
            <button
              type="button"
              onClick={signOut}
              className="hidden min-h-11 items-center gap-2 rounded-control px-3 text-sm font-bold text-slate-700 hover:bg-ink/[0.06] hover:text-ink sm:flex"
            >
              <LogoutIcon className="h-[18px] w-[18px]" /> Sign out
            </button>
          </div>
        </div>
        <nav className="no-scrollbar mx-auto hidden max-w-4xl gap-1 overflow-x-auto px-4 sm:flex sm:px-6" aria-label="Portal">
          {NAV.map((n) => {
            const active = isActive(pathname, n);
            return (
              <Link
                key={n.href}
                href={portalHref(n.href, id)}
                aria-current={active ? "page" : undefined}
                className={cx(
                  "relative min-h-11 shrink-0 px-3 pt-2 pb-2.5 text-sm font-medium whitespace-nowrap",
                  active ? "text-ink" : "text-muted hover:text-ink",
                )}
              >
                {n.label}
                <span aria-hidden="true" className={cx("absolute inset-x-2 -bottom-px h-[3px] rounded-full", active ? "bg-scrub" : "bg-transparent")} />
              </Link>
            );
          })}
        </nav>
      </header>

      <main className="mx-auto w-full max-w-4xl px-4 pt-5 pb-28 sm:px-6 sm:pt-8 sm:pb-12">
        {ungated ? children : <PatientGate>{children}</PatientGate>}
      </main>

      <nav
        className="fixed inset-x-0 bottom-0 z-30 grid grid-cols-5 border-t border-rule bg-surface pb-[env(safe-area-inset-bottom)] sm:hidden"
        aria-label="Portal"
      >
        {bottom.map((n) => {
          const active = isActive(pathname, n);
          return (
            <Link
              key={n.href}
              href={portalHref(n.href, id)}
              aria-current={active ? "page" : undefined}
              className={cx("flex min-h-14 flex-col items-center justify-center gap-0.5 text-xs font-bold", active ? "text-scrub" : "text-muted")}
            >
              <n.icon className="h-5 w-5" />
              {n.label}
            </Link>
          );
        })}
        <Link
          href={portalHref("/portal/more", id)}
          aria-current={moreActive ? "page" : undefined}
          className={cx("flex min-h-14 flex-col items-center justify-center gap-0.5 text-xs font-bold", moreActive ? "text-scrub" : "text-muted")}
        >
          <MoreIcon className="h-5 w-5" />
          More
        </Link>
      </nav>
    </div>
  );
}

/** The linked patients, across clinics: name, and the clinic when there is more than one. */
function PatientSwitcher() {
  const { list, patient, select, blockedId } = usePortal();
  if (list.length === 0) return null;
  if (list.length === 1 && !blockedId) {
    return <span className="hidden truncate px-2 text-sm font-semibold text-ink sm:inline">{list[0].fullName}</span>;
  }
  const manyClinics = new Set(list.map((p) => p.clinicId)).size > 1;
  return (
    <label className="block min-w-0">
      <span className="sr-only">Patient</span>
      <select
        value={patient?.id ?? ""}
        onChange={(e) => select(e.target.value)}
        className="max-w-[12rem] min-h-11 truncate rounded-control border border-rule bg-surface px-2 py-2 text-sm font-semibold text-ink sm:max-w-xs"
      >
        {!patient && <option value="">Choose a patient</option>}
        {list.map((p) => (
          <option key={p.id} value={p.id}>
            {p.fullName}
            {manyClinics ? `, ${p.clinicName}` : ""}
          </option>
        ))}
      </select>
    </label>
  );
}

/** Pages need a chosen patient. A suspended clinic shows its own sentence; an unlinked id shows plainly; an empty account says how to join. */
function PatientGate({ children }: { children: ReactNode }) {
  const portal = usePortal();
  const auth = useAuth();
  if (portal.loading) return <PageLoader />;
  if (portal.error) return <ErrorBox error={portal.error} onRetry={portal.reload} />;
  if (portal.blockedId) {
    if (portal.blockedOk) return <ErrorBox error="This patient was added to your account a moment ago." onRetry={portal.reload} />;
    if (!portal.blockedError) return <PageLoader />;
    const unavailable = isPortalUnavailable(portal.blockedError);
    return (
      <div className={cx(panelClass, "p-5")} role="alert">
        <h1 className="display text-xl font-bold text-ink">
          {unavailable ? portal.blockedError.message : portal.blockedError.status === 404 ? "This patient isn't on your account" : "This page can't open"}
        </h1>
        <p className="mt-2 max-w-prose text-sm text-slate-700">
          {unavailable
            ? "The clinic can tell you when it will open again. You can still call them."
            : portal.blockedError.status === 404
              ? "The link may be old, or the clinic may have removed it. Choose a patient above, or ask the clinic."
              : portal.blockedError.message}
        </p>
        {portal.list.length > 0 && (
          <p className="mt-3 text-sm text-muted">Use the patient list at the top to open someone else.</p>
        )}
      </div>
    );
  }
  if (!portal.patient) {
    return (
      <EmptyState
        title="No patients on this account yet"
        text={
          <>
            Ask your clinic to send you a portal invitation. Once you accept it, the patient will show here. You&apos;re signed in as{" "}
            <strong>{auth?.user.email}</strong>.
          </>
        }
      />
    );
  }
  return (
    <div key={portal.patient.id}>
      <PatientBand />
      {children}
    </div>
  );
}

/** Who and where, above every page: the name, the wristband number and the clinic. */
function PatientBand() {
  const { patient } = usePortal();
  if (!patient) return null;
  return (
    <p className="mb-4 flex flex-wrap items-center gap-x-2 gap-y-1 text-sm text-muted">
      <Wristband number={patient.number} />
      <span className="font-semibold text-ink">{patient.fullName}</span>
      <span aria-hidden="true">·</span>
      <span>{patient.clinicName}</span>
      <span aria-hidden="true">·</span>
      <span>{relationshipLabel(patient.relationship)}</span>
      {patient.status === "Inactive" && <span className="text-iodine">· Marked inactive at the clinic</span>}
      {patient.status === "Deceased" && <span>· Recorded as deceased</span>}
    </p>
  );
}
