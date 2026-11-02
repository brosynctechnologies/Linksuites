"use client";

import type { GeoJSONSource, LngLatBoundsLike, Map as MlMap, Marker } from "maplibre-gl";
import { useRouter } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { DEFAULT_CENTER, MAP_STYLE } from "@/lib/config";
import { listingPath } from "@/lib/format";
import { shortMoney } from "@/lib/money";
import type { MapPin } from "@/lib/types";
import { cx } from "./styles";
import { SearchIcon } from "./icons";

// MapLibre GL with the OpenFreeMap "liberty" style (no API key). Loaded on the client only.

const loadLib = () => import("maplibre-gl");

function reducedMotion() {
  return typeof window !== "undefined" && window.matchMedia?.("(prefers-reduced-motion: reduce)").matches;
}

/** "w,s,e,n" with 5 decimals, the format the search API takes. */
export function boundsToBbox(map: MlMap): string {
  const b = map.getBounds();
  const clamp = (v: number, lo: number, hi: number) => Math.min(hi, Math.max(lo, v));
  return [clamp(b.getWest(), -180, 180), clamp(b.getSouth(), -90, 90), clamp(b.getEast(), -180, 180), clamp(b.getNorth(), -90, 90)].map((n) => n.toFixed(5)).join(",");
}

function parseBbox(bbox: string | undefined): [[number, number], [number, number]] | null {
  const p = bbox?.split(",").map(Number);
  if (!p || p.length !== 4 || !p.every(Number.isFinite)) return null;
  return [
    [p[0], p[1]],
    [p[2], p[3]],
  ];
}

/**
 * Search map: price pins as Adire pills ("₦3.5m") that link to the listing page, clusters as Adire circles with
 * counts, the selected pin inverted. Hovering or focusing a pin highlights its card.
 * "Search this area" appears once the visitor moves the map and hands back the visible bbox.
 */
