"use client";

import Link from "next/link";
import {
  useEffect,
  useId,
  type ButtonHTMLAttributes,
  type CSSProperties,
  type InputHTMLAttributes,
  type ReactNode,
  type SelectHTMLAttributes,
  type TextareaHTMLAttributes,
} from "react";
import { createPortal } from "react-dom";
import type { ApiError } from "@/lib/api";
import { BackIcon, CloseIcon, SearchIcon, WhatsAppIcon } from "./icons";
import {
  buttonClass,
  chipClass,
  cx,
  dataRowClass,
  inputClass,
  labelClass,
  linkClass,
  panelClass,
  type ButtonVariant as Variant,
} from "./styles";

export { buttonClass, chipClass, cx, dataRowClass, inputClass, labelClass, linkClass, panelClass };

export function Button({
  variant = "primary",
  loading,
  className,
  children,
  disabled,
  type = "button",
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: Variant; loading?: boolean }) {
  return (
    <button type={type} className={buttonClass(variant, className)} disabled={disabled || loading} aria-busy={loading || undefined} {...props}>
      {loading && <Spinner className="h-4 w-4" />}
      {children}
    </button>
  );
}

// ---------- Form fields ----------

interface FieldProps {
  label: string;
  hint?: ReactNode;
  error?: string;
  /** Visually hide the label (still read by screen readers). */
  srLabel?: boolean;
}

function FieldShell({ id, label, hint, error, srLabel, children }: FieldProps & { id: string; children: ReactNode }) {
  return (
    <div className="min-w-0 space-y-1.5">
      <label htmlFor={id} className={srLabel ? "sr-only" : labelClass}>
        {label}
      </label>
      {children}
      {hint && !error && (
        <p id={`${id}-hint`} className="text-xs text-muted">
          {hint}
        </p>
      )}
      {error && (
        <p id={`${id}-error`} className="text-xs font-medium text-arterial">
          {error}
        </p>
      )}
    </div>
  );
}

function describedBy(id: string, hint: ReactNode, error?: string) {
  if (error) return `${id}-error`;
  if (hint) return `${id}-hint`;
  return undefined;
}

export function Input({ label, hint, error, srLabel, className, ...props }: FieldProps & InputHTMLAttributes<HTMLInputElement>) {
  const generated = useId();
  const id = props.id ?? generated;
  return (
    <FieldShell id={id} label={label} hint={hint} error={error} srLabel={srLabel}>
      <input id={id} className={cx(inputClass, className)} aria-invalid={!!error || undefined} aria-describedby={describedBy(id, hint, error)} {...props} />
    </FieldShell>
  );
}

export function Textarea({ label, hint, error, srLabel, className, ...props }: FieldProps & TextareaHTMLAttributes<HTMLTextAreaElement>) {
  const generated = useId();
  const id = props.id ?? generated;
  return (
    <FieldShell id={id} label={label} hint={hint} error={error} srLabel={srLabel}>
      <textarea
        id={id}
        rows={3}
        className={cx(inputClass, "leading-normal", className)}
        aria-invalid={!!error || undefined}
        aria-describedby={describedBy(id, hint, error)}
        {...props}
      />
    </FieldShell>
  );
}

export function Select({ label, hint, error, srLabel, className, children, ...props }: FieldProps & SelectHTMLAttributes<HTMLSelectElement>) {
  const generated = useId();
  const id = props.id ?? generated;
  return (
    <FieldShell id={id} label={label} hint={hint} error={error} srLabel={srLabel}>
      <select id={id} className={cx(inputClass, "pr-8", className)} aria-invalid={!!error || undefined} aria-describedby={describedBy(id, hint, error)} {...props}>
        {children}
      </select>
    </FieldShell>
  );
}

/** Search box with a leading icon and a screen-reader label. */
export function SearchField({
  id,
  label,
  value,
  onChange,
  placeholder,
  className,
}: {
  id: string;
  label: string;
  value: string;
  onChange: (v: string) => void;
  placeholder?: string;
  className?: string;
}) {
  return (
    <div className={cx("relative", className)}>
      <SearchIcon className="pointer-events-none absolute top-1/2 left-3 h-4 w-4 -translate-y-1/2 text-faint" />
      <label htmlFor={id} className="sr-only">
        {label}
      </label>
      <input id={id} type="search" className={cx(inputClass, "pl-9")} placeholder={placeholder} value={value} onChange={(e) => onChange(e.target.value)} />
    </div>
  );
}

