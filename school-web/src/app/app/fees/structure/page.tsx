"use client";

import Link from "next/link";
import { useState, type FormEvent } from "react";
import { api } from "@/lib/api";
import { useAction } from "@/lib/mutate";
import { useCan } from "@/lib/roles";
import { useApi } from "@/lib/use-api";
import type { LevelDto } from "@/lib/types";
import type { FeeItemDto, FeeLineInput, FeeStructureCopyDto, FeeStructureDto } from "@/lib/types/fees";
import { ImportDialog } from "@/components/import-dialog";
import { ApiErrorView, Badge, Button, EmptyState, ErrorBox, Input, Modal, Notice, PageLoader, RuledSheet, Select, linkClass, ruledRowClass } from "@/components/ui";
import { TermSelect, money, nairaInput, parseNaira, useTermChoice, type TermOption } from "../fees-shared";

/** What each level pays per term. Invoices are generated from this. */
export default function FeeStructurePage() {
  const canEdit = useCan("fees.manage");
  const t = useTermChoice();
  const levels = useApi<LevelDto[]>("/api/school/levels");
  const items = useApi<FeeItemDto[]>("/api/school/fee-items");
  const structure = useApi<FeeStructureDto[]>(t.termId ? `/api/school/fee-structure/${t.termId}` : null);
  const [editing, setEditing] = useState<LevelDto | null>(null);
  const [copying, setCopying] = useState(false);
  const [copied, setCopied] = useState<string | null>(null);

  const error = t.error ?? levels.error ?? items.error;
  if (error) return <ApiErrorView error={error} onRetry={() => (t.reload(), levels.reload(), items.reload())} />;
  if (!t.loaded || !levels.data || !items.data) return <PageLoader />;
  if (t.terms.length === 0) return <EmptyState title="No terms yet" text="Add the session and its terms on the setup page first." />;
  if (items.data.length === 0)
    return (
      <EmptyState
        title="Add fee items first"
        text="The fee structure sets an amount for each fee item, per level."
        action={
          <Link href="/app/fees/items" className={linkClass}>
            Add fee items
          </Link>
        }
      />
    );

  const byLevel = new Map((structure.data ?? []).map((s) => [s.levelId, s]));
  const sorted = [...levels.data].sort((a, b) => a.order - b.order);
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <TermSelect terms={t.terms} value={t.termId} onChange={(id) => (t.setTermId(id), setCopied(null))} />
        {canEdit && t.terms.length > 1 && (
          <Button variant="secondary" onClick={() => setCopying(true)}>
            Copy from another term
          </Button>
        )}
      </div>
      <p className="max-w-prose text-sm text-slate-700">
        Set what each level pays this term. Changing it doesn&apos;t touch invoices already sent: generate invoices again to refresh the ones with no
        payments.
      </p>
      {copied && <Notice tone="success">{copied}</Notice>}
      {structure.error && <ApiErrorView error={structure.error} onRetry={structure.reload} />}
      {!structure.data && !structure.error && <PageLoader />}
      {structure.data && (
        <RuledSheet
          label="Fee structure"
          template={canEdit ? "12rem minmax(0,1fr) 9rem 15rem" : "12rem minmax(0,1fr) 9rem 6rem"}
          head={[{ label: "Level" }, { label: "Fee items" }, { label: "Required total", align: "right" }, { label: "" }]}
          busy={structure.loading}
        >
          {sorted.map((level) => {
            const lines = byLevel.get(level.id)?.lines ?? [];
            const required = lines.filter((l) => !l.isOptional).reduce((sum, l) => sum + l.amountMinor, 0);
            return (
              <li key={level.id} className={ruledRowClass}>
                <span className="font-bold text-ink">{level.name}</span>
                <span className="col-start-1 text-sm text-slate-700 md:col-start-auto">
                  {lines.length === 0 ? (
                    <span className="text-muted">Not set</span>
                  ) : (
                    lines.map((l) => (
                      <span key={l.feeItemId} className="mr-3 inline-block">
                        {l.feeItemName} <span className="tnum">{money(l.amountMinor)}</span>
                        {l.isOptional && <span className="text-muted"> (optional)</span>}
                      </span>
                    ))
                  )}
                </span>
                <span className="tnum col-start-1 text-sm font-semibold md:col-start-auto md:text-right">{lines.length ? money(required) : "—"}</span>
                {canEdit ? (
                  <span className="col-start-2 row-span-3 row-start-1 flex flex-wrap items-start justify-end gap-1 md:col-start-auto md:row-span-1 md:row-start-auto">
                    <Button variant="ghost" className="px-2.5" onClick={() => setEditing(level)}>
                      Edit
                    </Button>
                    <ImportDialog
                      kind="fee-structure"
                      scope={{ termId: t.termId, levelId: level.id }}
                      title={`Import fees for ${level.name}`}
                      onDone={structure.reload}
                    />
                  </span>
                ) : (
                  <span />
                )}
              </li>
            );
          })}
        </RuledSheet>
      )}
      {editing && t.term && (
        <LevelFees
          termId={t.termId}
          termLabel={t.term.label}
          level={editing}
          items={items.data}
          current={byLevel.get(editing.id)}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            structure.reload();
          }}
        />
      )}
      {copying && (
        <CopyTerm
          terms={t.terms.filter((x) => x.id !== t.termId)}
          termId={t.termId}
          onClose={() => setCopying(false)}
          onCopied={(r) => {
            setCopying(false);
            setCopied(
              r.levels === 0
                ? "Nothing copied. Every level that has fees in that term already has fees in this one."
                : `Copied the fees for ${r.levels} ${r.levels === 1 ? "level" : "levels"}. Levels that already had fees were left alone.`,
            );
            structure.reload();
          }}
        />
      )}
    </div>
  );
}

