"use client";

import { useId, useState } from "react";
import { formatStamp } from "@/lib/format";
import { VITAL_RANGES } from "@/lib/clinical";
import type { VitalSignsDto } from "@/lib/types/clinical";
import { chipClass, cx } from "./ui";

// The vitals trend on the record: one measure at a time (one axis), the clinic's normal range as a quiet band, a 2px line with 8px
// points, and a hover readout. Blood pressure draws systolic and diastolic together (same unit), told apart by line style and labels
// as well as colour. A table of every reading sits under the chart. Inline SVG, no chart library.

type Metric = "bp" | "pulse" | "temperatureC" | "spO2" | "respRate" | "weightKg";
const METRICS: { value: Metric; label: string; unit: string }[] = [
  { value: "bp", label: "Blood pressure", unit: "mmHg" },
  { value: "pulse", label: "Pulse", unit: "per minute" },
  { value: "temperatureC", label: "Temperature", unit: "°C" },
  { value: "spO2", label: "SpO2", unit: "%" },
  { value: "respRate", label: "Respiratory rate", unit: "per minute" },
  { value: "weightKg", label: "Weight", unit: "kg" },
];

interface Series {
  key: string;
  label: string;
  stroke: string;
  dash?: string;
  values: (number | null)[];
}

const W = 640;
const H = 220;
const PAD = { top: 16, right: 64, bottom: 28, left: 44 };

function seriesFor(metric: Metric, rows: VitalSignsDto[]): Series[] {
  if (metric === "bp")
    return [
      { key: "systolic", label: "Systolic", stroke: "var(--color-scrub)", values: rows.map((r) => r.systolic) },
      { key: "diastolic", label: "Diastolic", stroke: "var(--color-ink-2)", dash: "5 4", values: rows.map((r) => r.diastolic) },
    ];
  const label = METRICS.find((m) => m.value === metric)!.label;
  return [{ key: metric, label, stroke: "var(--color-scrub)", values: rows.map((r) => r[metric]) }];
}

function bandFor(metric: Metric): [number, number] | null {
  if (metric === "weightKg") return null;
  if (metric === "bp") return null; // two ranges; each line's own band would overlap, so the flags carry it instead
  const r = VITAL_RANGES[metric];
  return r.normalMin !== null ? [r.normalMin, r.normalMax ?? 100] : null;
}

const dayFmt = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", timeZone: "Africa/Lagos" });

