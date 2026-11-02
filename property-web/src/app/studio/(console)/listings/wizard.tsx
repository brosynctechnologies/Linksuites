"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState, type ReactNode } from "react";
import { api, asList, toApiError, type ApiError } from "@/lib/api";
import { COST_KINDS } from "@/lib/config";
import { formatMoney, groupDigits, toMajorInput, toMinor } from "@/lib/money";
import { useCan } from "@/lib/permissions";
import { useApi } from "@/lib/use-api";
import type {
  AgentProfileDto,
  CostKind,
  ListingPurpose,
  PricePeriod,
  RefItem,
  SaveListingRequest,
  StudioListingDto,
} from "@/lib/types";
import { Ledger, priceLineLabel } from "@/components/ledger";
import { CheckIcon, PlusIcon, TrashIcon } from "@/components/icons";
import { ApiErrorView, Button, Checkbox, ErrorBox, Input, Notice, Select, Textarea, cx } from "@/components/ui";
import { LocationStep } from "./location-step";
import { PhotosStep } from "./photos-step";

export const STEPS = ["Basics", "Location", "Costs", "Photos", "Review"] as const;
export type StepIndex = 0 | 1 | 2 | 3 | 4;

interface CostRow {
  kind: CostKind;
  label: string;
  amount: string;
}

interface Form {
  title: string;
  description: string;
  purpose: ListingPurpose;
  propertyTypeId: string;
  pricePeriod: PricePeriod;
  price: string;
  bedrooms: string;
  bathrooms: string;
  toilets: string;
  sizeSqm: string;
  furnished: boolean;
  tourUrl: string;
  videoUrl: string;
  amenityIds: string[];
  agentProfileId: string;
  cleaningFee: string;
  minNights: string;
  maxGuests: string;
  checkInTime: string;
  checkOutTime: string;
  houseRules: string;
  locationId: string;
  address: string;
  lat: number | null;
  lng: number | null;
  costs: CostRow[];
}

const PERIODS: Record<ListingPurpose, { value: PricePeriod; label: string }[]> = {
  Rent: [
    { value: "Year", label: "Per year" },
    { value: "Month", label: "Per month" },
  ],
  Sale: [{ value: "Total", label: "Total price" }],
  ShortLet: [{ value: "Night", label: "Per night" }],
};

function fromListing(l: StudioListingDto | null): Form {
  return {
    title: l?.title ?? "",
    description: l?.description ?? "",
    purpose: l?.purpose ?? "Rent",
    propertyTypeId: l?.propertyTypeId ?? "",
    pricePeriod: l?.pricePeriod ?? "Year",
    price: l ? groupDigits(toMajorInput(l.priceMinor)) : "",
    bedrooms: l ? String(l.bedrooms) : "",
    bathrooms: l ? String(l.bathrooms) : "",
    toilets: l ? String(l.toilets) : "",
    sizeSqm: l?.sizeSqm ? String(l.sizeSqm) : "",
    furnished: l?.furnished ?? false,
    tourUrl: l?.tourUrl ?? "",
    videoUrl: l?.videoUrl ?? "",
    amenityIds: l?.amenityIds ?? [],
    agentProfileId: l?.agentProfileId ?? "",
    cleaningFee: l?.shortLet ? groupDigits(toMajorInput(l.shortLet.cleaningFeeMinor)) : "",
    minNights: l?.shortLet ? String(l.shortLet.minNights) : "1",
    maxGuests: l?.shortLet ? String(l.shortLet.maxGuests) : "2",
    checkInTime: l?.shortLet?.checkInTime ?? "14:00",
    checkOutTime: l?.shortLet?.checkOutTime ?? "12:00",
    houseRules: l?.shortLet?.houseRules ?? "",
    locationId: l?.locationId ?? "",
    address: l?.address ?? "",
    lat: l?.lat ?? null,
    lng: l?.lng ?? null,
    costs: (l?.costs ?? []).map((c) => ({ kind: c.kind, label: c.label, amount: groupDigits(toMajorInput(c.amountMinor)) })),
  };
}

