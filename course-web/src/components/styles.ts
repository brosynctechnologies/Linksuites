// Plain (non-client) style helpers so server components can use them too.

export function cx(...parts: (string | false | null | undefined)[]) {
  return parts.filter(Boolean).join(" ");
}

export type ButtonVariant = "primary" | "secondary" | "danger" | "ghost" | "progress";
const variants: Record<ButtonVariant, string> = {
  primary: "bg-lagoon text-on-lagoon hover:bg-lagoon-dark active:bg-lagoon-dark disabled:opacity-45",
  secondary: "border border-line bg-surface text-ink hover:border-slate-300 hover:bg-slate-50 disabled:text-faint",
  danger: "border border-brick/30 bg-surface text-brick hover:bg-brick-wash disabled:text-brick/50",
  ghost: "text-slate-700 hover:bg-ink/[0.06] disabled:text-faint",
  /** Completion actions (Mark complete): lime is reserved for progress. */
  progress: "bg-lime-dark text-white hover:bg-[#365f19] disabled:opacity-45",
};

export const buttonClass = (variant: ButtonVariant = "primary", extra?: string) =>
  cx(
    "inline-flex min-h-10 items-center justify-center gap-2 rounded-control px-4 py-2 text-sm font-semibold whitespace-nowrap transition-colors disabled:cursor-not-allowed",
    variants[variant],
    extra,
  );

export const inputClass =
  "block w-full rounded-control border border-field bg-surface px-3 py-2.5 text-base text-ink placeholder:text-muted hover:border-slate-700 focus:border-lagoon disabled:bg-slate-50 disabled:text-muted aria-[invalid=true]:border-brick sm:text-sm";

/** Small label text above fields. */
export const labelClass = "block text-sm font-medium text-slate-700";

/** White panel on the mist (no shadow: shadows are for course cards and floating things). */
export const panelClass = "rounded-panel border border-line bg-surface";

/** Text link in Lagoon. */
export const linkClass = "font-semibold text-lagoon-text underline-offset-2 hover:underline";

/** Chip (category filters, small toggles). */
export const chipClass = (active: boolean, extra?: string) =>
  cx(
    "inline-flex min-h-9 items-center gap-1.5 rounded-full border px-3.5 py-1.5 text-sm font-medium whitespace-nowrap transition-colors",
    active ? "border-lagoon bg-lagoon text-on-lagoon" : "border-line bg-surface text-slate-700 hover:border-lagoon-line hover:text-ink",
    extra,
  );

/** Row layout for DataList: two columns on phones, the shared template on md+. */
export const dataRowClass =
  "grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-3 gap-y-0.5 px-4 py-3 md:gap-x-4 md:[grid-template-columns:var(--cols)]";