export function VitalsChart({ vitals }: { vitals: VitalSignsDto[] }) {
  const id = useId();
  const [metric, setMetric] = useState<Metric>("bp");
  const [hover, setHover] = useState<number | null>(null);
  const available = METRICS.filter((m) =>
    vitals.some((v) => (m.value === "bp" ? v.systolic !== null || v.diastolic !== null : v[m.value] !== null)),
  );
  const current = available.some((m) => m.value === metric) ? metric : available[0]?.value;
  if (!current) return <p className="text-sm text-muted">No vital signs recorded yet.</p>;

  const rows = vitals.filter((v) => (current === "bp" ? v.systolic !== null || v.diastolic !== null : v[current] !== null));
  const series = seriesFor(current, rows);
  const unit = METRICS.find((m) => m.value === current)!.unit;
  const band = bandFor(current);
  const all = series.flatMap((s) => s.values).filter((v): v is number => v !== null);
  let lo = Math.min(...all, ...(band ? [band[0]] : []));
  let hi = Math.max(...all, ...(band ? [band[1]] : []));
  const span = hi - lo || Math.max(1, Math.abs(hi) * 0.1);
  lo -= span * 0.12;
  hi += span * 0.12;
  const plotW = W - PAD.left - PAD.right;
  const plotH = H - PAD.top - PAD.bottom;
  const x = (i: number) => PAD.left + (rows.length === 1 ? plotW / 2 : (i / (rows.length - 1)) * plotW);
  const y = (v: number) => PAD.top + (1 - (v - lo) / (hi - lo)) * plotH;
  const ticks = [lo + (hi - lo) * 0.15, lo + (hi - lo) * 0.5, lo + (hi - lo) * 0.85].map((t) => Math.round(t * 10) / 10);
  const xLabels = rows.length <= 1 ? [0] : [0, rows.length - 1];
  const fmt = (v: number | null) => (v === null ? "—" : String(v));

  return (
    <div className="space-y-3">
      <div className="no-scrollbar -mx-1 flex gap-2 overflow-x-auto px-1" role="tablist" aria-label="Measure">
        {available.map((m) => (
          <button key={m.value} type="button" role="tab" aria-selected={current === m.value} className={chipClass(current === m.value, "min-h-9 px-3")} onClick={() => setMetric(m.value)}>
            {m.label}
          </button>
        ))}
      </div>
      {series.length > 1 && (
        <p className="flex flex-wrap gap-4 text-xs text-muted" aria-hidden="true">
          {series.map((s) => (
            <span key={s.key} className="flex items-center gap-1.5">
              <svg width="22" height="8">
                <line x1="1" y1="4" x2="21" y2="4" stroke={s.stroke} strokeWidth="2" strokeDasharray={s.dash} />
              </svg>
              {s.label}
            </span>
          ))}
        </p>
      )}
      <div className="relative">
        <svg
          viewBox={`0 0 ${W} ${H}`}
          className="h-auto w-full"
          role="img"
          aria-labelledby={`${id}-desc`}
          onMouseLeave={() => setHover(null)}
        >
          <desc id={`${id}-desc`}>
            {METRICS.find((m) => m.value === current)!.label} over {rows.length} readings, in {unit}. The table below lists every reading.
          </desc>
          {band && (
            <g>
              <rect x={PAD.left} width={plotW} y={y(Math.min(band[1], hi))} height={Math.max(0, y(Math.max(band[0], lo)) - y(Math.min(band[1], hi)))} fill="var(--color-pass-wash)" />
              <text x={W - PAD.right + 6} y={y(Math.min(band[1], hi)) + 10} className="fill-muted text-[10px]">
                Normal
              </text>
            </g>
          )}
          {ticks.map((t) => (
            <g key={t}>
              <line x1={PAD.left} x2={W - PAD.right} y1={y(t)} y2={y(t)} stroke="var(--color-rule-soft)" strokeWidth="1" />
              <text x={PAD.left - 6} y={y(t) + 3} textAnchor="end" className="tnum fill-muted text-[10px]">
                {t}
              </text>
            </g>
          ))}
          {xLabels.map((i) => (
            <text key={i} x={x(i)} y={H - 8} textAnchor={rows.length === 1 ? "middle" : i === 0 ? "start" : "end"} className="fill-muted text-[10px]">
              {dayFmt.format(new Date(rows[i].takenAt))}
            </text>
          ))}
          {hover !== null && <line x1={x(hover)} x2={x(hover)} y1={PAD.top} y2={PAD.top + plotH} stroke="var(--color-slate-300)" strokeWidth="1" />}
          {series.map((s) => {
            const pts = s.values.map((v, i) => (v === null ? null : ([x(i), y(v)] as const))).filter((p): p is readonly [number, number] => p !== null);
            const last = [...s.values].reverse().find((v) => v !== null);
            return (
              <g key={s.key}>
                <polyline points={pts.map((p) => p.join(",")).join(" ")} fill="none" stroke={s.stroke} strokeWidth="2" strokeDasharray={s.dash} strokeLinejoin="round" />
                {s.values.map((v, i) =>
                  v === null ? null : (
                    <circle
                      key={i}
                      cx={x(i)}
                      cy={y(v)}
                      r={hover === i ? 5 : 4}
                      fill={rows[i].flags.length > 0 ? "var(--color-surface)" : s.stroke}
                      stroke={s.stroke}
                      strokeWidth="2"
                    />
                  ),
                )}
                {pts.length > 0 && last !== undefined && (
                  <text x={W - PAD.right + 6} y={pts[pts.length - 1][1] + 4} className="tnum fill-ink text-[11px] font-bold">
                    {series.length > 1 ? `${s.label.slice(0, 3)} ${last}` : last}
                  </text>
                )}
              </g>
            );
          })}
          {/* Hit areas wider than the marks: one column per reading. */}
          {rows.map((r, i) => (
            <rect
              key={r.id}
              x={rows.length === 1 ? PAD.left : x(i) - plotW / (rows.length - 1) / 2}
              width={rows.length === 1 ? plotW : plotW / (rows.length - 1)}
              y={PAD.top}
              height={plotH}
              fill="transparent"
              onMouseEnter={() => setHover(i)}
            />
          ))}
        </svg>
        {hover !== null && rows[hover] && (
          <div
            className="pointer-events-none absolute top-1 rounded-control border border-rule bg-surface px-2.5 py-1.5 text-xs shadow-float"
            style={{ left: `${Math.min(70, Math.max(0, (x(hover) / W) * 100 - 10))}%` }}
            role="status"
          >
            <p className="text-muted">{formatStamp(rows[hover].takenAt)}</p>
            {series.map((s) => (
              <p key={s.key} className="tnum font-bold text-ink">
                {series.length > 1 ? `${s.label} ` : ""}
                {fmt(s.values[hover])} {unit}
              </p>
            ))}
            {rows[hover].flags.length > 0 && <p className="text-arterial-dark">{rows[hover].flags.join(", ")}</p>}
          </div>
        )}
      </div>
      <p className="text-xs text-muted">Hollow points have a flag. {band ? "The shaded band is the clinic's normal range." : ""}</p>
      <details>
        <summary className="cursor-pointer text-sm font-semibold text-scrub">Readings as a table</summary>
        <div className="mt-2 overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="text-xs text-muted">
              <tr>
                <th className="py-1.5 pr-3 font-bold">When</th>
                {series.map((s) => (
                  <th key={s.key} className="py-1.5 pr-3 font-bold">
                    {s.label} ({unit})
                  </th>
                ))}
                <th className="py-1.5 font-bold">Flags</th>
              </tr>
            </thead>
            <tbody>
              {[...rows].reverse().map((r) => {
                const i = rows.indexOf(r);
                return (
                  <tr key={r.id} className="border-t border-rule-soft">
                    <td className="tnum py-1.5 pr-3">{formatStamp(r.takenAt)}</td>
                    {series.map((s) => (
                      <td key={s.key} className="tnum py-1.5 pr-3">
                        {fmt(s.values[i])}
                      </td>
                    ))}
                    <td className={cx("py-1.5", r.flags.length > 0 && "text-arterial-dark")}>{r.flags.join(", ") || "—"}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </details>
    </div>
  );
}
