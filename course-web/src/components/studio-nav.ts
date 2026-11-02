import { can, type Action } from "@/lib/permissions";
import {
  AwardIcon,
  BookIcon,
  CardIcon,
  HomeIcon,
  MessageIcon,
  PercentIcon,
  SettingsIcon,
  StarIcon,
  TeamIcon,
  UsersIcon,
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

/** Bottom bar on phones (plus More), and the top of the sidebar. */
export const primaryNav: NavItem[] = [
  { href: "/studio", label: "Dashboard", icon: HomeIcon, exact: true },
  { href: "/studio/courses", label: "Courses", icon: BookIcon },
  { href: "/studio/students", label: "Students", icon: UsersIcon, requires: "learners.view" },
  { href: "/studio/questions", label: "Q&A", icon: MessageIcon },
];

/** Pages reachable from the sidebar and the mobile "More" screen. */
export const secondaryNav: NavItem[] = [
  { href: "/studio/earnings", label: "Earnings", icon: WalletIcon, requires: "earnings.view" },
  { href: "/studio/reviews", label: "Reviews", icon: StarIcon, requires: "learners.view" },
  { href: "/studio/coupons", label: "Coupons", icon: PercentIcon, requires: "coupons.manage" },
  { href: "/studio/settings", label: "Academy settings", icon: SettingsIcon, requires: "settings.edit" },
  { href: "/studio/team", label: "Team", icon: TeamIcon, requires: "team.manage" },
  { href: "/studio/billing", label: "Plan and billing", icon: CardIcon },
];

/** Links out of the studio. */
export const elsewhereNav: NavItem[] = [{ href: "/learn", label: "My learning", icon: AwardIcon }];

/** Nav items visible to a role. */
export function navFor(items: NavItem[], role: string | null | undefined): NavItem[] {
  return items.filter((i) => !i.requires || can(role, i.requires));
}
