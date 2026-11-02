"use client";

import { useState, type FormEvent } from "react";
import { RequireArea } from "@/components/console";
import { FilterBar, SearchFilter } from "@/components/filters";
import { ChevronDownIcon, ChevronRightIcon, EditIcon, PlusIcon } from "@/components/icons";
import { useToast } from "@/components/toast";
import { Button, EmptyState, ErrorPanel, FormErrors, Input, Loading, Mono, PageHeader, Panel, SidePanel } from "@/components/ui";
import { api, toApiError, type ApiError } from "@/lib/api";
import type { AdminLocationDto, LocationKind } from "@/lib/types";
import { useApi } from "@/lib/use-api";

const CHILD: Record<LocationKind, LocationKind | null> = { State: "City", City: "Area", Area: null };
const FIELDS = ["name", "slug", "kind", "parentId", "lat", "lng", "position"];

type Editing = { loc: AdminLocationDto | null; parent: AdminLocationDto | null; kind: LocationKind };

export default function LocationsPage() {
  return (
    <RequireArea area="moderation">
      <Locations />
    </RequireArea>
  );
}

/** PropertyLink's states → cities → areas. Listings are filed under areas; coordinates centre the map. */
function Locations() {
  const { data, error, reload } = useApi<AdminLocationDto[]>("/api/admin/property/locations");
  const [editing, setEditing] = useState<Editing | null>(null);
  const [q, setQ] = useState("");
  const [open, setOpen] = useState<Set<string>>(new Set());
  const all = data ?? [];
  const byId = new Map(all.map((l) => [l.id, l]));
  const byParent = new Map<string | null, AdminLocationDto[]>();
  for (const l of all) byParent.set(l.parentId ?? null, [...(byParent.get(l.parentId ?? null) ?? []), l]);
  for (const list of byParent.values()) list.sort((a, b) => a.position - b.position || a.name.localeCompare(b.name));
  const needle = q.trim().toLowerCase();
  const matches = needle ? all.filter((l) => l.name.toLowerCase().includes(needle) || l.slug.includes(needle)).slice(0, 100) : [];

  const toggle = (id: string) =>
    setOpen((s) => {
      const n = new Set(s);
      if (n.has(id)) n.delete(id);
      else n.add(id);
      return n;
    });

  const path = (l: AdminLocationDto) => {
    const parts: string[] = [];
    let p = l.parentId ? byId.get(l.parentId) : undefined;
    while (p) {
      parts.unshift(p.name);
      p = p.parentId ? byId.get(p.parentId) : undefined;
    }
    return parts.join(" › ");
  };

  const row = (l: AdminLocationDto, depth: number, flat = false) => {
    const kids = byParent.get(l.id) ?? [];
    const childKind = CHILD[l.kind];
    const expanded = open.has(l.id);
    return (
      <li key={l.id}>
        <div className="flex items-center gap-1 border-b border-line-soft py-0.5 pr-2" style={{ paddingLeft: 8 + depth * 20 }}>
          {!flat && kids.length > 0 ? (
            <button
              type="button"
              onClick={() => toggle(l.id)}
              aria-expanded={expanded}
              className="inline-flex h-10 w-10 items-center justify-center rounded-control text-muted hover:bg-concrete"
              aria-label={`${expanded ? "Collapse" : "Expand"} ${l.name}`}
            >
              {expanded ? <ChevronDownIcon className="h-4 w-4" /> : <ChevronRightIcon className="h-4 w-4" />}
            </button>
          ) : (
            <span className="w-10" />
          )}
          <span className="min-w-0 flex-1 text-sm">
            <span className="font-medium">{l.name}</span> <span className="text-xs text-muted">{l.kind}</span> <Mono className="text-xs text-muted">{l.slug}</Mono>
            {flat && path(l) && <span className="block text-xs text-muted">{path(l)}</span>}
            {!flat && kids.length > 0 && <span className="tnum ml-1 text-xs text-muted">({kids.length})</span>}
          </span>
          <button
            type="button"
            onClick={() => setEditing({ loc: l, parent: l.parentId ? (byId.get(l.parentId) ?? null) : null, kind: l.kind })}
            className="inline-flex h-10 w-10 items-center justify-center rounded-control text-muted hover:bg-concrete"
            aria-label={`Edit ${l.name}`}
          >
            <EditIcon className="h-4 w-4" />
          </button>
          {childKind && (
            <Button variant="ghost" className="px-2" onClick={() => setEditing({ loc: null, parent: l, kind: childKind })} aria-label={`Add a ${childKind.toLowerCase()} in ${l.name}`}>
              <PlusIcon className="h-4 w-4" /> <span className="hidden sm:inline">{childKind}</span>
            </Button>
          )}
        </div>
        {!flat && expanded && kids.length > 0 && <ul>{kids.map((k) => row(k, depth + 1))}</ul>}
      </li>
    );
  };

  return (
    <>
      <PageHeader
        title="Locations"
        subtitle="PropertyLink's states, cities and areas. Listings are filed under areas."
        action={
          <Button onClick={() => setEditing({ loc: null, parent: null, kind: "State" })}>
            <PlusIcon className="h-4 w-4" /> Add state
          </Button>
        }
      />
      <FilterBar>
        <SearchFilter label="Find a location" placeholder="Name or slug" value={q} onCommit={setQ} />
      </FilterBar>
      <Panel flush>
        {error && <ErrorPanel error={error} onRetry={reload} className="m-3" />}
        {!data && !error && <Loading />}
        {data && all.length === 0 && <EmptyState title="No locations yet" text="Add a state to start." />}
        {data && needle && matches.length === 0 && <EmptyState title={`No location matches "${q}"`} />}
        {data && all.length > 0 && <ul>{needle ? matches.map((l) => row(l, 0, true)) : (byParent.get(null) ?? []).map((l) => row(l, 0))}</ul>}
      </Panel>

      <SidePanel open={!!editing} onClose={() => setEditing(null)} title={editing ? (editing.loc ? `Edit ${editing.loc.name}` : `Add a ${editing.kind.toLowerCase()}${editing.parent ? ` in ${editing.parent.name}` : ""}`) : "Location"}>
        {editing && (
          <LocationForm
            key={editing.loc?.id ?? `new-${editing.parent?.id ?? "root"}-${editing.kind}`}
            editing={editing}
            onSaved={(isNew) => {
              setEditing(null);
              reload();
              if (editing.parent) setOpen((s) => new Set(s).add(editing.parent!.id));
              return isNew;
            }}
          />
        )}
      </SidePanel>
    </>
  );
}

