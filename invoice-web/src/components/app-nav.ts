import { can, type Action } from "@/lib/permissions";
import {
  CardIcon,
  ChartIcon,
  HomeIcon,
  InvoiceIcon,
  QuoteIcon,
  ReceiptIcon,
  RepeatIcon,
  SettingsIcon,
  TagIcon,
  TeamIcon,
  UsersIcon,
} from "./icons";

interface NavItem {
  href: string;
  label: string;
  icon: typeof HomeIcon;
  exact?: boolean;
  /** Only shown to roles allowed to do this. */
  requires?: Action;
}

export const primaryNav: NavItem[] = [
  { href: "/app", label: "Home", icon: HomeIcon, exact: true },
  { href: "/app/invoices", label: "Invoices", icon: InvoiceIcon },
  { href: "/app/clients", label: "Clients", icon: UsersIcon },
];

/** Pages reachable from the sidebar and the mobile "More" screen. */
export const secondaryNav: NavItem[] = [
  { href: "/app/quotes", label: "Quotes", icon: QuoteIcon },
  { href: "/app/recurring", label: "Recurring", icon: RepeatIcon },
  { href: "/app/items", label: "Items", icon: TagIcon },
  { href: "/app/expenses", label: "Expenses", icon: ReceiptIcon },
  { href: "/app/reports", label: "Reports", icon: ChartIcon, requires: "reports.view" },
  { href: "/app/team", label: "Team", icon: TeamIcon, requires: "team.manage" },
  { href: "/app/settings", label: "Settings", icon: SettingsIcon, requires: "settings.view" },
  { href: "/app/billing", label: "Plan & billing", icon: CardIcon },
];

/** Nav items visible to a role. */
export function navFor(items: NavItem[], role: string | null | undefined): NavItem[] {
  return items.filter((i) => !i.requires || can(role, i.requires));
}
