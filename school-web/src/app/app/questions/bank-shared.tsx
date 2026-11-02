"use client";

import type { LevelDto } from "@/lib/types";
import type { BankQuestionDto } from "@/lib/types/academic";
import { Badge, Button, RuledSheet } from "@/components/ui";
import { score } from "../scores/academic-shared";

/**
 * The bank as a ruled list. The answer is shown: this is staff only. Pass `onPick` to tick questions for a paper, and
 * `onArchive` to offer archive and restore on each question.
 */
export function BankList({
  questions,
  levels,
  busy,
  picked,
  onPick,
  onArchive,
  archivingId,
}: {
  questions: BankQuestionDto[];
  levels: LevelDto[];
  busy?: boolean;
  picked?: Set<string>;
  onPick?: (id: string, on: boolean) => void;
  onArchive?: (question: BankQuestionDto, archived: boolean) => void;
  archivingId?: string | null;
}) {
  return (
    <RuledSheet label="Questions" busy={busy}>
      {questions.map((q, n) => (
        <li key={q.id} className="ruled-pad w-full min-w-0 space-y-2 py-3">
          <div className="flex min-w-0 items-start gap-2.5">
            {onPick && (
              <input
                type="checkbox"
                aria-label={`Pick question ${n + 1}`}
                className="mt-0.5 h-4 w-4 shrink-0 rounded border-rule accent-[#6b1d33]"
                checked={picked?.has(q.id) ?? false}
                onChange={(e) => onPick(q.id, e.target.checked)}
              />
            )}
            <p className="min-w-0 flex-1 text-sm font-bold break-words whitespace-pre-line text-ink">
              {n + 1}. {q.stem}
            </p>
            {q.archived && <Badge tone="crest">Archived</Badge>}
          </div>
          <ol className="min-w-0 space-y-1 pl-5 text-sm text-slate-800" style={{ listStyleType: "upper-alpha" }}>
            {q.options.map((o, i) => (
              <li key={i} className="break-words">
                {o} {i === q.correctIndex && <Badge tone="pass">Answer</Badge>}
              </li>
            ))}
          </ol>
          <div className="flex min-w-0 flex-wrap items-center justify-between gap-2">
            <p className="text-xs text-muted">
              {score(q.marks)} {q.marks === 1 ? "mark" : "marks"} · {levels.find((l) => l.id === q.levelId)?.name ?? "Every level"}
              {q.archived ? " · not used in new tests" : ""}
            </p>
            {onArchive && (
              <Button
                variant="secondary"
                loading={archivingId === q.id}
                disabled={!!archivingId && archivingId !== q.id}
                onClick={() => onArchive(q, !q.archived)}
              >
                {q.archived ? "Restore" : "Archive"}
              </Button>
            )}
          </div>
        </li>
      ))}
    </RuledSheet>
  );
}
