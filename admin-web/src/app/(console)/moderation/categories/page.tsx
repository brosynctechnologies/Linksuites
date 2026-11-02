"use client";

import { useState, type FormEvent } from "react";
import { RequireArea } from "@/components/console";
import { EditIcon, PlusIcon } from "@/components/icons";
import { useToast } from "@/components/toast";
import { Button, EmptyState, ErrorPanel, FormErrors, Input, Loading, Mono, PageHeader, Panel, Select, SidePanel, TableScroll } from "@/components/ui";
import { api, toApiError, type ApiError } from "@/lib/api";
import type { CategoryDto } from "@/lib/types";
import { useApi } from "@/lib/use-api";

type Editing = { cat: CategoryDto | null; parentId: string | null };

export default function CategoriesPage() {
  return (
    <RequireArea area="moderation">
      <Categories />
    </RequireArea>
  );
}

/** LearnLink marketplace categories (one level of subcategories). */
function Categories() {
  const { data, error, reload } = useApi<CategoryDto[]>("/api/admin/categories");
  const [editing, setEditing] = useState<Editing | null>(null);
  const all = data ?? [];
  const top = all.filter((c) => !c.parentId).sort((a, b) => a.name.localeCompare(b.name));
  const childrenOf = (id: string) => all.filter((c) => c.parentId === id).sort((a, b) => a.name.localeCompare(b.name));
  const orphans = all.filter((c) => c.parentId && !all.some((p) => p.id === c.parentId));
  const rows = [...top.flatMap((c) => [{ c, depth: 0 }, ...childrenOf(c.id).map((k) => ({ c: k, depth: 1 }))]), ...orphans.map((c) => ({ c, depth: 1 }))];

  return (
    <>
      <PageHeader
        title="Categories"
        subtitle="LearnLink marketplace categories. Tutors pick one per course; learners browse by them."
        action={
          <Button onClick={() => setEditing({ cat: null, parentId: null })}>
            <PlusIcon className="h-4 w-4" /> Add category
          </Button>
        }
      />
      <Panel flush>
        {error && <ErrorPanel error={error} onRetry={reload} className="m-3" />}
        {!data && !error && <Loading />}
        {data && all.length === 0 && <EmptyState title="No categories yet" text="Add the first category tutors can file courses under." />}
        {data && all.length > 0 && (
          <TableScroll label="Categories">
            <table className="data-table min-w-[520px]">
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Slug</th>
                  <th className="w-px">
                    <span className="sr-only">Actions</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {rows.map(({ c, depth }) => (
                  <tr key={c.id}>
                    <td style={{ paddingLeft: 12 + depth * 24 }}>
                      {depth > 0 && (
                        <span className="mr-1 text-muted" aria-hidden="true">
                          └
                        </span>
                      )}
                      <span className={depth === 0 ? "font-medium" : ""}>{c.name}</span>
                    </td>
                    <td>
                      <Mono>{c.slug}</Mono>
                    </td>
                    <td className="!py-1 whitespace-nowrap">
                      <button
                        type="button"
                        onClick={() => setEditing({ cat: c, parentId: c.parentId })}
                        className="inline-flex h-9 w-9 items-center justify-center rounded-control text-muted hover:bg-concrete"
                        aria-label={`Edit ${c.name}`}
                      >
                        <EditIcon className="h-4 w-4" />
                      </button>
                      {depth === 0 && (
                        <Button variant="ghost" className="px-2" onClick={() => setEditing({ cat: null, parentId: c.id })} aria-label={`Add a subcategory to ${c.name}`}>
                          <PlusIcon className="h-4 w-4" /> <span className="hidden sm:inline">Subcategory</span>
                        </Button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </TableScroll>
        )}
      </Panel>

      <SidePanel open={!!editing} onClose={() => setEditing(null)} title={editing?.cat ? `Edit ${editing.cat.name}` : "Add a category"}>
        {editing && (
          <CategoryForm
            key={editing.cat?.id ?? `new-${editing.parentId ?? "root"}`}
            editing={editing}
            parents={top.filter((p) => p.id !== editing.cat?.id)}
            onSaved={() => {
              setEditing(null);
              reload();
            }}
          />
        )}
      </SidePanel>
    </>
  );
}

function CategoryForm({ editing, parents, onSaved }: { editing: Editing; parents: CategoryDto[]; onSaved: () => void }) {
  const toast = useToast();
  const c = editing.cat;
  const [name, setName] = useState(c?.name ?? "");
  const [slug, setSlug] = useState(c?.slug ?? "");
  const [parentId, setParentId] = useState(editing.parentId ?? "");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    const body = { name: name.trim(), slug: slug.trim(), parentId: parentId || null };
    try {
      if (c) await api(`/api/admin/categories/${c.id}`, { method: "PUT", body });
      else await api("/api/admin/categories", { method: "POST", body });
      toast(c ? "Category saved" : "Category added");
      onSaved();
    } catch (err) {
      setError(toApiError(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-4" noValidate>
      <Input label="Name" value={name} onChange={(e) => setName(e.target.value)} error={error?.field("name")} required />
      <Input label="Slug" value={slug} onChange={(e) => setSlug(e.target.value)} error={error?.field("slug")} className="font-mono" hint="Lowercase words joined by dashes, used in catalogue links." />
      <Select label="Parent" value={parentId} onChange={(e) => setParentId(e.target.value)} error={error?.field("parentId")}>
        <option value="">None (top level)</option>
        {parents.map((p) => (
          <option key={p.id} value={p.id}>
            {p.name}
          </option>
        ))}
      </Select>
      <FormErrors error={error} fields={["name", "slug", "parentId"]} />
      <div className="flex justify-end">
        <Button type="submit" loading={busy} disabled={!name.trim()}>
          {c ? "Save category" : "Add category"}
        </Button>
      </div>
    </form>
  );
}
