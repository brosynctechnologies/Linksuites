"use client";

import { useEffect, useId, useState } from "react";
import { api } from "@/lib/api";
import { HOME_CITY } from "@/lib/config";
import { slugify } from "@/lib/format";
import type { LocationDto } from "@/lib/types";
import { MapPinIcon } from "./icons";
import { cx, inputClass, labelClass } from "./styles";

let cache: Promise<LocationDto[]> | null = null;

/** Suggestions: the states plus the home city and its areas (the contract has no place-search endpoint). */
function loadSuggestions(): Promise<LocationDto[]> {
  cache ??= Promise.all([
    api<LocationDto[]>("/api/locations", { auth: false }).catch(() => [] as LocationDto[]),
    api<LocationDto[]>(`/api/locations?parent=${HOME_CITY.slug}`, { auth: false }).catch(() => [] as LocationDto[]),
  ]).then(([states, areas]) => {
    const city: LocationDto = { id: HOME_CITY.slug, name: HOME_CITY.name, slug: HOME_CITY.slug, kind: "City", parentSlug: null, lat: null, lng: null };
    const list = [...(Array.isArray(areas) ? areas : []), ...(areas?.length ? [city] : []), ...(Array.isArray(states) ? states : [])];
    if (!list.length) cache = null;
    return list;
  });
  return cache;
}

/** Turn what was typed into a location slug: a known name, a slug, or a best-effort slug of the text. */
export function resolveLocation(text: string, options: LocationDto[]): string | undefined {
  const t = text.trim();
  if (!t) return undefined;
  const hit = options.find((o) => o.name.toLowerCase() === t.toLowerCase() || o.slug === t.toLowerCase());
  return hit?.slug ?? slugify(t);
}

export function useLocationOptions() {
  const [options, setOptions] = useState<LocationDto[]>([]);
  useEffect(() => {
    let live = true;
    loadSuggestions().then((l) => live && setOptions(l));
    return () => {
      live = false;
    };
  }, []);
  return options;
}

/** A text field with place suggestions (a native datalist, so it works with keyboards and screen readers). */
export function LocationField({
  value,
  onChange,
  options,
  label = "Location",
  srLabel,
  className,
  inputClassName,
}: {
  value: string;
  onChange: (v: string) => void;
  options: LocationDto[];
  label?: string;
  srLabel?: boolean;
  className?: string;
  inputClassName?: string;
}) {
  const id = useId();
  return (
    <div className={cx("min-w-0", className)}>
      <label htmlFor={id} className={srLabel ? "sr-only" : labelClass}>
        {label}
      </label>
      <div className={cx("relative", !srLabel && "mt-1.5")}>
        <MapPinIcon className="pointer-events-none absolute top-1/2 left-3 h-4 w-4 -translate-y-1/2 text-faint" />
        <input
          id={id}
          list={`${id}-list`}
          className={cx(inputClass, "pl-9", inputClassName)}
          placeholder="Area, city or state, e.g. Lekki"
          autoComplete="off"
          value={value}
          onChange={(e) => onChange(e.target.value)}
        />
        <datalist id={`${id}-list`}>
          {options.map((o) => (
            <option key={o.id} value={o.name} />
          ))}
        </datalist>
      </div>
    </div>
  );
}
