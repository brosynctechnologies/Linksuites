"use client";

import { useId, useState } from "react";
import { formatDay } from "@/lib/format";
import type { PatientSummaryDto } from "@/lib/types";
import { useDebounced } from "@/lib/use-debounced";
import { qs, useApi } from "@/lib/use-api";
import { ErrorBox, PatientStatusBadge, SearchField, Spinner, Wristband, cx } from "./ui";

/**
 * Find a patient by name, number or phone and pick one. The search text stays in this component (never the address), and only
 * demographics come back (`GET /api/care/patients?q=`). Inactive and deceased patients are shown but can't be picked.
 */
export function PatientPicker({ onPick, label = "Find the patient" }: { onPick: (p: PatientSummaryDto) => void; label?: string }) {
  const id = useId();
  const [q, setQ] = useState("");
  const term = useDebounced(q.trim(), 300);
  const found = useApi<PatientSummaryDto[]>(term.length >= 2 ? `/api/care/patients${qs({ q: term })}` : null);
  const rows = term.length >= 2 ? (found.data ?? []) : [];

  return (
    <div className="space-y-2">
      <div className="flex items-center gap-2">
        <SearchField id={`${id}-q`} label={label} placeholder="Name, patient number or phone" value={q} onChange={setQ} className="flex-1" />
        {found.loading && <Spinner className="h-4 w-4 text-muted" />}
      </div>
      {found.error && <ErrorBox error={found.error} onRetry={found.reload} />}
      {term.length >= 2 && !found.loading && rows.length === 0 && !found.error && <p className="text-sm text-muted">No patient matches that.</p>}
      {rows.length > 0 && (
        <ul className="max-h-72 divide-y divide-rule-soft overflow-y-auto rounded-control border border-rule bg-surface" aria-label="Matching patients">
          {rows.map((p) => {
            const usable = p.status === "Active";
            return (
              <li key={p.id}>
                <button
                  type="button"
                  disabled={!usable}
                  onClick={() => onPick(p)}
                  className={cx("flex min-h-12 w-full flex-wrap items-center gap-x-3 gap-y-1 px-3 py-2 text-left text-sm", usable ? "hover:bg-scrub-wash/50" : "cursor-not-allowed opacity-70")}
                >
                  <Wristband number={p.number} />
                  <span className="font-bold text-ink">{p.fullName}</span>
                  <span className="tnum text-muted">
                    {p.sex}, born {formatDay(p.dateOfBirth)}
                  </span>
                  <PatientStatusBadge status={p.status} />
                </button>
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