export function Toggle({
  checked,
  onChange,
  label,
  description,
  disabled,
}: {
  checked: boolean;
  onChange: (v: boolean) => void;
  label: string;
  description?: ReactNode;
  disabled?: boolean;
}) {
  const id = useId();
  return (
    <div className="flex items-center justify-between gap-4">
      <div className="min-w-0">
        <label htmlFor={id} className="text-sm font-medium text-slate-800">
          {label}
        </label>
        {description && <p className="text-xs text-muted">{description}</p>}
      </div>
      <button
        id={id}
        type="button"
        role="switch"
        aria-checked={checked}
        disabled={disabled}
        onClick={() => onChange(!checked)}
        className={cx(
          "relative inline-flex h-6 w-11 shrink-0 items-center rounded-full transition-colors disabled:opacity-50",
          checked ? "bg-scrub" : "bg-slate-300",
        )}
      >
        <span
          className={cx(
            "inline-block h-5 w-5 rounded-full bg-surface shadow-[0_1px_2px_rgb(31_27_36/0.3)] transition-transform",
            checked ? "translate-x-5.5" : "translate-x-0.5",
          )}
        />
      </button>
    </div>
  );
}

export function Checkbox({ checked, onChange, label, disabled }: { checked: boolean; onChange: (v: boolean) => void; label: ReactNode; disabled?: boolean }) {
  return (
    <label className="flex items-start gap-2.5 text-sm text-slate-800">
      <input
        type="checkbox"
        className="mt-0.5 h-4 w-4 shrink-0 rounded border-rule accent-[#0f766e]"
        checked={checked}
        disabled={disabled}
        onChange={(e) => onChange(e.target.checked)}
      />
      <span>{label}</span>
    </label>
  );
}

// ---------- Feedback ----------

export function Spinner({ className = "h-5 w-5" }: { className?: string }) {
  return (
    <svg className={cx("animate-spin", className)} viewBox="0 0 24 24" fill="none" aria-hidden="true">
      <circle cx="12" cy="12" r="10" stroke="currentColor" strokeOpacity="0.25" strokeWidth="3" />
      <path d="M22 12a10 10 0 0 0-10-10" stroke="currentColor" strokeWidth="3" strokeLinecap="round" />
    </svg>
  );
}

export function PageLoader({ label = "Loading…" }: { label?: string }) {
  return (
    <div className="flex items-center justify-center gap-2.5 py-16 text-muted" role="status">
      <Spinner className="h-4 w-4" />
      <span className="text-sm">{label}</span>
    </div>
  );
}

/** A quiet placeholder bar while content loads. */
export function Skeleton({ className, style }: { className?: string; style?: CSSProperties }) {
  return <span aria-hidden="true" className={cx("block animate-pulse rounded-[4px] bg-gauze-2", className)} style={style} />;
}

export function ErrorBox({ error, onRetry }: { error: ApiError | string | null | undefined; onRetry?: () => void }) {
  if (!error) return null;
  const message = typeof error === "string" ? error : error.message;
  const extra = typeof error === "string" ? [] : error.fieldMessages.filter((m) => m !== message);
  return (
    <div role="alert" className="rounded-control border-l-[3px] border-arterial bg-arterial-wash px-3 py-2.5 text-sm text-arterial-dark">
      <p>{message}</p>
      {extra.length > 0 && (
        <ul className="mt-1 list-disc pl-5">
          {extra.map((m) => (
            <li key={m}>{m}</li>
          ))}
        </ul>
      )}
      {onRetry && (
        <button type="button" onClick={onRetry} className="mt-1.5 font-semibold underline underline-offset-2">
          Try again
        </button>
      )}
    </div>
  );
}

const noticeTones = {
  info: "border-scrub bg-scrub-wash text-ink",
  success: "border-pass bg-pass-wash text-ink",
  warning: "border-iodine bg-iodine-wash text-ink",
  error: "border-arterial bg-arterial-wash text-arterial-dark",
};

export function Notice({ children, tone = "info", className }: { children: ReactNode; tone?: "info" | "success" | "warning" | "error"; className?: string }) {
  return (
    <div role={tone === "error" ? "alert" : "status"} className={cx("rounded-control border-l-[3px] px-3 py-2.5 text-sm", noticeTones[tone], className)}>
      {children}
    </div>
  );
}

const UPGRADE_COPY =
  "You have reached a limit of your current plan. Clinic takes 15 staff and 20 beds and adds online payments; Hospital takes 60 staff and 150 beds. Existing records are never blocked.";

