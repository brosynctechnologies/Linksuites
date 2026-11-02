"use client";

import { useEffect, useRef, useState, type DragEvent } from "react";
import { api, toApiError, uploadWithProgress, type ApiError } from "@/lib/api";
import type { ListingMediaDto, StudioListingDto } from "@/lib/types";
import { ArrowDownIcon, ArrowUpIcon, CameraIcon, GripIcon, TrashIcon, UploadIcon } from "@/components/icons";
import { ErrorBox, Notice, StatusBadge, buttonClass, cx } from "@/components/ui";

const MAX_PHOTOS = 30;
const MAX_BYTES = 15 * 1024 * 1024;
const TYPES = ["image/jpeg", "image/png", "image/webp"];

interface Upload {
  key: string;
  name: string;
  progress: number;
  error?: string;
}

/**
 * Step 4: drag-drop or camera, per-photo progress, a shimmer while each photo is Processing (the listing is polled
 * every 3 s until all are Ready), Failed with the reason, a duplicate warning naming the other agency, and drag (or
 * arrow buttons) to reorder. The first photo is the cover.
 */
export function PhotosStep({ listing, onListing }: { listing: StudioListingDto; onListing: (l: StudioListingDto) => void }) {
  const media = [...(listing.media ?? [])].sort((a, b) => a.position - b.position);
  const [uploads, setUploads] = useState<Upload[]>([]);
  const [error, setError] = useState<ApiError | string | null>(null);
  const [dragOver, setDragOver] = useState(false);
  const [dragId, setDragId] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const fileRef = useRef<HTMLInputElement>(null);
  const cameraRef = useRef<HTMLInputElement>(null);
  const processing = media.some((m) => m.status === "Processing");
  const listingRef = useRef(onListing);

  useEffect(() => {
    listingRef.current = onListing;
  }, [onListing]);

  // Poll every 3 s while any photo is still processing.
  useEffect(() => {
    if (!processing) return;
    const t = setInterval(() => {
      api<StudioListingDto>(`/api/property/listings/${listing.id}`).then((l) => listingRef.current(l), () => {});
    }, 3000);
    return () => clearInterval(t);
  }, [processing, listing.id]);

  async function addFiles(files: FileList | File[]) {
    setError(null);
    const list = Array.from(files);
    const room = MAX_PHOTOS - media.length - uploads.filter((u) => !u.error).length;
    if (room <= 0) return setError(`A listing can have up to ${MAX_PHOTOS} photos.`);
    const accepted = list.slice(0, room);
    if (list.length > room) setError(`Only ${room} more photo${room === 1 ? "" : "s"} fit (${MAX_PHOTOS} per listing).`);
    for (const file of accepted) {
      const key = `${file.name}-${file.size}-${Math.random().toString(36).slice(2)}`;
      if (!TYPES.includes(file.type)) {
        setUploads((u) => [...u, { key, name: file.name, progress: 0, error: "Use a JPEG, PNG or WebP photo." }]);
        continue;
      }
      if (file.size > MAX_BYTES) {
        setUploads((u) => [...u, { key, name: file.name, progress: 0, error: "This photo is over 15 MB." }]);
        continue;
      }
      setUploads((u) => [...u, { key, name: file.name, progress: 0 }]);
      try {
        const m = await uploadWithProgress<ListingMediaDto>(`/api/property/listings/${listing.id}/media`, file, (p) =>
          setUploads((u) => u.map((x) => (x.key === key ? { ...x, progress: p } : x))),
        );
        setUploads((u) => u.filter((x) => x.key !== key));
        listingRef.current({ ...listing, media: [...(listing.media ?? []), m] });
        // Read back the whole listing so concurrent uploads don't overwrite each other's additions.
        api<StudioListingDto>(`/api/property/listings/${listing.id}`).then((l) => listingRef.current(l), () => {});
      } catch (e) {
        setUploads((u) => u.map((x) => (x.key === key ? { ...x, error: toApiError(e).message } : x)));
      }
    }
  }

  async function remove(m: ListingMediaDto) {
    if (!window.confirm("Delete this photo?")) return;
    setBusy(m.id);
    setError(null);
    try {
      await api(`/api/property/listings/${listing.id}/media/${m.id}`, { method: "DELETE" });
      onListing({ ...listing, media: (listing.media ?? []).filter((x) => x.id !== m.id) });
    } catch (e) {
      setError(toApiError(e));
    } finally {
      setBusy(null);
    }
  }

  async function reorder(ids: string[]) {
    const before = listing;
    onListing({ ...listing, media: ids.map((id, i) => ({ ...media.find((m) => m.id === id)!, position: i })) });
    try {
      await api(`/api/property/listings/${listing.id}/media/order`, { method: "PUT", body: { ids } });
    } catch (e) {
      onListing(before);
      setError(toApiError(e));
    }
  }

  const move = (id: string, d: number) => {
    const ids = media.map((m) => m.id);
    const i = ids.indexOf(id);
    const j = i + d;
    if (j < 0 || j >= ids.length) return;
    [ids[i], ids[j]] = [ids[j], ids[i]];
    void reorder(ids);
  };

  const onDropOn = (targetId: string) => {
    if (!dragId || dragId === targetId) return;
    const ids = media.map((m) => m.id).filter((id) => id !== dragId);
    ids.splice(ids.indexOf(targetId), 0, dragId);
    setDragId(null);
    void reorder(ids);
  };

  const onDropZone = (e: DragEvent) => {
    e.preventDefault();
    setDragOver(false);
    if (e.dataTransfer.files?.length) void addFiles(e.dataTransfer.files);
  };

  return (
    <div className="space-y-4">
      <div
        onDragOver={(e) => {
          if (e.dataTransfer.types.includes("Files")) {
            e.preventDefault();
            setDragOver(true);
          }
        }}
        onDragLeave={() => setDragOver(false)}
        onDrop={onDropZone}
        className={cx(
          "flex flex-col items-center gap-3 rounded-panel border-2 border-dashed px-4 py-8 text-center",
          dragOver ? "border-adire bg-adire-wash" : "border-slate-300 bg-surface",
        )}
      >
        <UploadIcon className="h-7 w-7 text-muted" />
        <p className="text-sm text-slate-700">
          Drop photos here, or choose them. JPEG, PNG or WebP up to 15 MB, {MAX_PHOTOS} per listing. {media.length} added.
        </p>
        <div className="flex flex-wrap justify-center gap-2">
          <button type="button" className={buttonClass("primary")} onClick={() => fileRef.current?.click()}>
            <UploadIcon className="h-4 w-4" /> Choose photos
          </button>
          <button type="button" className={buttonClass("secondary", "sm:hidden")} onClick={() => cameraRef.current?.click()}>
            <CameraIcon className="h-4 w-4" /> Take a photo
          </button>
        </div>
        <input
          ref={fileRef}
          type="file"
          accept={TYPES.join(",")}
          multiple
          className="sr-only"
          tabIndex={-1}
          aria-hidden="true"
          onChange={(e) => {
            if (e.target.files) void addFiles(e.target.files);
            e.target.value = "";
          }}
        />
        <input
          ref={cameraRef}
          type="file"
          accept="image/*"
          capture="environment"
          className="sr-only"
          tabIndex={-1}
          aria-hidden="true"
          onChange={(e) => {
            if (e.target.files) void addFiles(e.target.files);
            e.target.value = "";
          }}
        />
      </div>

      {error && <ErrorBox error={error} />}
      {processing && <Notice>Photos are being processed (resized, watermarked and checked for duplicates). This takes a few seconds.</Notice>}

      {uploads.length > 0 && (
        <ul className="space-y-2" aria-label="Uploads">
          {uploads.map((u) => (
            <li key={u.key} className="rounded-panel border border-line bg-surface px-3 py-2 text-sm">
              <div className="flex items-center justify-between gap-2">
                <span className="truncate">{u.name}</span>
                {u.error ? (
                  <button type="button" className="min-h-11 text-xs font-semibold text-muted hover:underline" onClick={() => setUploads((x) => x.filter((y) => y.key !== u.key))}>
                    Dismiss
                  </button>
                ) : (
                  <span className="tnum text-xs text-muted">{Math.round(u.progress * 100)}%</span>
                )}
              </div>
              {u.error ? (
                <p className="mt-1 text-xs font-medium text-brick">{u.error}</p>
              ) : (
                <div className="mt-1.5 h-1.5 overflow-hidden rounded-full bg-plaster-2" role="progressbar" aria-label={`Uploading ${u.name}`} aria-valuenow={Math.round(u.progress * 100)} aria-valuemin={0} aria-valuemax={100}>
                  <div className="h-full rounded-full bg-adire transition-[width]" style={{ width: `${Math.round(u.progress * 100)}%` }} />
                </div>
              )}
            </li>
          ))}
        </ul>
      )}

      {media.length > 0 && (
        <ol className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4" aria-label="Photos in order">
          {media.map((m, i) => (
            <li
              key={m.id}
              draggable
              onDragStart={(e) => {
                setDragId(m.id);
                e.dataTransfer.effectAllowed = "move";
              }}
              onDragOver={(e) => {
                if (dragId) e.preventDefault();
              }}
              onDrop={(e) => {
                e.preventDefault();
                onDropOn(m.id);
              }}
              onDragEnd={() => setDragId(null)}
              className={cx("overflow-hidden rounded-panel border bg-surface", m.duplicateOfAgency ? "border-brick" : "border-line", dragId === m.id && "opacity-50")}
            >
              <div className="relative aspect-[4/3] bg-plaster-2">
                {m.status === "Processing" ? (
                  <div className="shimmer h-full w-full" aria-label="Processing" />
                ) : m.status === "Failed" ? (
                  <div className="flex h-full items-center justify-center p-2 text-center text-xs font-medium text-brick">{m.error || "This photo couldn't be processed."}</div>
                ) : (
                  <img src={m.thumbUrl ?? m.cardUrl ?? ""} alt={`Photo ${i + 1}`} className="h-full w-full object-cover" />
                )}
                {i === 0 && <span className="absolute top-1.5 left-1.5 rounded-[4px] bg-asphalt/80 px-1.5 py-0.5 text-xs font-semibold text-white">Cover</span>}
                <span className="absolute top-1.5 right-1.5 cursor-grab rounded-[4px] bg-surface/90 p-1 text-muted" aria-hidden="true">
                  <GripIcon className="h-4 w-4" />
                </span>
              </div>
              <div className="space-y-1.5 p-2">
                <div className="flex items-center justify-between gap-1">
                  <StatusBadge tone={m.status === "Ready" ? "seal" : m.status === "Failed" ? "brick" : "ochre"}>{m.status}</StatusBadge>
                  <div className="flex">
                    <button type="button" onClick={() => move(m.id, -1)} disabled={i === 0} className="flex h-11 w-9 items-center justify-center text-muted hover:text-asphalt disabled:opacity-30" aria-label={`Move photo ${i + 1} earlier`}>
                      <ArrowUpIcon className="h-4 w-4" />
                    </button>
                    <button
                      type="button"
                      onClick={() => move(m.id, 1)}
                      disabled={i === media.length - 1}
                      className="flex h-11 w-9 items-center justify-center text-muted hover:text-asphalt disabled:opacity-30"
                      aria-label={`Move photo ${i + 1} later`}
                    >
                      <ArrowDownIcon className="h-4 w-4" />
                    </button>
                    <button type="button" onClick={() => remove(m)} disabled={busy === m.id} className="flex h-11 w-9 items-center justify-center text-brick hover:bg-brick-wash" aria-label={`Delete photo ${i + 1}`}>
                      <TrashIcon className="h-4 w-4" />
                    </button>
                  </div>
                </div>
                {m.duplicateOfAgency && (
                  <p className="text-xs font-medium text-brick">
                    This photo matches a listing by {m.duplicateOfAgency}. The listing will go to a moderator. Delete it if it isn&apos;t yours.
                  </p>
                )}
              </div>
            </li>
          ))}
        </ol>
      )}
    </div>
  );
}