const int = (s: string) => (s.trim() === "" ? 0 : Math.max(0, Math.round(Number(s))));

function toRequest(f: Form): SaveListingRequest {
  return {
    title: f.title.trim(),
    description: f.description.trim() || null,
    purpose: f.purpose,
    propertyTypeId: f.propertyTypeId,
    locationId: f.locationId,
    address: f.address.trim() || null,
    lat: f.lat ?? 0,
    lng: f.lng ?? 0,
    priceMinor: toMinor(f.price) ?? 0,
    pricePeriod: f.pricePeriod,
    bedrooms: int(f.bedrooms),
    bathrooms: int(f.bathrooms),
    toilets: int(f.toilets),
    sizeSqm: f.sizeSqm.trim() ? int(f.sizeSqm) : null,
    furnished: f.furnished,
    tourUrl: f.tourUrl.trim() || null,
    videoUrl: f.videoUrl.trim() || null,
    costs: f.costs.map((c) => ({ kind: c.kind, label: c.label.trim(), amountMinor: toMinor(c.amount) ?? 0 })),
    amenityIds: f.amenityIds,
    shortLet:
      f.purpose === "ShortLet"
        ? {
            cleaningFeeMinor: toMinor(f.cleaningFee) ?? 0,
            minNights: Math.max(1, int(f.minNights)),
            maxGuests: Math.max(1, int(f.maxGuests)),
            checkInTime: f.checkInTime || null,
            checkOutTime: f.checkOutTime || null,
            houseRules: f.houseRules.trim() || null,
          }
        : null,
    agentProfileId: f.agentProfileId || null,
  };
}

/** Which step a server field error belongs to, so the wizard can jump back to it. */
function stepOf(field: string): StepIndex {
  const f = field.toLowerCase();
  if (f.startsWith("locationid") || f === "lat" || f === "lng" || f === "address") return 1;
  if (f.startsWith("costs")) return 2;
  return 0;
}

/**
 * The 5-step listing wizard: Basics → Location pin → Costs (with a live ledger preview) → Photos → Review & submit.
 * Steps 1–3 are saved together as a draft when leaving Costs (the API needs the location and price to create it);
 * an existing listing saves on every step.
 */
