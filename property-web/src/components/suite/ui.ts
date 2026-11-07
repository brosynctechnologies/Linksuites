// LinkSuite shared style helpers. Plain functions, so server components can use them. Copy verbatim into each app.

export function cx(...parts: (string | false | null | undefined)[]) {
  return parts.filter(Boolean).join(" ");
}

export type LsButton = "app" | "secondary" | "inverse" | "quiet";

const buttonVariants: Record<LsButton, string> = {
  /** The one main action: "Start free". The app's colour (Ink on the hub). */
  app: "bg-app text-app-ink hover:bg-app-hover",
  /** The second action beside it. */
  secondary: "bg-ls-surface text-ls-ink ring-1 ring-ls-field ring-inset hover:ring-ls-ink",
  /** On a dark or coloured band. */
  inverse: "bg-ls-surface text-ls-ink hover:bg-ls-chalk",
  /** Header links and other low-weight actions. */
  quiet: "text-ls-ink hover:bg-ls-ink/[0.06]",
};

/** Buttons and button-links: 48px tall (44px for "md"), 8px radius, sentence-case labels that say what happens. */
export const lsButton = (variant: LsButton = "app", size: "md" | "lg" = "lg", extra?: string) =>
  cx(
    "inline-flex items-center justify-center gap-2 rounded-ls-control font-semibold whitespace-nowrap transition-colors duration-150",
    size === "lg" ? "min-h-12 px-5 text-base" : "min-h-11 px-3.5 text-[0.9375rem]",
    buttonVariants[variant],
    extra,
  );

/** Inline text links: always underlined, so they never rely on colour alone. */
export const lsLink = "font-semibold text-app-text underline decoration-1 underline-offset-[0.2em] hover:decoration-2";
