import type { CbtAttemptMarkedDto, CbtAttemptOpenDto, CbtAttemptViewDto, StudentCbtPaperDto } from "@/lib/types/academic";

export function isOpenAttempt(view: CbtAttemptViewDto): view is CbtAttemptOpenDto {
  return !("submittedAt" in view);
}

export function isMarkedAttempt(view: CbtAttemptViewDto): view is CbtAttemptMarkedDto {
  return "submittedAt" in view && "items" in view;
}

/** Where the paper's window is now: not open yet, open, or closed. */
export function windowState(paper: Pick<StudentCbtPaperDto, "openAt" | "closeAt">, now = Date.now()): "upcoming" | "open" | "closed" {
  if (now < new Date(paper.openAt).getTime()) return "upcoming";
  if (now >= new Date(paper.closeAt).getTime()) return "closed";
  return "open";
}

/** 1:05:09 or 4:07. */
export function clock(ms: number): string {
  const total = Math.max(0, Math.ceil(ms / 1000));
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  const pad = (n: number) => String(n).padStart(2, "0");
  return h > 0 ? `${h}:${pad(m)}:${pad(s)}` : `${m}:${pad(s)}`;
}
