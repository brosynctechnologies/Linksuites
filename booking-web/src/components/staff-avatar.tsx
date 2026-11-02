import { initials, staffColor } from "@/lib/color";
import type { StaffDto } from "@/lib/types";
import { cx } from "./styles";

export function StaffAvatar({ staff, size = "md" }: { staff: Pick<StaffDto, "id" | "displayName" | "photoUrl" | "color">; size?: "md" | "lg" }) {
  const cls = size === "lg" ? "h-20 w-20 text-xl" : "h-10 w-10 text-sm";
  if (staff.photoUrl) return <img src={staff.photoUrl} alt="" className={cx(cls, "shrink-0 rounded-full object-cover")} />;
  return (
    <span
      className={cx(cls, "flex shrink-0 items-center justify-center rounded-full font-bold text-white")}
      style={{ background: staffColor(staff.color, staff.id) }}
      aria-hidden="true"
    >
      {initials(staff.displayName)}
    </span>
  );
}
