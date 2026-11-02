"use client";

import { useState, type FormEvent, type ReactNode } from "react";
import { toApiError, type ApiError } from "@/lib/api";
import { Button, Dialog, FormErrors, Textarea } from "./ui";

export const REASON_MIN = 10;
export const REASON_MAX = 300;

export function reasonProblem(reason: string): string | null {
  const n = reason.trim().length;
  if (n < REASON_MIN) return `Write at least ${REASON_MIN} characters.`;
  if (n > REASON_MAX) return `Keep it to ${REASON_MAX} characters.`;
  return null;
}

/**
 * Confirmation for actions that touch a customer or money: names the target and the effect in one sentence,
 * optionally asks for a typed reason (10–300 characters, counter shown), and the confirm button repeats the action.
 * `onConfirm` throws an ApiError to keep the dialog open with the message.
 */
export function ConfirmDialog({
  open,
  onClose,
  title,
  effect,
  confirmLabel,
  danger,
  reason,
  reasonLabel = "Reason",
  reasonHint = "Saved in the audit log with your name.",
  customer = true,
  children,
  fields = [],
  canConfirm = true,
  onConfirm,
}: {
  open: boolean;
  onClose: () => void;
  title: string;
  effect: ReactNode;
  confirmLabel: string;
  danger?: boolean;
  /** Ask for a typed reason (10–300 characters). */
  reason?: boolean;
  reasonLabel?: string;
  reasonHint?: string;
  customer?: boolean;
  /** Extra fields rendered above the reason. */
  children?: ReactNode | ((error: ApiError | null) => ReactNode);
  /** Field names the extra children render errors for. */
  fields?: string[];
  canConfirm?: boolean;
  onConfirm: (reason: string) => Promise<void>;
}) {
  return (
    <Dialog open={open} onClose={onClose} title={title} customer={customer}>
      {open && (
        <ConfirmBody
          effect={effect}
          confirmLabel={confirmLabel}
          danger={danger}
          reason={reason}
          reasonLabel={reasonLabel}
          reasonHint={reasonHint}
          fields={fields}
          canConfirm={canConfirm}
          onConfirm={onConfirm}
          onClose={onClose}
        >
          {children}
        </ConfirmBody>
      )}
    </Dialog>
  );
}

function ConfirmBody({
  effect,
  confirmLabel,
  danger,
  reason: needsReason,
  reasonLabel,
  reasonHint,
  fields,
  canConfirm,
  onConfirm,
  onClose,
  children,
}: {
  effect: ReactNode;
  confirmLabel: string;
  danger?: boolean;
  reason?: boolean;
  reasonLabel: string;
  reasonHint: string;
  fields: string[];
  canConfirm: boolean;
  onConfirm: (reason: string) => Promise<void>;
  onClose: () => void;
  children?: ReactNode | ((error: ApiError | null) => ReactNode);
}) {
  const [reason, setReason] = useState("");
  const [touched, setTouched] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const localProblem = needsReason ? reasonProblem(reason) : null;

  async function submit(e: FormEvent) {
    e.preventDefault();
    setTouched(true);
    if (localProblem || !canConfirm) return;
    setBusy(true);
    setError(null);
    try {
      await onConfirm(reason.trim());
    } catch (err) {
      setError(toApiError(err));
    } finally {
      setBusy(false);
    }
  }

  const reasonError = (touched ? localProblem : null) ?? error?.field("reason");

  return (
    <form onSubmit={submit} className="space-y-4" noValidate>
      <p className="text-base text-graphite">{effect}</p>
      {typeof children === "function" ? children(error) : children}
      {needsReason && (
        <Textarea
          label={reasonLabel}
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          onBlur={() => setTouched(true)}
          counter={{ min: REASON_MIN, max: REASON_MAX }}
          maxLength={REASON_MAX + 50}
          hint={reasonHint}
          error={reasonError ?? undefined}
          rows={3}
          required
        />
      )}
      <FormErrors error={error} fields={[...fields, "reason"]} />
      <div className="flex flex-wrap justify-end gap-2 pt-1">
        <Button variant="secondary" onClick={onClose}>
          Cancel
        </Button>
        <Button type="submit" variant={danger ? "danger" : "primary"} loading={busy} disabled={!canConfirm}>
          {confirmLabel}
        </Button>
      </div>
    </form>
  );
}
