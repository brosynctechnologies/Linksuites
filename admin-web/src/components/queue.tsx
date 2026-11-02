"use client";

import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent, type ReactNode } from "react";
import { toApiError, type ApiError } from "@/lib/api";
import { CheckIcon, ChevronLeftIcon, CloseIcon, KeyboardIcon } from "./icons";
import { useToast } from "./toast";
import { Button, Dialog, EmptyState, ErrorPanel, Loading, Textarea, cx } from "./ui";

export interface QueueConfig<T> {
  /** Queue name for headings and screen readers ("Listings queue"). */
  label: string;
  getId: (item: T) => string;
  getTitle: (item: T) => string;
  renderRow: (item: T) => ReactNode;
  renderDetail: (item: T) => ReactNode;
  approveLabel: string;
  rejectLabel: string;
  /** Rejecting asks for a note (r opens the note box). */
  rejectNote?: { label: string; prompt: (item: T) => string; required: boolean };
  /** Items that can't be decided (e.g. already reviewed) show no actions. */
  canDecide?: (item: T) => boolean;
  /** Perform the decision; throw an ApiError to show it. Returns the toast text. */
  decide: (item: T, approve: boolean, note: string | null) => Promise<string>;
  emptyTitle: string;
  emptyText?: string;
}

/**
 * Two-pane moderation queue: the list left, the item right (stacked on phones).
 * Keyboard: j/k next/previous, a approve, r reject (opens the note box), ? shortcuts.
 */
