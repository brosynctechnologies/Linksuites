"use client";

import Link from "next/link";
import {
  useEffect,
  useId,
  useRef,
  type ButtonHTMLAttributes,
  type InputHTMLAttributes,
  type ReactNode,
  type SelectHTMLAttributes,
  type TextareaHTMLAttributes,
} from "react";
import type { ApiError } from "@/lib/api";
import { cx } from "./badges";
import { AlertIcon, BackIcon, CheckIcon, ChevronDownIcon, CloseIcon, InfoIcon } from "./icons";

export { cx, Badge, StatusBadge, PaymentBadge } from "./badges";

// ---------- Buttons ----------

export type Variant = "primary" | "secondary" | "danger" | "ghost" | "whatsapp";
const variants: Record<Variant, string> = {
  primary: "bg-marigold text-ink hover:bg-marigold-hover",
  secondary: "border border-line-strong bg-surface text-ink hover:border-ink-2 hover:bg-paper",
  danger: "border border-hibiscus/40 bg-surface text-hibiscus-deep hover:border-hibiscus hover:bg-hibiscus-soft",
  ghost: "text-ink-2 hover:bg-ink-soft hover:text-ink",
  /** Only for actions that open WhatsApp. */
  whatsapp: "bg-leaf text-white hover:bg-leaf-hover",
};
export const buttonClass = (variant: Variant = "primary", extra?: string) =>
  cx(
    "inline-flex min-h-11 items-center justify-center gap-2 rounded-control px-4 py-2 text-sm font-semibold whitespace-nowrap transition-colors disabled:cursor-not-allowed disabled:opacity-50",
    variants[variant],
    extra,
  );

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

export const inputClass =
  "block min-h-11 w-full rounded-control border border-line-strong bg-surface px-3 py-2.5 text-base text-ink placeholder:text-subtle transition-colors hover:border-ink-2/50 focus:border-ink disabled:bg-paper disabled:text-muted aria-[invalid=true]:border-hibiscus";

export const labelClass = "block text-sm font-medium text-ink";
export const hintClass = "text-xs text-subtle";

interface FieldProps {
  label: string;
  hint?: ReactNode;
  error?: string;
}

function FieldShell({
  id,
  label,
  hint,
  error,
  children,
}: FieldProps & { id: string; children: ReactNode }) {
  return (
    <div className="space-y-1.5">
      <label htmlFor={id} className={labelClass}>
        {label}
      </label>
      {children}
      {hint && !error && (
        <p id={`${id}-hint`} className={hintClass}>
          {hint}
        </p>
      )}
      {error && (
        <p id={`${id}-error`} className="flex items-start gap-1.5 text-xs font-medium text-hibiscus-deep">
          <AlertIcon width={14} height={14} className="mt-0.5 shrink-0" />
          {error}
        </p>
      )}
    </div>
  );
}

const describedBy = (id: string, hint: unknown, error: unknown) =>
  error ? `${id}-error` : hint ? `${id}-hint` : undefined;

