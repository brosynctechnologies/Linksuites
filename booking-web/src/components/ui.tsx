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
import { STATUS_LABEL, STATUS_TONE, type Tone } from "@/lib/status";
import type { BookingStatus } from "@/lib/types";
import { BackIcon, CloseIcon, SearchIcon } from "./icons";
import { buttonClass, cx, dataRowClass, inputClass, labelClass, linkClass, panelClass, type ButtonVariant as Variant } from "./styles";

export { buttonClass, cx, dataRowClass, inputClass, labelClass, linkClass, panelClass };

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
    <button
      type={type}
      className={buttonClass(variant, className)}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      {...props}
    >
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
        <p id={`${id}-error`} className="text-xs font-medium text-brick">
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
      <input
        id={id}
        className={cx(inputClass, className)}
        aria-invalid={!!error || undefined}
        aria-describedby={describedBy(id, hint, error)}
        {...props}
      />
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

export function Select({
  label,
  hint,
  error,
  srLabel,
  className,
  children,
  ...props
}: FieldProps & SelectHTMLAttributes<HTMLSelectElement>) {
  const generated = useId();
  const id = props.id ?? generated;
  return (
    <FieldShell id={id} label={label} hint={hint} error={error} srLabel={srLabel}>
      <select
        id={id}
        className={cx(inputClass, "pr-8", className)}
        aria-invalid={!!error || undefined}
        aria-describedby={describedBy(id, hint, error)}
        {...props}
      >
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
      <input
        id={id}
        type="search"
        className={cx(inputClass, "pl-9")}
        placeholder={placeholder}
        value={value}
        onChange={(e) => onChange(e.target.value)}
      />
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
          checked ? "bg-rose" : "bg-slate-300",
        )}
      >
        <span
          className={cx(
            "inline-block h-5 w-5 rounded-full bg-surface shadow-[0_1px_2px_rgb(43_24_56/0.3)] transition-transform",
            checked ? "translate-x-5.5" : "translate-x-0.5",
          )}
        />
      </button>
    </div>
  );
}

