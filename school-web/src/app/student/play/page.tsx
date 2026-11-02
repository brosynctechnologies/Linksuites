"use client";

import { useState } from "react";
import type { ApiError } from "@/lib/api";
import { useApi } from "@/lib/use-api";
import type { PlayTeaserDto, WordSearchDto } from "@/lib/types/academic";
import { ApiErrorView, Badge, Button, EmptyState, Notice, PageHeader, PageLoader, SectionTitle, cx, panelClass } from "@/components/ui";

const PLAY_OFF = "Play is turned off for this school.";

function isPlayOff(error: ApiError | undefined): boolean {
  return !!error && error.status === 404 && (error.code === "play_off" || error.detail === PLAY_OFF);
}

export default function StudentPlayPage() {
  const puzzle = useApi<WordSearchDto>("/api/student/play/word-search");
  const teasers = useApi<PlayTeaserDto[]>("/api/student/play/teasers");
  const off = isPlayOff(puzzle.error) || isPlayOff(teasers.error);

  return (
    <div className="min-w-0 space-y-5">
      <PageHeader title="Play" subtitle="A word search and a few brain teasers. Nothing here counts towards your scores." />
      {off ? (
        <Notice>{PLAY_OFF} Ask your teacher if you think it should be on.</Notice>
      ) : (
        <>
          <section className={cx(panelClass, "w-full min-w-0 space-y-3 p-4")} aria-labelledby="word-search">
            <SectionTitle
              id="word-search"
              action={
                <Button variant="secondary" onClick={puzzle.reload} loading={puzzle.loading && !!puzzle.data}>
                  New puzzle
                </Button>
              }
            >
              Word search
            </SectionTitle>
            {puzzle.error && <ApiErrorView error={puzzle.error} onRetry={puzzle.reload} />}
            {!puzzle.data && !puzzle.error && <PageLoader label="Making a puzzle…" />}
            {puzzle.data && <WordSearch key={puzzle.data.grid.map((row) => row.join("")).join("|")} puzzle={puzzle.data} />}
          </section>

          <section className={cx(panelClass, "w-full min-w-0 space-y-3 p-4")} aria-labelledby="teasers">
            <SectionTitle
              id="teasers"
              action={
                <Button variant="secondary" onClick={teasers.reload} loading={teasers.loading && !!teasers.data}>
                  Other teasers
                </Button>
              }
            >
              Brain teasers
            </SectionTitle>
            {teasers.error && <ApiErrorView error={teasers.error} onRetry={teasers.reload} />}
            {!teasers.data && !teasers.error && <PageLoader />}
            {teasers.data && teasers.data.length === 0 && (
              <EmptyState title="No teasers yet" text="When your teachers add brain teasers, a few show here each time." />
            )}
            {teasers.data && teasers.data.length > 0 && (
              <ol className="min-w-0 list-decimal space-y-2 pl-5 text-sm text-slate-800">
                {teasers.data.map((t) => (
                  <li key={t.id} className="break-words whitespace-pre-line">
                    {t.prompt}
                  </li>
                ))}
              </ol>
            )}
            {teasers.data && teasers.data.length > 0 && <p className="text-xs text-muted">Talk it over with a classmate or your teacher.</p>}
          </section>
        </>
      )}
    </div>
  );
}

type Cell = { row: number; col: number };

/** Cells from `a` to `b` along one row (left to right) or one column (top to bottom). Null when they are not in a line. */
function cellsBetween(a: Cell, b: Cell): Cell[] | null {
  if (a.row === b.row) {
    const [from, to] = a.col <= b.col ? [a.col, b.col] : [b.col, a.col];
    return Array.from({ length: to - from + 1 }, (_, i) => ({ row: a.row, col: from + i }));
  }
  if (a.col === b.col) {
    const [from, to] = a.row <= b.row ? [a.row, b.row] : [b.row, a.row];
    return Array.from({ length: to - from + 1 }, (_, i) => ({ row: from + i, col: a.col }));
  }
  return null;
}

const key = (c: Cell) => `${c.row}:${c.col}`;

function WordSearch({ puzzle }: { puzzle: WordSearchDto }) {
  const size = puzzle.grid.length;
  const [start, setStart] = useState<Cell | null>(null);
  const [found, setFound] = useState<Record<string, string[]>>({});
  const [miss, setMiss] = useState(false);
  const foundCells = new Set(Object.values(found).flat());
  const done = puzzle.words.length > 0 && puzzle.words.every((w) => found[w]);

  function tap(cell: Cell) {
    setMiss(false);
    if (!start) {
      setStart(cell);
      return;
    }
    const line = cellsBetween(start, cell);
    setStart(null);
    if (!line || line.length < 2) return;
    const text = line.map((c) => puzzle.grid[c.row][c.col]).join("");
    const word = puzzle.words.find((w) => w === text && !found[w]);
    if (word) setFound((current) => ({ ...current, [word]: line.map(key) }));
    else setMiss(true);
  }

  return (
    <div className="min-w-0 space-y-3">
      <p className="text-sm text-muted">Tap the first letter of a word, then its last letter. Words read left to right or top to bottom.</p>
      <div className="w-full min-w-0 overflow-auto rounded-control border border-rule">
        <div
          role="group"
          aria-label="Word search letters"
          className="grid w-full max-w-xl"
          style={{ gridTemplateColumns: `repeat(${size}, minmax(1.75rem, 1fr))` }}
        >
          {puzzle.grid.map((row, r) =>
            row.map((letter, c) => {
              const cell = { row: r, col: c };
              const isStart = start?.row === r && start?.col === c;
              const isFound = foundCells.has(key(cell));
              return (
                <button
                  key={key(cell)}
                  type="button"
                  aria-label={`${letter}, row ${r + 1}, column ${c + 1}${isFound ? ", found" : ""}`}
                  aria-pressed={isStart}
                  onClick={() => tap(cell)}
                  className={cx(
                    "slab flex aspect-square min-h-7 items-center justify-center border-r border-b border-rule-soft text-sm font-bold",
                    isStart ? "bg-blazer text-white" : isFound ? "bg-pass-wash text-pass" : "bg-surface text-ink hover:bg-slate-50",
                  )}
                >
                  {letter}
                </button>
              );
            }),
          )}
        </div>
      </div>
      {miss && <p className="text-sm text-muted">That isn&apos;t one of the words. Try another pair of letters.</p>}
      <ul className="flex flex-wrap gap-1.5" aria-label="Words to find">
        {puzzle.words.map((w) => (
          <li key={w}>
            <Badge tone={found[w] ? "pass" : "neutral"} className={found[w] ? "line-through" : undefined}>
              {w}
            </Badge>
          </li>
        ))}
      </ul>
      <p className="text-sm text-slate-700" role="status">
        {done ? "You found every word. Try a new puzzle." : `${Object.keys(found).length} of ${puzzle.words.length} found.`}
      </p>
    </div>
  );
}
