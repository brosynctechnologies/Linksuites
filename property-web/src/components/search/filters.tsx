"use client";

import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { SORTS } from "@/lib/config";
import { hasFilters, searchHref, type SearchState } from "@/lib/search";
import type { LocationDto, RefItem } from "@/lib/types";
import { FilterIcon } from "../icons";
import { LocationField, resolveLocation, useLocationOptions } from "../location-field";
import { Button, Checkbox, Input, Modal, Select, cx, inputClass } from "../ui";
import { SaveSearchButton } from "./save-search";

interface Draft {
  location: string;
  type: string;
  beds: string;
  minPrice: string;
  maxPrice: string;
  amenities: string[];
  verified: boolean;
}

function toDraft(s: SearchState, locationName?: string): Draft {
  return {
    location: locationName ?? s.location ?? "",
    type: s.type ?? "",
    beds: s.beds ?? "",
    minPrice: s.minPrice ?? "",
    maxPrice: s.maxPrice ?? "",
    amenities: s.amenities ? s.amenities.split(",").filter(Boolean) : [],
    verified: !!s.verified,
  };
}

function Fields({ draft, set, types, amenities, options, compact }: {
  draft: Draft;
  set: (patch: Partial<Draft>) => void;
  types: RefItem[];
  amenities: RefItem[];
  options: LocationDto[];
  compact?: boolean;
}) {
  const priceError = draft.minPrice && draft.maxPrice && Number(draft.minPrice) > Number(draft.maxPrice) ? "The lowest price is above the highest." : undefined;
  return (
    <div className={cx("grid gap-4", compact ? "grid-cols-2 xl:grid-cols-[minmax(0,1.4fr)_repeat(4,minmax(0,1fr))]" : "")}>
      <LocationField value={draft.location} onChange={(location) => set({ location })} options={options} className={compact ? "col-span-2 xl:col-span-1" : undefined} />
      <Select label="Property type" value={draft.type} onChange={(e) => set({ type: e.target.value })}>
        <option value="">Any type</option>
        {types.map((t) => (
          <option key={t.id} value={t.slug}>
            {t.name}
          </option>
        ))}
      </Select>
      <Select label="Bedrooms" value={draft.beds} onChange={(e) => set({ beds: e.target.value })}>
        <option value="">Any</option>
        {[1, 2, 3, 4, 5].map((n) => (
          <option key={n} value={n}>
            {n}+ beds
          </option>
        ))}
      </Select>
      <Input label="Min price (₦)" inputMode="numeric" placeholder="No min" value={draft.minPrice} onChange={(e) => set({ minPrice: e.target.value.replace(/\D/g, "") })} />
      <Input
        label="Max price (₦)"
        inputMode="numeric"
        placeholder="No max"
        value={draft.maxPrice}
        onChange={(e) => set({ maxPrice: e.target.value.replace(/\D/g, "") })}
        error={priceError}
      />
      {!compact && (
        <fieldset>
          <legend className="text-sm font-medium text-slate-700">Amenities</legend>
          <div className="mt-2 grid grid-cols-2 gap-x-3 gap-y-2.5">
            {amenities.map((a) => (
              <Checkbox
                key={a.id}
                label={a.name}
                checked={draft.amenities.includes(a.slug)}
                onChange={(on) => set({ amenities: on ? [...draft.amenities, a.slug] : draft.amenities.filter((x) => x !== a.slug) })}
              />
            ))}
          </div>
        </fieldset>
      )}
      {!compact && <Checkbox label="Verified agents only" checked={draft.verified} onChange={(verified) => set({ verified })} />}
    </div>
  );
}

/**
 * Filters held in the URL. On desktop they sit in a row above the results (amenities in a "More" panel);
 * on phones a "Filters" button opens them in a bottom sheet.
 */
