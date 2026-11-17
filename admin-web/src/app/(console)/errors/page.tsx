"use client";

import { useState } from "react";
import { RequireArea } from "@/components/console";
import { FilterBar } from "@/components/filters";
import { RefreshIcon } from "@/components/icons";
import { useToast } from "@/components/toast";
import { Button, EmptyState, ErrorPanel, Loading, Mono, PageHeader, Panel, StatusBadge, TableScroll, Tabs } from "@/components/ui";
import { api } from "@/lib/api";
import { ago, num, stamp } from "@/lib/format";
import type { ErrorEventDto } from "@/lib/types";
import { qs, useApi } from "@/lib/use-api";
import { useUrlFilters } from "@/lib/use-filters";
import { useNow } from "@/lib/use-now";

type Status = "open" | "resolved" | "all";
const KEYS = ["status"] as const;
const TABS: { value: Status; label: string }[] = [
  { value: "open", label: "Open" },
  { value: "resolved", label: "Fixed" },
  { value: "all", label: "All" },
];

const APP_NAMES: Record<string, string> = {
  hub: "Hub", orders: "OrderLink", invoices: "InvoiceLink", bookings: "BookLink", courses: "LearnLink",
  property: "PropertyLink", school: "SchoolLink", care: "CareLink", admin: "Admin console",
};

/** "API" or "SchoolLink (browser)". */
function where(source: string) {
  if (source === "api") return "API";
  const app = source.startsWith("web:") ? source.slice(4) : source;
  return `${APP_NAMES[app] ?? app} (browser)`;
}

export default function ErrorsPage() {
  return (
    <RequireArea area="errors">
      <Errors />
    </RequireArea>
  );
}

function Errors() {
  const toast = useToast();
  const f = useUrlFilters(KEYS);
  const status = (TABS.some((t) => t.value === f.values.status) ? f.values.status : "open") as Status;
  const { data, error, loading, reload } = useApi<ErrorEventDto[]>(`/api/platform/errors${qs({ status: status === "open" ? "" : status })}`);
  const [open, setOpen] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const now = useNow(30_000);
  const list = data ?? [];

  async function resolve(e: ErrorEventDto) {
    setBusy(e.id);
    try {
      await api<ErrorEventDto>(`/api/platform/errors/${e.id}/resolve`, { method: "POST" });
      toast("Marked fixed. If it happens again, it comes back to Open.");
      reload();
    } catch (err) {
      toast(err instanceof Error ? err.message : "Couldn't mark it fixed.");
    } finally {
      setBusy(null);
    }
  }

  return (
    <>
      <PageHeader
        title="Errors"
        subtitle="Crashes in the API and in the web apps, one row per kind of error with how often it happened. A new API error emails super and support admins once. Mark an error fixed after the fix is deployed; it reopens if it happens again."
        action={
          <Button variant="secondary" onClick={reload} loading={loading && !!data}>
            <RefreshIcon className="h-4 w-4" /> Refresh
          </Button>
        }
      />
      <FilterBar>
        <Tabs<Status> label="Error status" value={status} onChange={(v) => f.set({ status: v === "open" ? "" : v })} items={TABS} />
      </FilterBar>
      <Panel flush>
        {error && <ErrorPanel error={error} onRetry={reload} className="m-3" />}
        {!data && !error && <Loading />}
        {data && list.length === 0 && (
          <EmptyState
            title={status === "open" ? "No open errors" : status === "resolved" ? "Nothing marked fixed yet" : "No errors recorded"}
            text={status === "open" ? "When the API or a web app crashes, it shows up here." : undefined}
          />
        )}
        {data && list.length > 0 && (
          <TableScroll label="Errors">
            <table className="data-table min-w-[980px]">
              <thead>
                <tr>
                  <th>Last seen</th>
                  <th>Where</th>
                  <th>Error</th>
                  <th className="num">Times</th>
                  <th>First seen</th>
                  <th>
                    <span className="sr-only">Actions</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {list.map((e) => (
                  <tr key={e.id} className="align-top">
                    <td className="tnum">
                      {stamp(e.lastSeenAt)}
                      {now > 0 && <span className="block text-xs text-muted">{ago(e.lastSeenAt, now)}</span>}
                    </td>
                    <td>
                      <span className="block font-medium">{where(e.source)}</span>
                      {e.path && <Mono className="text-xs text-muted">{e.path}</Mono>}
                    </td>
                    <td className="max-w-[28rem]">
                      <span className="block break-words">{e.message}</span>
                      {e.kind !== "client" && <span className="block text-xs text-muted">{e.kind}</span>}
                      {e.stack && (
                        <button
                          type="button"
                          className="mt-1 text-xs font-medium underline underline-offset-2"
                          aria-expanded={open === e.id}
                          onClick={() => setOpen(open === e.id ? null : e.id)}
                        >
                          {open === e.id ? "Hide details" : "Show details"}
                        </button>
                      )}
                      {open === e.id && e.stack && (
                        <pre className="mt-2 max-h-80 overflow-auto whitespace-pre-wrap break-words rounded border border-line bg-concrete p-2 text-xs">{e.stack}</pre>
                      )}
                    </td>
                    <td className="num font-medium">{num(e.count)}</td>
                    <td className="tnum">{stamp(e.firstSeenAt)}</td>
                    <td className="whitespace-nowrap">
                      {e.resolvedAt ? (
                        <StatusBadge tone="ok">Fixed {stamp(e.resolvedAt)}</StatusBadge>
                      ) : (
                        <Button variant="secondary" onClick={() => resolve(e)} loading={busy === e.id}>
                          Mark fixed
                        </Button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </TableScroll>
        )}
      </Panel>
    </>
  );
}
