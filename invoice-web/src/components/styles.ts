// Plain (non-client) style helpers so server components can use them too.

export function cx(...parts: (string | false | null | undefined)[]) {
  return parts.filter(Boolean).join(" ");
}

export type ButtonVariant = "primary" | "secondary" | "danger" | "ghost";
const variants: Record<ButtonVariant, string> = {
  primary: "bg-ink text-white hover:bg-ink-dark active:bg-ink-dark disabled:bg-ink/45",
  secondary: "border border-rule bg-paper text-graphite hover:border-slate-300 hover:bg-slate-50 disabled:text-faint",
  danger: "border border-overdue/30 bg-paper text-overdue hover:bg-overdue-wash disabled:text-overdue/50",
  ghost: "text-slate-700 hover:bg-graphite/[0.06] disabled:text-faint",
};

export const buttonClass = (variant: ButtonVariant = "primary", extra?: string) =>
  cx(
    "inline-flex min-h-10 items-center justify-center gap-2 rounded-control px-4 py-2 text-sm font-semibold whitespace-nowrap transition-colors disabled:cursor-not-allowed",
    variants[variant],
    extra,
  );

export const inputClass =
  "block w-full rounded-control border border-rule bg-paper px-3 py-2.5 text-base text-graphite placeholder:text-faint hover:border-slate-300 focus:border-ink disabled:bg-slate-50 disabled:text-muted aria-[invalid=true]:border-overdue sm:text-sm";

/** Small label text above fields. */
export const labelClass = "block text-sm font-medium text-slate-700";

/** White panel on the desk (no shadow: shadows are reserved for paper and floating things). */
export const panelClass = "rounded-panel border border-rule bg-paper";

/** Text link in ink blue. */
export const linkClass = "font-semibold text-ink underline-offset-2 hover:underline";

/** Row layout for DataList: two columns on phones, the shared template on md+. */
export const dataRowClass =
  "grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-3 gap-y-0.5 px-4 py-3 md:gap-x-4 md:[grid-template-columns:var(--cols)]";
