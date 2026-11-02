import { CardIcon, PercentIcon, SettingsIcon, ShareIcon, TagIcon, TruckIcon } from "./icons";

/** Dashboard pages reachable from the sidebar and the mobile "More" screen. */
export const secondaryNav = [
  { href: "/dashboard/categories", label: "Categories", icon: TagIcon },
  { href: "/dashboard/delivery", label: "Delivery zones", icon: TruckIcon },
  { href: "/dashboard/discounts", label: "Discount codes", icon: PercentIcon },
  { href: "/dashboard/share", label: "Share store", icon: ShareIcon },
  { href: "/dashboard/settings", label: "Store settings", icon: SettingsIcon },
  { href: "/dashboard/billing", label: "Plan & billing", icon: CardIcon },
];
