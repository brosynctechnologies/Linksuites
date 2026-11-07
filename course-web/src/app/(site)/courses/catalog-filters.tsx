"use client";

import { useRouter } from "next/navigation";
import { useTransition } from "react";
import { LEVELS, SORTS } from "@/lib/config";
import type { CategoryDto } from "@/lib/types";
import { Spinner } from "@/components/ui";
import { chipClass, cx, inputClass } from "@/components/styles";

export interface CatalogParams {
  q?: string;
  category?: string;
  level?: string;
  price?: string;
  sort?: string;
}

function href(params: CatalogParams) {
  const sp = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) if (v) sp.set(k, v);
  const s = sp.toString();
  return s ? `/courses?${s}` : "/courses";
}

/** Filters write to the URL (page resets to 1), so every view is linkable and the server renders it. */
export function CatalogFilters({ params, categories }: { params: CatalogParams; categories: CategoryDto[] }) {
  const router = useRouter();
  const [pending, start] = useTransition();
  const set = (patch: Partial<CatalogParams>) => start(() => router.push(href({ ...params, ...patch }), { scroll: false }));
  const top = categories.filter((c) => !c.parentId);
  const children = (id: string) => categories.filter((c) => c.parentId === id);

  return (
    <div className="flex flex-wrap items-end gap-3" aria-busy={pending || undefined}>
      <div className="w-full min-[480px]:w-auto">
        <label htmlFor="f-category" className="mb-1 block text-xs font-medium text-muted">
          Category
        </label>
        <select id="f-category" className={cx(inputClass, "min-w-48 py-2")} value={params.category ?? ""} onChange={(e) => set({ category: e.target.value || undefined })}>
          <option value="">All categories</option>
          {top.map((c) => {
            const subs = children(c.id);
            return subs.length ? (
              <optgroup key={c.id} label={c.name}>
                <option value={c.slug}>All of {c.name}</option>
                {subs.map((s) => (
                  <option key={s.id} value={s.slug}>
                    {s.name}
                  </option>
                ))}
              </optgroup>
            ) : (
              <option key={c.id} value={c.slug}>
                {c.name}
              </option>
            );
          })}
        </select>
      </div>
      <div className="min-w-0 flex-1 min-[480px]:flex-none">
        <label htmlFor="f-level" className="mb-1 block text-xs font-medium text-muted">
          Level
        </label>
        <select id="f-level" className={cx(inputClass, "py-2")} value={params.level ?? ""} onChange={(e) => set({ level: e.target.value || undefined })}>
          <option value="">Any level</option>
          {LEVELS.map((l) => (
            <option key={l.value} value={l.value}>
              {l.label}
            </option>
          ))}
        </select>
      </div>
      <div className="min-w-0 flex-1 min-[480px]:flex-none">
        <label htmlFor="f-sort" className="mb-1 block text-xs font-medium text-muted">
          Sort by
        </label>
        <select id="f-sort" className={cx(inputClass, "py-2")} value={params.sort ?? "popular"} onChange={(e) => set({ sort: e.target.value === "popular" ? undefined : e.target.value })}>
          {SORTS.map((s) => (
            <option key={s.value} value={s.value}>
              {s.label}
            </option>
          ))}
        </select>
      </div>
      <fieldset className="flex items-center gap-1.5">
        <legend className="mb-1 block text-xs font-medium text-muted">Price</legend>
        {[
          { v: undefined, label: "Any" },
          { v: "free", label: "Free" },
          { v: "paid", label: "Paid" },
        ].map((o) => (
          <button key={o.label} type="button" aria-pressed={(params.price ?? undefined) === o.v} className={chipClass((params.price ?? undefined) === o.v)} onClick={() => set({ price: o.v })}>
            {o.label}
          </button>
        ))}
      </fieldset>
      {pending && <Spinner className="mb-2.5 h-4 w-4 text-muted" />}
    </div>
  );
}