export function Checkbox({ checked, onChange, label }: { checked: boolean; onChange: (v: boolean) => void; label: ReactNode }) {
  return (
    <label className="flex items-start gap-2.5 text-sm text-slate-800">
      <input
        type="checkbox"
        className="mt-0.5 h-4 w-4 shrink-0 rounded border-line accent-rose"
        checked={checked}
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
  return <span aria-hidden="true" className={cx("block animate-pulse rounded-[4px] bg-mist-2", className)} style={style} />;
}

export function ErrorBox({ error, onRetry }: { error: ApiError | string | null | undefined; onRetry?: () => void }) {
  if (!error) return null;
  const message = typeof error === "string" ? error : error.message;
  const extra = typeof error === "string" ? [] : error.fieldMessages.filter((m) => m !== message);
  return (
    <div role="alert" className="rounded-control border-l-[3px] border-brick bg-brick-wash px-3 py-2.5 text-sm text-brick-dark">
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
  info: "border-rose bg-rose-wash text-rose-dark",
  success: "border-mint bg-mint-wash text-mint-dark",
  warning: "border-amber bg-amber-wash text-amber-text",
  error: "border-brick bg-brick-wash text-brick-dark",
};

export function Notice({ children, tone = "info" }: { children: ReactNode; tone?: "info" | "success" | "warning" | "error" }) {
  return (
    <div role={tone === "error" ? "alert" : "status"} className={cx("rounded-control border-l-[3px] px-3 py-2.5 text-sm", noticeTones[tone])}>
      {children}
    </div>
  );
}

const UPGRADE_COPY = "You have reached a limit of your current plan. Paid plans take unlimited bookings, deposits and email reminders, and add more staff and team seats.";

export function UpgradePrompt({ message, plan }: { message?: string; plan?: string }) {
  return (
    <div className={cx(panelClass, "border-l-[3px] border-l-amber p-4 text-sm")} role="alert">
      <p className="font-semibold text-aubergine">{plan ? `Upgrade to ${plan} to continue` : "Upgrade your plan to continue"}</p>
      <p className="mt-1 max-w-prose text-slate-600">{message || UPGRADE_COPY}</p>
      <Link href="/app/billing" className={buttonClass("primary", "mt-3")}>
        See plans
      </Link>
    </div>
  );
}

/** Render an API error, switching to the upgrade prompt for 402 plan_limit. */
export function ApiErrorView({
  error,
  onRetry,
  plan,
}: {
  error: ApiError | null | undefined;
  onRetry?: () => void;
  /** Plan named in the upgrade prompt for 402s. */
  plan?: string;
}) {
  if (!error) return null;
  if (error.isPlanLimit) return <UpgradePrompt message={error.detail} plan={plan} />;
  return <ErrorBox error={error} onRetry={onRetry} />;
}

/** Shown in place of a page the signed-in role can't use. */
export function NoAccess({ text = "Your role in this business doesn't include this page." }: { text?: string }) {
  return (
    <div className={cx(panelClass, "p-5 text-sm")} role="status">
      <p className="font-semibold text-aubergine">No access</p>
      <p className="mt-1 max-w-prose text-slate-600">{text} Ask the business owner or an admin if you need it.</p>
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
        className="mt-1.5 h-1.5 overflow-hidden rounded-full bg-mist"
        role="progressbar"
        aria-label={label}
        aria-valuenow={used}
        aria-valuemin={0}
        aria-valuemax={unlimited ? undefined : limit}
      >
        <div
          className={cx("h-full rounded-full", pct >= 100 ? "bg-brick" : pct >= 80 ? "bg-amber" : "bg-rose")}
          style={{ width: `${unlimited ? 4 : pct}%` }}
        />
      </div>
    </div>
  );
}

/** Empty list or page: a plain sentence and the action that fills it. */
export function EmptyState({
  icon,
  title,
  text,
  action,
}: {
  icon?: ReactNode;
  title: string;
  text?: string;
  action?: ReactNode;
}) {
  return (
    <div className="flex flex-col items-center rounded-panel border border-dashed border-slate-300 px-6 py-12 text-center">
      <div className="mb-4 flex h-12 w-12 items-center justify-center rounded-full bg-mist-2 text-muted" aria-hidden="true">
        {icon ?? <span className="block h-0.5 w-5 bg-faint" />}
      </div>
      <p className="font-semibold text-aubergine">{title}</p>
      {text && <p className="mt-1 max-w-sm text-sm text-muted">{text}</p>}
      {action && <div className="mt-5">{action}</div>}
    </div>
  );
}

// ---------- Layout ----------

export function Card({ children, className }: { children: ReactNode; className?: string }) {
  return <div className={cx(panelClass, "p-4 sm:p-5", className)}>{children}</div>;
}

export function SectionTitle({ children, action }: { children: ReactNode; action?: ReactNode }) {
  return (
    <div className="mb-3 flex items-center justify-between gap-3">
      <h2 className="font-semibold text-aubergine">{children}</h2>
      {action}
    </div>
  );
}

export function PageHeader({
  title,
  back,
  action,
  subtitle,
}: {
  title: ReactNode;
  back?: string;
  action?: ReactNode;
  subtitle?: ReactNode;
}) {
  return (
    <div className="mb-5 flex flex-wrap items-end justify-between gap-x-4 gap-y-3 sm:mb-6">
      <div className="flex min-w-0 items-start gap-1.5">
        {back && (
          <Link
            href={back}
            className="-ml-2 flex h-10 w-10 shrink-0 items-center justify-center rounded-control text-slate-600 hover:bg-aubergine/[0.06] hover:text-aubergine"
            aria-label="Back"
          >
            <BackIcon />
          </Link>
        )}
        <div className="min-w-0">
          <h1 className="truncate text-xl font-semibold tracking-[-0.01em] text-aubergine sm:text-2xl">{title}</h1>
          {subtitle && <div className="mt-0.5 text-sm text-muted">{subtitle}</div>}
        </div>
      </div>
      {action && <div className="flex shrink-0 flex-wrap gap-2">{action}</div>}
    </div>
  );
}

/** Underlined tabs on a line (filters such as invoice status). */
export function Tabs<T extends string>({
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
      className={cx("no-scrollbar -mx-4 flex gap-1 overflow-x-auto border-b border-line px-4 sm:mx-0 sm:px-0", className)}
      role="tablist"
      aria-label={label}
    >
      {items.map((t) => {
        const active = value === t.value;
        return (
          <button
            key={t.value}
            type="button"
            role="tab"
            aria-selected={active}
            onClick={() => onChange(t.value)}
            className={cx(
              "relative shrink-0 rounded-t-control px-3 pt-2 pb-2.5 text-sm font-medium whitespace-nowrap transition-colors",
              active ? "text-aubergine" : "text-muted hover:text-aubergine",
            )}
          >
            {t.label}
            <span
              aria-hidden="true"
              className={cx("absolute inset-x-2 -bottom-px h-[3px] rounded-full", active ? "bg-rose" : "bg-transparent")}
            />
          </button>
        );
      })}
    </div>
  );
}

/** Segmented control (two or three exclusive views, e.g. Edit / Preview). */
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
      className={cx("grid rounded-control border border-line bg-mist-2 p-1", className)}
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
            "rounded-[6px] py-1.5 text-sm font-semibold",
            value === t.value ? "bg-surface text-aubergine shadow-[0_1px_2px_rgb(43_24_56/0.12)]" : "text-muted hover:text-aubergine",
          )}
        >
          {t.label}
        </button>
      ))}
    </div>
  );
}

