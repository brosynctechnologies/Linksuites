"use client";

import { useApi } from "@/lib/use-api";
import type { LevelDto } from "@/lib/types";
import { Select } from "@/components/ui";

/** "Whole school" or one level: assessment and grading have a school default and optional per-level overrides. */
export function LevelScope({ value, onChange }: { value: string; onChange: (levelId: string) => void }) {
  const { data } = useApi<LevelDto[]>("/api/school/levels");
  const levels = [...(data ?? [])].sort((a, b) => a.order - b.order);
  return (
    <div className="max-w-xs">
      <Select label="Applies to" value={value} onChange={(e) => onChange(e.target.value)}>
        <option value="">The whole school</option>
        {levels.map((l) => (
          <option key={l.id} value={l.id}>
            {l.name} only
          </option>
        ))}
      </Select>
    </div>
  );
}