export function SearchFilters({
  state,
  locationName,
  types,
  amenities,
}: {
  state: SearchState;
  locationName?: string;
  types: RefItem[];
  amenities: RefItem[];
}) {
  const router = useRouter();
  const options = useLocationOptions();
  const [draft, setDraft] = useState<Draft>(() => toDraft(state, locationName));
  const [sheet, setSheet] = useState(false);
  const [more, setMore] = useState(false);
  const set = (patch: Partial<Draft>) => setDraft((d) => ({ ...d, ...patch }));
  const count = [state.type, state.beds, state.minPrice, state.maxPrice, state.amenities, state.verified ? "v" : "", state.bbox].filter(Boolean).length;

  function go(d: Draft, extra: Partial<SearchState> = {}) {
    const location = resolveLocation(d.location, options);
    const sameLocation = location === state.location;
    router.push(
      searchHref({
        purpose: state.purpose,
        location,
        type: d.type || undefined,
        beds: d.beds || undefined,
        minPrice: d.minPrice || undefined,
        maxPrice: d.maxPrice || undefined,
        amenities: d.amenities.length ? d.amenities.join(",") : undefined,
        verified: d.verified,
        // A new place replaces the drawn map area.
        bbox: sameLocation ? state.bbox : undefined,
        sort: state.sort,
        page: 1,
        ...extra,
      }),
    );
  }

  function submit(e: FormEvent) {
    e.preventDefault();
    if (draft.minPrice && draft.maxPrice && Number(draft.minPrice) > Number(draft.maxPrice)) return;
    setSheet(false);
    setMore(false);
    go(draft);
  }

  const clear = () => {
    const d = { ...toDraft({ purpose: state.purpose }), location: draft.location };
    setDraft(d);
    setSheet(false);
    go(d, { bbox: undefined });
  };

  return (
    <div>
      {/* Desktop row */}
      <form onSubmit={submit} className="hidden lg:block" aria-label="Filters">
        <div className="flex items-end gap-3">
          <div className="min-w-0 flex-1">
            <Fields draft={draft} set={set} types={types} amenities={amenities} options={options} compact />
          </div>
          <Button variant="secondary" onClick={() => setMore((m) => !m)} aria-expanded={more}>
            More{draft.amenities.length || draft.verified ? ` (${draft.amenities.length + (draft.verified ? 1 : 0)})` : ""}
          </Button>
          <Button type="submit">Apply</Button>
        </div>
        {more && (
          <div className="mt-3 grid gap-4 rounded-panel border border-line bg-surface p-4 xl:grid-cols-[minmax(0,1fr)_auto]">
            <fieldset>
              <legend className="text-sm font-medium text-slate-700">Amenities</legend>
              <div className="mt-2 grid grid-cols-2 gap-x-4 gap-y-2.5 sm:grid-cols-3 xl:grid-cols-4">
                {amenities.map((a) => (
                  <Checkbox
                    key={a.id}
                    label={a.name}
                    checked={draft.amenities.includes(a.slug)}
                    onChange={(on) => set({ amenities: on ? [...draft.amenities, a.slug] : draft.amenities.filter((x) => x !== a.slug) })}
                  />
                ))}
              </div>
            </fieldset>
            <Checkbox label="Verified agents only" checked={draft.verified} onChange={(verified) => set({ verified })} />
          </div>
        )}
      </form>

      {/* Phone and tablet: one row of actions */}
      <div className="flex flex-wrap items-center gap-2 lg:hidden">
        <Button variant="secondary" onClick={() => setSheet(true)}>
          <FilterIcon className="h-4 w-4" /> Filters{count ? ` (${count})` : ""}
        </Button>
        <SortSelect state={state} />
        <SaveSearchButton state={state} className="ml-auto" />
      </div>

      <div className="mt-3 hidden items-center gap-3 lg:flex">
        <SortSelect state={state} />
        {hasFilters(state) && (
          <button type="button" onClick={clear} className="min-h-11 text-sm font-semibold text-adire hover:underline">
            Clear filters
          </button>
        )}
        <SaveSearchButton state={state} className="ml-auto" />
      </div>

      <Modal
        open={sheet}
        title="Filters"
        onClose={() => setSheet(false)}
        footer={
          <>
            <Button variant="ghost" onClick={clear}>
              Clear all
            </Button>
            <Button type="submit" form="filter-sheet">
              Show homes
            </Button>
          </>
        }
      >
        <form id="filter-sheet" onSubmit={submit}>
          <Fields draft={draft} set={set} types={types} amenities={amenities} options={options} />
        </form>
      </Modal>
    </div>
  );
}

function SortSelect({ state }: { state: SearchState }) {
  const router = useRouter();
  return (
    <div className="flex items-center gap-2">
      <label htmlFor="sort" className="sr-only">
        Sort by
      </label>
      <select
        id="sort"
        className={cx(inputClass, "w-auto min-w-[10rem]")}
        value={state.sort ?? "recommended"}
        onChange={(e) => router.push(searchHref({ ...state, sort: e.target.value, page: 1 }))}
      >
        {SORTS.map((s) => (
          <option key={s.value} value={s.value}>
            {s.label}
          </option>
        ))}
      </select>
    </div>
  );
}
