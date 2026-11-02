"use client";

import { useState } from "react";
import { asList } from "@/lib/api";
import { useApi } from "@/lib/use-api";
import { useDebounced } from "@/lib/use-debounced";
import type { ClientDto } from "@/lib/types";
import { ClientModal } from "./client-form";
import { PlusIcon, SearchIcon } from "./icons";
import { Button, Spinner, cx, inputClass } from "./ui";

/** The bits of a client the editor needs (works for ClientDto and ClientSnapshot). */
export interface PickedClient {
  id: string;
  name: string;
  company?: string | null;
  email?: string | null;
  phone?: string | null;
  address?: string | null;
  taxId?: string | null;
}

export function ClientPicker({
  value,
  onChange,
  error,
}: {
  value: PickedClient | null;
  onChange: (c: PickedClient | null) => void;
  error?: string;
}) {
  const [q, setQ] = useState("");
  const [open, setOpen] = useState(false);
  const [adding, setAdding] = useState(false);
  const dq = useDebounced(q.trim(), 250);
  const { data, loading } = useApi<ClientDto[]>(open && !value ? `/api/clients${dq ? `?q=${encodeURIComponent(dq)}` : ""}` : null);
  const results = asList(data).slice(0, 8);

  return (
    <div>
      <p className="mb-1.5 block text-sm font-medium text-slate-700" id="client-picker-label">
        Client
      </p>
      {value ? (
        <div className="flex items-start justify-between gap-3 rounded-control border border-rule bg-slate-50 p-3">
          <div className="min-w-0 text-sm">
            <p className="font-semibold text-graphite">{value.name}</p>
            {value.company && <p className="text-slate-600">{value.company}</p>}
            {value.email ? <p className="truncate text-slate-600">{value.email}</p> : <p className="text-xs text-amber-text">No email address. Add one to send by email.</p>}
          </div>
          <Button variant="ghost" className="min-h-8 px-2 py-1" onClick={() => onChange(null)}>
            Change
          </Button>
        </div>
      ) : (
        <div className="relative">
          <SearchIcon className="pointer-events-none absolute top-3.5 left-3 h-4 w-4 text-faint" />
          <input
            aria-labelledby="client-picker-label"
            role="combobox"
            aria-expanded={open}
            aria-controls="client-picker-list"
            aria-invalid={!!error || undefined}
            className={cx(inputClass, "pl-9")}
            placeholder="Search your clients"
            value={q}
            onFocus={() => setOpen(true)}
            onBlur={() => setOpen(false)}
            onChange={(e) => {
              setQ(e.target.value);
              setOpen(true);
            }}
          />
          {open && (
            <ul
              id="client-picker-list"
              role="listbox"
              className="absolute inset-x-0 top-full z-20 mt-1 max-h-72 overflow-y-auto rounded-control border border-rule bg-paper py-1 shadow-float"
            >
              {loading && !data && (
                <li className="flex items-center gap-2 px-3 py-2 text-sm text-muted">
                  <Spinner className="h-4 w-4" /> Searching…
                </li>
              )}
              {results.map((c) => (
                <li key={c.id} role="option" aria-selected={false}>
                  <button
                    type="button"
                    className="block w-full px-3 py-2 text-left text-sm hover:bg-ink-wash"
                    onMouseDown={(e) => e.preventDefault()}
                    onClick={() => {
                      onChange(c);
                      setQ("");
                      setOpen(false);
                    }}
                  >
                    <span className="block font-medium text-graphite">{c.name}</span>
                    <span className="block truncate text-xs text-muted">{[c.company, c.email].filter(Boolean).join(", ")}</span>
                  </button>
                </li>
              ))}
              {data && results.length === 0 && <li className="px-3 py-2 text-sm text-muted">No clients match. Add them below.</li>}
              <li>
                <button
                  type="button"
                  className="flex w-full items-center gap-2 border-t border-rule-soft px-3 py-2 text-left text-sm font-semibold text-ink hover:bg-ink-wash"
                  onMouseDown={(e) => e.preventDefault()}
                  onClick={() => {
                    setOpen(false);
                    setAdding(true);
                  }}
                >
                  <PlusIcon className="h-4 w-4" /> Add new client{q.trim() ? ` “${q.trim()}”` : ""}
                </button>
              </li>
            </ul>
          )}
          {!open && (
            <button type="button" className="mt-1.5 text-sm font-semibold text-ink underline-offset-2 hover:underline" onClick={() => setAdding(true)}>
              Add a new client
            </button>
          )}
        </div>
      )}
      {error && <p className="mt-1 text-xs font-medium text-overdue">{error}</p>}
      <ClientModal open={adding} initialName={q.trim()} onClose={() => setAdding(false)} onSaved={(c) => onChange(c)} />
    </div>
  );
}