export interface SummaryItem {
  label: string;
  value: string;
  sub?: ReactNode;
  tone?: "red" | "green";
  href?: string;
}

/**
 * Ruled summary strip: one panel divided by hairlines, money figures in Fraunces.
 * Columns: 2 on phones, `cols` from xl up.
 */
export function SummaryStrip({ items, cols = 4, label }: { items: SummaryItem[]; cols?: 2 | 3 | 4 | 5; label?: string }) {
  const wide = { 2: "md:grid-cols-2", 3: "md:grid-cols-3", 4: "xl:grid-cols-4", 5: "md:grid-cols-3 2xl:grid-cols-5" }[cols];
  return (
    <section aria-label={label} className="overflow-hidden rounded-panel border border-line bg-surface">
      <div className={cx("-mr-px -mb-px grid grid-cols-2", wide)}>
        {items.map((it) => {
          const body = (
            <>
              <p className="text-sm text-muted">{it.label}</p>
              <p
                className={cx(
                  "font-expanded tnum mt-1 font-bold text-[clamp(1.2rem,12cqw,1.75rem)] leading-tight",
                  it.tone === "red" ? "text-brick" : it.tone === "green" ? "text-mint" : "text-aubergine",
                )}
              >
                {it.value}
              </p>
              {it.sub && <p className="mt-1 text-xs text-muted">{it.sub}</p>}
            </>
          );
          return (
            <div key={it.label} className="@container min-w-0 border-r border-b border-line">
              {it.href ? (
                <Link href={it.href} className="block h-full px-4 py-4 hover:bg-slate-50 sm:px-5">
                  {body}
                </Link>
              ) : (
                <div className="h-full px-4 py-4 sm:px-5">{body}</div>
              )}
            </div>
          );
        })}
      </div>
    </section>
  );
}

/**
 * Ruled data table. On md+ it is a table with a header row; on phones each row stacks.
 * `template` is a CSS grid-template-columns value shared by the header and rows.
 */
export function DataList({
  head,
  template,
  children,
  className,
  busy,
}: {
  head: { label: string; align?: "right" }[];
  template: string;
  children: ReactNode;
  className?: string;
  busy?: boolean;
}) {
  return (
    <div
      className={cx("overflow-hidden rounded-panel border border-line bg-surface", busy && "opacity-60 transition-opacity", className)}
      style={{ "--cols": template } as CSSProperties}
    >
      <div
        aria-hidden="true"
        className="hidden gap-4 border-b border-line bg-slate-50 px-4 py-2.5 text-xs font-medium text-muted md:grid md:[grid-template-columns:var(--cols)]"
      >
        {head.map((h, i) => (
          <span key={i} className={h.align === "right" ? "text-right" : undefined}>
            {h.label}
          </span>
        ))}
      </div>
      <ul className="divide-y divide-line-soft">{children}</ul>
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
}: {
  open: boolean;
  title: string;
  onClose: () => void;
  children: ReactNode;
  footer?: ReactNode;
  wide?: boolean;
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
      <div className="absolute inset-0 animate-fade-in bg-aubergine/45" aria-hidden="true" onClick={onClose} />
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        className={cx(
          "relative flex max-h-[92dvh] w-full animate-sheet-up flex-col rounded-t-[16px] bg-surface shadow-float sm:animate-pop-in sm:rounded-panel",
          wide ? "sm:max-w-2xl" : "sm:max-w-md",
        )}
      >
        <div className="mx-auto mt-2 h-1 w-10 rounded-full bg-line sm:hidden" aria-hidden="true" />
        <div className="flex items-center justify-between gap-3 border-b border-line-soft px-5 py-3.5">
          <h2 id={titleId} className="text-lg font-semibold text-aubergine">
            {title}
          </h2>
          <button
            type="button"
            onClick={onClose}
            className="-mr-2 flex h-9 w-9 items-center justify-center rounded-control text-muted hover:bg-aubergine/[0.06] hover:text-aubergine"
            aria-label="Close"
          >
            <CloseIcon />
          </button>
        </div>
        <div className="overflow-y-auto px-5 py-4 pb-[max(1rem,env(safe-area-inset-bottom))]">{children}</div>
        {footer && <div className="flex justify-end gap-2 border-t border-line-soft px-5 py-3">{footer}</div>}
      </div>
    </div>,
    document.body,
  );
}

