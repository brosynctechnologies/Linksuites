// Types for the shared Excel and CSV import, mirroring api/src/Saas.Application/School/Imports/IImporter.cs.
// Routes: GET /api/school/imports/{kind}/template.xlsx|.csv?{scope}, POST /api/school/imports/{kind}/preview?{scope} (multipart file),
// POST /api/school/imports/{token}/confirm.

export type ImportStatus = "New" | "Update" | "Unchanged" | "Error";

/** One row of the file as checked: its row number in the file, what will happen, who or what it is, and why it is refused. */
export interface ImportPreviewRow {
  row: number;
  status: ImportStatus;
  label: string;
  message: string | null;
}

export interface ImportCounts {
  new: number;
  update: number;
  unchanged: number;
  error: number;
  /** Saved records the confirm would remove. Only the timetable removes (lessons missing from the file); 0 for every other kind. */
  remove: number;
}

/** POST /imports/{kind}/preview. The token confirms this preview for 30 minutes, for the user who uploaded it. */
export interface ImportPreviewResult {
  token: string;
  kind: string;
  rows: ImportPreviewRow[];
  counts: ImportCounts;
  /** Do not block the import, for example an invigilator clash. */
  warnings: string[];
}

/**
 * POST /imports/{token}/confirm. A 409 import_errors instead carries the error rows as ImportPreviewRow. A repeat confirm of a token
 * already confirmed by the same user (for example after a lost response) returns the first result with alreadyImported: true.
 */
export interface ImportApplied {
  created: number;
  updated: number;
  /** Rows removed. Only the timetable import removes (it replaces the week); absent otherwise. */
  removed?: number;
  /** True when this file had already been imported with this token; nothing was saved again. Absent otherwise. */
  alreadyImported?: boolean;
}

/** What a page passes to open the import for one kind with its current scope (class, subject, term, period, assignment). */
export interface ImportDialogProps {
  kind: string;
  scope?: Record<string, string>;
  title: string;
  /** The button's text when a page has more than one import (default "Import from Excel"). */
  label?: string;
  onDone?: () => void;
  /** When set, the button is disabled and this sentence is shown under it (for example unsaved typed scores). */
  disabledReason?: string;
  /** Runs when the button is clicked, before the dialog opens; the dialog opens only when it resolves true (for example after saving typed edits). */
  beforeOpen?: () => Promise<boolean>;
}
