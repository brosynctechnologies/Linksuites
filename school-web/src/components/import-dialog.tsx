"use client";

import { useCallback, useId, useRef, useState } from "react";
import { api, downloadFile, upload, type ApiError } from "@/lib/api";
import { IMPORT_MAX_BYTES, IMPORT_MAX_ROWS } from "@/lib/config";
import { plural } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { qs } from "@/lib/use-api";
import type { ImportApplied, ImportDialogProps, ImportPreviewResult, ImportPreviewRow, ImportStatus } from "@/lib/types/imports";
import { DownloadIcon, UploadIcon } from "@/components/icons";
import { Badge, Button, ErrorBox, Modal, Notice, Toggle, cx, type BadgeTone } from "@/components/ui";

const TONE: Record<ImportStatus, BadgeTone> = { New: "pass", Update: "crest", Unchanged: "neutral", Error: "brick" };

/**
 * The rows of a checked file: row number, result, what the row is, and the message. The table scrolls inside its own box, so it
 * works at 375px. Shared by every import (the dialog and the student import page).
 */
export function ImportPreviewTable({ rows, labelHeading = "Row details", caption = "Rows in the file" }: { rows: ImportPreviewRow[]; labelHeading?: string; caption?: string }) {
  return (
    <div className="w-full min-w-0 overflow-x-auto rounded-panel border border-rule bg-surface">
      <table className="w-full min-w-[36rem] text-left text-sm">
        <caption className="sr-only">{caption}</caption>
        <thead className="border-b border-rule bg-slate-50 text-xs font-bold text-muted">
          <tr>
            <th scope="col" className="w-16 px-3 py-2.5">
              Row
            </th>
            <th scope="col" className="w-28 px-3 py-2.5">
              Result
            </th>
            <th scope="col" className="px-3 py-2.5">
              {labelHeading}
            </th>
            <th scope="col" className="px-3 py-2.5">
              Message
            </th>
          </tr>
        </thead>
        <tbody className="divide-y divide-rule-soft">
          {rows.map((r) => (
            <tr key={r.row} className={cx(r.status === "Error" && "bg-brick-wash/40")}>
              <td className="tnum px-3 py-2.5 align-top text-muted">{r.row}</td>
              <td className="px-3 py-2.5 align-top">
                <Badge tone={TONE[r.status] ?? "neutral"}>{r.status}</Badge>
              </td>
              <td className="px-3 py-2.5 align-top font-bold break-words text-ink">{r.label || "—"}</td>
              <td className={cx("px-3 py-2.5 align-top break-words", r.status === "Error" ? "text-brick-dark" : "text-muted")}>{r.message ?? ""}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/** Picks a file, checks its type and size here so a wrong file fails fast, then posts it for a preview. */
function FilePicker({
  path,
  label,
  variant = "primary",
  onPreview,
}: {
  path: string;
  label: string;
  variant?: "primary" | "secondary";
  onPreview: (p: ImportPreviewResult, fileName: string) => void;
}) {
  const input = useRef<HTMLInputElement>(null);
  const { busy, error, setError, run } = useAction();
  const [local, setLocal] = useState<string | null>(null);

  async function choose(file: File) {
    setLocal(null);
    setError(null);
    if (!/\.(xlsx|csv)$/i.test(file.name)) return setLocal("Upload an .xlsx or .csv file. Download the template to start.");
    if (file.size > IMPORT_MAX_BYTES) return setLocal("This file is larger than 5 MB. Split it into smaller files and upload them one at a time.");
    const res = await run(() => upload<ImportPreviewResult>(path, file));
    if (res.ok) onPreview(res.data, file.name);
  }

  return (
    <div className="space-y-2">
      <input
        ref={input}
        type="file"
        accept=".xlsx,.csv,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet,text/csv"
        className="sr-only"
        aria-label="Choose a file to import"
        onChange={(e) => {
          const file = e.target.files?.[0];
          e.target.value = "";
          if (file) void choose(file);
        }}
      />
      <Button variant={variant} loading={busy} onClick={() => input.current?.click()}>
        <UploadIcon className="h-4 w-4" /> {busy ? "Checking the file…" : label}
      </Button>
      {local && <Notice tone="error">{local}</Notice>}
      <ErrorBox error={error} />
    </div>
  );
}

/**
 * "Import from Excel" for one kind: template downloads (.xlsx and .csv), a file picker, the preview (counts, warnings, every row), Confirm
 * and the result. Nothing is saved until Confirm, which saves every row or none. Pages hide it where the user cannot use that import.
 */
export function ImportDialog({ kind, scope, title, label, onDone, disabledReason, beforeOpen }: ImportDialogProps) {
  const [open, setOpen] = useState(false);
  const [opening, setOpening] = useState(false);
  const hintId = useId();
  const [preview, setPreview] = useState<ImportPreviewResult | null>(null);
  const [fileName, setFileName] = useState("");
  const [result, setResult] = useState<ImportApplied | null>(null);
  const [confirmError, setConfirmError] = useState<ApiError | null>(null);
  const [errorsOnly, setErrorsOnly] = useState(false);
  const template = useAction();
  const confirm = useAction();
  // Set before the request starts, so a second click before the busy state renders sends nothing.
  const confirming = useRef(false);

  const query = qs(scope ?? {});
  const base = `/api/school/imports/${encodeURIComponent(kind)}`;

  function reset() {
    setPreview(null);
    setFileName("");
    setResult(null);
    setConfirmError(null);
    setErrorsOnly(false);
  }

  const close = useCallback(() => {
    setOpen(false);
    reset();
  }, []);

  async function start() {
    if (opening) return;
    if (beforeOpen) {
      setOpening(true);
      try {
        if (!(await beforeOpen())) return;
      } finally {
        setOpening(false);
      }
    }
    setOpen(true);
  }

  function showPreview(p: ImportPreviewResult, name: string) {
    setPreview(p);
    setFileName(name);
    setResult(null);
    setConfirmError(null);
    setErrorsOnly(p.counts.error > 0);
  }

  async function save() {
    if (!preview || confirming.current) return;
    confirming.current = true;
    setConfirmError(null);
    try {
      const res = await confirm.run(() => api<ImportApplied>(`/api/school/imports/${encodeURIComponent(preview.token)}/confirm`, { method: "POST" }));
      if (res.ok) {
        setResult(res.data);
        onDone?.();
      } else setConfirmError(res.error);
    } finally {
      confirming.current = false;
    }
  }

  // Rows the confirm refused (409 import_errors) replace the preview's rows; on the generic route they have the preview row shape.
  const refused = confirmError?.code === "import_errors" ? ((confirmError.rows ?? []) as unknown as ImportPreviewRow[]) : null;
  const counts = preview?.counts;
  const changes = counts ? counts.new + counts.update : 0;
  // A timetable file that only removes lessons still has something to save.
  const removes = counts?.remove ?? 0;
  const toSave = changes + removes;
  // An expired or unknown token cannot be confirmed again; the file has to be uploaded again.
  const expired = confirmError?.status === 404;
  const shown = refused ?? (preview ? (errorsOnly ? preview.rows.filter((r) => r.status === "Error") : preview.rows) : []);

  return (
    <>
      <span className="inline-flex flex-col items-start gap-1">
        <Button
          variant="secondary"
          onClick={() => void start()}
          loading={opening}
          disabled={!!disabledReason}
          aria-describedby={disabledReason ? hintId : undefined}
        >
          <UploadIcon className="h-4 w-4" /> {label ?? "Import from Excel"}
        </Button>
        {disabledReason && (
          <span id={hintId} className="max-w-56 text-xs text-muted">
            {disabledReason}
          </span>
        )}
      </span>
      <Modal open={open} title={title} onClose={close} wide>
        <div className="min-w-0 space-y-4">
          {result ? (
            <div className="space-y-3" role="status">
              {result.alreadyImported && <Notice tone="info">This file was already imported.</Notice>}
              <p className="text-slate-700">
                {result.removed
                  ? `Added ${plural(result.created, "row")}, updated ${plural(result.updated, "row")} and removed ${plural(result.removed, "row")} from ${fileName || "your file"}.`
                  : `Added ${plural(result.created, "row")} and updated ${plural(result.updated, "row")} from ${fileName || "your file"}.`}
              </p>
              <div className="flex flex-wrap gap-2">
                <Button onClick={close}>Done</Button>
                <Button variant="ghost" onClick={reset}>
                  Import another file
                </Button>
              </div>
            </div>
          ) : !preview ? (
            <>
              <section className="space-y-2">
                <h3 className="font-semibold text-ink">1. Fill in the template</h3>
                <p className="text-sm text-slate-700">
                  Download the template, fill in one row per record and keep the heading row. The &ldquo;How to fill in&rdquo; sheet explains each column.
                </p>
                <div className="flex flex-wrap gap-2">
                  <Button
                    variant="secondary"
                    loading={template.busy}
                    onClick={() => template.run(() => downloadFile(`${base}/template.xlsx${query}`, `schoollink-${kind}-template.xlsx`))}
                  >
                    <DownloadIcon className="h-4 w-4" /> Excel template
                  </Button>
                  <Button
                    variant="secondary"
                    loading={template.busy}
                    onClick={() => template.run(() => downloadFile(`${base}/template.csv${query}`, `schoollink-${kind}-template.csv`))}
                  >
                    <DownloadIcon className="h-4 w-4" /> CSV template
                  </Button>
                </div>
                <ErrorBox error={template.error} />
              </section>
              <section className="space-y-2">
                <h3 className="font-semibold text-ink">2. Upload and check</h3>
                <p className="text-sm text-slate-700">
                  An .xlsx or .csv file, up to {IMPORT_MAX_ROWS.toLocaleString("en-NG")} rows and 5 MB. You&apos;ll see every row before anything is saved.
                </p>
                <FilePicker path={`${base}/preview${query}`} label="Upload a file" onPreview={showPreview} />
              </section>
            </>
          ) : (
            <>
              <p className="text-sm text-muted">Checked {fileName}</p>
              <dl className="grid grid-cols-2 gap-2 sm:grid-cols-4">
                {[
                  { label: "New", value: counts!.new, tone: "text-pass" },
                  { label: "Update", value: counts!.update, tone: "text-crest" },
                  { label: "Unchanged", value: counts!.unchanged, tone: "text-muted" },
                  { label: "Errors", value: counts!.error, tone: counts!.error ? "text-brick" : "text-muted" },
                  ...(removes > 0 ? [{ label: "Remove", value: removes, tone: "text-brick" }] : []),
                ].map((s) => (
                  <div key={s.label} className="rounded-control border border-rule px-3 py-2">
                    <dt className="text-xs text-muted">{s.label}</dt>
                    <dd className={cx("slab tnum text-2xl font-bold", s.tone)}>{s.value.toLocaleString("en-NG")}</dd>
                  </div>
                ))}
              </dl>
              {preview.warnings.length > 0 && (
                <Notice tone="warning">
                  <ul className="list-disc space-y-1 pl-5">
                    {preview.warnings.map((w) => (
                      <li key={w}>{w}</li>
                    ))}
                  </ul>
                </Notice>
              )}
              {counts!.error > 0 && !refused && (
                <Notice tone="error">Fix the {plural(counts!.error, "row")} with errors in your file and upload it again. Nothing has been saved.</Notice>
              )}
              {confirmError && (
                <Notice tone="error">{confirmError.status === 404 ? "This import has expired. Upload the file again." : confirmError.message}</Notice>
              )}
              {!refused && counts!.error > 0 ? (
                <div className="w-56">
                  <Toggle checked={errorsOnly} onChange={setErrorsOnly} label="Show errors only" />
                </div>
              ) : (
                <p className="text-sm text-muted">{refused ? "Rows the import refused" : plural(preview.rows.length, "row")}</p>
              )}
              <ImportPreviewTable rows={shown} />
              <div className="flex flex-wrap items-start gap-2">
                <Button onClick={save} loading={confirm.busy} disabled={counts!.error > 0 || !!refused || toSave === 0 || expired}>
                  {counts!.error > 0 || refused
                    ? "Fix the errors to import"
                    : expired
                      ? "Upload the file again"
                      : toSave === 0
                        ? "Nothing to import"
                        : changes === 0
                          ? `Remove ${plural(removes, "row")}`
                          : `Import ${plural(changes, "row")}`}
                </Button>
                <FilePicker path={`${base}/preview${query}`} label="Upload the fixed file" variant="secondary" onPreview={showPreview} />
                <Button variant="ghost" onClick={reset}>
                  Start again
                </Button>
              </div>
            </>
          )}
        </div>
      </Modal>
    </>
  );
}
