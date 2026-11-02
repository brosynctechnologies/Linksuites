"use client";

import { STAFF_COLORS } from "@/lib/config";
import { CheckIcon } from "@/components/icons";
import { cx } from "@/components/ui";

/** The eight staff colours; the calendar reads by person. */
export function ColorSwatches({ value, onChange, disabled }: { value: string; onChange: (c: string) => void; disabled?: boolean }) {
  return (
    <fieldset disabled={disabled}>
      <legend className="mb-2 text-sm font-medium text-slate-700">Calendar colour</legend>
      <div className="flex flex-wrap gap-2">
        {STAFF_COLORS.map((c) => {
          const on = value?.toUpperCase() === c;
          return (
            <button
              key={c}
              type="button"
              onClick={() => onChange(c)}
              aria-pressed={on}
              aria-label={`Colour ${c}`}
              className={cx("flex h-9 w-9 items-center justify-center rounded-full text-white disabled:opacity-50", on && "ring-2 ring-aubergine ring-offset-2")}
              style={{ background: c }}
            >
              {on && <CheckIcon className="h-4 w-4" />}
            </button>
          );
        })}
      </div>
    </fieldset>
  );
}
