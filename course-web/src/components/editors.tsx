"use client";

import { useId, useRef, useState, type ChangeEvent } from "react";
import { Markdown } from "./markdown";
import { ArrowDownIcon, ArrowUpIcon, CloseIcon, ImageIcon, PlusIcon } from "./icons";
import { Segmented, Spinner, cx, inputClass, labelClass } from "./ui";

/** Markdown textarea with a Write / Preview switch and a one-line syntax hint. */
export function MarkdownField({
  label,
  value,
  onChange,
  rows = 10,
  hint,
  lesson,
  placeholder,
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
  rows?: number;
  hint?: string;
  /** Preview in the reading face (lessons) instead of the UI face. */
  lesson?: boolean;
  placeholder?: string;
}) {
  const id = useId();
  const [mode, setMode] = useState<"write" | "preview">("write");
  return (
    <div className="space-y-1.5">
      <div className="flex flex-wrap items-end justify-between gap-2">
        <label htmlFor={id} className={labelClass}>
          {label}
        </label>
        <Segmented
          label={`${label} view`}
          className="w-44"
          value={mode}
          onChange={setMode}
          items={[
            { value: "write", label: "Write" },
            { value: "preview", label: "Preview" },
          ]}
        />
      </div>
      {mode === "write" ? (
        <textarea
          id={id}
          rows={rows}
          className={cx(inputClass, "font-mono text-[0.875rem] leading-relaxed sm:text-[0.875rem]")}
          value={value}
          placeholder={placeholder}
          onChange={(e) => onChange(e.target.value)}
          aria-describedby={`${id}-hint`}
        />
      ) : (
        <div className="min-h-40 rounded-control border border-line bg-surface px-4 py-3">
          {value.trim() ? <Markdown source={value} variant={lesson ? "lesson" : "ui"} /> : <p className="text-sm text-muted">Nothing to preview yet.</p>}
        </div>
      )}
      <p id={`${id}-hint`} className="text-xs text-muted">
        {hint ?? "Markdown: ## heading, **bold**, *italic*, - list, 1. list, [link](https://…), `code`."}
      </p>
    </div>
  );
}

/** An editable list of short lines (outcomes, requirements, accepted answers). */
export function ListField({
  label,
  items,
  onChange,
  placeholder,
  addLabel = "Add",
  hint,
}: {
  label: string;
  items: string[];
  onChange: (items: string[]) => void;
  placeholder?: string;
  addLabel?: string;
  hint?: string;
}) {
  const id = useId();
  const [draft, setDraft] = useState("");
  const set = (i: number, v: string) => onChange(items.map((x, j) => (j === i ? v : x)));
  const remove = (i: number) => onChange(items.filter((_, j) => j !== i));
  const move = (i: number, d: number) => {
    const j = i + d;
    if (j < 0 || j >= items.length) return;
    const next = items.slice();
    [next[i], next[j]] = [next[j], next[i]];
    onChange(next);
  };
  const add = () => {
    if (!draft.trim()) return;
    onChange([...items, draft.trim()]);
    setDraft("");
  };
  return (
    <fieldset className="space-y-2">
      <legend className={labelClass}>{label}</legend>
      {hint && <p className="text-xs text-muted">{hint}</p>}
      {items.length > 0 && (
        <ol className="space-y-1.5">
          {items.map((it, i) => (
            <li key={i} className="flex items-center gap-1.5">
              <label htmlFor={`${id}-${i}`} className="sr-only">
                {label} {i + 1}
              </label>
              <input id={`${id}-${i}`} className={cx(inputClass, "py-2")} value={it} onChange={(e) => set(i, e.target.value)} />
              <IconButton label={`Move ${label.toLowerCase()} ${i + 1} up`} disabled={i === 0} onClick={() => move(i, -1)}>
                <ArrowUpIcon className="h-4 w-4" />
              </IconButton>
              <IconButton label={`Move ${label.toLowerCase()} ${i + 1} down`} disabled={i === items.length - 1} onClick={() => move(i, 1)}>
                <ArrowDownIcon className="h-4 w-4" />
              </IconButton>
              <IconButton label={`Remove ${label.toLowerCase()} ${i + 1}`} onClick={() => remove(i)} danger>
                <CloseIcon className="h-4 w-4" />
              </IconButton>
            </li>
          ))}
        </ol>
      )}
      <div className="flex gap-2">
        <label htmlFor={`${id}-new`} className="sr-only">
          New {label.toLowerCase()}
        </label>
        <input
          id={`${id}-new`}
          className={cx(inputClass, "py-2")}
          value={draft}
          placeholder={placeholder}
          onChange={(e) => setDraft(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter") {
              e.preventDefault();
              add();
            }
          }}
        />
        <button type="button" onClick={add} disabled={!draft.trim()} className="inline-flex shrink-0 items-center gap-1.5 rounded-control border border-line bg-surface px-3 text-sm font-semibold text-ink hover:bg-slate-50 disabled:text-faint">
          <PlusIcon className="h-4 w-4" /> {addLabel}
        </button>
      </div>
    </fieldset>
  );
}

export function IconButton({
  label,
  onClick,
  disabled,
  danger,
  children,
  className,
}: {
  label: string;
  onClick: () => void;
  disabled?: boolean;
  danger?: boolean;
  children: React.ReactNode;
  className?: string;
}) {
  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      onClick={onClick}
      disabled={disabled}
      className={cx(
        "flex h-9 w-9 shrink-0 items-center justify-center rounded-control disabled:opacity-30",
        danger ? "text-brick hover:bg-brick-wash" : "text-slate-600 hover:bg-ink/[0.06] hover:text-ink",
        className,
      )}
    >
      {children}
    </button>
  );
}

/** Image picker that uploads at once (thumbnails, logos). */
export function ImageUpload({
  label,
  src,
  onUpload,
  hint,
  shape = "wide",
}: {
  label: string;
  src: string | null | undefined;
  onUpload: (file: File) => Promise<void>;
  hint?: string;
  shape?: "wide" | "round";
}) {
  const id = useId();
  const input = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState(false);
  async function pick(e: ChangeEvent<HTMLInputElement>) {
    const f = e.target.files?.[0];
    e.target.value = "";
    if (!f) return;
    setBusy(true);
    try {
      await onUpload(f);
    } finally {
      setBusy(false);
    }
  }
  return (
    <div className="flex items-center gap-4">
      <div
        className={cx(
          "flex shrink-0 items-center justify-center overflow-hidden border border-line bg-mist-2 text-faint",
          shape === "round" ? "h-20 w-20 rounded-full" : "aspect-video w-40 rounded-[10px]",
        )}
      >
        {src ? <img src={src} alt="" className="h-full w-full object-cover" /> : <ImageIcon className="h-6 w-6" />}
      </div>
      <div>
        <input ref={input} id={id} type="file" accept="image/png,image/jpeg,image/webp" className="sr-only" onChange={pick} />
        <button
          type="button"
          onClick={() => input.current?.click()}
          disabled={busy}
          className="inline-flex min-h-10 items-center gap-2 rounded-control border border-line bg-surface px-4 text-sm font-semibold text-ink hover:bg-slate-50 disabled:text-faint"
        >
          {busy && <Spinner className="h-4 w-4" />}
          {src ? `Replace ${label.toLowerCase()}` : `Upload ${label.toLowerCase()}`}
        </button>
        {hint && <p className="mt-1.5 text-xs text-muted">{hint}</p>}
      </div>
    </div>
  );
}