function LocationForm({ editing, onSaved }: { editing: Editing; onSaved: (isNew: boolean) => void }) {
  const toast = useToast();
  const l = editing.loc;
  const [name, setName] = useState(l?.name ?? "");
  const [slug, setSlug] = useState(l?.slug ?? "");
  const [lat, setLat] = useState(l?.lat?.toString() ?? "");
  const [lng, setLng] = useState(l?.lng?.toString() ?? "");
  const [position, setPosition] = useState(l?.position?.toString() ?? "");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    const body = {
      name: name.trim(),
      slug: slug.trim() || null,
      kind: editing.kind,
      parentId: editing.parent?.id ?? null,
      lat: lat.trim() ? Number(lat) : null,
      lng: lng.trim() ? Number(lng) : null,
      position: position.trim() ? Number(position) : null,
    };
    try {
      if (l) await api(`/api/admin/property/locations/${l.id}`, { method: "PUT", body });
      else await api("/api/admin/property/locations", { method: "POST", body });
      toast(l ? "Location saved" : "Location added");
      onSaved(!l);
    } catch (err) {
      setError(toApiError(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-4" noValidate>
      <p className="text-sm text-muted">
        {editing.kind}
        {editing.parent ? ` in ${editing.parent.name}` : ""}
      </p>
      <Input label="Name" value={name} onChange={(e) => setName(e.target.value)} error={error?.field("name")} required />
      <Input label="Slug" value={slug} onChange={(e) => setSlug(e.target.value)} error={error?.field("slug")} className="font-mono" hint="Leave empty to make one from the name. Changing it breaks old links." />
      <div className="grid grid-cols-2 gap-3">
        <Input label="Latitude" inputMode="decimal" value={lat} onChange={(e) => setLat(e.target.value)} error={error?.field("lat")} className="tnum" />
        <Input label="Longitude" inputMode="decimal" value={lng} onChange={(e) => setLng(e.target.value)} error={error?.field("lng")} className="tnum" />
      </div>
      <Input label="Position" type="number" value={position} onChange={(e) => setPosition(e.target.value)} error={error?.field("position")} hint="Order among its siblings (lower first)." className="tnum" />
      <FormErrors error={error} fields={FIELDS} />
      <div className="flex justify-end">
        <Button type="submit" loading={busy} disabled={!name.trim()}>
          {l ? "Save location" : "Add location"}
        </Button>
      </div>
    </form>
  );
}
