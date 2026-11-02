"use client";

import Link from "next/link";
import { normalizeColor, tint } from "@/lib/templates";
import type { TemplateDto } from "@/lib/types";
import { LockIcon } from "./icons";
import { cx } from "./ui";

/** Tiny schematic of each template so the picker is visual without rendering a full preview. */
function Thumb({ k, accent }: { k: string; accent: string }) {
  const bar = (w: string, c = "#c3cad5") => <span className="block h-1 rounded" style={{ width: w, background: c }} />;
  return (
    <span
      className={cx(
        "flex aspect-[210/297] w-full flex-col gap-1 overflow-hidden rounded-[2px] bg-paper p-1.5 shadow-[0_1px_2px_rgb(30_36_48/0.12),0_4px_10px_-4px_rgb(30_36_48/0.2)]",
        k === "bold" && "border-l-4",
        k === "elegant" && "items-center",
      )}
      style={k === "bold" ? { borderLeftColor: accent } : undefined}
      aria-hidden="true"
    >
      {k === "modern" ? (
        <span className="-mx-1.5 -mt-1.5 mb-0.5 block h-4" style={{ background: accent }} />
      ) : k === "elegant" ? (
        <span className="block w-3/4 border-y-2 border-double py-0.5" style={{ borderColor: accent }}>
          {bar("100%", accent)}
        </span>
      ) : (
        bar(k === "bold" ? "70%" : "40%", k === "minimal" ? "#1e2430" : accent)
      )}
      {k === "classic" && <span className="block h-px" style={{ background: accent }} />}
      {bar("60%")}
      <span className="block h-1.5 rounded" style={{ background: k === "classic" ? tint(accent, 0.2) : k === "bold" ? accent : "#e8ebf0" }} />
      {bar("90%")}
      {bar("80%")}
      <span className="mt-auto ml-auto block h-1.5 w-2/5 rounded" style={{ background: k === "minimal" ? "#1e2430" : accent }} />
    </span>
  );
}

export function TemplatePicker({
  templates,
  value,
  onChange,
  brandColor,
  label = "Template",
}: {
  templates: TemplateDto[];
  value: string;
  onChange: (key: string) => void;
  brandColor?: string | null;
  label?: string;
}) {
  const accent = normalizeColor(brandColor);
  const anyLocked = templates.some((t) => t.isPremium && t.available === false);
  return (
    <fieldset>
      <legend className="mb-2 block text-sm font-medium text-slate-700">{label}</legend>
      <div className="grid grid-cols-3 gap-2.5 sm:grid-cols-5">
        {templates.map((t) => {
          const locked = t.isPremium && t.available === false;
          const selected = value === t.key;
          return (
            <button
              key={t.key}
              type="button"
              disabled={locked}
              aria-pressed={selected}
              onClick={() => onChange(t.key)}
              className={cx(
                "relative flex flex-col items-center gap-1.5 rounded-control border p-2 text-xs font-medium transition-colors",
                selected ? "border-ink bg-ink-wash text-ink-dark ring-1 ring-ink" : "border-rule bg-desk text-slate-700 hover:border-slate-300",
                locked && "cursor-not-allowed opacity-60",
              )}
              title={locked ? `${t.name} is a Pro template` : t.name}
            >
              <Thumb k={t.key} accent={accent} />
              <span className="flex items-center gap-1">
                {locked && <LockIcon className="h-3 w-3" />}
                {t.name}
                {t.isPremium && !locked && <span className="rounded-full bg-amber-wash px-1.5 text-[0.6875rem] text-amber-text">Pro</span>}
              </span>
            </button>
          );
        })}
      </div>
      {anyLocked && (
        <p className="mt-2 text-xs text-muted">
          Bold and Elegant are Pro templates.{" "}
          <Link href="/app/billing" className="font-semibold text-ink underline-offset-2 hover:underline">
            Upgrade to use them
          </Link>
        </p>
      )}
    </fieldset>
  );
}
