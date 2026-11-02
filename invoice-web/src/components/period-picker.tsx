"use client";

import { PERIOD_LABELS, presetRange, type PeriodPreset } from "@/lib/dates";
import { Input, cx } from "./ui";

export interface Period {
  preset: PeriodPreset;
  from: string;
  to: string;
}

export function periodFrom(preset: PeriodPreset): Period {
  return { preset, ...presetRange(preset) };
}

/** Preset chips plus from/to inputs for "Custom". */
export function PeriodPicker({ presets, value, onChange }: { presets: PeriodPreset[]; value: Period; onChange: (p: Period) => void }) {
  return (
    <div className="space-y-3">
      <div className="no-scrollbar -mx-4 overflow-x-auto px-4 sm:mx-0 sm:px-0">
        <div className="inline-flex gap-1 rounded-control border border-rule bg-desk-2 p-1" role="group" aria-label="Period">
        {presets.map((p) => (
          <button
            key={p}
            type="button"
            aria-pressed={value.preset === p}
            onClick={() => onChange(p === "custom" ? { ...value, preset: "custom" } : periodFrom(p))}
            className={cx(
              "shrink-0 rounded-[6px] px-3 py-1.5 text-sm font-medium whitespace-nowrap",
              value.preset === p ? "bg-paper text-graphite shadow-[0_1px_2px_rgb(30_36_48/0.12)]" : "text-muted hover:text-graphite",
            )}
          >
            {PERIOD_LABELS[p]}
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
