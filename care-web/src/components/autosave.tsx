"use client";

// The draft autosave used by ward rounds and the discharge summary (Task 19). It is the consultation's approach from Task 18 with its two
// fix rounds, lifted into a hook (the consultation page keeps its own copy and is not changed):
// - the whole draft is PUT 1.5 seconds after typing stops; saves run one after another, so the last edit lands last;
// - leaving in-app saves what is left through the same chain (an ordinary PUT, never keepalive);
// - closing the tab sends one keepalive PUT only when the body is at most 60,000 bytes (browsers cap keepalive at 64 KB), and the browser
//   warns before leaving while anything is unsaved;
// - a 409 `note_signed` or a 403 locks the editor at once: no further save, on leave either, and the text that didn't reach the API is
//   handed back so the page can show it to copy;
// - signing first saves any pending edit, re-saving up to three times if edits slipped in, then the autosave timer restarts if signing
//   didn't go through.
// Nothing is kept in browser storage and nothing is logged.

import { useCallback, useEffect, useRef, useState } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { Button, Card, Textarea } from "./ui";
import { lagosTime } from "./check-in";

const AUTOSAVE_MS = 1500;
const KEEPALIVE_MAX = 60_000;

export interface Autosave<B> {
  /** Record an edit: the new whole draft. */
  edit: (next: B) => void;
  dirty: boolean;
  saving: boolean;
  saveError: ApiError | null;
  /** "Saving…", "Saved at 14:05", "Unsaved changes", "Not saved"… */
  status: string;
  /** Save now (the retry button). */
  saveNow: () => void;
  /** Before signing: lands every pending edit. False when a save failed or edits kept coming. Call `endSigning` in a finally. */
  flushForSigning: () => Promise<boolean>;
  endSigning: () => void;
  /** Stop saving for good (signed, discarded or taken over). */
  lock: () => void;
  /** True once the editor is locked: the fields go read-only (Task 18 carry R1). */
  locked: boolean;
  /** Before a discard: stops every later save and waits for any save already sent, so none lands after the discard. */
  stopAndWait: () => Promise<void>;
  /** Saving again after a lock that did not hold (a refused discard). */
  unlock: () => void;
}

/**
 * @param url the draft's PUT route
 * @param initial the draft as loaded
 * @param savedAt when the API last stored it
 * @param onSaved the API's answer to each save
 * @param onLocked the API refused the draft (signed meanwhile, or no longer the author): its sentence and what wasn't saved
 */