export function Input({ label, hint, error, className, ...props }: FieldProps & InputHTMLAttributes<HTMLInputElement>) {
  const auto = useId();
  const id = props.id ?? auto;
  return (
    <FieldShell id={id} label={label} hint={hint} error={error}>
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

export function Textarea({ label, hint, error, className, ...props }: FieldProps & TextareaHTMLAttributes<HTMLTextAreaElement>) {
  const auto = useId();
  const id = props.id ?? auto;
  return (
    <FieldShell id={id} label={label} hint={hint} error={error}>
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
  className,
  children,
  ...props
}: FieldProps & SelectHTMLAttributes<HTMLSelectElement>) {
  const auto = useId();
  const id = props.id ?? auto;
  return (
    <FieldShell id={id} label={label} hint={hint} error={error}>
      <div className="relative">
        <select
          id={id}
          className={cx(inputClass, "appearance-none pr-10", className)}
          aria-invalid={!!error || undefined}
          aria-describedby={describedBy(id, hint, error)}
          {...props}
        >
          {children}
        </select>
        <ChevronDownIcon className="pointer-events-none absolute top-1/2 right-3 -translate-y-1/2 text-muted" />
      </div>
    </FieldShell>
  );
}

export function Toggle({
  checked,
  onChange,
  label,
  description,
  disabled,
  hideLabel,
}: {
  checked: boolean;
  onChange: (v: boolean) => void;
  label: string;
  description?: string;
  disabled?: boolean;
  /** Keep the label for screen readers only (compact list rows). */
  hideLabel?: boolean;
}) {
  const id = useId();
  return (
    <div className="flex items-center justify-between gap-4">
      <div className={hideLabel ? "sr-only" : "min-w-0"}>
        <label htmlFor={id} className="text-sm font-medium text-ink">
          {label}
        </label>
        {description && (
          <p id={`${id}-desc`} className="text-xs text-subtle">
            {description}
          </p>
        )}
      </div>
      <button
        id={id}
        type="button"
        role="switch"
        aria-checked={checked}
        aria-describedby={description ? `${id}-desc` : undefined}
        disabled={disabled}
        onClick={() => onChange(!checked)}
        className="group -my-1.5 inline-flex h-11 w-14 shrink-0 items-center justify-center rounded-full disabled:cursor-not-allowed disabled:opacity-50"
      >
        <span
          className={cx(
            "relative inline-flex h-7 w-12 items-center rounded-full transition-colors",
            checked ? "bg-ink" : "bg-line-strong group-hover:bg-subtle/60",
          )}
        >
          <span
            className={cx(
              "inline-block h-5.5 w-5.5 rounded-full bg-surface shadow-sm transition-transform",
              checked ? "translate-x-[1.4375rem]" : "translate-x-[0.1875rem]",
            )}
          />
        </span>
      </button>
    </div>
  );
}

// ---------- Feedback ----------

export function Spinner({ className = "h-5 w-5" }: { className?: string }) {
  return (
    <svg className={cx("animate-spin", className)} viewBox="0 0 24 24" fill="none" aria-hidden="true">
      <circle cx="12" cy="12" r="10" stroke="currentColor" strokeOpacity="0.25" strokeWidth="4" />
      <path d="M22 12a10 10 0 0 0-10-10" stroke="currentColor" strokeWidth="4" strokeLinecap="round" />
    </svg>
  );
}

export function Skeleton({ className }: { className?: string }) {
  return <span className={cx("block animate-pulse rounded-md bg-line", className)} aria-hidden="true" />;
}

/** Placeholder rows while a page's data loads. */
export function PageLoader({ label = "Loading…", rows = 4 }: { label?: string; rows?: number }) {
  return (
    <div role="status" className="overflow-hidden rounded-panel border border-line bg-surface">
      <span className="sr-only">{label}</span>
      {Array.from({ length: rows }).map((_, i) => (
        <div key={i} className="flex items-center gap-4 border-b border-line px-4 py-4 last:border-b-0">
          <Skeleton className="h-10 w-10 shrink-0 rounded-control" />
          <div className="flex-1 space-y-2">
            <Skeleton className="h-3.5 w-2/5" />
            <Skeleton className="h-3 w-3/5 bg-line/60" />
          </div>
          <Skeleton className="h-4 w-16" />
        </div>
      ))}
    </div>
  );
}

export function ErrorBox({ error, onRetry }: { error: ApiError | string | null | undefined; onRetry?: () => void }) {
  if (!error) return null;
  const message = typeof error === "string" ? error : error.message;
  const extra = typeof error === "string" ? [] : error.fieldMessages.filter((m) => m !== message);
  return (
    <div role="alert" className="flex gap-3 rounded-control bg-hibiscus-soft p-3.5 text-sm text-hibiscus-deep">
      <AlertIcon className="mt-0.5 shrink-0" />
      <div className="min-w-0">
        <p className="font-medium">{message}</p>
        {extra.length > 0 && (
          <ul className="mt-1 list-disc pl-5">
            {extra.map((m) => (
              <li key={m}>{m}</li>
            ))}
          </ul>
        )}
        {onRetry && (
          <button type="button" onClick={onRetry} className="mt-1 -ml-1 min-h-11 px-1 font-semibold underline underline-offset-2">
            Try again
          </button>
        )}
      </div>
    </div>
  );
}

export function Notice({ children, tone = "info" }: { children: ReactNode; tone?: "info" | "success" | "warning" }) {
  const tones = {
    info: "bg-ink-soft text-ink",
    success: "bg-ink-soft text-ink",
    warning: "bg-marigold-soft text-marigold-deep",
  };
  const Icon = tone === "success" ? CheckIcon : tone === "warning" ? AlertIcon : InfoIcon;
  return (
    <div role={tone === "success" ? "status" : undefined} className={cx("flex gap-3 rounded-control p-3.5 text-sm", tones[tone])}>
      <Icon className="mt-0.5 shrink-0" width={18} height={18} />
      <div className="min-w-0">{children}</div>
    </div>
  );
}

export function UpgradePrompt({ message }: { message?: string }) {
  return (
    <div className="rounded-panel border border-marigold/50 bg-marigold-soft p-4 text-sm text-ink sm:p-5">
      <p className="font-display text-lg font-bold">You&apos;ve reached your plan limit</p>
      <p className="mt-1 text-ink-2">{message || "Upgrade to Pro for unlimited products, zones and discount codes."}</p>
      <Link href="/dashboard/billing" className={buttonClass("primary", "mt-3")}>
        See Pro plan
      </Link>
    </div>
  );
}

/** Render an API error, switching to the upgrade prompt for 402 plan_limit. */
export function ApiErrorView({ error, onRetry }: { error: ApiError | null | undefined; onRetry?: () => void }) {
  if (!error) return null;
  if (error.isPlanLimit) return <UpgradePrompt message={error.detail} />;
  return <ErrorBox error={error} onRetry={onRetry} />;
}

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
    <div className="flex flex-col items-start gap-4 rounded-panel border border-dashed border-line-strong bg-surface px-5 py-8 sm:flex-row sm:px-6">
      {icon && (
        <div className="flex h-12 w-12 shrink-0 items-center justify-center rounded-control bg-ink-soft text-ink [&_svg]:h-6 [&_svg]:w-6">
          {icon}
        </div>
      )}
      <div className="min-w-0">
        <p className="font-display text-lg font-bold text-ink">{title}</p>
        {text && <p className="mt-1 max-w-prose text-sm text-muted">{text}</p>}
        {action && <div className="mt-4">{action}</div>}
      </div>
    </div>
  );
}

