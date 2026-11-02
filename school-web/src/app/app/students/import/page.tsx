"use client";

import Link from "next/link";
import { useRef, useState } from "react";
import { api, downloadFile, upload, type ApiError } from "@/lib/api";
import { IMPORT_MAX_BYTES, IMPORT_MAX_ROWS } from "@/lib/config";
import { plural } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { useCan } from "@/lib/roles";
import type { ImportPreviewDto, ImportResultDto, ImportRowDto } from "@/lib/types";
import { DownloadIcon, UploadIcon } from "@/components/icons";
import { ImportPreviewTable } from "@/components/import-dialog";
import { Button, Card, ErrorBox, NoAccess, Notice, SectionTitle, Toggle, buttonClass, cx } from "@/components/ui";

/** Why the file doesn't fit the plan, whether or not the API sent the room left. */
function planFullText(p: ImportPreviewDto) {
  const adds = p.newCount.toLocaleString("en-NG");
  if (p.room === null) return `Your plan doesn't have room for the ${adds} new students in this file.`;
  if (p.room === 0) return `Your plan is full, and this file adds ${adds} new students.`;
  return `Your plan has room for ${plural(p.room, "more student")} and this file adds ${adds}.`;
}

/**
 * Import wizard: download the template, upload the filled file, check the preview (each row New, Update or Error), confirm.
 * Nothing is saved until the confirm, which saves every row or none.
 */
export default function ImportPage() {
  const canManage = useCan("school.manage");
  const [preview, setPreview] = useState<ImportPreviewDto | null>(null);
  const [fileName, setFileName] = useState("");
  const [result, setResult] = useState<ImportResultDto | null>(null);

  if (!canManage) return <NoAccess />;

  function reset() {
    setPreview(null);
    setResult(null);
    setFileName("");
  }

  if (result) {
    return (
      <Card>
        <SectionTitle>Import finished</SectionTitle>
        <p className="text-slate-700">
          Added {plural(result.created, "new student")} and updated {plural(result.updated, "student")} from {fileName}.
        </p>
        <div className="mt-4 flex flex-wrap gap-2">
          <Link href="/app/students" className={buttonClass("primary")}>
            See the students
          </Link>
          <Link href="/app/students/invites" className={buttonClass("secondary")}>
            Invite their parents
          </Link>
          <Button variant="ghost" onClick={reset}>
            Import another file
          </Button>
        </div>
      </Card>
    );
  }

  return (
    <div className="space-y-5">
      <ol className="flex flex-wrap gap-x-6 gap-y-1 text-sm" aria-label="Import steps">
        {["Fill in the template", "Upload and check", "Confirm"].map((step, i) => {
          const at = preview ? 2 : 1;
          return (
            <li key={step} className={cx("flex items-center gap-2", i + 1 === at ? "font-bold text-ink" : "text-muted")} aria-current={i + 1 === at ? "step" : undefined}>
              <span
                className={cx(
                  "slab tnum flex h-6 w-6 items-center justify-center rounded-full text-xs",
                  i + 1 < at ? "bg-pass text-white" : i + 1 === at ? "bg-blazer text-white" : "bg-ruled-2 text-muted",
                )}
              >
                {i + 1}
              </span>
              {step}
            </li>
          );
        })}
      </ol>
      {preview ? (
        <Preview
          key={preview.token}
          preview={preview}
          fileName={fileName}
          onBack={reset}
          onDone={(r) => setResult(r)}
          onReplaced={(p, name) => {
            setPreview(p);
            setFileName(name);
          }}
        />
      ) : (
        <Start
          onPreview={(p, name) => {
            setPreview(p);
            setFileName(name);
          }}
        />
      )}
    </div>
  );
}

function Template() {
  const { busy, error, run } = useAction();
  return (
    <Card>
      <SectionTitle>1. Fill in the template</SectionTitle>
      <p className="-mt-1 max-w-prose text-sm text-slate-700">
        One row per student. Required: admission number, first name, last name, gender (F or M) and class, written as it appears in SchoolLink (for
        example JSS 1A). Up to two parents per student, with phone or email. Dates can be 2014-03-21 or 21/03/2014.
      </p>
      <p className="mt-2 max-w-prose text-sm text-slate-700">
        Students already in SchoolLink are matched by admission number and updated. Parents with the same phone number are shared between siblings.
      </p>
      <div className="mt-4 flex flex-wrap gap-2">
        <Button variant="secondary" loading={busy} onClick={() => run(() => downloadFile("/api/school/students/import/template.xlsx", "schoollink-students-template.xlsx"))}>
          <DownloadIcon className="h-4 w-4" /> Excel template
        </Button>
        <Button variant="secondary" loading={busy} onClick={() => run(() => downloadFile("/api/school/students/import/template.csv", "schoollink-students-template.csv"))}>
          <DownloadIcon className="h-4 w-4" /> CSV template
        </Button>
      </div>
      <div className="mt-3">
        <ErrorBox error={error} />
      </div>
    </Card>
  );
}

