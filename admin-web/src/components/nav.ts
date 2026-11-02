import type { ComponentType, SVGProps } from "react";
import type { Area } from "@/lib/roles";
import type { OverviewDto } from "@/lib/types";
import {
  BadgeIcon,
  BankIcon,
  BookIcon,
  BuildingIcon,
  CardIcon,
  FlagIcon,
  GridIcon,
  HomeIcon,
  KeyIcon,
  LayersIcon,
  ListIcon,
  PinIcon,
  PulseIcon,
  StarIcon,
  TagIcon,
  UserIcon,
  UsersIcon,
} from "./icons";

export type QueueKey = keyof Omit<OverviewDto["queues"], "payoutsMinor">;

export interface NavItem {
  href: string;
  label: string;
  icon: ComponentType<SVGProps<SVGSVGElement>>;
  area: Area;
  queue?: QueueKey;
  exact?: boolean;
}

export interface NavGroup {
  label?: string;
  items: NavItem[];
}

export const NAV: NavGroup[] = [
  {
    items: [
      { href: "/", label: "Overview", icon: GridIcon, area: "overview", exact: true },
      { href: "/businesses", label: "Businesses", icon: BuildingIcon, area: "businesses" },
      { href: "/users", label: "Users", icon: UsersIcon, area: "users" },
    ],
  },
  {
    label: "Moderation",
    items: [
      { href: "/moderation/listings", label: "Listings", icon: HomeIcon, area: "moderation", queue: "listings" },
      { href: "/moderation/courses", label: "Courses", icon: BookIcon, area: "moderation", queue: "courses" },
      { href: "/moderation/verifications", label: "Verifications", icon: BadgeIcon, area: "moderation", queue: "verifications" },
      { href: "/moderation/reports", label: "Reports", icon: FlagIcon, area: "moderation", queue: "reports" },
      { href: "/moderation/reviews", label: "Reviews", icon: StarIcon, area: "moderation", queue: "reviews" },
      { href: "/moderation/locations", label: "Locations", icon: PinIcon, area: "moderation" },
      { href: "/moderation/categories", label: "Categories", icon: TagIcon, area: "moderation" },
    ],
  },
  {
    label: "Money",
    items: [
      { href: "/money/payments", label: "Payments", icon: CardIcon, area: "money" },
      { href: "/money/payouts", label: "Payouts", icon: BankIcon, area: "money", queue: "payouts" },
      { href: "/money/plans", label: "Plans", icon: LayersIcon, area: "money" },
    ],
  },
  {
    items: [
      { href: "/audit", label: "Audit log", icon: ListIcon, area: "audit" },
      { href: "/health", label: "Health", icon: PulseIcon, area: "health" },
      { href: "/admins", label: "Admins", icon: KeyIcon, area: "admins" },
      { href: "/account", label: "Your account", icon: UserIcon, area: "account" },
    ],
  },
];

export function isActive(pathname: string, item: NavItem) {
  return item.exact ? pathname === item.href : pathname === item.href || pathname.startsWith(item.href + "/");
}