// ---------- Layout ----------

export function Panel({ children, className }: { children: ReactNode; className?: string }) {
  return <div className={cx("rounded-panel border border-line bg-surface p-4 sm:p-5", className)}>{children}</div>;
}

/** Back-compat alias. */
export const Card = Panel;

/** A ruled list container: one surface, rows separated by hairlines. */
export const listClass = "divide-y divide-line overflow-hidden rounded-panel border border-line bg-surface";

/** A labelled group of form fields; label column sits beside the fields on desktop. */
export function FormSection({
  title,
  description,
  children,
  className,
}: {
  title: string;
  description?: ReactNode;
  children: ReactNode;
  className?: string;
}) {
  return (
    <section className="grid gap-3 lg:grid-cols-[14rem_minmax(0,1fr)] lg:gap-8">
      <div className="lg:pt-1">
        <h2 className="font-display text-lg font-bold text-ink">{title}</h2>
        {description && <p className="mt-0.5 max-w-prose text-sm text-muted">{description}</p>}
      </div>
      <div className={cx("min-w-0 space-y-4 rounded-panel border border-line bg-surface p-4 sm:p-5", className)}>{children}</div>
    </section>
  );
}

export function SectionHeading({ title, action, id }: { title: string; action?: ReactNode; id?: string }) {
  return (
    <div className="mb-3 flex min-h-11 items-end justify-between gap-3">
      <h2 id={id} className="font-display text-lg font-bold text-ink">
        {title}
      </h2>
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
  title: string;
  back?: string;
  action?: ReactNode;
  subtitle?: ReactNode;
}) {
  return (
    <div className="mb-6 flex flex-wrap items-start justify-between gap-x-4 gap-y-3">
      <div className="flex min-w-0 flex-1 items-start gap-1">
        {back && (
          <Link
            href={back}
            className="-ml-3 flex h-11 w-11 shrink-0 items-center justify-center rounded-control text-ink-2 hover:bg-ink-soft hover:text-ink"
            aria-label="Back"
          >
            <BackIcon />
          </Link>
        )}
        <div className="min-w-0 pt-1">
          <h1 className="font-display text-2xl font-bold break-words text-ink sm:text-3xl">{title}</h1>
          {subtitle && <div className="mt-1 text-sm text-muted">{subtitle}</div>}
        </div>
      </div>
      {action && <div className="flex shrink-0 items-center gap-2">{action}</div>}
    </div>
  );
}

// ---------- Sheet (modal on desktop, bottom sheet on mobile) ----------

export function Sheet({
  open,
  onClose,
  title,
  children,
}: {
  open: boolean;
  onClose: () => void;
  title: string;
  children: ReactNode;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  const titleId = useId();
  useEffect(() => {
    const d = ref.current;
    if (!d) return;
    if (open && !d.open) d.showModal();
    if (!open && d.open) d.close();
  }, [open]);
  return (
    <dialog
      ref={ref}
      onClose={onClose}
      aria-labelledby={titleId}
      className="m-0 mt-auto w-full max-w-none rounded-t-panel bg-surface p-0 text-ink shadow-lift backdrop:bg-ink/50 sm:m-auto sm:max-w-lg sm:rounded-panel"
    >
      <div className="flex items-center justify-between gap-3 border-b border-line px-5 py-3">
        <h2 id={titleId} className="font-display text-lg font-bold">
          {title}
        </h2>
        <button
          type="button"
          onClick={onClose}
          className="-mr-2 flex h-11 w-11 items-center justify-center rounded-control text-ink-2 hover:bg-ink-soft"
          aria-label="Close"
        >
          <CloseIcon />
        </button>
      </div>
      <div className="max-h-[75vh] overflow-y-auto px-5 py-4 pb-[max(1rem,env(safe-area-inset-bottom))]">{children}</div>
    </dialog>
  );
}

export function formatDate(iso: string, withTime = true) {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return d.toLocaleString("en-GB", {
    day: "numeric",
    month: "short",
    ...(withTime ? { hour: "2-digit", minute: "2-digit" } : { year: "numeric" }),
  });
}