// ---------- Badges ----------

export function Badge({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <span className={cx("inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-xs font-medium whitespace-nowrap", className)}>
      {children}
    </span>
  );
}

const toneClass: Record<Tone, { text: string; dot: string }> = {
  neutral: { text: "bg-mist text-slate-700", dot: "bg-faint" },
  rose: { text: "bg-rose-wash text-rose-dark", dot: "bg-rose" },
  green: { text: "bg-mint-wash text-mint-dark", dot: "bg-mint" },
  amber: { text: "bg-amber-wash text-amber-text", dot: "bg-amber" },
  red: { text: "bg-brick-wash text-brick-dark", dot: "bg-brick" },
  void: { text: "bg-mist text-muted", dot: "border border-faint bg-transparent" },
};

/** Quiet status badge: a coloured dot plus the word. */
export function StatusBadge({ tone, children }: { tone: Tone; children: ReactNode }) {
  const t = toneClass[tone];
  return (
    <Badge className={t.text}>
      <span className={cx("h-1.5 w-1.5 shrink-0 rounded-full", t.dot)} aria-hidden="true" />
      {children}
    </Badge>
  );
}

export { type Tone };

/** Booking status: a coloured dot plus the word. */
export function BookingStatusBadge({ status }: { status: BookingStatus }) {
  return <StatusBadge tone={STATUS_TONE[status] ?? "neutral"}>{STATUS_LABEL[status] ?? status}</StatusBadge>;
}

export function Pager({ page, pageSize, total, onPage }: { page: number; pageSize: number; total: number; onPage: (p: number) => void }) {
  const pages = Math.max(1, Math.ceil(total / pageSize));
  if (pages <= 1) return null;
  return (
    <nav className="mt-4 flex items-center justify-between gap-3 text-sm" aria-label="Pagination">
      <Button variant="secondary" disabled={page <= 1} onClick={() => onPage(page - 1)}>
        Previous
      </Button>
      <span className="text-muted">
        Page {page} of {pages}
      </span>
      <Button variant="secondary" disabled={page >= pages} onClick={() => onPage(page + 1)}>
        Next
      </Button>
    </nav>
  );
}

/** Side drawer on large screens, bottom sheet on phones. */
export function Drawer({ open, title, onClose, children }: { open: boolean; title: string; onClose: () => void; children: ReactNode }) {
  const titleId = useId();
  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [open, onClose]);

  if (!open || typeof document === "undefined") return null;
  return createPortal(
    <div className="fixed inset-0 z-50 flex items-end sm:items-stretch sm:justify-end">
      <div className="absolute inset-0 animate-fade-in bg-aubergine/35" aria-hidden="true" onClick={onClose} />
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        className="relative flex max-h-[88dvh] w-full animate-sheet-up flex-col rounded-t-[16px] bg-surface shadow-float sm:max-h-none sm:max-w-md sm:animate-pop-in sm:rounded-none"
      >
        <div className="flex items-center justify-between gap-3 border-b border-line-soft px-5 py-3.5">
          <h2 id={titleId} className="text-lg font-semibold text-aubergine">
            {title}
          </h2>
          <button
            type="button"
            onClick={onClose}
            className="-mr-2 flex h-9 w-9 items-center justify-center rounded-control text-muted hover:bg-aubergine/[0.06] hover:text-aubergine"
            aria-label="Close"
          >
            <CloseIcon />
          </button>
        </div>
        <div className="overflow-y-auto px-5 py-4 pb-[max(1rem,env(safe-area-inset-bottom))]">{children}</div>
      </div>
    </div>,
    document.body,
  );
}
