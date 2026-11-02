"use client";

import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { PURPOSES } from "@/lib/config";
import { searchHref } from "@/lib/search";
import type { ListingPurpose } from "@/lib/types";
import { LocationField, resolveLocation, useLocationOptions } from "@/components/location-field";
import { SearchIcon } from "@/components/icons";
import { Button, Input, Select, cx } from "@/components/ui";

/** The one big search: purpose tabs, a location with suggestions, beds and a max price, one Adire button. */
export function HomeSearch() {
  const router = useRouter();
  const options = useLocationOptions();
  const [purpose, setPurpose] = useState<ListingPurpose>("Rent");
  const [location, setLocation] = useState("");
  const [beds, setBeds] = useState("");
  const [maxPrice, setMaxPrice] = useState("");

  function submit(e: FormEvent) {
    e.preventDefault();
    router.push(searchHref({ purpose, location: resolveLocation(location, options), beds: beds || undefined, maxPrice: maxPrice || undefined }));
  }

  return (
    <form onSubmit={submit} role="search" aria-label="Find a home" className="rounded-panel border border-line bg-surface p-4 sm:p-5">
      <div role="radiogroup" aria-label="What are you looking for?" className="flex gap-1 border-b border-line-soft pb-3">
        {PURPOSES.map((p) => (
          <button
            key={p.value}
            type="button"
            role="radio"
            aria-checked={purpose === p.value}
            onClick={() => setPurpose(p.value)}
            className={cx(
              "min-h-11 rounded-full px-4 text-sm font-semibold",
              purpose === p.value ? "bg-asphalt text-white" : "text-slate-700 hover:bg-plaster-2",
            )}
          >
            {p.label}
          </button>
        ))}
      </div>
      <div className="mt-4 grid gap-3 md:grid-cols-[minmax(0,2fr)_minmax(0,1fr)_minmax(0,1fr)_auto] md:items-end">
        <LocationField value={location} onChange={setLocation} options={options} label="Where?" inputClassName="min-h-12 text-base sm:text-base" />
        <Select label="Bedrooms" value={beds} onChange={(e) => setBeds(e.target.value)} className="min-h-12">
          <option value="">Any</option>
          {[1, 2, 3, 4, 5].map((n) => (
            <option key={n} value={n}>
              {n}+
            </option>
          ))}
        </Select>
        <Input
          label={purpose === "ShortLet" ? "Max per night (₦)" : purpose === "Sale" ? "Max price (₦)" : "Max rent a year (₦)"}
          inputMode="numeric"
          placeholder="No limit"
          value={maxPrice}
          onChange={(e) => setMaxPrice(e.target.value.replace(/\D/g, ""))}
          className="min-h-12"
        />
        <Button type="submit" className="min-h-12 px-6 text-base">
          <SearchIcon className="h-5 w-5" /> Search
        </Button>
      </div>
    </form>
  );
}
