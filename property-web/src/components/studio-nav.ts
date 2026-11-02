import { can, type Action } from "@/lib/permissions";
import {
  BoltIcon,
  BuildingIcon,
  CalendarIcon,
  CardIcon,
  ChartIcon,
  HomeIcon,
  KeyIcon,
  MessageIcon,
  ShieldIcon,
  TeamIcon,
  UserIcon,
  WalletIcon,
} from "./icons";

export interface NavItem {
  href: string;
  label: string;
  icon: typeof HomeIcon;
  exact?: boolean;
  /** Only shown to roles allowed to do this. */
  requires?: Action;
}

/** Bottom bar on phones (plus More), and the top of the rail. */
export const primaryNav: NavItem[] = [
  { href: "/studio", label: "Dashboard", icon: HomeIcon, exact: true },
  { href: "/studio/listings", label: "Listings", icon: BuildingIcon },
  { href: "/studio/enquiries", label: "Enquiries", icon: MessageIcon },
  { href: "/studio/viewings", label: "Viewings", icon: CalendarIcon },
];

/** Pages reachable from the rail and the phone "More" screen. */
export const secondaryNav: NavItem[] = [
  { href: "/studio/stays", label: "Short-lets", icon: KeyIcon },
  { href: "/studio/earnings", label: "Earnings", icon: WalletIcon },
  { href: "/studio/boosts", label: "Boosts", icon: BoltIcon },
  { href: "/studio/stats", label: "Stats", icon: ChartIcon },
  { href: "/studio/verification", label: "Verification", icon: ShieldIcon },
  { href: "/studio/profile", label: "Profile", icon: UserIcon },
  { href: "/studio/team", label: "Team", icon: TeamIcon, requires: "team.manage" },
  { href: "/studio/billing", label: "Plan and billing", icon: CardIcon },
];

/** Nav items visible to a role. */
export function navFor(items: NavItem[], role: string | null | undefined): NavItem[] {
  return items.filter((i) => !i.requires || can(role, i.requires));
}