export function SearchMap({
  pins,
  selectedId,
  onSelect,
  onSearchArea,
  bbox,
  center,
  periodSuffix,
  className,
}: {
  pins: MapPin[];
  selectedId: string | null;
  onSelect: (id: string) => void;
  onSearchArea: (bbox: string) => void;
  /** The bbox the results were searched with (fits the map to it). */
  bbox?: string;
  /** [lng, lat] of the searched location, used when there are no pins. */
  center?: [number, number] | null;
  periodSuffix: string;
  className?: string;
}) {
  const box = useRef<HTMLDivElement>(null);
  const mapRef = useRef<MlMap | null>(null);
  const markers = useRef(new Map<string, { marker: Marker; el: HTMLElement }>());
  const onSelectRef = useRef(onSelect);
  const selectedRef = useRef(selectedId);
  const resultsKey = `${bbox ?? ""}|${pins.length}|${pins[0]?.id ?? ""}`;
  const keyRef = useRef(resultsKey);
  const [movedKey, setMovedKey] = useState<string | null>(null);
  const moved = movedKey === resultsKey;
  const [failed, setFailed] = useState(false);

  const router = useRouter();
  const navigateRef = useRef<(href: string) => void>(() => {});

  useEffect(() => {
    onSelectRef.current = onSelect;
  }, [onSelect]);
  useEffect(() => {
    navigateRef.current = (href) => router.push(href);
  }, [router]);

  // Create the map once; pins are pushed in by the next effect.
  useEffect(() => {
    let cancelled = false;
    const markerMap = markers.current;
    let ro: ResizeObserver | null = null;
    loadLib()
      .then((ml) => {
        if (cancelled || !box.current) return;
        const map = new ml.Map({
          container: box.current,
          style: MAP_STYLE,
          center: center ?? DEFAULT_CENTER,
          zoom: 11,
          attributionControl: { compact: false },
          dragRotate: false,
          pitchWithRotate: false,
        });
        map.touchZoomRotate.disableRotation();
        map.addControl(new ml.NavigationControl({ showCompass: false }), "top-right");
        mapRef.current = map;
        ro = new ResizeObserver(() => map.resize());
        ro.observe(box.current);

        const markUserMove = (e: { originalEvent?: unknown }) => {
          if (e.originalEvent) setMovedKey(keyRef.current);
        };
        map.on("dragstart", markUserMove);
        map.on("zoomstart", markUserMove);

        map.on("load", () => {
          map.addSource("pins", {
            type: "geojson",
            data: { type: "FeatureCollection", features: [] },
            cluster: true,
            clusterRadius: 48,
            clusterMaxZoom: 15,
          });
          // An invisible layer keeps the source's features queryable; the visible pins are HTML markers.
          map.addLayer({ id: "pins-hit", type: "circle", source: "pins", paint: { "circle-radius": 0, "circle-opacity": 0 } });
          const render = () => renderMarkers(ml, map);
          map.on("moveend", render);
          map.on("sourcedata", (e) => {
            if (e.sourceId === "pins" && e.isSourceLoaded) render();
          });
          window.dispatchEvent(new CustomEvent("pl-map-ready"));
        });
      })
      .catch(() => setFailed(true));

    function renderMarkers(ml: typeof import("maplibre-gl"), map: MlMap) {
      const seen = new Set<string>();
      for (const f of map.querySourceFeatures("pins")) {
        const props = f.properties as Record<string, unknown>;
        const [lng, lat] = (f.geometry as GeoJSON.Point).coordinates;
        if (props.cluster) {
          const cid = Number(props.cluster_id);
          const key = `c${cid}`;
          if (seen.has(key)) continue;
          seen.add(key);
          if (!markerMap.has(key)) {
            const count = Number(props.point_count);
            const el = document.createElement("button");
            el.type = "button";
            el.className = "map-cluster";
            const size = count < 10 ? 34 : count < 50 ? 40 : 48;
            el.style.width = el.style.height = `${size}px`;
            el.textContent = String(props.point_count_abbreviated ?? count);
            el.setAttribute("aria-label", `${count} homes here. Zoom in`);
            el.addEventListener("click", async () => {
              const src = map.getSource("pins") as GeoJSONSource;
              const zoom = await src.getClusterExpansionZoom(cid);
              map.easeTo({ center: [lng, lat], zoom, duration: reducedMotion() ? 0 : 400 });
            });
            markerMap.set(key, { marker: new ml.Marker({ element: el }).setLngLat([lng, lat]).addTo(map), el });
          }
        } else {
          const id = String(props.id);
          if (seen.has(id)) continue;
          seen.add(id);
          if (!markerMap.has(id)) {
            // A real link to the listing page, so Enter, new-tab and middle-click all work. Plain clicks navigate in-app.
            const href = props.slug && props.agencySlug ? listingPath(String(props.agencySlug), String(props.slug)) : null;
            const label = shortMoney(Number(props.price));
            let el: HTMLElement;
            if (href) {
              const a = document.createElement("a");
              a.href = href;
              a.addEventListener("click", (e) => {
                if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
                e.preventDefault();
                navigateRef.current(href);
              });
              el = a;
            } else {
              const b = document.createElement("button");
              b.type = "button";
              b.addEventListener("click", () => onSelectRef.current(id));
              el = b;
            }
            el.className = "map-pin";
            el.textContent = label;
            el.setAttribute("aria-label", `${label}${periodSuffix}. ${href ? "Open this home" : "Show this home"}`);
            el.dataset.selected = String(selectedRef.current === id);
            el.addEventListener("mouseenter", () => onSelectRef.current(id));
            el.addEventListener("focus", () => onSelectRef.current(id));
            markerMap.set(id, { marker: new ml.Marker({ element: el }).setLngLat([lng, lat]).addTo(map), el });
          }
        }
      }
      for (const [key, m] of markerMap) {
        if (!seen.has(key)) {
          m.marker.remove();
          markerMap.delete(key);
        }
      }
    }

    return () => {
      cancelled = true;
      ro?.disconnect();
      markerMap.forEach((m) => m.marker.remove());
      markerMap.clear();
      mapRef.current?.remove();
      mapRef.current = null;
    };
    // The map is created once per mount; later prop changes are applied by the effects below.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Push pins and fit the view whenever the results change.
  useEffect(() => {
    keyRef.current = `${bbox ?? ""}|${pins.length}|${pins[0]?.id ?? ""}`;
    const apply = () => {
      const map = mapRef.current;
      if (!map) return false;
      const src = map.getSource("pins") as GeoJSONSource | undefined;
      if (!src) return false;
      markers.current.forEach((m) => m.marker.remove());
      markers.current.clear();
      src.setData({
        type: "FeatureCollection",
        features: pins.map((p) => ({ type: "Feature", geometry: { type: "Point", coordinates: [p.lng, p.lat] }, properties: { id: p.id, price: p.priceMinor, slug: p.slug, agencySlug: p.agencySlug } })),
      });
      const fit = parseBbox(bbox);
      const duration = reducedMotion() ? 0 : 500;
      if (fit) {
        map.fitBounds(fit as LngLatBoundsLike, { duration, padding: 0 });
      } else if (pins.length > 0) {
        const lngs = pins.map((p) => p.lng);
        const lats = pins.map((p) => p.lat);
        map.fitBounds(
          [
            [Math.min(...lngs), Math.min(...lats)],
            [Math.max(...lngs), Math.max(...lats)],
          ],
          { padding: 56, maxZoom: 15, duration },
        );
      } else if (center) {
        map.easeTo({ center, zoom: 12, duration });
      }
      return true;
    };
    if (apply()) return;
    const onReady = () => apply();
    window.addEventListener("pl-map-ready", onReady, { once: true });
    return () => window.removeEventListener("pl-map-ready", onReady);
  }, [pins, bbox, center]);

  // Reflect the selected listing on its pin.
  useEffect(() => {
    selectedRef.current = selectedId;
    markers.current.forEach((m, key) => {
      if (m.el.classList.contains("map-pin")) m.el.dataset.selected = String(key === selectedId);
    });
  }, [selectedId]);

  return (
    <div className={cx("relative h-full w-full bg-plaster-2", className)}>
      <div ref={box} className="h-full w-full" role="region" aria-label="Map of results" />
      {failed && <p className="absolute inset-x-4 top-4 rounded-control bg-surface p-3 text-sm text-slate-700">The map couldn&apos;t load. The list still shows every result.</p>}
      {moved && (
        <button
          type="button"
          onClick={() => mapRef.current && onSearchArea(boundsToBbox(mapRef.current))}
          className="absolute top-3 left-1/2 z-10 inline-flex min-h-11 -translate-x-1/2 items-center gap-2 rounded-full bg-adire px-4 text-sm font-semibold text-white shadow-float hover:bg-adire-dark"
        >
          <SearchIcon className="h-4 w-4" /> Search this area
        </button>
      )}
    </div>
  );
}

/** A small map with one marker (listing page). Scroll-zoom needs Ctrl/two fingers so the page scrolls freely. */
export function PointMap({ lat, lng, label, className, zoom = 14 }: { lat: number; lng: number; label: string; className?: string; zoom?: number }) {
  const box = useRef<HTMLDivElement>(null);
  const [failed, setFailed] = useState(false);
  useEffect(() => {
    let map: MlMap | null = null;
    let cancelled = false;
    loadLib()
      .then((ml) => {
        if (cancelled || !box.current) return;
        map = new ml.Map({
          container: box.current,
          style: MAP_STYLE,
          center: [lng, lat],
          zoom,
          attributionControl: { compact: false },
          cooperativeGestures: true,
          dragRotate: false,
        });
        map.addControl(new ml.NavigationControl({ showCompass: false }), "top-right");
        new ml.Marker({ color: "#33309A" }).setLngLat([lng, lat]).addTo(map);
      })
      .catch(() => setFailed(true));
    return () => {
      cancelled = true;
      map?.remove();
    };
  }, [lat, lng, zoom]);
  return (
    <div className={cx("relative overflow-hidden rounded-panel border border-line bg-plaster-2", className)}>
      <div ref={box} className="h-full w-full" role="region" aria-label={label} />
      {failed && <p className="absolute inset-x-3 top-3 text-sm text-slate-700">The map couldn&apos;t load.</p>}
    </div>
  );
}

/**
 * Wizard location step: a draggable Adire pin. Tapping the map also moves the pin. `centerKey` changes when the
 * agent picks another area, which recentres the map on `center`.
 */
export function PinPicker({
  value,
  onChange,
  center,
  centerKey,
  className,
}: {
  value: { lat: number; lng: number } | null;
  onChange: (v: { lat: number; lng: number }) => void;
  center: [number, number];
  centerKey: string;
  className?: string;
}) {
  const box = useRef<HTMLDivElement>(null);
  const mapRef = useRef<MlMap | null>(null);
  const markerRef = useRef<Marker | null>(null);
  const changeRef = useRef(onChange);
  const [failed, setFailed] = useState(false);
  const initial = useRef({ value, center });

  useEffect(() => {
    changeRef.current = onChange;
  }, [onChange]);

  useEffect(() => {
    let cancelled = false;
    loadLib()
      .then((ml) => {
        if (cancelled || !box.current) return;
        const start = initial.current.value ? ([initial.current.value.lng, initial.current.value.lat] as [number, number]) : initial.current.center;
        const map = new ml.Map({
          container: box.current,
          style: MAP_STYLE,
          center: start,
          zoom: 15,
          attributionControl: { compact: false },
          dragRotate: false,
        });
        map.addControl(new ml.NavigationControl({ showCompass: false }), "top-right");
        const marker = new ml.Marker({ color: "#33309A", draggable: true }).setLngLat(start).addTo(map);
        marker.on("dragend", () => {
          const p = marker.getLngLat();
          changeRef.current({ lat: Number(p.lat.toFixed(6)), lng: Number(p.lng.toFixed(6)) });
        });
        map.on("click", (e) => {
          marker.setLngLat(e.lngLat);
          changeRef.current({ lat: Number(e.lngLat.lat.toFixed(6)), lng: Number(e.lngLat.lng.toFixed(6)) });
        });
        mapRef.current = map;
        markerRef.current = marker;
        if (!initial.current.value) changeRef.current({ lat: start[1], lng: start[0] });
      })
      .catch(() => setFailed(true));
    return () => {
      cancelled = true;
      mapRef.current?.remove();
      mapRef.current = null;
      markerRef.current = null;
    };
  }, []);

  // Another area was chosen: move map and pin to its centre.
  const lastKey = useRef(centerKey);
  useEffect(() => {
    if (lastKey.current === centerKey) return;
    lastKey.current = centerKey;
    const map = mapRef.current;
    if (!map) return;
    map.jumpTo({ center, zoom: 15 });
    markerRef.current?.setLngLat(center);
    changeRef.current({ lat: center[1], lng: center[0] });
  }, [centerKey, center]);

  // Keep the marker where the form says (e.g. after loading a saved listing).
  useEffect(() => {
    if (value && markerRef.current) {
      const cur = markerRef.current.getLngLat();
      if (Math.abs(cur.lat - value.lat) > 1e-6 || Math.abs(cur.lng - value.lng) > 1e-6) markerRef.current.setLngLat([value.lng, value.lat]);
    }
  }, [value]);

  return (
    <div className={cx("relative overflow-hidden rounded-panel border border-line bg-plaster-2", className)}>
      <div ref={box} className="h-full w-full" role="application" aria-label="Map: drag the pin onto the property" />
      {failed && <p className="absolute inset-x-3 top-3 text-sm text-slate-700">The map couldn&apos;t load. Check your connection and reload the page.</p>}
    </div>
  );
}
