"use client";

import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { PURPOSES } from "@/lib/config";
import { searchHref } from "@/lib/search";
import type { ListingPurpose } from "@/lib/types";
import { LocationField, resolveLocation, useLocationOptions } from "@/components/location-field";
import { SearchIcon } from "@/components/icons";
import { Button, Input, Select, cx } from "@/components/ui";

/**
 * The one big search: what you want (native radios, so arrow keys move between them), a location with suggestions,
 * beds and a top price, one button.
 */
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
    <form onSubmit={submit} role="search" aria-label="Find a home" className="rounded-ls-panel border border-ls-line bg-ls-surface p-4 sm:p-5">
      <fieldset className="border-b border-ls-line pb-3">
        <legend className="sr-only">What are you looking for?</legend>
        <div className="flex flex-wrap gap-1">
          {PURPOSES.map((p) => (
            <label
              key={p.value}
              className={cx(
                "relative inline-flex min-h-11 cursor-pointer items-center rounded-full px-4 text-[0.9375rem] font-semibold",
                "has-[:focus-visible]:outline-3 has-[:focus-visible]:outline-offset-3 has-[:focus-visible]:outline-ls-ink has-[:focus-visible]:outline-solid",
                purpose === p.value ? "bg-ls-ink text-white" : "text-ls-ink-2 hover:bg-ls-ink/[0.06]",
              )}
            >
              <input type="radio" name="purpose" value={p.value} checked={purpose === p.value} onChange={() => setPurpose(p.value)} className="sr-only" />
              {p.label}
            </label>
          ))}
        </div>
      </fieldset>
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
          label={purpose === "ShortLet" ? "Most per night (₦)" : purpose === "Sale" ? "Most you'll pay (₦)" : "Most rent a year (₦)"}
          inputMode="numeric"
          placeholder="No limit"
          value={maxPrice}
          onChange={(e) => setMaxPrice(e.target.value.replace(/\D/g, ""))}
          className="min-h-12"
        />
        <Button type="submit" className="min-h-12 px-6 text-base">
          <SearchIcon className="h-5 w-5" /> Search homes
        </Button>
      </div>
    </form>
  );
}