export function UpgradePrompt({ message, plan }: { message?: string; plan?: string }) {
  return (
    <div className={cx(panelClass, "border-l-[3px] border-l-iodine p-4 text-sm")} role="alert">
      <p className="font-semibold text-ink">{plan ? `Upgrade to ${plan} to continue` : "Upgrade your plan to continue"}</p>
      <p className="mt-1 max-w-prose text-slate-600">{message || UPGRADE_COPY}</p>
      <Link href="/app/settings#plan" className={buttonClass("primary", "mt-3")}>
        See plans
      </Link>
    </div>
  );
}

/** Render an API error, switching to the upgrade prompt for 402 plan_limit. */
export function ApiErrorView({ error, onRetry, plan }: { error: ApiError | null | undefined; onRetry?: () => void; plan?: string }) {
  if (!error) return null;
  if (error.isPlanLimit) return <UpgradePrompt message={error.detail} plan={plan} />;
  return <ErrorBox error={error} onRetry={onRetry} />;
}

/** Shown in place of a page the signed-in role can't use. */
export function NoAccess({ text = "Your role in this clinic doesn't include this page." }: { text?: string }) {
  return (
    <div className={cx(panelClass, "p-5 text-sm")} role="status">
      <p className="font-semibold text-ink">No access</p>
      <p className="mt-1 max-w-prose text-slate-600">{text} Ask the medical director or an admin if you need it.</p>
    </div>
  );
}

/** A determinate meter (seats, usage). `limit` -1 = unlimited. */
export function UsageMeter({ label, used, limit }: { label: string; used: number; limit: number }) {
  const unlimited = limit === -1;
  const pct = unlimited || limit <= 0 ? 0 : Math.min(100, Math.round((used / limit) * 100));
  return (
    <div>
      <div className="flex items-baseline justify-between gap-3 text-sm">
        <span className="text-slate-700">{label}</span>
        <span className="tnum font-semibold">
          {used} / {unlimited ? "Unlimited" : limit}
        </span>
      </div>
      <div
        className="mt-1.5 h-1.5 overflow-hidden rounded-full bg-gauze-2"
        role="progressbar"
        aria-label={label}
        aria-valuenow={used}
        aria-valuemin={0}
        aria-valuemax={unlimited ? undefined : limit}
      >
        <div className={cx("h-full rounded-full", pct >= 100 ? "bg-arterial" : pct >= 80 ? "bg-iodine" : "bg-scrub")} style={{ width: `${unlimited ? 4 : pct}%` }} />
      </div>
    </div>
  );
}

/** Empty list or page: a plain sentence and the action that fills it. */
export function EmptyState({ icon, title, text, action }: { icon?: ReactNode; title: string; text?: ReactNode; action?: ReactNode }) {
  return (
    <div className="flex flex-col items-center rounded-panel border border-dashed border-slate-300 px-6 py-12 text-center">
      <div className="mb-4 flex h-12 w-12 items-center justify-center rounded-full bg-gauze-2 text-muted" aria-hidden="true">
        {icon ?? <span className="block h-0.5 w-5 bg-faint" />}
      </div>
      <p className="font-semibold text-ink">{title}</p>
      {text && <p className="mt-1 max-w-sm text-sm text-muted">{text}</p>}
      {action && <div className="mt-5">{action}</div>}
    </div>
  );
}

// ---------- Layout ----------

export function Card({ children, className }: { children: ReactNode; className?: string }) {
  return <div className={cx(panelClass, "p-4 sm:p-5", className)}>{children}</div>;
}

export function SectionTitle({ children, action, id }: { children: ReactNode; action?: ReactNode; id?: string }) {
  return (
    <div className="mb-3 flex items-center justify-between gap-3">
      <h2 id={id} className="font-semibold text-ink">
        {children}
      </h2>
      {action}
    </div>
  );
}

export function PageHeader({ title, back, action, subtitle }: { title: ReactNode; back?: string; action?: ReactNode; subtitle?: ReactNode }) {
  return (
    <div className="mb-5 flex flex-wrap items-end justify-between gap-x-4 gap-y-3 sm:mb-6">
      <div className="flex min-w-0 items-start gap-1.5">
        {back && (
          <Link
            href={back}
            className="-ml-2 flex h-11 w-11 shrink-0 items-center justify-center rounded-control text-slate-600 hover:bg-ink/[0.06] hover:text-ink"
            aria-label="Back"
          >
            <BackIcon />
          </Link>
        )}
        <div className="min-w-0">
          <h1 className="truncate text-xl font-semibold tracking-[-0.01em] text-ink sm:text-2xl">{title}</h1>
          {subtitle && <div className="mt-0.5 text-sm text-muted">{subtitle}</div>}
        </div>
      </div>
      {action && <div className="flex shrink-0 flex-wrap gap-2">{action}</div>}
    </div>
  );
}

