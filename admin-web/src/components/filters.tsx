"use client";

import { useEffect, useId, useRef, useState, type ReactNode } from "react";
import { useDebounced } from "@/lib/use-api";
import { SearchIcon } from "./icons";
import { Button, cx, inputClass, labelClass } from "./ui";

/** The sticky filter row above a list's table. */
export function FilterBar({ children, onClear, active }: { children: ReactNode; onClear?: () => void; active?: boolean }) {
  return (
    <div className="sticky top-9 z-20 -mx-4 mb-3 border-b border-line bg-concrete/95 px-4 py-2.5 backdrop-blur-sm md:-mx-6 md:px-6">
      <div className="flex flex-wrap items-end gap-2">
        {children}
        {onClear && active && (
          <Button variant="ghost" onClick={onClear} className="text-muted">
            Clear filters
          </Button>
        )}
      </div>
    </div>
  );
}

/** Search text that commits to the URL after typing stops (or on Enter). */
export function SearchFilter({
  label,
  value,
  onCommit,
  placeholder,
  className,
  minLength = 0,
}: {
  label: string;
  value: string;
  onCommit: (v: string) => void;
  placeholder?: string;
  className?: string;
  minLength?: number;
}) {
  const id = useId();
  const [text, setText] = useState(value);
  const debounced = useDebounced(text.trim(), 350);
  const committed = useRef(value);

  useEffect(() => {
    if (debounced === committed.current) return;
    if (debounced.length > 0 && debounced.length < minLength) return;
    committed.current = debounced;
    onCommit(debounced);
  }, [debounced, onCommit, minLength]);

  return (
    <form
      role="search"
      className={cx("min-w-0 flex-1 basis-56", className)}
      onSubmit={(e) => {
        e.preventDefault();
        const v = text.trim();
        if (v.length > 0 && v.length < minLength) return;
        committed.current = v;
        onCommit(v);
      }}
    >
      <label htmlFor={id} className={cx(labelClass, "mb-1 text-xs text-muted")}>
        {label}
      </label>
      <div className="relative">
        <SearchIcon className="pointer-events-none absolute top-1/2 left-3 h-4 w-4 -translate-y-1/2 text-muted" />
        <input id={id} type="search" className={cx(inputClass, "pl-9")} placeholder={placeholder} value={text} onChange={(e) => setText(e.target.value)} />
      </div>
    </form>
  );
}

/** Compact labelled select for filter rows. */
export function FilterSelect({
  label,
  value,
  onChange,
  options,
  className,
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
  options: { value: string; label: string }[];
  className?: string;
}) {
  const id = useId();
  return (
    <div className={cx("min-w-0 basis-36", className)}>
      <label htmlFor={id} className={cx(labelClass, "mb-1 text-xs text-muted")}>
        {label}
      </label>
      <select id={id} className={cx(inputClass, "pr-8")} value={value} onChange={(e) => onChange(e.target.value)}>
        {options.map((o) => (
          <option key={o.value} value={o.value}>
            {o.label}
          </option>
        ))}
      </select>
    </div>
  );
}

/** Compact labelled input (dates, codes, ids) for filter rows; commits on change for dates, on blur/Enter for text. */
export function FilterInput({
  label,
  value,
  onCommit,
  type = "text",
  placeholder,
  className,
  mono,
}: {
  label: string;
  value: string;
  onCommit: (v: string) => void;
  type?: "text" | "date";
  placeholder?: string;
  className?: string;
  mono?: boolean;
}) {
  const id = useId();
  const [text, setText] = useState(value);
  const [prev, setPrev] = useState(value);
  if (value !== prev) {
    setPrev(value);
    setText(value);
  }
  const commit = (v: string) => {
    if (v.trim() !== value) onCommit(v.trim());
  };
  return (
    <div className={cx("min-w-0 basis-36", className)}>
      <label htmlFor={id} className={cx(labelClass, "mb-1 text-xs text-muted")}>
        {label}
      </label>
      <input
        id={id}
        type={type}
        className={cx(inputClass, mono && "font-mono text-sm")}
        placeholder={placeholder}
        value={text}
        onChange={(e) => {
          setText(e.target.value);
          if (type === "date") commit(e.target.value);
        }}
        onBlur={(e) => commit(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === "Enter") commit((e.target as HTMLInputElement).value);
        }}
      />
    </div>
  );
}
