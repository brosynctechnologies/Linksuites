"use client";

import { PERIOD_LABELS, presetRange, type PeriodPreset } from "@/lib/time";
import { Input, cx } from "./ui";

export interface Period {
  preset: PeriodPreset;
  from: string;
  to: string;
}

export function periodFrom(preset: PeriodPreset, today: string): Period {
  return { preset, ...presetRange(preset, today) };
}

/** Preset chips plus from/to inputs for "Custom". `today` is the business's date. */
export function PeriodPicker({
  presets,
  value,
  onChange,
  today,
  labels,
}: {
  presets: PeriodPreset[];
  value: Period;
  onChange: (p: Period) => void;
  today: string;
  labels?: Partial<Record<PeriodPreset, string>>;
}) {
  return (
    <div className="space-y-3">
      <div className="no-scrollbar -mx-4 overflow-x-auto px-4 sm:mx-0 sm:px-0">
        <div className="inline-flex gap-1 rounded-control border border-line bg-mist-2 p-1" role="group" aria-label="Period">
          {presets.map((p) => (
            <button
              key={p}
              type="button"
              aria-pressed={value.preset === p}
              onClick={() => onChange(p === "custom" ? { ...value, preset: "custom" } : periodFrom(p, today))}
              className={cx(
                "shrink-0 rounded-[7px] px-3 py-1.5 text-sm font-medium whitespace-nowrap",
                value.preset === p ? "bg-surface text-aubergine shadow-[0_1px_2px_rgb(43_24_56/0.12)]" : "text-muted hover:text-aubergine",
              )}
            >
              {labels?.[p] ?? PERIOD_LABELS[p]}
            </button>
          ))}
        </div>
      </div>
      {value.preset === "custom" && (
        <div className="grid max-w-md grid-cols-2 gap-3">
          <Input label="From" type="date" value={value.from} max={value.to || undefined} onChange={(e) => onChange({ ...value, from: e.target.value })} />
          <Input label="To" type="date" value={value.to} min={value.from || undefined} onChange={(e) => onChange({ ...value, to: e.target.value })} />
        </div>
      )}
    </div>
  );
}