/** Picks a file and posts it for a preview. Checks type and size first so a wrong file fails fast. */
function UploadButton({ onPreview, label, variant = "primary" }: { onPreview: (p: ImportPreviewDto, name: string) => void; label: string; variant?: "primary" | "secondary" }) {
  const input = useRef<HTMLInputElement>(null);
  const { busy, error, setError, run } = useAction();
  const [local, setLocal] = useState<string | null>(null);

  async function choose(file: File) {
    setLocal(null);
    setError(null);
    if (!/\.(xlsx|csv)$/i.test(file.name)) return setLocal("Upload an Excel (.xlsx) or CSV file.");
    if (file.size > IMPORT_MAX_BYTES) return setLocal("This file is over 5 MB. Split it into smaller files.");
    const res = await run(() => upload<ImportPreviewDto>("/api/school/students/import/preview", file));
    if (res.ok) onPreview(res.data, file.name);
  }

  return (
    <div className="space-y-3">
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

function Start({ onPreview }: { onPreview: (p: ImportPreviewDto, name: string) => void }) {
  return (
    <div className="space-y-5">
      <Template />
      <Card>
        <SectionTitle>2. Upload and check</SectionTitle>
        <p className="-mt-1 mb-4 max-w-prose text-sm text-slate-700">
          Upload the filled file (.xlsx or .csv, up to {IMPORT_MAX_ROWS.toLocaleString("en-NG")} rows and 5 MB). You&apos;ll see every row before anything is saved.
        </p>
        <UploadButton onPreview={onPreview} label="Upload a file" />
      </Card>
    </div>
  );
}

function Preview({
  preview,
  fileName,
  onBack,
  onDone,
  onReplaced,
}: {
  preview: ImportPreviewDto;
  fileName: string;
  onBack: () => void;
  onDone: (r: ImportResultDto) => void;
  onReplaced: (p: ImportPreviewDto, name: string) => void;
}) {
  const [errorsOnly, setErrorsOnly] = useState(preview.errorCount > 0);
  const [confirmError, setConfirmError] = useState<ApiError | null>(null);
  const confirm = useAction();
  const blocked = preview.errorCount > 0 || !preview.fitsPlan;

  // Rows the confirm refused (409 import_errors) replace the preview's own errors.
  const refused = confirmError?.rows;
  const rows: ImportRowDto[] = refused ?? (errorsOnly ? preview.rows.filter((r) => r.status === "Error") : preview.rows);

  async function save() {
    setConfirmError(null);
    const res = await confirm.run(() => api<ImportResultDto>(`/api/school/students/import/${encodeURIComponent(preview.token)}/confirm`, { method: "POST" }));
    if (res.ok) onDone(res.data);
    else setConfirmError(res.error);
  }

  return (
    <div className="space-y-4">
      <Card>
        <SectionTitle>Checked {fileName}</SectionTitle>
        <dl className="grid grid-cols-3 gap-3 sm:max-w-md">
          {[
            { label: "New", value: preview.newCount, tone: "text-pass" },
            { label: "Update", value: preview.updateCount, tone: "text-crest" },
            { label: "Errors", value: preview.errorCount, tone: preview.errorCount ? "text-brick" : "text-muted" },
          ].map((s) => (
            <div key={s.label} className="rounded-control border border-rule px-3 py-2">
              <dt className="text-xs text-muted">{s.label}</dt>
              <dd className={cx("slab tnum text-2xl font-bold", s.tone)}>{s.value.toLocaleString("en-NG")}</dd>
            </div>
          ))}
        </dl>
        {preview.fitsPlan && (
          <p className="mt-3 text-sm text-slate-700">
            {preview.room === null
              ? "Your plan has no student limit."
              : `Your plan has room for ${plural(preview.room, "more student")}; this file adds ${preview.newCount.toLocaleString("en-NG")}.`}
          </p>
        )}
        {!preview.fitsPlan && (
          <Notice tone="warning" className="mt-3">
            {planFullText(preview)} Upgrade your plan or remove rows, then upload the file again.{" "}
            <Link href="/pricing" className="font-bold underline underline-offset-2">
              See plans
            </Link>
          </Notice>
        )}
        {preview.errorCount > 0 && (
          <Notice tone="error" className="mt-2">
            Fix the {plural(preview.errorCount, "row")} with errors in your file and upload it again. Nothing has been saved.
          </Notice>
        )}
      </Card>

      {confirmError && (
        <Notice tone="error">
          {confirmError.status === 404 ? "This check has expired. Upload the file again." : confirmError.message}
        </Notice>
      )}

      <div className="flex flex-wrap items-center justify-between gap-3">
        {!refused && preview.errorCount > 0 ? (
          <div className="w-56">
            <Toggle checked={errorsOnly} onChange={setErrorsOnly} label="Show errors only" />
          </div>
        ) : (
          <span className="text-sm text-muted">{refused ? "Rows the import refused" : plural(preview.rows.length, "row")}</span>
        )}
      </div>

      <ImportPreviewTable
        labelHeading="Student"
        rows={rows.map((r) => ({
          row: r.row,
          status: r.status,
          label: [r.admissionNo, r.name, r.className].filter(Boolean).join(" · "),
          message: r.message,
        }))}
      />

      <div className="sticky bottom-16 z-10 -mx-4 flex flex-wrap items-center gap-2 border-t border-rule bg-ruled/95 px-4 py-3 backdrop-blur sm:mx-0 sm:rounded-panel sm:border lg:bottom-4">
        <Button onClick={save} loading={confirm.busy} disabled={blocked || !!refused}>
          {preview.errorCount > 0 || refused
            ? "Fix the errors to import"
            : !preview.fitsPlan
              ? preview.room
                ? `Your plan has room for ${plural(preview.room, "more student")}`
                : "Your plan is full"
              : `Import ${plural(preview.newCount + preview.updateCount, "student")}`}
        </Button>
        {!preview.fitsPlan && !refused && (
          <Link href="/pricing" className={buttonClass("secondary")}>
            Upgrade your plan
          </Link>
        )}
        <UploadButton onPreview={(p, name) => { setConfirmError(null); onReplaced(p, name); }} label="Upload the fixed file" variant="secondary" />
        <Button variant="ghost" onClick={onBack}>
          Start again
        </Button>
      </div>
    </div>
  );
}