function LevelFees({
  termId,
  termLabel,
  level,
  items,
  current,
  onClose,
  onSaved,
}: {
  termId: string;
  termLabel: string;
  level: LevelDto;
  items: FeeItemDto[];
  current: FeeStructureDto | undefined;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [amounts, setAmounts] = useState<Record<string, string>>(() =>
    Object.fromEntries(items.map((i) => {
      const line = current?.lines.find((l) => l.feeItemId === i.id);
      return [i.id, line ? nairaInput(line.amountMinor) : ""];
    })),
  );
  const [bad, setBad] = useState<string[]>([]);
  const { busy, error, run } = useAction();

  async function submit(e: FormEvent) {
    e.preventDefault();
    const lines: FeeLineInput[] = [];
    const wrong: string[] = [];
    for (const item of items) {
      const text = amounts[item.id]?.trim() ?? "";
      if (!text) continue;
      const minor = parseNaira(text);
      if (minor === null) wrong.push(item.id);
      else lines.push({ feeItemId: item.id, amountMinor: minor });
    }
    setBad(wrong);
    if (wrong.length) return;
    const res = await run(() => api(`/api/school/fee-structure/${termId}/${level.id}`, { method: "PUT", body: { lines } }));
    if (res.ok) onSaved();
  }

  return (
    <Modal open title={`${level.name} fees`} onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        <p className="text-sm text-slate-700">{termLabel}. Leave an item blank if this level doesn&apos;t pay it.</p>
        {items.map((item) => (
          <Input
            key={item.id}
            label={`${item.name} (₦)`}
            inputMode="decimal"
            placeholder="Not charged"
            value={amounts[item.id] ?? ""}
            onChange={(e) => setAmounts((a) => ({ ...a, [item.id]: e.target.value }))}
            hint={item.isOptional ? <Badge tone="blazer">Optional</Badge> : undefined}
            error={bad.includes(item.id) ? "Enter an amount in naira above zero, for example 45000." : undefined}
          />
        ))}
        <ErrorBox error={error} />
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" loading={busy}>
            Save fees
          </Button>
        </div>
      </form>
    </Modal>
  );
}

function CopyTerm({
  terms,
  termId,
  onClose,
  onCopied,
}: {
  terms: TermOption[];
  termId: string;
  onClose: () => void;
  onCopied: (r: FeeStructureCopyDto) => void;
}) {
  const [from, setFrom] = useState(terms[0]?.id ?? "");
  const { busy, error, run } = useAction();

  async function submit(e: FormEvent) {
    e.preventDefault();
    const res = await run(() => api<FeeStructureCopyDto>(`/api/school/fee-structure/${termId}/copy-from/${from}`, { method: "POST" }));
    if (res.ok) onCopied(res.data);
  }

  return (
    <Modal open title="Copy fees from another term" onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        <Select label="Copy from" value={from} onChange={(e) => setFrom(e.target.value)} error={error?.field("fromTermId")}>
          {terms.map((x) => (
            <option key={x.id} value={x.id}>
              {x.label}
            </option>
          ))}
        </Select>
        <p className="text-sm text-muted">Only levels with no fees in this term are filled in. Nothing you have already set is changed.</p>
        <ErrorBox error={error && !error.field("fromTermId") ? error : null} />
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" loading={busy} disabled={!from}>
            Copy fees
          </Button>
        </div>
      </form>
    </Modal>
  );
}