export function useAutosave<B, R extends { updatedAt: string | null; createdAt?: string }>(
  url: string,
  initial: B,
  savedAt: string | null,
  onSaved: (r: R) => void,
  onLocked: (message: string, unsaved: B | null, code?: string) => void,
): Autosave<B> & { body: B } {
  const [body, setBody] = useState<B>(initial);
  const [dirty, setDirty] = useState(false);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<ApiError | null>(null);
  const [lastSaved, setLastSaved] = useState<string | null>(savedAt);
  const [retry, setRetry] = useState(0);
  const edits = useRef(0);
  const chain = useRef<Promise<boolean>>(Promise.resolve(true));
  const signing = useRef(false);
  const locked = useRef(false);
  const [isLocked, setIsLocked] = useState(false);
  const latest = useRef<{ body: B; dirty: boolean }>({ body: initial, dirty: false });
  const callbacks = useRef({ onSaved, onLocked });
  useEffect(() => {
    callbacks.current = { onSaved, onLocked };
  }, [onSaved, onLocked]);
  useEffect(() => {
    latest.current = { body, dirty };
  }, [body, dirty]);

  const persist = useCallback(
    (request: B): Promise<boolean> => {
      const at = edits.current;
      chain.current = chain.current.then(async () => {
        if (locked.current) return false;
        setSaving(true);
        try {
          const saved = await api<R>(url, { method: "PUT", body: request });
          setSaveError(null);
          // A first save that created the draft has no updatedAt yet: its creation time is the save time.
          setLastSaved(saved.updatedAt ?? saved.createdAt ?? null);
          if (edits.current === at) setDirty(false);
          callbacks.current.onSaved(saved);
          return true;
        } catch (e) {
          const error = toApiError(e);
          // Signed meanwhile, discarded by the director (the discharge summary), no longer the author or on the care team (403), or the
          // patient was discharged (ward rounds): stop.
          if (error.code === "note_signed" || error.code === "note_discarded" || error.code === "admission_discharged" || error.status === 403) {
            locked.current = true;
            setIsLocked(true);
            callbacks.current.onLocked(error.message, request, error.code);
            return false;
          }
          setSaveError(error);
          return false;
        } finally {
          setSaving(false);
        }
      });
      return chain.current;
    },
    [url],
  );

  useEffect(() => {
    if (!dirty) return;
    const t = setTimeout(() => {
      if (!signing.current && !locked.current) void persist(latest.current.body);
    }, AUTOSAVE_MS);
    return () => clearTimeout(t);
  }, [dirty, body, persist, retry]);

  // Leaving in-app: save what is left (the chain outlives the editor).
  useEffect(() => {
    const draft = latest;
    const lock = locked;
    return () => {
      if (draft.current.dirty && !lock.current) void persist(draft.current.body);
    };
  }, [persist]);

  // Closing the tab: one keepalive save under the browsers' cap, and the browser's own warning meanwhile.
  useEffect(() => {
    const onHide = () => {
      if (!latest.current.dirty || locked.current) return;
      if (new Blob([JSON.stringify(latest.current.body)]).size > KEEPALIVE_MAX) return;
      void api(url, { method: "PUT", body: latest.current.body, keepalive: true }).catch(() => undefined);
    };
    window.addEventListener("pagehide", onHide);
    return () => window.removeEventListener("pagehide", onHide);
  }, [url]);

  useEffect(() => {
    if (!dirty) return;
    const warn = (e: BeforeUnloadEvent) => {
      e.preventDefault();
      e.returnValue = "";
    };
    window.addEventListener("beforeunload", warn);
    return () => window.removeEventListener("beforeunload", warn);
  }, [dirty]);

  const edit = useCallback((next: B) => {
    edits.current++;
    latest.current = { body: next, dirty: true };
    setBody(next);
    setDirty(true);
  }, []);

  const flushForSigning = useCallback(async () => {
    signing.current = true;
    let at = edits.current;
    if (!(latest.current.dirty ? await persist(latest.current.body) : await chain.current)) return false;
    for (let i = 0; i < 3 && edits.current !== at; i++) {
      at = edits.current;
      if (!(await persist(latest.current.body))) return false;
    }
    return edits.current === at;
  }, [persist]);

  const endSigning = useCallback(() => {
    signing.current = false;
    setRetry((n) => n + 1);
  }, []);

  const lock = useCallback(() => {
    locked.current = true;
    setIsLocked(true);
  }, []);

  const stopAndWait = useCallback(async () => {
    // Locked first: the timer, the queue, the leave-save and pagehide all check it, so nothing new is sent. Then the save already
    // in flight (if any) settles before the caller's discard goes out, so it can't re-create the draft afterwards.
    locked.current = true;
    setIsLocked(true);
    await chain.current.catch(() => false);
  }, []);

  const status = saving ? "Saving…" : saveError ? "Not saved" : dirty ? "Unsaved changes" : lastSaved ? `Saved at ${lagosTime(lastSaved)}` : "Not saved yet";
  const unlock = useCallback(() => {
    locked.current = false;
    setIsLocked(false);
    setRetry((n) => n + 1);
  }, []);

  return {
    body,
    edit,
    dirty,
    saving,
    saveError,
    status,
    saveNow: () => void persist(latest.current.body),
    flushForSigning,
    endSigning,
    lock,
    locked: isLocked,
    stopAndWait,
    unlock,
  };
}

/** What didn't reach the API when a draft locked, shown read-only to copy (never stored). */
export function UnsavedBox({ text }: { text: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <Card className="space-y-2 border-l-[3px] border-l-iodine">
      <h2 className="font-semibold text-ink">Your last version of the draft wasn&apos;t saved</h2>
      <p className="text-sm text-slate-700">
        The note locked before it reached the record. It is shown here only, and goes when you leave this page. Copy what you need into an addendum, or pass it to the
        note&apos;s doctor.
      </p>
      <Textarea label="Unsaved text" srLabel readOnly rows={6} value={text} onFocus={(e) => e.currentTarget.select()} />
      <Button
        variant="secondary"
        onClick={async () => {
          try {
            await navigator.clipboard.writeText(text);
            setCopied(true);
          } catch {
            setCopied(false);
          }
        }}
      >
        {copied ? "Copied" : "Copy the text"}
      </Button>
    </Card>
  );
}
