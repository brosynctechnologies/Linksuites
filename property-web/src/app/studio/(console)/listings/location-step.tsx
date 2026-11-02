"use client";

import { useEffect, useMemo, useState } from "react";
import { api, asList } from "@/lib/api";
import { DEFAULT_CENTER } from "@/lib/config";
import type { LocationDto } from "@/lib/types";
import { PinPicker } from "@/components/maps";
import { Button, Input, Select } from "@/components/ui";

const children = new Map<string, Promise<LocationDto[]>>();
function loadChildren(parent: string | null): Promise<LocationDto[]> {
  const key = parent ?? "";
  let p = children.get(key);
  if (!p) {
    p = api<LocationDto[]>(`/api/locations${parent ? `?parent=${encodeURIComponent(parent)}` : ""}`, { auth: false })
      .then((l) => asList(l))
      .catch((e) => {
        children.delete(key);
        throw e;
      });
    children.set(key, p);
  }
  return p;
}

/**
 * Find the state and city above a saved area. The contract has no location-by-id read, so this walks the public
 * tree (states → their children → cities' children) once and caches it.
 */
async function findPath(areaId: string): Promise<{ state: LocationDto; city: LocationDto; area: LocationDto } | null> {
  const states = await loadChildren(null);
  const level2 = await Promise.all(states.map((s) => loadChildren(s.slug).then((c) => c.map((city) => ({ state: s, city }))).catch(() => [])));
  const cities = level2.flat();
  for (const { state, city } of cities) {
    const areas = await loadChildren(city.slug).catch(() => [] as LocationDto[]);
    const area = areas.find((a) => a.id === areaId);
    if (area) return { state, city, area };
  }
  return null;
}

export interface LocationValue {
  locationId: string;
  address: string;
  lat: number | null;
  lng: number | null;
}

/** Step 2: state → city → area, then drag the pin; lat/lng shown small under the map. */
export function LocationStep({
  value,
  onChange,
  errors,
}: {
  value: LocationValue;
  onChange: (patch: Partial<LocationValue>) => void;
  errors: { locationId?: string; lat?: string; address?: string };
}) {
  const [states, setStates] = useState<LocationDto[]>([]);
  const [cities, setCities] = useState<LocationDto[]>([]);
  const [areas, setAreas] = useState<LocationDto[]>([]);
  const [stateSlug, setStateSlug] = useState("");
  const [citySlug, setCitySlug] = useState("");
  const [finding, setFinding] = useState(!!value.locationId);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [nonce, setNonce] = useState(0);

  useEffect(() => {
    loadChildren(null).then(setStates, () => setLoadError("We couldn't load the list of states."));
  }, [nonce]);

  // Editing: preselect the saved area's state and city.
  const [initialId] = useState(value.locationId);
  useEffect(() => {
    if (!initialId) return;
    let live = true;
    findPath(initialId)
      .then((p) => {
        if (!live) return;
        if (p) {
          setStateSlug(p.state.slug);
          setCitySlug(p.city.slug);
        }
        setFinding(false);
      })
      .catch(() => live && setFinding(false));
    return () => {
      live = false;
    };
  }, [initialId]);

  useEffect(() => {
    if (!stateSlug) return;
    let live = true;
    loadChildren(stateSlug).then(
      (c) => live && setCities(c),
      () => live && setLoadError("We couldn't load the cities."),
    );
    return () => {
      live = false;
    };
  }, [stateSlug]);

  useEffect(() => {
    if (!citySlug) return;
    let live = true;
    loadChildren(citySlug).then(
      (a) => live && setAreas(a),
      () => live && setLoadError("We couldn't load the areas."),
    );
    return () => {
      live = false;
    };
  }, [citySlug]);

  const shownCities = stateSlug ? cities : [];
  const shownAreas = citySlug ? areas : [];
  const area = shownAreas.find((a) => a.id === value.locationId);
  const city = shownCities.find((c) => c.slug === citySlug);
  const state = states.find((s) => s.slug === stateSlug);
  const center = useMemo<[number, number]>(() => {
    const src = [area, city, state].find((l) => l?.lat != null && l?.lng != null);
    if (src) return [src.lng!, src.lat!];
    return value.lat != null && value.lng != null ? [value.lng, value.lat] : DEFAULT_CENTER;
  }, [area, city, state, value.lat, value.lng]);

  return (
    <div className="space-y-5">
      {loadError && (
        <p className="text-sm text-brick" role="alert">
          {loadError}{" "}
          <Button
            variant="ghost"
            onClick={() => {
              setLoadError(null);
              setNonce((n) => n + 1);
            }}
          >
            Try again
          </Button>
        </p>
      )}
      {finding && <p className="text-sm text-muted">Finding the saved area…</p>}
      <div className="grid gap-4 sm:grid-cols-3">
        <Select
          label="State"
          value={stateSlug}
          onChange={(e) => {
            setStateSlug(e.target.value);
            setCitySlug("");
            onChange({ locationId: "" });
          }}
        >
          <option value="">Choose a state</option>
          {states.map((s) => (
            <option key={s.id} value={s.slug}>
              {s.name}
            </option>
          ))}
        </Select>
        <Select
          label="City"
          value={citySlug}
          disabled={!stateSlug}
          onChange={(e) => {
            setCitySlug(e.target.value);
            onChange({ locationId: "" });
          }}
          hint={stateSlug && shownCities.length === 0 ? "No cities listed in this state yet." : undefined}
        >
          <option value="">Choose a city</option>
          {shownCities.map((c) => (
            <option key={c.id} value={c.slug}>
              {c.name}
            </option>
          ))}
        </Select>
        <Select
          label="Area"
          value={value.locationId}
          disabled={!citySlug}
          onChange={(e) => onChange({ locationId: e.target.value })}
          error={errors.locationId}
          hint={citySlug && shownAreas.length === 0 ? "No areas listed in this city yet." : undefined}
        >
          <option value="">Choose an area</option>
          {shownAreas.map((a) => (
            <option key={a.id} value={a.id}>
              {a.name}
            </option>
          ))}
        </Select>
      </div>
      <Input
        label="Street address (optional)"
        value={value.address}
        onChange={(e) => onChange({ address: e.target.value })}
        hint="Shown on the listing. Leave out the flat number if you prefer."
        error={errors.address}
      />
      <div>
        <p className="text-sm font-medium text-slate-700">Pin the property</p>
        <p className="text-xs text-muted">Drag the pin, or tap the map where the property is.</p>
        {value.locationId || (value.lat != null && value.lng != null) ? (
          <>
            <PinPicker
              className="mt-2 h-72 sm:h-96"
              value={value.lat != null && value.lng != null ? { lat: value.lat, lng: value.lng } : null}
              onChange={(p) => onChange({ lat: p.lat, lng: p.lng })}
              center={center}
              centerKey={value.locationId || "saved"}
            />
            <p className="tnum mt-1.5 text-xs text-muted">
              {value.lat != null && value.lng != null ? `Lat ${value.lat.toFixed(5)}, Lng ${value.lng.toFixed(5)}` : "No pin yet"}
            </p>
          </>
        ) : (
          <p className="mt-2 rounded-panel border border-dashed border-slate-300 bg-surface p-6 text-center text-sm text-muted">Choose the area first; the map opens on it.</p>
        )}
        {errors.lat && (
          <p className="mt-1 text-xs font-medium text-brick" role="alert">
            {errors.lat}
          </p>
        )}
      </div>
    </div>
  );
}
