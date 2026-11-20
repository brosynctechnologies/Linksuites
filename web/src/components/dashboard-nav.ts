import { canManageSettings } from "@/lib/roles";
import type { Role } from "@/lib/types";
import { CardIcon, PercentIcon, SettingsIcon, ShareIcon, StockIcon, TagIcon, TeamIcon, TruckIcon, WalletIcon } from "./icons";

/**
 * Dashboard pages reachable from the sidebar and the mobile "More" screen.
 * `settings` pages are for the Owner and Admins only; Staff (orders and products) never sees them.
 */
export const secondaryNav = [
  { href: "/dashboard/inventory", label: "Stock", icon: StockIcon, settings: false },
  { href: "/dashboard/categories", label: "Categories", icon: TagIcon, settings: false },
  { href: "/dashboard/delivery", label: "Delivery zones", icon: TruckIcon, settings: true },
  { href: "/dashboard/discounts", label: "Discount codes", icon: PercentIcon, settings: true },
  { href: "/dashboard/share", label: "Share store", icon: ShareIcon, settings: false },
  { href: "/dashboard/team", label: "Team", icon: TeamIcon, settings: true },
  { href: "/dashboard/payments", label: "Payments", icon: WalletIcon, settings: true },
  { href: "/dashboard/settings", label: "Store settings", icon: SettingsIcon, settings: true },
  { href: "/dashboard/billing", label: "Plan & billing", icon: CardIcon, settings: true },
];

/** The secondary pages this role may open. */
export function navFor(role: Role | null) {
  return secondaryNav.filter((item) => !item.settings || canManageSettings(role));
}
