"use client";

import { formatDay } from "@/lib/dates";
import { RATES_ATTRIBUTION_URL, formatRate, parseRate, toBaseMinor } from "@/lib/fx";
import { formatMoney } from "@/lib/money";
import { cx, inputClass } from "./styles";

/** Required credit wherever exchange rates are shown. */
export function RatesAttribution({ asOf, className }: { asOf?: string | null; className?: string }) {
  return (
    <p className={cx("text-xs text-slate-500", className)}>
      {asOf && <>Rates as of {formatDay(asOf)}. </>}
      <a href={RATES_ATTRIBUTION_URL} target="_blank" rel="noopener noreferrer" className="underline hover:text-slate-700">
        Rates by Exchange Rate API
      </a>
    </p>
  );
}

/**
 * Editable "1 USD = ₦1,580.25" rate input, shown when a document's currency differs from the base currency.
 * `value` is the raw input string; the parent decides what to send as `exchangeRate`.
 */
export function ExchangeRateField({
  id,
  currency,
  base,
  value,
  onChange,
  amountMinor,
  error,
  asOf,
  loading,
}: {
  id: string;
  currency: string;
  base: string;
  value: string;
  onChange: (v: string) => void;
  /** Amount in `currency` to show converted to base (optional). */
  amountMinor?: number;
  error?: string | null;
  asOf?: string | null;
  loading?: boolean;
}) {
  const rate = parseRate(value);
  return (
    <div className="space-y-1.5 rounded-control border border-rule-soft bg-slate-50 p-3">
      <label htmlFor={id} className="block text-sm font-medium text-slate-700">
        Exchange rate
      </label>
      <div className="flex flex-wrap items-center gap-2 text-sm text-slate-700">
        <span className="whitespace-nowrap">1 {currency} =</span>
        <input
          id={id}
          inputMode="decimal"
          className={cx(inputClass, "w-36")}
          value={value}
          placeholder={loading ? "Loading…" : "Enter rate"}
          onChange={(e) => onChange(e.target.value)}
          aria-invalid={!!error || undefined}
          aria-describedby={`${id}-help`}
        />
        <span className="whitespace-nowrap">{base}</span>
      </div>
      <div id={`${id}-help`} className="space-y-0.5">
        {error && <p className="text-xs font-medium text-overdue">{error}</p>}
        {rate !== null && (
          <p className="text-xs text-slate-600">
            Exchange rate: {formatRate(currency, rate, base)}
            {amountMinor !== undefined && (
              <>
                , so about <span className="amount font-semibold">{formatMoney(toBaseMinor(amountMinor, rate), base)}</span>
              </>
            )}
          </p>
        )}
        <RatesAttribution asOf={asOf} />
      </div>
    </div>
  );
}