export function Queue<T>({
  items,
  error,
  reload,
  config,
  toolbar,
}: {
  items: T[] | undefined;
  error?: ApiError;
  reload: () => void;
  config: QueueConfig<T>;
  toolbar?: ReactNode;
}) {
  const toast = useToast();
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [lastIndex, setLastIndex] = useState(0);
  const [phoneDetail, setPhoneDetail] = useState(false);
  const [noteOpen, setNoteOpen] = useState(false);
  const [busy, setBusy] = useState<"approve" | "reject" | null>(null);
  const [actionError, setActionError] = useState<ApiError | null>(null);
  const [help, setHelp] = useState(false);
  const listRef = useRef<HTMLUListElement>(null);
  const list = useMemo(() => items ?? [], [items]);

  let index = selectedId ? list.findIndex((i) => config.getId(i) === selectedId) : -1;
  if (index < 0 && list.length > 0) index = Math.min(lastIndex, list.length - 1);
  const current = index >= 0 ? list[index] : undefined;
  const currentId = current ? config.getId(current) : null;
  const decidable = !!current && (config.canDecide?.(current) ?? true);

  const select = useCallback(
    (i: number, fromKeyboard = false) => {
      if (!list.length) return;
      const clamped = Math.max(0, Math.min(list.length - 1, i));
      setSelectedId(config.getId(list[clamped]));
      setLastIndex(clamped);
      setNoteOpen(false);
      setActionError(null);
      if (fromKeyboard) {
        listRef.current?.querySelectorAll<HTMLElement>("[data-queue-item]")[clamped]?.scrollIntoView({ block: "nearest" });
      }
    },
    [list, config],
  );

  const decide = useCallback(
    async (approve: boolean, note: string | null) => {
      if (!current || busy) return;
      setBusy(approve ? "approve" : "reject");
      setActionError(null);
      try {
        const text = await config.decide(current, approve, note);
        toast(text);
        setNoteOpen(false);
        setSelectedId(null);
        setLastIndex(index);
      } catch (e) {
        setActionError(toApiError(e));
      } finally {
        setBusy(null);
      }
    },
    [current, busy, config, toast, index],
  );

  const startReject = useCallback(() => {
    if (!decidable) return;
    if (config.rejectNote) setNoteOpen(true);
    else void decide(false, null);
  }, [decidable, config.rejectNote, decide]);

  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (e.ctrlKey || e.metaKey || e.altKey) return;
      const t = e.target as HTMLElement | null;
      if (t && (t.closest("input, textarea, select, [contenteditable=true]") || t.closest("dialog"))) return;
      if (document.querySelector("dialog[open]")) return;
      if (e.key === "j") {
        e.preventDefault();
        select(index + 1, true);
      } else if (e.key === "k") {
        e.preventDefault();
        select(index - 1, true);
      } else if (e.key === "a" && decidable) {
        e.preventDefault();
        void decide(true, null);
      } else if (e.key === "r" && decidable) {
        e.preventDefault();
        startReject();
      } else if (e.key === "?") {
        e.preventDefault();
        setHelp(true);
      }
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [index, decidable, select, decide, startReject]);

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex flex-wrap items-center gap-2">{toolbar}</div>
        <Button variant="ghost" onClick={() => setHelp(true)} className="text-muted">
          <KeyboardIcon className="h-4 w-4" /> Shortcuts <kbd className="rounded-control border border-line bg-surface px-1.5 font-mono text-xs">?</kbd>
        </Button>
      </div>

      {!items && error && <ErrorPanel error={error} onRetry={reload} />}
      {!items && !error && <Loading />}
      {items && error && <ErrorPanel error={error} onRetry={reload} />}
      {items && list.length === 0 && (
        <div className="rounded-panel border border-line bg-surface">
          <EmptyState title={config.emptyTitle} text={config.emptyText} />
        </div>
      )}

      {list.length > 0 && (
        <div className="grid items-start gap-4 lg:grid-cols-[minmax(260px,340px)_minmax(0,1fr)]">
          <section aria-label={config.label} className={cx("min-w-0 rounded-panel border border-line bg-surface lg:sticky lg:top-12", phoneDetail && "hidden lg:block")}>
            <p className="border-b border-line px-3 py-2 text-xs text-muted">
              <span className="tnum">{list.length}</span> {list.length === 1 ? "item" : "items"}
            </p>
            <ul ref={listRef} className="max-h-[calc(100dvh-10rem)] overflow-y-auto">
              {list.map((item, i) => {
                const id = config.getId(item);
                const active = id === currentId;
                return (
                  <li key={id} data-queue-item>
                    <button
                      type="button"
                      onClick={() => {
                        select(i);
                        setPhoneDetail(true);
                      }}
                      aria-current={active ? "true" : undefined}
                      className={cx(
                        "relative block w-full border-b border-line-soft px-3 py-2.5 text-left text-sm",
                        active ? "bg-concrete" : "hover:bg-concrete-2",
                      )}
                    >
                      {active && <span className="absolute inset-y-0 left-0 w-[3px] bg-graphite" aria-hidden="true" />}
                      {config.renderRow(item)}
                    </button>
                  </li>
                );
              })}
            </ul>
          </section>

          {current && (
            <section aria-label={config.getTitle(current)} className={cx("min-w-0 rounded-panel border border-line bg-surface", !phoneDetail && "hidden lg:block")}>
              <div className="flex flex-wrap items-center gap-2 border-b border-line px-4 py-2.5">
                <Button variant="ghost" className="-ml-2 lg:hidden" onClick={() => setPhoneDetail(false)}>
                  <ChevronLeftIcon className="h-4 w-4" /> Queue
                </Button>
                <span className="tnum text-xs text-muted">
                  {index + 1} of {list.length}
                </span>
                {decidable && (
                  <div className="ml-auto flex flex-wrap gap-2">
                    <Button loading={busy === "approve"} disabled={!!busy} onClick={() => decide(true, null)} aria-keyshortcuts="a">
                      <CheckIcon className="h-4 w-4" /> {config.approveLabel}
                    </Button>
                    <Button variant="danger" loading={busy === "reject" && !noteOpen} disabled={!!busy} onClick={startReject} aria-keyshortcuts="r">
                      <CloseIcon className="h-4 w-4" /> {config.rejectLabel}
                    </Button>
                  </div>
                )}
              </div>
              {actionError && <ErrorPanel error={actionError} className="m-4 mb-0" />}
              {noteOpen && config.rejectNote && (
                <NoteBox
                  key={currentId}
                  label={config.rejectNote.label}
                  prompt={config.rejectNote.prompt(current)}
                  required={config.rejectNote.required}
                  action={config.rejectLabel}
                  busy={busy === "reject"}
                  error={actionError}
                  onCancel={() => setNoteOpen(false)}
                  onSubmit={(note) => decide(false, note)}
                />
              )}
              <div className="p-4">{config.renderDetail(current)}</div>
            </section>
          )}
        </div>
      )}

      <Dialog open={help} onClose={() => setHelp(false)} title="Keyboard shortcuts">
        <dl className="grid grid-cols-[max-content_1fr] items-center gap-x-4 gap-y-2 text-sm">
          {[
            ["j", "Next item"],
            ["k", "Previous item"],
            ["a", config.approveLabel],
            ["r", config.rejectNote ? `${config.rejectLabel} (opens the note box)` : config.rejectLabel],
            ["?", "Show these shortcuts"],
            ["Esc", "Close a dialog or the note box"],
          ].map(([k, v]) => (
            <div key={k} className="contents">
              <dt>
                <kbd className="inline-flex min-w-7 justify-center rounded-control border border-line bg-concrete-2 px-1.5 py-0.5 font-mono text-xs">{k}</kbd>
              </dt>
              <dd>{v}</dd>
            </div>
          ))}
        </dl>
      </Dialog>
    </div>
  );
}

function NoteBox({
  label,
  prompt,
  required,
  action,
  busy,
  error,
  onCancel,
  onSubmit,
}: {
  label: string;
  prompt: string;
  required: boolean;
  action: string;
  busy: boolean;
  error: ApiError | null;
  onCancel: () => void;
  onSubmit: (note: string | null) => void;
}) {
  const [note, setNote] = useState("");
  const [touched, setTouched] = useState(false);
  const missing = required && !note.trim();

  function submit(e: FormEvent) {
    e.preventDefault();
    setTouched(true);
    if (missing) return;
    onSubmit(note.trim() || null);
  }

  return (
    <form
      onSubmit={submit}
      onKeyDown={(e) => {
        if (e.key === "Escape") {
          e.preventDefault();
          onCancel();
        }
      }}
      className="space-y-3 border-b border-line bg-concrete-2 p-4"
    >
      <p className="text-sm">{prompt}</p>
      <Textarea
        label={label}
        value={note}
        onChange={(e) => setNote(e.target.value)}
        autoFocus
        rows={3}
        maxLength={1000}
        error={(touched && missing ? "Write a note." : undefined) ?? error?.field("note")}
      />
      <div className="flex flex-wrap justify-end gap-2">
        <Button variant="secondary" onClick={onCancel}>
          Cancel
        </Button>
        <Button type="submit" variant="danger" loading={busy}>
          {action}
        </Button>
      </div>
    </form>
  );
}
