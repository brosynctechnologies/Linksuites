import { can, type Action } from "@/lib/permissions";
import {
  CalendarIcon,
  CardIcon,
  ChartIcon,
  ListIcon,
  ScissorsIcon,
  SettingsIcon,
  TeamIcon,
  TodayIcon,
  UserIcon,
  UsersIcon,
} from "./icons";

export interface NavItem {
  href: string;
  label: string;
  icon: typeof TodayIcon;
  exact?: boolean;
  /** Only shown to roles allowed to do this. */
  requires?: Action;
}

/** Bottom bar on phones (plus More), and the top of the sidebar. */
export const primaryNav: NavItem[] = [
  { href: "/app", label: "Today", icon: TodayIcon, exact: true },
  { href: "/app/calendar", label: "Calendar", icon: CalendarIcon },
  { href: "/app/customers", label: "Customers", icon: UsersIcon },
];

/** Pages reachable from the sidebar and the mobile "More" screen. */
export const secondaryNav: NavItem[] = [
  { href: "/app/bookings", label: "Bookings", icon: ListIcon },
  { href: "/app/services", label: "Services", icon: ScissorsIcon },
  { href: "/app/staff", label: "Staff", icon: UserIcon },
  { href: "/app/reports", label: "Reports", icon: ChartIcon, requires: "reports.view" },
  { href: "/app/team", label: "Team", icon: TeamIcon, requires: "team.manage" },
  { href: "/app/settings", label: "Settings", icon: SettingsIcon, requires: "settings.view" },
  { href: "/app/billing", label: "Plan and billing", icon: CardIcon },
];

/** Nav items visible to a role. */
export function navFor(items: NavItem[], role: string | null | undefined): NavItem[] {
  return items.filter((i) => !i.requires || can(role, i.requires));
}
