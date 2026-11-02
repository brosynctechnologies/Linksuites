"use client";

import { useState, type FormEvent } from "react";
import { api, ApiError, toApiError } from "@/lib/api";
import { useApi } from "@/lib/use-api";
import type { CategoryDto } from "@/lib/types";
import { ApiErrorView, Button, EmptyState, Input, PageHeader, PageLoader, Panel, cx, listClass } from "@/components/ui";
import { TagIcon, TrashIcon } from "@/components/icons";

interface Draft {
  id?: string;
  name: string;
  position: string;
}

export default function CategoriesPage() {
  const { data, error, loading, reload, setData } = useApi<CategoryDto[]>("/api/categories");
  const [draft, setDraft] = useState<Draft>({ name: "", position: "" });
  const [busy, setBusy] = useState(false);
  const [formError, setFormError] = useState<ApiError | null>(null);

  const sorted = [...(data ?? [])].sort((a, b) => a.position - b.position || a.name.localeCompare(b.name));

  async function save(e: FormEvent) {
    e.preventDefault();
    if (!draft.name.trim()) return;
    setBusy(true);
    setFormError(null);
    const body = {
      name: draft.name.trim(),
      position: draft.position === "" ? (draft.id ? 0 : (data?.length ?? 0)) : Number(draft.position),
    };
    try {
      const saved = draft.id
        ? await api<CategoryDto>(`/api/categories/${draft.id}`, { method: "PUT", body })
        : await api<CategoryDto>("/api/categories", { method: "POST", body });
      setData((list = []) => (draft.id ? list.map((c) => (c.id === saved.id ? saved : c)) : [...list, saved]));
      setDraft({ name: "", position: "" });
    } catch (err) {
      setFormError(toApiError(err));
    } finally {
      setBusy(false);
    }
  }

  async function remove(c: CategoryDto) {
    if (!window.confirm(`Delete “${c.name}”? Its products become uncategorised.`)) return;
    try {
      await api(`/api/categories/${c.id}`, { method: "DELETE" });
      setData((list = []) => list.filter((x) => x.id !== c.id));
    } catch (err) {
      setFormError(toApiError(err));
    }
  }

  return (
    <div>
      <PageHeader title="Categories" back="/dashboard/more" subtitle="Group products so customers find them faster." />

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_20rem] lg:items-start">
        <Panel className="lg:sticky lg:top-8 lg:col-start-2">
          <form onSubmit={save} className="space-y-4">
            <h2 className="font-display text-lg font-bold text-ink">{draft.id ? "Edit category" : "Add category"}</h2>
            <div className="grid grid-cols-[minmax(0,1fr)_6rem] gap-3">
              <Input label="Name" value={draft.name} onChange={(e) => setDraft({ ...draft, name: e.target.value })} placeholder="e.g. Dresses" required />
              <Input
                label="Order"
                inputMode="numeric"
                value={draft.position}
                onChange={(e) => setDraft({ ...draft, position: e.target.value.replace(/\D/g, "") })}
                placeholder="Auto"
              />
            </div>
            <ApiErrorView error={formError} />
            <div className="flex flex-wrap gap-2">
              <Button type="submit" loading={busy} disabled={!draft.name.trim()}>
                {draft.id ? "Save" : "Add category"}
              </Button>
              {draft.id && (
                <Button variant="ghost" onClick={() => setDraft({ name: "", position: "" })}>
                  Cancel
                </Button>
              )}
            </div>
          </form>
        </Panel>

        <div className="min-w-0 lg:col-start-1 lg:row-start-1">
          {error && <ApiErrorView error={error} onRetry={reload} />}
          {!data && loading && <PageLoader label="Loading categories…" rows={3} />}
          {data &&
            (sorted.length === 0 ? (
              <EmptyState
                icon={<TagIcon />}
                title="No categories yet"
                text="Add one with the form. Categories show as filter chips at the top of your store."
              />
            ) : (
              <ul className={listClass}>
                {sorted.map((c) => (
                  <li key={c.id} className={cx("flex items-center gap-2 pr-2", draft.id === c.id && "bg-marigold-soft/60")}>
                    <button
                      type="button"
                      className="flex min-h-14 min-w-0 flex-1 items-center justify-between gap-3 px-4 py-3 text-left focus-visible:-outline-offset-3"
                      onClick={() => setDraft({ id: c.id, name: c.name, position: String(c.position) })}
                      aria-label={`Edit ${c.name}`}
                    >
                      <span className="min-w-0">
                        <span className="block truncate font-medium text-ink">{c.name}</span>
                        <span className="text-xs text-subtle num">
                          {c.productCount} product{c.productCount === 1 ? "" : "s"}
                        </span>
                      </span>
                      <span className="shrink-0 text-sm font-medium text-ink-2">{draft.id === c.id ? "Editing" : "Edit"}</span>
                    </button>
                    <button
                      type="button"
                      onClick={() => remove(c)}
                      className="flex h-11 w-11 shrink-0 items-center justify-center rounded-control text-hibiscus-deep hover:bg-hibiscus-soft"
                      aria-label={`Delete ${c.name}`}
                    >
                      <TrashIcon />
                    </button>
                  </li>
                ))}
              </ul>
            ))}
        </div>
      </div>
    </div>
  );
}
