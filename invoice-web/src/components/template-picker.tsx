"use client";

import type { CSSProperties, ReactNode } from "react";
import Link from "next/link";
import { normalizeColor, secondAccent, tint } from "@/lib/templates";
import type { TemplateDto } from "@/lib/types";
import { LockIcon } from "./icons";
import { cx } from "./ui";

const GREY = "#c3cad5";
const INK = "#1e2430";

function Bar({ w, c = GREY, h = "h-1", className, style }: { w: string; c?: string; h?: string; className?: string; style?: CSSProperties }) {
  return <span className={cx("block shrink-0 rounded-[1px]", h, className)} style={{ width: w, background: c, ...style }} />;
}

/** Three item rows. */
function Rows({ zebra, rule }: { zebra?: boolean; rule?: string }) {
  return (
    <>
      {["90%", "80%", "85%"].map((w, i) => (
        <span key={i} className="block self-stretch py-px" style={{ background: zebra && i % 2 === 1 ? "#f0f2f5" : undefined, borderBottom: rule ? `1px solid ${rule}` : undefined }}>
          <Bar w={w} />
        </span>
      ))}
    </>
  );
}

/** Tiny schematic of each design so the picker is visual without rendering a full preview. */
function Thumb({ k, accent, second }: { k: string; accent: string; second: string }) {
  let body: ReactNode;
  switch (k) {
    case "modern":
      body = (
        <>
          <span className="-mx-1.5 -mt-1.5 block h-4" style={{ background: accent }} />
          <span className="-mx-1.5 mb-0.5 block h-0.5" style={{ background: second }} />
          <Bar w="50%" />
          <Bar w="100%" h="h-1.5" c={INK} />
          <Rows zebra />
          <Bar w="40%" h="h-1.5" c={tint(accent, 0.35)} className="mt-auto ml-auto" style={{ borderTop: `1px solid ${accent}` }} />
        </>
      );
      break;
    case "minimal":
      body = (
        <>
          <span className="flex justify-between">
            <Bar w="35%" c={INK} />
            <Bar w="25%" c={GREY} />
          </span>
          <span className="mt-1.5 block border-b" style={{ borderColor: INK }} />
          <Rows />
          <Bar w="30%" h="h-1.5" c={accent} className="mt-auto ml-auto" />
        </>
      );
      break;
    case "bold":
      body = (
        <>
          <Bar w="45%" c={INK} />
          <Bar w="75%" h="h-2.5" c={accent} />
          <Bar w="100%" h="h-1.5" c={accent} />
          <Rows />
          <Bar w="45%" h="h-2" c={accent} className="mt-auto ml-auto" />
        </>
      );
      break;
    case "elegant":
      body = (
        <>
          <Bar w="45%" c={INK} />
          <span className="block w-3/4 border-y-2 border-double py-0.5" style={{ borderColor: accent }}>
            <Bar w="60%" c={accent} className="mx-auto" />
          </span>
          <span className="block w-full border-b" style={{ borderColor: accent }} />
          <Rows />
          <span className="mt-auto block w-1/2 border-t-[3px] border-double pt-0.5" style={{ borderColor: accent }}>
            <Bar w="100%" h="h-1.5" c={INK} />
          </span>
        </>
      );
      break;
    case "executive":
      body = (
        <>
          <span className="-mx-1.5 -mt-1.5 flex h-5">
            <span className="block w-1/3" style={{ background: accent }} />
            <span className="flex flex-1 items-end px-1 pb-0.5">
              <Bar w="60%" c={accent} />
            </span>
          </span>
          <span className="-mx-1.5 mb-0.5 block h-0.5" style={{ background: second }} />
          <Bar w="100%" h="h-1.5" c={second} />
          <Rows />
          <span className="mt-auto ml-auto block w-1/2 rounded-[1px] border border-rule bg-slate-50 p-0.5">
            <Bar w="100%" c={second} />
          </span>
        </>
      );
      break;
    case "corporate":
      body = (
        <>
          <span className="flex justify-between">
            <Bar w="35%" c={INK} />
            <Bar w="30%" c={accent} />
          </span>
          <span className="block border-y py-0.5" style={{ borderColor: accent }}>
            <Bar w="85%" />
          </span>
          <span className="block border border-rule">
            <Bar w="100%" h="h-1" c="#e8ebf0" />
            <Rows rule="#e8ebf0" />
          </span>
          <Bar w="45%" h="h-1.5" c={tint(accent, 0.3)} className="mt-auto ml-auto" style={{ borderTop: `1px solid ${accent}` }} />
        </>
      );
      break;
    case "ledger":
      body = (
        <>
          <span className="flex items-start justify-between">
            <Bar w="40%" c={INK} />
            <span className="block w-1/4 border border-graphite p-px">
              <Bar w="100%" h="h-0.5" c={INK} />
            </span>
          </span>
          <span className="block border-t border-b-2 py-0.5" style={{ borderTopColor: INK, borderBottomColor: accent }}>
            <Bar w="40%" c={INK} />
          </span>
          <span className="block border-b-2 border-graphite" />
          <Rows rule={GREY} />
          <span className="mt-auto ml-auto block w-1/2 border-[3px] border-double border-graphite p-px">
            <Bar w="100%" h="h-1" c={INK} />
          </span>
        </>
      );
      break;
    case "studio":
      body = (
        <>
          <span className="absolute top-0 right-0 block h-4 w-[38%]" style={{ background: second }} />
          <span className="absolute top-0 right-0 block size-6 rounded-bl-full" style={{ background: accent }} />
          <Bar w="55%" h="h-2" c={INK} />
          <Bar w="35%" />
          <span className="mt-1 block border-b-2 border-graphite" />
          <Rows />
          <Bar w="50%" h="h-2.5" c={accent} className="mt-auto ml-auto rounded-[2px]" />
        </>
      );
      break;
    case "prestige":
      body = (
        <>
          <span className="block border p-px" style={{ borderColor: accent }}>
            <span className="block size-2.5 border" style={{ borderColor: accent }} />
          </span>
          <Bar w="45%" c={INK} />
          <span className="block h-px w-2/5" style={{ background: second }} />
          <Bar w="35%" c={accent} />
          <span className="block w-full border-b" style={{ borderColor: second }} />
          <Rows />
          <span className="mt-auto ml-auto block w-1/2 border-y py-0.5" style={{ borderColor: second }}>
            <Bar w="100%" c={accent} />
          </span>
        </>
      );
      break;
    default:
      // classic
      body = (
        <>
          <span className="flex justify-between">
            <Bar w="35%" c={INK} />
            <Bar w="30%" c={accent} />
          </span>
          <span className="block h-px" style={{ background: accent }} />
          <Bar w="100%" h="h-1.5" c={tint(accent, 0.2)} />
          <Rows />
          <Bar w="40%" h="h-1.5" c={accent} className="mt-auto ml-auto" />
        </>
      );
  }
  return (
    <span
      className={cx(
        "relative flex aspect-[210/297] w-full flex-col gap-1 overflow-hidden rounded-[2px] bg-paper p-1.5 shadow-[0_1px_2px_rgb(30_36_48/0.12),0_4px_10px_-4px_rgb(30_36_48/0.2)]",
        k === "bold" && "border-l-4",
        (k === "elegant" || k === "prestige") && "items-center",
      )}
      style={k === "bold" ? { borderLeftColor: accent } : undefined}
      aria-hidden="true"
    >
      {body}
    </span>
  );
}

export function TemplatePicker({
  templates,
  value,
  onChange,
  brandColor,
  secondaryColor,
  label = "Template",
}: {
  templates: TemplateDto[];
  value: string;
  onChange: (key: string) => void;
  brandColor?: string | null;
  secondaryColor?: string | null;
  label?: string;
}) {
  const accent = normalizeColor(brandColor);
  const second = secondAccent(accent, secondaryColor);
  const anyLocked = templates.some((t) => t.isPremium && t.available === false);
  return (
    <fieldset>
      <legend className="mb-2 block text-sm font-medium text-slate-700">{label}</legend>
      <div className="grid grid-cols-[repeat(auto-fill,minmax(5.25rem,1fr))] gap-2.5 sm:grid-cols-5">
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
              <Thumb k={t.key} accent={accent} second={second} />
              <span className="flex flex-wrap items-center justify-center gap-1">
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
          The five premium templates are part of Pro.{" "}
          <Link href="/app/billing" className="font-semibold text-ink underline-offset-2 hover:underline">
            Upgrade to use them.
          </Link>
        </p>
      )}
    </fieldset>
  );
}
