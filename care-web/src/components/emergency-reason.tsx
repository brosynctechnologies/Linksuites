"use client";

import Link from "next/link";
import { useCallback, useEffect, useRef, useState, type FormEvent, type ReactNode } from "react";
import { api, ApiError, toApiError } from "@/lib/api";
import { reasonHeaders } from "@/lib/care";
import { Button, Card, ErrorBox, Modal, PageHeader, Textarea, buttonClass } from "./ui";

// Emergency access (Task 3 rulings): without a care relationship the API answers 409 `emergency_reason_required`. The reason goes in
// the X-Care-Reason header (UTF-8, percent-encoded), never in the address, and is kept only in memory for the page's later reads.

export const REASON_REQUIRED = "emergency_reason_required";

type ReasonHeaders = Record<string, string> | undefined;

/** The reason form. Shown as a page in place of a record the member isn't caring for (Task 17's screen, shared). */
export function EmergencyReason({
  onSubmit,
  back = "/app/patients",
  backLabel = "Go back",
}: {
  onSubmit: (reason: string) => Promise<ApiError | null>;
  back?: string;
  backLabel?: string;
}) {
  return (
    <div className="space-y-5">
      <PageHeader title="Give a reason to open this patient's record." back={back} />
      <Card className="max-w-xl border-l-[3px] border-l-iodine">
        <ReasonForm onSubmit={onSubmit} actions={<Link href={back} className={buttonClass("ghost")}>{backLabel}</Link>} />
      </Card>
    </div>
  );
}

function ReasonForm({ onSubmit, actions }: { onSubmit: (reason: string) => Promise<ApiError | null>; actions: ReactNode }) {
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    const err = await onSubmit(reason.trim());
    setError(err);
    setBusy(false);
  }

  return (
    <form onSubmit={submit} className="space-y-4">
      <p className="text-sm text-slate-700">
        You aren&apos;t caring for this patient now. You can still open the record in an emergency: your reason is logged and the medical director is told.
      </p>
      <Textarea
        label="Reason"
        required
        maxLength={300}
        value={reason}
        onChange={(e) => setReason(e.target.value)}
        error={error?.field("reason")}
        hint="Up to 300 characters, such as covering for the patient's doctor."
      />
      <ErrorBox error={error && !error.field("reason") ? error : null} />
      <div className="flex flex-wrap gap-2">
        <Button type="submit" loading={busy} disabled={!reason.trim()}>
          Open the record
        </Button>
        {actions}
      </div>
    </form>
  );
}

/** The same form in a dialog, for an action on an open page that the API refuses without a reason. */
function ReasonDialog({ onSubmit, onCancel }: { onSubmit: (reason: string) => Promise<ApiError | null>; onCancel: () => void }) {
  return (
    <Modal open title="Give a reason to open this patient's record." onClose={onCancel}>
      <ReasonForm
        onSubmit={onSubmit}
        actions={
          <Button variant="ghost" onClick={onCancel}>
            Cancel
          </Button>
        }
      />
    </Modal>
  );
}

export type Gate<T> = { kind: "loading" } | { kind: "ok"; data: T } | { kind: "reason" } | { kind: "error"; error: ApiError };

interface Pending {
  fn: (headers: ReasonHeaders) => Promise<unknown>;
  resolve: (v: unknown) => void;
  reject: (e: unknown) => void;
}

/**
 * Loads one clinical read (`GET`, or the consultation's `POST` open) and handles emergency access:
 * - `gate.kind === "reason"` → render `<EmergencyReason onSubmit={submitReason} />`;
 * - `run(fn)` performs a later clinical call with the accepted reason, and on a 409 `emergency_reason_required` shows the reason dialog
 *   (`dialog`) and retries with the reason given.
 * Nothing is stored outside this component's memory.
 */
export function useClinicalRead<T>(path: string | null, method: "GET" | "POST" = "GET") {
  const reasonRef = useRef<string | null>(null);
  const [nonce, setNonce] = useState(0);
  const [state, setState] = useState<{ key: string | null; gate: Gate<T> }>({ key: null, gate: { kind: "loading" } });
  // Every call waiting for a reason: one dialog answers them all, so none is left hanging.
  const [pending, setPending] = useState<Pending[]>([]);
  const key = path ? `${method} ${path}#${nonce}` : null;

  useEffect(() => {
    if (!path || !key) return;
    let live = true;
    api<T>(path, { method, headers: reasonHeaders(reasonRef.current) }).then(
      (data) => {
        if (live) setState({ key, gate: { kind: "ok", data } });
      },
      (e) => {
        if (!live) return;
        const error = toApiError(e);
        setState({ key, gate: error.code === REASON_REQUIRED ? { kind: "reason" } : { kind: "error", error } });
      },
    );
    return () => {
      live = false;
    };
  }, [path, method, key]);

  /** Opens the read with a reason. Returns the error to show on the form, or null once open. */
  const submitReason = useCallback(
    async (reason: string): Promise<ApiError | null> => {
      if (!path) return null;
      try {
        const data = await api<T>(path, { method, headers: reasonHeaders(reason) });
        reasonRef.current = reason;
        setState({ key, gate: { kind: "ok", data } });
        return null;
      } catch (e) {
        return toApiError(e);
      }
    },
    [path, method, key],
  );

  /** A later clinical call on the same patient: sends the accepted reason, and asks for one if the API needs it. */
  const run = useCallback(<R,>(fn: (headers: ReasonHeaders) => Promise<R>): Promise<R> => {
    return fn(reasonHeaders(reasonRef.current)).catch((e: unknown) => {
      const error = toApiError(e);
      if (error.code !== REASON_REQUIRED) throw error;
      return new Promise<R>((resolve, reject) => setPending((list) => [...list, { fn, resolve: resolve as (v: unknown) => void, reject }]));
    });
  }, []);

  const reload = useCallback(() => setNonce((n) => n + 1), []);
  const setData = useCallback(
    (updater: (prev: T) => T) =>
      setState((s) => (s.gate.kind === "ok" ? { ...s, gate: { kind: "ok", data: updater(s.gate.data) } } : s)),
    [],
  );

  // While a reload runs, keep showing what was open.
  const gate: Gate<T> = state.key === key ? state.gate : state.gate.kind === "ok" ? state.gate : { kind: "loading" };

  const dialog =
    pending.length > 0 ? (
      <ReasonDialog
        onCancel={() => {
          const refused = new ApiError(409, { detail: "Give a reason to open this patient's record.", code: REASON_REQUIRED });
          pending.forEach((p) => p.reject(refused));
          setPending((list) => list.filter((p) => !pending.includes(p)));
        }}
        onSubmit={async (reason) => {
          // The first waiting call proves the reason; the rest then run with it, each settling on its own.
          const [first, ...rest] = pending;
          try {
            const value = await first.fn(reasonHeaders(reason));
            reasonRef.current = reason;
            first.resolve(value);
          } catch (e) {
            const error = toApiError(e);
            if (error.field("reason")) return error;
            first.reject(error);
          }
          for (const p of rest) p.fn(reasonHeaders(reasonRef.current)).then(p.resolve, p.reject);
          setPending((list) => list.filter((p) => !pending.includes(p)));
          return null;
        }}
      />
    ) : null;

  return { gate, submitReason, run, reload, setData, dialog };
}