/** Underlined tabs on a rule. Pass `ids` to wire tabpanels (`${idBase}-tab-x` / `${idBase}-panel-x`). */
export function Tabs<T extends string>({
  items,
  value,
  onChange,
  label,
  className,
  idBase,
}: {
  items: { value: T; label: ReactNode }[];
  value: T;
  onChange: (v: T) => void;
  label: string;
  className?: string;
  idBase?: string;
}) {
  return (
    <div className={cx("no-scrollbar -mx-4 flex gap-1 overflow-x-auto border-b border-rule px-4 sm:mx-0 sm:px-0", className)} role="tablist" aria-label={label}>
      {items.map((t) => {
        const active = value === t.value;
        return (
          <button
            key={t.value}
            id={idBase ? `${idBase}-tab-${t.value}` : undefined}
            aria-controls={idBase ? `${idBase}-panel-${t.value}` : undefined}
            type="button"
            role="tab"
            aria-selected={active}
            onClick={() => onChange(t.value)}
            className={cx(
              "relative min-h-11 shrink-0 rounded-t-control px-3 pt-2 pb-2.5 text-sm font-medium whitespace-nowrap transition-colors",
              active ? "text-ink" : "text-muted hover:text-ink",
            )}
          >
            {t.label}
            <span aria-hidden="true" className={cx("absolute inset-x-2 -bottom-px h-[3px] rounded-full", active ? "bg-scrub" : "bg-transparent")} />
          </button>
        );
      })}
    </div>
  );
}

/** Segmented control (two or three exclusive views, e.g. Write / Preview). */
export function Segmented<T extends string>({
  items,
  value,
  onChange,
  label,
  className,
}: {
  items: { value: T; label: string }[];
  value: T;
  onChange: (v: T) => void;
  label: string;
  className?: string;
}) {
  return (
    <div
      className={cx("grid rounded-control border border-rule bg-gauze-2 p-1", className)}
      style={{ gridTemplateColumns: `repeat(${items.length}, minmax(0, 1fr))` }}
      role="tablist"
      aria-label={label}
    >
      {items.map((t) => (
        <button
          key={t.value}
          type="button"
          role="tab"
          aria-selected={value === t.value}
          onClick={() => onChange(t.value)}
          className={cx(
            "min-h-10 rounded-[4px] px-2 py-1.5 text-sm font-semibold",
            value === t.value ? "bg-surface text-ink shadow-[0_1px_2px_rgb(31_27_36/0.12)]" : "text-muted hover:text-ink",
          )}
        >
          {t.label}
        </button>
      ))}
    </div>
  );
}

/** Accessible modal dialog: a bottom sheet on phones, centred panel on larger screens. */
export function Modal({
  open,
  title,
  onClose,
  children,
  footer,
  wide,
  dark,
}: {
  open: boolean;
  title: string;
  onClose: () => void;
  children: ReactNode;
  footer?: ReactNode;
  wide?: boolean;
  /** Theatre chrome (full-screen viewer). */
  dark?: boolean;
}) {
  const titleId = useId();
  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    document.addEventListener("keydown", onKey);
    const prev = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => {
      document.removeEventListener("keydown", onKey);
      document.body.style.overflow = prev;
    };
  }, [open, onClose]);

  if (!open || typeof document === "undefined") return null;
  return createPortal(
    <div className="fixed inset-0 z-50 flex items-end justify-center sm:items-center sm:p-4">
      <div className="absolute inset-0 animate-fade-in bg-ink/55" aria-hidden="true" onClick={onClose} />
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        className={cx(
          "relative flex max-h-[92dvh] w-full animate-sheet-up flex-col rounded-t-[14px] shadow-float sm:rounded-panel",
          dark ? "on-scrub bg-ink text-white" : "bg-surface",
          wide ? "sm:max-w-3xl" : "sm:max-w-md",
        )}
      >
        <div className={cx("mx-auto mt-2 h-1 w-10 rounded-full sm:hidden", dark ? "bg-white/20" : "bg-rule")} aria-hidden="true" />
        <div className={cx("flex items-center justify-between gap-3 border-b px-5 py-3.5", dark ? "border-white/10" : "border-rule-soft")}>
          <h2 id={titleId} className={cx("min-w-0 truncate text-lg font-semibold", dark ? "text-white" : "text-ink")}>
            {title}
          </h2>
          <button
            type="button"
            onClick={onClose}
            className={cx(
              "-mr-2 flex h-11 w-11 shrink-0 items-center justify-center rounded-control",
              dark ? "text-on-scrub hover:bg-white/10 hover:text-white" : "text-muted hover:bg-ink/[0.06] hover:text-ink",
            )}
            aria-label="Close"
          >
            <CloseIcon />
          </button>
        </div>
        <div className={cx("overflow-y-auto pb-[max(1rem,env(safe-area-inset-bottom))]", dark ? "p-0" : "px-5 py-4")}>{children}</div>
        {footer && <div className="flex justify-end gap-2 border-t border-rule-soft px-5 py-3">{footer}</div>}
      </div>
    </div>,
    document.body,
  );
}


