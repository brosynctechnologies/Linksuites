import type { ThreadDto } from "@/lib/types/communication";

/** Threads, messages and the open thread refresh this often, and when the window regains focus. */
export const POLL_MS = 30_000;

/** Who the parent wrote to: the office or the named teacher. */
export function threadTo(t: ThreadDto): string {
  if (t.isOffice) return "School office";
  return t.staffName ?? "A former teacher";
}
