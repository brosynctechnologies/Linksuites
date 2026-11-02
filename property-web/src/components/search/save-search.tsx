"use client";

import Link from "next/link";
import { useState, type FormEvent } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { useAuth, useHydrated } from "@/lib/auth";
import { purposeLabel, searchHref, toListingFilter, type SearchState } from "@/lib/search";
import type { AlertFrequency, SavedSearchDto } from "@/lib/types";
import { BellIcon } from "../icons";
import { Button, ErrorBox, Input, Modal, Notice, buttonClass, cx } from "../ui";

export const FREQUENCIES: { value: AlertFrequency; label: string; hint: string }[] = [
  { value: "Instant", label: "Instantly", hint: "An email as soon as a matching home goes live." },
  { value: "Daily", label: "Daily", hint: "One email a day with new matches." },
  { value: "Off", label: "Off", hint: "Keep the search, no emails." },
];

/** "Save search": signed in → name it and pick alerts; signed out → log in first. */
export function SaveSearchButton({ state, className }: { state: SearchState; className?: string }) {
  const auth = useAuth();
  const hydrated = useHydrated();
  const [open, setOpen] = useState(false);
  const [saved, setSaved] = useState<SavedSearchDto | null>(null);

  if (!hydrated) return null;
  if (!auth) {
    return (
      <Link href={`/login?next=${encodeURIComponent(searchHref(state))}`} className={buttonClass("secondary", className)}>
        <BellIcon className="h-4 w-4" /> Save search
      </Link>
    );
  }
  if (saved) {
    return (
      <Link href="/me/searches" className={buttonClass("ghost", cx("text-seal", className))}>
        Saved. Manage alerts
      </Link>
    );
  }
  return (
    <>
      <Button variant="secondary" className={className} onClick={() => setOpen(true)}>
        <BellIcon className="h-4 w-4" /> Save search
      </Button>
      <Modal open={open} title="Save this search" onClose={() => setOpen(false)}>
        {open && (
          <SaveForm
            state={state}
            onSaved={(s) => {
              setSaved(s);
              setOpen(false);
            }}
          />
        )}
      </Modal>
    </>
  );
}

function SaveForm({ state, onSaved }: { state: SearchState; onSaved: (s: SavedSearchDto) => void }) {
  const place = state.location ? state.location.replace(/-/g, " ") : state.bbox ? "this map area" : "anywhere";
  const [name, setName] = useState(`Homes ${purposeLabel(state.purpose)} in ${place}`.slice(0, 80));
  const [frequency, setFrequency] = useState<AlertFrequency>("Daily");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      onSaved(await api<SavedSearchDto>("/api/me/saved-searches", { method: "POST", body: { name: name.trim(), filter: toListingFilter(state), frequency } }));
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-4">
      <Input label="Name" required value={name} onChange={(e) => setName(e.target.value)} error={error?.field("name")} />
      <fieldset className="space-y-2">
        <legend className="text-sm font-medium text-slate-700">Email me new matches</legend>
        {FREQUENCIES.map((f) => (
          <label
            key={f.value}
            className={cx("flex min-h-11 cursor-pointer items-start gap-3 rounded-control border p-3", frequency === f.value ? "border-adire bg-adire-wash" : "border-line")}
          >
            <input type="radio" name="frequency" checked={frequency === f.value} onChange={() => setFrequency(f.value)} className="mt-1 accent-[#33309a]" />
            <span>
              <span className="block text-sm font-semibold text-asphalt">{f.label}</span>
              <span className="block text-xs text-slate-600">{f.hint}</span>
            </span>
          </label>
        ))}
      </fieldset>
      {error?.status === 409 || error?.status === 400 ? <Notice tone="warning">{error.message}</Notice> : <ErrorBox error={error} />}
      <Button type="submit" className="w-full" loading={busy}>
        Save search
      </Button>
    </form>
  );
}
