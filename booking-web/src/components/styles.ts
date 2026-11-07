// Plain (non-client) style helpers so server components can use them too.

export function cx(...parts: (string | false | null | undefined)[]) {
  return parts.filter(Boolean).join(" ");
}

export type ButtonVariant = "primary" | "secondary" | "danger" | "ghost";
const variants: Record<ButtonVariant, string> = {
  primary: "bg-rose text-white hover:bg-rose-dark active:bg-rose-dark disabled:bg-rose/45",
  secondary: "border border-line bg-surface text-aubergine hover:border-slate-300 hover:bg-slate-50 disabled:text-faint",
  danger: "border border-brick/30 bg-surface text-brick hover:bg-brick-wash disabled:text-brick/50",
  ghost: "text-slate-700 hover:bg-aubergine/[0.06] disabled:text-faint",
};

export const buttonClass = (variant: ButtonVariant = "primary", extra?: string) =>
  cx(
    "inline-flex min-h-10 items-center justify-center gap-2 rounded-control px-4 py-2 text-sm font-semibold whitespace-nowrap transition-colors disabled:cursor-not-allowed",
    variants[variant],
    extra,
  );

export const inputClass =
  "block w-full rounded-control border border-field bg-surface px-3 py-2.5 text-base text-aubergine placeholder:text-muted hover:border-slate-600 focus:border-rose disabled:bg-slate-50 disabled:text-muted aria-[invalid=true]:border-brick sm:text-sm";

/** Small label text above fields. */
export const labelClass = "block text-sm font-medium text-slate-700";

/** White panel on the mist (no shadow: shadows are reserved for surface and floating things). */
export const panelClass = "rounded-panel border border-line bg-surface";

/** Text link in rose blue. */
export const linkClass = "font-semibold text-rose underline-offset-2 hover:underline";

/** Row layout for DataList: two columns on phones, the shared template on md+. */
export const dataRowClass =
  "grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-3 gap-y-0.5 px-4 py-3 md:gap-x-4 md:[grid-template-columns:var(--cols)]";
