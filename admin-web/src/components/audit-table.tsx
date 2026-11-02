"use client";

import Link from "next/link";
import { Fragment, useState } from "react";
import { shortId, stamp } from "@/lib/format";
import type { AuditRowDto } from "@/lib/types";
import { ChevronDownIcon, ChevronRightIcon } from "./icons";
import { Mono, ProductChip, TableScroll, cx } from "./ui";

/** Audit rows; expanding one shows before/after side by side with the changed keys highlighted. */
export function AuditTable({ rows, showBusiness = true }: { rows: AuditRowDto[]; showBusiness?: boolean }) {
  const [open, setOpen] = useState<Set<string>>(new Set());
  const toggle = (id: string) =>
    setOpen((s) => {
      const n = new Set(s);
      if (n.has(id)) n.delete(id);
      else n.add(id);
      return n;
    });
  const cols = showBusiness ? 6 : 5;

  return (
    <TableScroll label="Audit log">
      <table className={cx("data-table", showBusiness ? "min-w-[880px]" : "min-w-[760px]")}>
        <thead>
          <tr>
            <th className="w-10">
              <span className="sr-only">Details</span>
            </th>
            <th>Time</th>
            <th>Action</th>
            <th>Actor</th>
            {showBusiness && <th>Business</th>}
            <th>Entity</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((r) => {
            const expanded = open.has(r.id);
            const hasChanges = !!(r.beforeJson || r.afterJson);
            return (
              <Fragment key={r.id}>
                <tr>
                  <td className="!py-1">
                    <button
                      type="button"
                      onClick={() => toggle(r.id)}
                      aria-expanded={expanded}
                      aria-label={`${expanded ? "Hide" : "Show"} details of ${r.action}`}
                      className="inline-flex h-9 w-9 items-center justify-center rounded-control text-muted hover:bg-concrete"
                    >
                      {expanded ? <ChevronDownIcon className="h-4 w-4" /> : <ChevronRightIcon className="h-4 w-4" />}
                    </button>
                  </td>
                  <td className="tnum">{stamp(r.createdAt)}</td>
                  <td>
                    <Mono>{r.action}</Mono>
                  </td>
                  <td>{r.actorName ?? <span className="text-muted">System</span>}</td>
                  {showBusiness && (
                    <td>
                      <span className="flex flex-wrap items-center gap-x-2 gap-y-1">
                        {r.tenantId ? (
                          <Link href={`/businesses/${r.tenantId}`} className="underline decoration-line underline-offset-2 hover:decoration-graphite">
                            {r.businessName ?? <Mono>{shortId(r.tenantId)}</Mono>}
                          </Link>
                        ) : (
                          <span className="text-muted">{r.businessName ?? "—"}</span>
                        )}
                        {r.product && <ProductChip product={r.product} />}
                      </span>
                    </td>
                  )}
                  <td>
                    {r.entityType ?? "—"} {r.entityId && <Mono className="text-muted">{shortId(r.entityId)}</Mono>}
                  </td>
                </tr>
                {expanded && (
                  <tr>
                    <td colSpan={cols} className="bg-concrete-2">
                      <div className="space-y-3 py-1">
                        <dl className="flex flex-wrap gap-x-6 gap-y-1 text-xs text-muted">
                          <div>
                            <dt className="inline">Audit id </dt>
                            <dd className="inline">
                              <Mono>{r.id}</Mono>
                            </dd>
                          </div>
                          {r.actorId && (
                            <div>
                              <dt className="inline">Actor id </dt>
                              <dd className="inline">
                                <Mono>{r.actorId}</Mono>
                              </dd>
                            </div>
                          )}
                          {r.entityId && (
                            <div>
                              <dt className="inline">Entity id </dt>
                              <dd className="inline">
                                <Mono>{r.entityId}</Mono>
                              </dd>
                            </div>
                          )}
                          {r.tenantId && (
                            <div>
                              <dt className="inline">Business id </dt>
                              <dd className="inline">
                                <Mono>{r.tenantId}</Mono>
                              </dd>
                            </div>
                          )}
                          {r.ip && (
                            <div>
                              <dt className="inline">IP </dt>
                              <dd className="inline">
                                <Mono>{r.ip}</Mono>
                              </dd>
                            </div>
                          )}
                        </dl>
                        {hasChanges ? <JsonDiff before={r.beforeJson} after={r.afterJson} /> : <p className="text-sm text-muted">No before/after values were recorded.</p>}
                      </div>
                    </td>
                  </tr>
                )}
              </Fragment>
            );
          })}
        </tbody>
      </table>
    </TableScroll>
  );
}

function parse(json: string | null): { value: unknown; ok: boolean } {
  if (json === null || json === undefined) return { value: null, ok: true };
  try {
    return { value: JSON.parse(json), ok: true };
  } catch {
    return { value: json, ok: false };
  }
}

function isRecord(v: unknown): v is Record<string, unknown> {
  return typeof v === "object" && v !== null && !Array.isArray(v);
}

/** Before and after side by side; top-level keys whose value differs are highlighted on both sides. */
export function JsonDiff({ before, after }: { before: string | null; after: string | null }) {
  const b = parse(before);
  const a = parse(after);
  const bObj = isRecord(b.value) ? b.value : null;
  const aObj = isRecord(a.value) ? a.value : null;
  const changed = new Set<string>();
  if (bObj || aObj) {
    const keys = new Set([...Object.keys(bObj ?? {}), ...Object.keys(aObj ?? {})]);
    for (const k of keys) {
      if (JSON.stringify(bObj?.[k]) !== JSON.stringify(aObj?.[k])) changed.add(k);
    }
  }
  return (
    <div className="grid gap-3 md:grid-cols-2">
      <JsonSide title="Before" value={b.value} raw={!b.ok} changed={changed} empty={before === null} />
      <JsonSide title="After" value={a.value} raw={!a.ok} changed={changed} empty={after === null} />
      {changed.size > 0 && (
        <p className="text-xs text-muted md:col-span-2">
          Changed: <Mono>{[...changed].join(", ")}</Mono>
        </p>
      )}
    </div>
  );
}

function JsonSide({ title, value, raw, changed, empty }: { title: string; value: unknown; raw: boolean; changed: Set<string>; empty: boolean }) {
  return (
    <figure className="min-w-0 rounded-panel border border-line bg-surface">
      <figcaption className="border-b border-line px-3 py-1.5 text-xs font-medium text-muted">{title}</figcaption>
      <pre className="max-h-96 overflow-auto py-2 font-mono text-xs leading-relaxed">
        {empty ? (
          <span className="json-line text-muted">(none)</span>
        ) : raw ? (
          <span className="json-line">{String(value)}</span>
        ) : isRecord(value) ? (
          <>
            <span className="json-line">{"{"}</span>
            {Object.entries(value).map(([k, v], i, arr) => (
              <span key={k} className={cx("json-line", changed.has(k) && "changed")}>
                {"  "}
                {JSON.stringify(k)}: {indent(JSON.stringify(v, null, 2) ?? "null")}
                {i < arr.length - 1 ? "," : ""}
                {changed.has(k) && <span className="sr-only"> (changed)</span>}
              </span>
            ))}
            <span className="json-line">{"}"}</span>
          </>
        ) : (
          <span className="json-line">{JSON.stringify(value, null, 2)}</span>
        )}
      </pre>
    </figure>
  );
}

function indent(s: string) {
  return s.replace(/\n/g, "\n  ");
}