export function ListingWizard({ listing, initialStep = 0, onListing }: { listing: StudioListingDto | null; initialStep?: StepIndex; onListing?: (l: StudioListingDto) => void }) {
  const router = useRouter();
  const canEdit = useCan("listings.edit");
  const [step, setStep] = useState<StepIndex>(listing ? initialStep : 0);
  const [form, setForm] = useState<Form>(() => fromListing(listing));
  const [own, setSaved] = useState<StudioListingDto | null>(listing);
  // On the edit page the parent owns the listing (the manage panel changes its status too); on /new the wizard does.
  const saved = listing ?? own;
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [local, setLocal] = useState<Record<string, string>>({});
  const [submitted, setSubmitted] = useState<StudioListingDto | null>(null);
  const types = useApi<RefItem[]>("/api/property-types", { auth: false });
  const amenities = useApi<RefItem[]>("/api/amenities", { auth: false });
  const agents = useApi<AgentProfileDto[]>("/api/property/profile/agents");
  const set = (patch: Partial<Form>) => setForm((f) => ({ ...f, ...patch }));
  const fe = (name: string) => local[name] ?? error?.field(name);

  const updateListing = (l: StudioListingDto) => {
    setSaved(l);
    onListing?.(l);
  };

  function validate(s: StepIndex): boolean {
    const errs: Record<string, string> = {};
    if (s === 0) {
      if (!form.title.trim()) errs.title = "Give the listing a title, e.g. 3-bedroom flat with BQ in Lekki Phase 1.";
      if (!form.propertyTypeId) errs.propertyTypeId = "Choose the property type.";
      if (!(toMinor(form.price) ?? 0)) errs.priceMinor = "Enter the price.";
    }
    if (s === 1) {
      if (!form.locationId) errs.locationId = "Choose the area.";
      if (form.lat == null || form.lng == null) errs.lat = "Drop the pin on the property.";
    }
    setLocal(errs);
    return Object.keys(errs).length === 0;
  }

  async function save(): Promise<StudioListingDto | null> {
    setBusy(true);
    setError(null);
    try {
      const body = toRequest(form);
      const res = saved
        ? await api<StudioListingDto>(`/api/property/listings/${saved.id}`, { method: "PUT", body })
        : await api<StudioListingDto>("/api/property/listings", { method: "POST", body });
      updateListing(res);
      return res;
    } catch (e) {
      const err = toApiError(e);
      setError(err);
      const firstField = err.errors ? Object.keys(err.errors)[0] : undefined;
      if (firstField) setStep((cur) => Math.min(cur, stepOf(firstField)) as StepIndex);
      return null;
    } finally {
      setBusy(false);
    }
  }

  async function next() {
    if (!validate(step)) return;
    if (step <= 1 && !saved) {
      setStep((step + 1) as StepIndex);
      return;
    }
    if (step <= 2) {
      const res = await save();
      if (!res) return;
      if (!listing) {
        // First save: continue on the listing's own page, at the Photos step.
        router.replace(`/studio/listings/${res.id}?step=photos`);
        return;
      }
    }
    setStep((Math.min(4, step + 1)) as StepIndex);
  }

  async function submit() {
    if (!saved) return;
    setBusy(true);
    setError(null);
    try {
      const res = await api<StudioListingDto>(`/api/property/listings/${saved.id}/submit`, { method: "POST" });
      updateListing(res);
      setSubmitted(res);
    } catch (e) {
      setError(toApiError(e));
    } finally {
      setBusy(false);
    }
  }

  const typeList = asList(types.data);
  const amenityList = asList(amenities.data);
  const agentList = asList(agents.data);
  const reachable = (i: number) => i <= 2 || !!saved;
  const costTotal = form.costs.reduce((n, c) => n + (toMinor(c.amount) ?? 0), 0);
  const priceMinor = toMinor(form.price) ?? 0;
  const media = saved?.media ?? [];
  const readyPhotos = media.filter((m) => m.status === "Ready").length;

  if (!canEdit) {
    return <Notice>Your role can see listings but not edit them. Ask the agency owner or an admin.</Notice>;
  }

  const costLines = form.costs.map((c, i) => ({ key: `c${i}`, label: c.label || COST_KINDS.find((k) => k.value === c.kind)?.label || c.kind, amountMinor: toMinor(c.amount) ?? 0 }));
  // A short-let's online price is what /stay-quote charges: nightly × nights plus cleaning, shown here for the minimum stay.
  // Other costs are never charged online; guests see them as paid to the host.
  const minNights = Math.max(1, Number.parseInt(form.minNights, 10) || 1);
  const cleaningMinor = toMinor(form.cleaningFee) ?? 0;
  const ledgerPreview =
    form.purpose === "ShortLet" ? (
      <Ledger
        currency="NGN"
        title="Your guests' stay"
        totalLabel={`Charged online for ${minNights} night${minNights === 1 ? "" : "s"}`}
        lines={[
          { key: "p", label: `${formatMoney(priceMinor)} × ${minNights} night${minNights === 1 ? "" : "s"}`, amountMinor: priceMinor * minNights },
          { key: "clean", label: "Cleaning", amountMinor: cleaningMinor },
        ]}
        totalMinor={priceMinor * minNights + cleaningMinor}
        footnote={
          costLines.length > 0
            ? `Preview of the listing page. Guests also see ${costLines.map((c) => `${c.label} ${formatMoney(c.amountMinor)}`).join(", ")} as paid to the host, not charged here: collect it yourself.`
            : "Preview of the listing page. Guests pay the nights and cleaning online."
        }
      />
    ) : (
      <Ledger
        currency="NGN"
        title={form.purpose === "Sale" ? "What it costs to buy" : "What it costs to move in"}
        totalLabel={form.purpose === "Sale" ? "Total to buy" : "Move-in total"}
        lines={[{ key: "p", label: priceLineLabel(form.purpose, form.pricePeriod), amountMinor: priceMinor }, ...costLines]}
        totalMinor={priceMinor + costTotal}
        footnote="Preview: seekers see this on the listing page."
      />
    );

  return (
    <div className="space-y-5">
      <ol className="no-scrollbar -mx-4 flex gap-1 overflow-x-auto px-4 sm:mx-0 sm:px-0" aria-label="Steps">
        {STEPS.map((label, i) => {
          const active = i === step;
          const done = i < step;
          return (
            <li key={label} className="shrink-0">
              <button
                type="button"
                disabled={!reachable(i) || busy}
                onClick={() => setStep(i as StepIndex)}
                aria-current={active ? "step" : undefined}
                className={cx(
                  "flex min-h-11 items-center gap-2 rounded-full border px-3.5 text-sm font-medium",
                  active ? "border-adire bg-adire text-white" : done ? "border-line bg-surface text-asphalt" : "border-line bg-surface text-muted",
                  !reachable(i) && "opacity-50",
                )}
              >
                <span className={cx("flex h-5 w-5 items-center justify-center rounded-full text-xs", active ? "bg-white text-adire" : "bg-plaster-2")}>
                  {done ? <CheckIcon className="h-3 w-3" strokeWidth={3} /> : i + 1}
                </span>
                {label}
              </button>
            </li>
          );
        })}
      </ol>

      {error && !error.errors && <ApiErrorView error={error} />}
      {error?.errors && <ErrorBox error={`Check the highlighted fields. ${error.fieldMessages[0] ?? ""}`} />}

      {step === 0 && (
        <Section title="Basics" hint="What it is, what it costs and what it has.">
          <Input label="Title" required value={form.title} onChange={(e) => set({ title: e.target.value })} maxLength={120} error={fe("title")} placeholder="3-bedroom flat with BQ, Lekki Phase 1" />
          <div className="grid gap-4 sm:grid-cols-2">
            <Select
              label="Listing for"
              value={form.purpose}
              onChange={(e) => {
                const purpose = e.target.value as ListingPurpose;
                set({ purpose, pricePeriod: PERIODS[purpose][0].value });
              }}
              error={fe("purpose")}
            >
              <option value="Rent">Rent</option>
              <option value="Sale">Sale</option>
              <option value="ShortLet">Short-let</option>
            </Select>
            <Select label="Property type" value={form.propertyTypeId} onChange={(e) => set({ propertyTypeId: e.target.value })} error={fe("propertyTypeId")}>
              <option value="">Choose a type</option>
              {typeList.map((t) => (
                <option key={t.id} value={t.id}>
                  {t.name}
                </option>
              ))}
            </Select>
            <Input
              label={form.purpose === "ShortLet" ? "Price per night (₦)" : form.purpose === "Sale" ? "Asking price (₦)" : "Rent (₦)"}
              inputMode="numeric"
              value={form.price}
              onChange={(e) => set({ price: groupDigits(e.target.value) })}
              error={fe("priceMinor")}
            />
            <Select label="Price is" value={form.pricePeriod} onChange={(e) => set({ pricePeriod: e.target.value as PricePeriod })} error={fe("pricePeriod")}>
              {PERIODS[form.purpose].map((p) => (
                <option key={p.value} value={p.value}>
                  {p.label}
                </option>
              ))}
            </Select>
          </div>
          <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
            <Input label="Bedrooms" inputMode="numeric" value={form.bedrooms} onChange={(e) => set({ bedrooms: e.target.value.replace(/\D/g, "") })} error={fe("bedrooms")} />
            <Input label="Bathrooms" inputMode="numeric" value={form.bathrooms} onChange={(e) => set({ bathrooms: e.target.value.replace(/\D/g, "") })} error={fe("bathrooms")} />
            <Input label="Toilets" inputMode="numeric" value={form.toilets} onChange={(e) => set({ toilets: e.target.value.replace(/\D/g, "") })} error={fe("toilets")} />
            <Input label="Size (m², optional)" inputMode="numeric" value={form.sizeSqm} onChange={(e) => set({ sizeSqm: e.target.value.replace(/\D/g, "") })} error={fe("sizeSqm")} />
          </div>
          <Checkbox label="Furnished" checked={form.furnished} onChange={(furnished) => set({ furnished })} />
          <Textarea label="Description" rows={6} value={form.description} onChange={(e) => set({ description: e.target.value })} maxLength={5000} error={fe("description")} hint="Rooms, finishing, power and water, the street, what's nearby." />
          {form.purpose === "ShortLet" && (
            <fieldset className="space-y-4 rounded-panel border border-line p-4">
              <legend className="px-1 text-sm font-semibold text-asphalt">Short-let details</legend>
              <div className="grid grid-cols-2 gap-4 sm:grid-cols-3">
                <Input label="Cleaning fee (₦)" inputMode="numeric" value={form.cleaningFee} onChange={(e) => set({ cleaningFee: groupDigits(e.target.value) })} error={fe("shortLet.cleaningFeeMinor")} />
                <Input label="Minimum nights" inputMode="numeric" value={form.minNights} onChange={(e) => set({ minNights: e.target.value.replace(/\D/g, "") })} error={fe("shortLet.minNights")} />
                <Input label="Maximum guests" inputMode="numeric" value={form.maxGuests} onChange={(e) => set({ maxGuests: e.target.value.replace(/\D/g, "") })} error={fe("shortLet.maxGuests")} />
                <Input label="Check-in from" type="time" value={form.checkInTime} onChange={(e) => set({ checkInTime: e.target.value })} error={fe("shortLet.checkInTime")} />
                <Input label="Check-out by" type="time" value={form.checkOutTime} onChange={(e) => set({ checkOutTime: e.target.value })} error={fe("shortLet.checkOutTime")} />
              </div>
              <Textarea label="House rules (optional)" rows={3} value={form.houseRules} onChange={(e) => set({ houseRules: e.target.value })} error={fe("shortLet.houseRules")} />
              {fe("shortLet") && <p className="text-xs font-medium text-brick">{fe("shortLet")}</p>}
            </fieldset>
          )}
          {amenityList.length > 0 && (
            <fieldset>
              <legend className="text-sm font-medium text-slate-700">Amenities</legend>
              <div className="mt-2 grid grid-cols-2 gap-x-4 gap-y-2.5 sm:grid-cols-3">
                {amenityList.map((a) => (
                  <Checkbox
                    key={a.id}
                    label={a.name}
                    checked={form.amenityIds.includes(a.id)}
                    onChange={(on) => set({ amenityIds: on ? [...form.amenityIds, a.id] : form.amenityIds.filter((x) => x !== a.id) })}
                  />
                ))}
              </div>
            </fieldset>
          )}
          <div className="grid gap-4 sm:grid-cols-2">
            <Input label="Virtual tour link (optional)" type="url" value={form.tourUrl} onChange={(e) => set({ tourUrl: e.target.value })} error={fe("tourUrl")} placeholder="https://" />
            <Input label="Video link (optional)" type="url" value={form.videoUrl} onChange={(e) => set({ videoUrl: e.target.value })} error={fe("videoUrl")} placeholder="https://" />
          </div>
          {agentList.length > 1 && (
            <Select label="Agent for this listing" value={form.agentProfileId} onChange={(e) => set({ agentProfileId: e.target.value })} error={fe("agentProfileId")} hint="Seekers contact this agent.">
              <option value="">Me</option>
              {agentList.map((a) => (
                <option key={a.id} value={a.id}>
                  {a.displayName}
                </option>
              ))}
            </Select>
          )}
        </Section>
      )}

      {step === 1 && (
        <Section title="Location" hint="Choose the area, then pin the exact spot.">
          <LocationStep
            value={{ locationId: form.locationId, address: form.address, lat: form.lat, lng: form.lng }}
            onChange={(p) => set(p)}
            errors={{ locationId: fe("locationId"), lat: fe("lat") ?? fe("lng"), address: fe("address") }}
          />
        </Section>
      )}

      {step === 2 && (
        <Section
          title="Costs"
          hint={
            form.purpose === "ShortLet"
              ? "List any cost guests pay you on top of the nights and cleaning, such as a caution deposit. Guests see them as paid to the host: they aren't charged online, so collect them yourself. Inspection fees aren't allowed."
              : "List every fee a tenant or buyer pays to move in. Seekers see them added up. Inspection fees aren't allowed."
          }
        >
          <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_20rem] lg:items-start">
            <div className="space-y-3">
              {form.costs.length === 0 && (
                <p className="text-sm text-muted">
                  {form.purpose === "ShortLet"
                    ? "No extra costs. Most short-lets need none beyond the nightly price and cleaning."
                    : "No extra costs yet. Most rentals have an agency fee, a legal fee and a caution deposit."}
                </p>
              )}
              {form.costs.map((c, i) => (
                <fieldset key={i} className="grid grid-cols-2 gap-3 rounded-panel border border-line bg-surface p-3 sm:grid-cols-[10rem_minmax(0,1fr)_9rem_auto] sm:items-start">
                  <legend className="sr-only">Cost {i + 1}</legend>
                  <Select
                    label="Kind"
                    value={c.kind}
                    onChange={(e) => {
                      const kind = e.target.value as CostKind;
                      const costs = [...form.costs];
                      const prevDefault = COST_KINDS.find((k) => k.value === c.kind)?.label;
                      costs[i] = { ...c, kind, label: !c.label || c.label === prevDefault ? (COST_KINDS.find((k) => k.value === kind)?.label ?? "") : c.label };
                      set({ costs });
                    }}
                    error={fe(`costs[${i}].kind`)}
                  >
                    {COST_KINDS.map((k) => (
                      <option key={k.value} value={k.value}>
                        {k.label}
                      </option>
                    ))}
                  </Select>
                  <Input
                    label="Label"
                    value={c.label}
                    maxLength={60}
                    onChange={(e) => {
                      const costs = [...form.costs];
                      costs[i] = { ...c, label: e.target.value };
                      set({ costs });
                    }}
                    error={fe(`costs[${i}].label`)}
                  />
                  <Input
                    label="Amount (₦)"
                    inputMode="numeric"
                    value={c.amount}
                    onChange={(e) => {
                      const costs = [...form.costs];
                      costs[i] = { ...c, amount: groupDigits(e.target.value) };
                      set({ costs });
                    }}
                    error={fe(`costs[${i}].amountMinor`)}
                  />
                  <button
                    type="button"
                    onClick={() => set({ costs: form.costs.filter((_, j) => j !== i) })}
                    className="mt-6 flex h-11 w-11 items-center justify-center self-start rounded-control text-brick hover:bg-brick-wash"
                    aria-label={`Remove cost ${i + 1}`}
                  >
                    <TrashIcon className="h-4 w-4" />
                  </button>
                </fieldset>
              ))}
              {fe("costs") && <p className="text-xs font-medium text-brick">{fe("costs")}</p>}
              {form.costs.length < 10 && (
                <Button variant="secondary" onClick={() => set({ costs: [...form.costs, { kind: "AgencyFee", label: "Agency fee", amount: "" }] })}>
                  <PlusIcon className="h-4 w-4" /> Add a cost
                </Button>
              )}
            </div>
            {ledgerPreview}
          </div>
        </Section>
      )}

      {step === 3 && saved && (
        <Section title="Photos" hint="Clear, well-lit photos of every room. The first photo is the cover. Photos get your agency's watermark.">
          <PhotosStep listing={saved} onListing={updateListing} />
        </Section>
      )}

      {step === 4 && saved && (
        <Section title="Review and submit" hint="Check everything, then submit. Verified agencies go live straight away; others wait for a moderator.">
          {submitted ? (
            <Notice tone="success">
              {submitted.status === "Active" ? "Your listing is live." : "Submitted. A moderator will review it shortly; you'll get an email when it's live."}{" "}
              {submitted.status === "Active" && submitted.publicUrl && (
                <a href={submitted.publicUrl} className="font-semibold underline" target="_blank" rel="noopener noreferrer">
                  See it on the site
                </a>
              )}
            </Notice>
          ) : null}
          <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_20rem] lg:items-start">
            <dl className="divide-y divide-line-soft rounded-panel border border-line bg-surface">
              <Row label="Title" value={form.title} onEdit={() => setStep(0)} />
              <Row label="For" value={`${form.purpose === "ShortLet" ? "Short-let" : form.purpose}, ${typeList.find((t) => t.id === form.propertyTypeId)?.name ?? "—"}`} onEdit={() => setStep(0)} />
              <Row label="Rooms" value={`${int(form.bedrooms)} bed, ${int(form.bathrooms)} bath, ${int(form.toilets)} toilet${form.sizeSqm ? `, ${form.sizeSqm} m²` : ""}`} onEdit={() => setStep(0)} />
              <Row label="Location" value={form.address || (form.lat != null ? `Pinned at ${form.lat.toFixed(4)}, ${form.lng?.toFixed(4)}` : "—")} onEdit={() => setStep(1)} />
              <Row label="Costs" value={form.costs.length ? `${form.costs.length} listed, ${formatMoney(costTotal)}` : "None"} onEdit={() => setStep(2)} />
              <Row
                label="Photos"
                value={media.length ? `${readyPhotos} of ${media.length} ready${media.some((m) => m.duplicateOfAgency) ? ". One or more match another agency's photos" : ""}` : "No photos yet"}
                onEdit={() => setStep(3)}
              />
            </dl>
            {ledgerPreview}
          </div>
          {media.length === 0 && <Notice tone="warning">Add at least one photo. Listings without photos get very few enquiries.</Notice>}
          {media.some((m) => m.status !== "Ready") && <Notice tone="warning">Wait until every photo is Ready (or delete failed ones) before submitting.</Notice>}
          {error?.isPlanLimit && <ApiErrorView error={error} />}
          {!submitted && (saved.status === "Draft" || saved.status === "Rejected") && (
            <Button className="w-full sm:w-auto" loading={busy} onClick={submit} disabled={media.length === 0 || media.some((m) => m.status === "Processing")}>
              Submit for review
            </Button>
          )}
          {!submitted && saved.status !== "Draft" && saved.status !== "Rejected" && (
            <p className="text-sm text-muted">This listing has already been submitted. Changes you save are kept on it.</p>
          )}
        </Section>
      )}

      <div className="flex flex-wrap items-center justify-between gap-3 border-t border-line pt-4">
        <Button variant="ghost" disabled={step === 0 || busy} onClick={() => setStep((step - 1) as StepIndex)}>
          Back
        </Button>
        <div className="flex flex-wrap gap-2">
          {saved && step <= 2 && (
            <Button variant="secondary" loading={busy} onClick={() => validate(step) && save()}>
              Save
            </Button>
          )}
          {step < 4 && (
            <Button loading={busy} onClick={next}>
              {step === 2 && !saved ? "Save and add photos" : step === 3 ? "Review" : "Continue"}
            </Button>
          )}
          {step === 4 && (
            <Link href="/studio/listings" className="inline-flex min-h-11 items-center px-3 text-sm font-semibold text-adire hover:underline">
              Back to listings
            </Link>
          )}
        </div>
      </div>
    </div>
  );
}

function Section({ title, hint, children }: { title: string; hint?: string; children: ReactNode }) {
  return (
    <section aria-label={title} className="space-y-4">
      <div>
        <h2 className="text-lg font-semibold text-asphalt">{title}</h2>
        {hint && <p className="text-sm text-muted">{hint}</p>}
      </div>
      {children}
    </section>
  );
}

function Row({ label, value, onEdit }: { label: string; value: string; onEdit: () => void }) {
  return (
    <div className="flex items-start justify-between gap-3 px-4 py-2.5">
      <div className="min-w-0">
        <dt className="text-xs text-muted">{label}</dt>
        <dd className="text-sm font-medium text-asphalt">{value}</dd>
      </div>
      <button type="button" onClick={onEdit} className="min-h-11 shrink-0 text-sm font-semibold text-adire hover:underline">
        Edit
      </button>
    </div>
  );
}