// ---------- CareLink pieces ----------

const badgeTones = {
  neutral: "bg-gauze-2 text-ink-2",
  pass: "bg-pass-wash text-pass",
  iodine: "bg-iodine-wash text-iodine",
  arterial: "bg-arterial-wash text-arterial-dark",
  scrub: "bg-scrub-wash text-scrub",
};
export type BadgeTone = keyof typeof badgeTones;

/** A short status word on a tinted wash. Always a word, never colour alone. */
export function Badge({ tone = "neutral", children, className }: { tone?: BadgeTone; children: ReactNode; className?: string }) {
  return (
    <span className={cx("inline-flex items-center rounded-[4px] px-1.5 py-0.5 text-xs font-bold whitespace-nowrap", badgeTones[tone], className)}>{children}</span>
  );
}

/**
 * The case sheet: a white panel with rules between rows.
 * Children are the rows (`<li>`); pass `head` for a header row shown from md up.
 */
export function CaseSheet({
  children,
  head,
  template,
  className,
  busy,
  label,
}: {
  children: ReactNode;
  head?: { label: string; align?: "right" }[];
  template?: string;
  className?: string;
  busy?: boolean;
  label?: string;
}) {
  return (
    <div className={cx("case-sheet", busy && "opacity-60 transition-opacity", className)} style={template ? ({ "--cols": template } as CSSProperties) : undefined}>
      {head && (
        <div
          aria-hidden="true"
          className="case-pad hidden gap-4 border-b border-rule bg-slate-50 py-2.5 text-xs font-bold text-muted md:grid md:[grid-template-columns:var(--cols)]"
        >
          {head.map((h, i) => (
            <span key={i} className={h.align === "right" ? "text-right" : undefined}>
              {h.label}
            </span>
          ))}
        </div>
      )}
      <ul className="case-rows" aria-label={label}>
        {children}
      </ul>
    </div>
  );
}

/** A row in a CaseSheet with a template: two columns on phones, the template on md+. */
export const caseRowClass = "case-pad grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-3 gap-y-1 py-3 md:gap-x-4 md:[grid-template-columns:var(--cols)]";

/** Opens WhatsApp in a new tab. Only ever used for WhatsApp. */
export function WhatsAppLink({ href, children = "Send on WhatsApp", className }: { href: string; children?: ReactNode; className?: string }) {
  return (
    <a href={href} target="_blank" rel="noopener noreferrer" className={buttonClass("whatsapp", className)}>
      <WhatsAppIcon className="h-4 w-4" />
      {children}
    </a>
  );
}

/**
 * The wristband: how CareLink always draws a patient number. A rounded band with a punched hole at the left and the number in tabular
 * figures, so staff learn to check it the way they check a real band. `size="lg"` for the patient page header.
 */
export function Wristband({ number, size = "md", className }: { number: string; size?: "md" | "lg"; className?: string }) {
  return (
    <span
      className={cx(
        "tnum inline-flex items-center gap-1.5 rounded-full border border-scrub-line bg-scrub-wash font-bold text-scrub-dark",
        size === "lg" ? "py-1 pr-3.5 pl-1.5 text-base" : "py-0.5 pr-2.5 pl-1 text-xs",
        className,
      )}
    >
      <span aria-hidden="true" className={cx("rounded-full border border-scrub-line bg-surface", size === "lg" ? "h-3.5 w-3.5" : "h-2.5 w-2.5")} />
      <span>
        <span className="sr-only">Patient number </span>
        {number}
      </span>
    </span>
  );
}

/** The patient's status as a word: only Inactive and Deceased are shown (Active is the normal case). */
export function PatientStatusBadge({ status }: { status: string }) {
  if (status === "Inactive") return <Badge>Inactive</Badge>;
  if (status === "Deceased") return <Badge tone="arterial">Deceased</Badge>;
  return null;
}
