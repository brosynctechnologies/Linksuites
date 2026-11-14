"use client";

import { normalizeColor, parseColor, secondAccent } from "@/lib/templates";
import { Button, cx, inputClass, labelClass } from "./ui";

/** The optional second accent: a colour picker plus the code, which can be cleared to use the automatic shade. */
export function SecondAccentField({
  id,
  brandColor,
  value,
  onChange,
  error,
}: {
  id: string;
  brandColor: string | null | undefined;
  value: string;
  onChange: (v: string) => void;
  error?: string;
}) {
  const shown = parseColor(value) ?? secondAccent(normalizeColor(brandColor), null);
  const describedBy = error ? `${id}-error` : `${id}-hint`;
  return (
    <div className="min-w-0 space-y-1.5">
      <label htmlFor={id} className={labelClass}>
        Second accent (optional)
      </label>
      <div className="flex items-center gap-2">
        <input
          type="color"
          aria-label="Pick the second accent"
          value={shown}
          onChange={(e) => onChange(e.target.value)}
          className="h-10 w-14 shrink-0 cursor-pointer rounded-control border border-field bg-paper p-1"
        />
        <input
          id={id}
          className={cx(inputClass, "min-w-0 tabular-nums")}
          value={value}
          onChange={(e) => onChange(e.target.value)}
          placeholder={`Automatic (${shown})`}
          maxLength={7}
          spellCheck={false}
          autoComplete="off"
          aria-invalid={!!error || undefined}
          aria-describedby={describedBy}
        />
        {value && (
          <Button variant="ghost" className="min-h-8 shrink-0 px-2 py-1" onClick={() => onChange("")}>
            Clear
          </Button>
        )}
      </div>
      {error ? (
        <p id={`${id}-error`} className="text-xs font-medium text-overdue">
          {error}
        </p>
      ) : (
        <p id={`${id}-hint`} className="text-xs text-muted">
          Leave empty to use a darker shade of your brand colour.
        </p>
      )}
    </div>
  );
}
