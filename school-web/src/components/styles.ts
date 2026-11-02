// Plain (non-client) style helpers so server components can use them too.

export function cx(...parts: (string | false | null | undefined)[]) {
  return parts.filter(Boolean).join(" ");
}

export type ButtonVariant = "primary" | "secondary" | "danger" | "ghost" | "whatsapp";
const variants: Record<ButtonVariant, string> = {
  primary: "bg-blazer text-white hover:bg-blazer-dark active:bg-blazer-dark disabled:opacity-45",
  secondary: "border border-rule bg-surface text-ink hover:border-slate-300 hover:bg-slate-50 disabled:text-faint",
  danger: "border border-brick/30 bg-surface text-brick hover:bg-brick-wash disabled:text-brick/50",
  ghost: "text-slate-700 hover:bg-ink/[0.06] disabled:text-faint",
  /** Only "Chat on WhatsApp", always with the glyph. */
  whatsapp: "bg-whatsapp text-white hover:bg-whatsapp-dark disabled:opacity-45",
};

/** Buttons and button-links: 44px tall (the tap-target floor), 6px radius. */
export const buttonClass = (variant: ButtonVariant = "primary", extra?: string) =>
  cx(
    "inline-flex min-h-11 items-center justify-center gap-2 rounded-control px-4 py-2 text-sm font-semibold whitespace-nowrap transition-colors disabled:cursor-not-allowed",
    variants[variant],
    extra,
  );

export const inputClass =
  "block min-h-11 w-full rounded-control border border-rule bg-surface px-3 py-2.5 text-base text-ink placeholder:text-faint hover:border-slate-300 focus:border-blazer disabled:bg-slate-50 disabled:text-muted aria-[invalid=true]:border-brick sm:text-sm";

/** Label text above fields (always visible). */
export const labelClass = "block text-sm font-medium text-slate-700";

/** White panel on Plaster: 1px Line border, 10px radius, no shadow. */
export const panelClass = "rounded-panel border border-rule bg-surface";

/** Text link in Adire. */
export const linkClass = "font-semibold text-blazer underline-offset-2 hover:underline";

/** Chip (filters, area links, small toggles): full round. */
export const chipClass = (active: boolean, extra?: string) =>
  cx(
    "inline-flex min-h-11 items-center gap-1.5 rounded-full border px-4 py-1.5 text-sm font-medium whitespace-nowrap transition-colors",
    active ? "border-blazer bg-blazer text-white" : "border-rule bg-surface text-slate-700 hover:border-blazer-rule hover:text-ink",
    extra,
  );

/** Row layout for DataList: two columns on phones, the shared template on md+. */
export const dataRowClass =
  "grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-3 gap-y-1 px-4 py-3 md:gap-x-4 md:[grid-template-columns:var(--cols)]";
