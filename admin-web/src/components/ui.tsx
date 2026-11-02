"use client";

import {
  createContext,
  useContext,
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
import { needRoleText, type Area } from "@/lib/roles";
import { statusLabel, toneOf, type Tone } from "@/lib/status";
import { productName } from "@/lib/products";
import { AlertIcon, ChevronLeftIcon, ChevronRightIcon, CloseIcon, LockIcon, RefreshIcon } from "./icons";

export function cx(...parts: (string | false | null | undefined)[]) {
  return parts.filter(Boolean).join(" ");
}

// ---------- Buttons ----------

export type ButtonVariant = "primary" | "secondary" | "danger" | "ghost";

export function buttonClass(variant: ButtonVariant = "primary", extra?: string) {
  return cx(
    "inline-flex min-h-10 items-center justify-center gap-2 rounded-control px-3.5 text-sm font-medium whitespace-nowrap transition-colors disabled:cursor-not-allowed disabled:opacity-50",
    variant === "primary" && "bg-graphite text-white hover:bg-graphite-3",
    variant === "secondary" && "border border-line bg-surface text-graphite hover:bg-concrete-2",
    variant === "danger" && "bg-stop text-white hover:bg-stop-dark",
    variant === "ghost" && "text-graphite hover:bg-concrete",
    extra,
  );
}

export function Button({
  variant = "primary",
  loading,
  className,
  children,
  disabled,
  type = "button",
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: ButtonVariant; loading?: boolean }) {
  return (
    <button type={type} className={buttonClass(variant, className)} disabled={disabled || loading} aria-busy={loading || undefined} {...props}>
      {loading && <Spinner className="h-4 w-4" />}
      {children}
    </button>
  );
}

export const linkClass = "font-medium text-graphite underline decoration-line underline-offset-2 hover:decoration-graphite";

// ---------- Fields ----------

export const inputClass =
  "block min-h-10 w-full rounded-control border border-line bg-surface px-3 text-base text-graphite placeholder:text-muted/70 aria-[invalid=true]:border-stop disabled:bg-concrete-2 disabled:text-muted";
export const labelClass = "block text-sm font-medium text-graphite";

interface FieldProps {
  label: string;
  hint?: ReactNode;
  error?: string;
  srLabel?: boolean;
}

function FieldShell({ id, label, hint, error, srLabel, children, aside }: FieldProps & { id: string; children: ReactNode; aside?: ReactNode }) {
  return (
    <div className="min-w-0 space-y-1">
      <div className={cx("flex items-baseline justify-between gap-2", srLabel && "sr-only")}>
        <label htmlFor={id} className={labelClass}>
          {label}
        </label>
        {aside}
      </div>
      {children}
      {hint && !error && (
        <p id={`${id}-hint`} className="text-xs text-muted">
          {hint}
        </p>
      )}
      {error && (
        <p id={`${id}-error`} className="text-xs font-medium text-stop">
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

export function Textarea({
  label,
  hint,
  error,
  srLabel,
  className,
  counter,
  ...props
}: FieldProps & TextareaHTMLAttributes<HTMLTextAreaElement> & { counter?: { min?: number; max: number } }) {
  const generated = useId();
  const id = props.id ?? generated;
  const length = typeof props.value === "string" ? props.value.trim().length : 0;
  const outside = counter ? length > counter.max || (counter.min !== undefined && length > 0 && length < counter.min) : false;
  return (
    <FieldShell
      id={id}
      label={label}
      hint={hint}
      error={error}
      srLabel={srLabel}
      aside={
        counter && (
          <span className={cx("tnum text-xs", outside ? "font-medium text-stop" : "text-muted")} aria-live="polite">
            {length}/{counter.max}
          </span>
        )
      }
    >
      <textarea
        id={id}
        rows={3}
        className={cx(inputClass, "py-2 leading-normal", className)}
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

export function Checkbox({ checked, onChange, label, disabled, hint }: { checked: boolean; onChange: (v: boolean) => void; label: ReactNode; disabled?: boolean; hint?: ReactNode }) {
  return (
    <label className={cx("flex min-h-10 items-start gap-2.5 py-2 text-sm", disabled ? "text-muted" : "cursor-pointer")}>
      <input type="checkbox" className="mt-0.5 h-4 w-4 accent-graphite" checked={checked} disabled={disabled} onChange={(e) => onChange(e.target.checked)} />
      <span>
        {label}
        {hint && <span className="block text-xs text-muted">{hint}</span>}
      </span>
    </label>
  );
}

// ---------- Status ----------

const TONE_CLASS: Record<Tone, string> = {
  ok: "bg-ok-wash text-ok",
  wait: "bg-wait-wash text-wait",
  stop: "bg-stop-wash text-stop",
  neutral: "bg-concrete text-graphite",
};

export function StatusBadge({ status, tone, children }: { status?: string | null; tone?: Tone; children?: ReactNode }) {
  const t = tone ?? toneOf(status);
  return (
    <span className={cx("inline-flex items-center gap-1.5 rounded-control px-2 py-0.5 text-xs font-semibold whitespace-nowrap", TONE_CLASS[t])}>
      <span className="h-1.5 w-1.5 rounded-full bg-current" aria-hidden="true" />
      {children ?? statusLabel(status)}
    </span>
  );
}

export function ProductChip({ product }: { product: string }) {
  return (
    <span className="inline-flex items-center rounded-control border border-line bg-surface px-2 py-0.5 text-xs font-medium whitespace-nowrap text-graphite">
      {productName(product)}
    </span>
  );
}

/** A contact detail the API withholds from this role (emails and phones are for Support and Super admins). */
export function HiddenValue() {
  return (
    <span className="text-muted" title="Only Support and Super admins see contact details.">
      Hidden
    </span>
  );
}

export function Mono({ children, className, title }: { children: ReactNode; className?: string; title?: string }) {
  return (
    <span className={cx("font-mono text-[0.8125rem] break-all", className)} title={title}>
      {children}
    </span>
  );
}

// ---------- Layout ----------

export function Panel({ children, className, title, action, flush }: { children: ReactNode; className?: string; title?: ReactNode; action?: ReactNode; flush?: boolean }) {
  return (
    <section className={cx("min-w-0 rounded-panel border border-line bg-surface", className)}>
      {(title || action) && (
        <header className="flex flex-wrap items-center justify-between gap-2 border-b border-line px-4 py-2.5">
          {title && <h2 className="text-sm font-semibold text-graphite">{title}</h2>}
          {action}
        </header>
      )}
      <div className={flush ? "" : "p-4"}>{children}</div>
    </section>
  );
}

/** Wide tables scroll inside their panel, never the page. */
export function TableScroll({ children, label }: { children: ReactNode; label?: string }) {
  return (
    <div className="overflow-x-auto" tabIndex={0} role="region" aria-label={label ?? "Table"}>
      {children}
    </div>
  );
}

export function PageHeader({ title, subtitle, action, children }: { title: ReactNode; subtitle?: ReactNode; action?: ReactNode; children?: ReactNode }) {
  return (
    <header className="mb-4 flex flex-wrap items-start justify-between gap-3">
      <div className="min-w-0">
        <h1 className="text-xl font-semibold text-graphite">{title}</h1>
        {subtitle && <p className="mt-0.5 text-sm text-muted">{subtitle}</p>}
        {children}
      </div>
      {action && <div className="flex flex-wrap gap-2">{action}</div>}
    </header>
  );
}

export function Spinner({ className = "h-5 w-5" }: { className?: string }) {
  return (
    <svg viewBox="0 0 24 24" className={cx("animate-spin motion-reduce:animate-none", className)} aria-hidden="true">
      <circle cx="12" cy="12" r="9" fill="none" stroke="currentColor" strokeWidth="2.5" opacity="0.25" />
      <path d="M21 12a9 9 0 0 0-9-9" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" />
    </svg>
  );
}

export function Loading({ label = "Loading…", className }: { label?: string; className?: string }) {
  return (
    <div className={cx("flex items-center gap-2 px-4 py-8 text-sm text-muted", className)} role="status">
      <Spinner className="h-4 w-4" />
      {label}
    </div>
  );
}

export function EmptyState({ title, text, action }: { title: string; text?: ReactNode; action?: ReactNode }) {
  return (
    <div className="px-4 py-10 text-center">
      <p className="text-sm font-semibold text-graphite">{title}</p>
      {text && <p className="mx-auto mt-1 max-w-md text-sm text-muted">{text}</p>}
      {action && <div className="mt-3">{action}</div>}
    </div>
  );
}

// ---------- Errors and access ----------

/** The page's area, so a 403 can say which role is needed. */
const AreaContext = createContext<Area | null>(null);
export const AreaProvider = AreaContext.Provider;

/** `detail` is the API's 403 message ("You need the Finance role for this."); it wins over the page's area text. */
export function NoAccess({ area, detail }: { area: Area | null; detail?: string }) {
  return (
    <div className="flex items-start gap-3 rounded-panel border border-line bg-surface p-4" role="alert">
      <LockIcon className="mt-0.5 h-5 w-5 text-muted" />
      <div>
        <p className="text-sm font-semibold text-graphite">{detail || (area ? needRoleText(area) : "You do not have access to this.")}</p>
        <p className="mt-0.5 text-sm text-muted">Ask a super admin to change your role if you need it.</p>
      </div>
    </div>
  );
}

/** Shows ProblemDetails `detail` (or the first field message); a 403 becomes the role panel. */
export function ErrorPanel({ error, onRetry, className }: { error: ApiError | string | null | undefined; onRetry?: () => void; className?: string }) {
  const area = useContext(AreaContext);
  if (!error) return null;
  if (typeof error !== "string" && error.status === 403) return <NoAccess area={area} detail={error.detail} />;
  const message = typeof error === "string" ? error : error.message;
  return (
    <div className={cx("flex items-start gap-3 rounded-panel border border-stop/30 bg-stop-wash p-3 text-sm text-stop", className)} role="alert">
      <AlertIcon className="mt-0.5 h-4 w-4" />
      <p className="min-w-0 flex-1 font-medium">{message}</p>
      {onRetry && (
        <button type="button" onClick={onRetry} className="inline-flex min-h-8 items-center gap-1 rounded-control px-2 font-medium underline underline-offset-2">
          <RefreshIcon className="h-4 w-4" /> Try again
        </button>
      )}
    </div>
  );
}

/** Field errors the form has no input for. */
export function FormErrors({ error, fields }: { error: ApiError | null; fields: string[] }) {
  if (!error) return null;
  if (error.status === 403) return <ErrorPanel error={error} />;
  const extra = error.otherFieldMessages(fields);
  const known = fields.some((f) => error.field(f));
  const message = error.detail || extra[0] || (!known ? error.message : null);
  if (!message) return null;
  return <ErrorPanel error={message} />;
}

// ---------- Paging and tabs ----------

export function Pager({ page, pageSize, total, onPage }: { page: number; pageSize: number; total: number; onPage: (p: number) => void }) {
  const pages = Math.max(1, Math.ceil(total / Math.max(1, pageSize)));
  if (total === 0) return null;
  const from = (page - 1) * pageSize + 1;
  const to = Math.min(total, page * pageSize);
  return (
    <nav className="flex flex-wrap items-center justify-between gap-2 border-t border-line px-4 py-2.5 text-sm" aria-label="Pages">
      <p className="tnum text-muted">
        {from.toLocaleString("en-NG")}–{to.toLocaleString("en-NG")} of {total.toLocaleString("en-NG")}
      </p>
      <div className="flex items-center gap-1">
        <Button variant="secondary" className="px-2.5" disabled={page <= 1} onClick={() => onPage(page - 1)} aria-label="Previous page">
          <ChevronLeftIcon className="h-4 w-4" />
        </Button>
        <span className="tnum px-2 text-muted">
          Page {page} of {pages}
        </span>
        <Button variant="secondary" className="px-2.5" disabled={page >= pages} onClick={() => onPage(page + 1)} aria-label="Next page">
          <ChevronRightIcon className="h-4 w-4" />
        </Button>
      </div>
    </nav>
  );
}

export function Tabs<T extends string>({ items, value, onChange, label }: { items: { value: T; label: ReactNode }[]; value: T; onChange: (v: T) => void; label: string }) {
  const refs = useRef<(HTMLButtonElement | null)[]>([]);
  return (
    <div role="tablist" aria-label={label} className="flex gap-1 overflow-x-auto border-b border-line">
      {items.map((t, i) => {
        const active = t.value === value;
        return (
          <button
            key={t.value}
            ref={(el) => {
              refs.current[i] = el;
            }}
            type="button"
            role="tab"
            aria-selected={active}
            tabIndex={active ? 0 : -1}
            onClick={() => onChange(t.value)}
            onKeyDown={(e) => {
              if (e.key !== "ArrowRight" && e.key !== "ArrowLeft") return;
              e.preventDefault();
              const next = (i + (e.key === "ArrowRight" ? 1 : -1) + items.length) % items.length;
              onChange(items[next].value);
              refs.current[next]?.focus();
            }}
            className={cx(
              "-mb-px min-h-10 border-b-2 px-3 text-sm font-medium whitespace-nowrap",
              active ? "border-graphite text-graphite" : "border-transparent text-muted hover:text-graphite",
            )}
          >
            {t.label}
          </button>
        );
      })}
    </div>
  );
}

// ---------- Dialogs and panels (native <dialog>: focus trap and Esc for free) ----------

function useDialog(open: boolean, onClose: () => void) {
  const ref = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    const d = ref.current;
    if (!d) return;
    if (open && !d.open) d.showModal();
    if (!open && d.open) d.close();
  }, [open]);
  useEffect(() => {
    const d = ref.current;
    if (!d) return;
    const onCancel = (e: Event) => {
      e.preventDefault();
      onClose();
    };
    d.addEventListener("cancel", onCancel);
    return () => d.removeEventListener("cancel", onCancel);
  }, [onClose]);
  return ref;
}

/** The slim console bar repeated inside dialogs whose action affects a customer. */
export function SlimConsoleBar({ text = "Admin console: this affects a customer" }: { text?: string }) {
  return (
    <div className="flex h-6 items-center gap-2 bg-caution text-xs font-semibold text-graphite">
      <span className="caution-stripe h-full w-6" aria-hidden="true" />
      {text}
    </div>
  );
}

export function Dialog({
  open,
  onClose,
  title,
  children,
  customer,
  wide,
}: {
  open: boolean;
  onClose: () => void;
  title: string;
  children: ReactNode;
  /** Repeat the slim console bar: this action touches a customer. */
  customer?: boolean;
  wide?: boolean;
}) {
  const ref = useDialog(open, onClose);
  const titleId = useId();
  return (
    <dialog
      ref={ref}
      aria-labelledby={titleId}
      className={cx(
        "m-auto w-[calc(100%-2rem)] overflow-hidden rounded-panel border border-line bg-surface p-0 shadow-dialog open:animate-dialog-in",
        wide ? "max-w-4xl" : "max-w-lg",
      )}
    >
      {open && (
        <>
          {customer && <SlimConsoleBar />}
          <div className="flex items-center justify-between gap-3 border-b border-line px-4 py-3">
            <h2 id={titleId} className="text-base font-semibold">
              {title}
            </h2>
            <button type="button" onClick={onClose} className="inline-flex h-10 w-10 items-center justify-center rounded-control text-muted hover:bg-concrete" aria-label="Close">
              <CloseIcon className="h-5 w-5" />
            </button>
          </div>
          <div className="max-h-[calc(100dvh-8rem)] overflow-y-auto p-4">{children}</div>
        </>
      )}
    </dialog>
  );
}

/** Right-hand side panel (bottom-to-top full screen on phones). */
export function SidePanel({ open, onClose, title, children, footer }: { open: boolean; onClose: () => void; title: string; children: ReactNode; footer?: ReactNode }) {
  const ref = useDialog(open, onClose);
  const titleId = useId();
  return (
    <dialog
      ref={ref}
      aria-labelledby={titleId}
      className="fixed inset-y-0 right-0 left-auto m-0 h-dvh max-h-dvh w-full max-w-xl overflow-hidden border-l border-line bg-surface p-0 shadow-dialog open:flex open:animate-panel-in open:flex-col"
    >
      {open && (
        <>
          <div className="flex shrink-0 items-center justify-between gap-3 border-b border-line px-4 py-3">
            <h2 id={titleId} className="text-base font-semibold">
              {title}
            </h2>
            <button type="button" onClick={onClose} className="inline-flex h-10 w-10 items-center justify-center rounded-control text-muted hover:bg-concrete" aria-label="Close">
              <CloseIcon className="h-5 w-5" />
            </button>
          </div>
          <div className="min-h-0 flex-1 overflow-y-auto p-4">{children}</div>
          {footer && <div className="shrink-0 border-t border-line px-4 py-3">{footer}</div>}
        </>
      )}
    </dialog>
  );
}

// ---------- Definition list ----------

export function Facts({ items }: { items: { label: string; value: ReactNode }[] }) {
  return (
    <dl className="grid grid-cols-1 gap-x-6 gap-y-2 text-sm sm:grid-cols-[max-content_1fr]">
      {items.map((it) => (
        <div key={it.label} className="contents">
          <dt className="text-muted">{it.label}</dt>
          <dd className="min-w-0 break-words">{it.value}</dd>
        </div>
      ))}
    </dl>
  );
}
