"use client";

import { useState, type FormEvent } from "react";
import { api } from "@/lib/api";
import { useAction } from "@/lib/mutate";
import { useCan } from "@/lib/roles";
import { useApi } from "@/lib/use-api";
import type { FeeItemDto, SaveFeeItemRequest } from "@/lib/types/fees";
import { ImportDialog } from "@/components/import-dialog";
import { ApiErrorView, Badge, Button, Checkbox, EmptyState, ErrorBox, Input, Modal, PageLoader, RuledSheet, ruledRowClass } from "@/components/ui";

export default function FeeItemsPage() {
  const canEdit = useCan("fees.manage");
  const { data, error, reload } = useApi<FeeItemDto[]>("/api/school/fee-items");
  const [editing, setEditing] = useState<{ item: FeeItemDto | null } | null>(null);
  const del = useAction();

  async function remove(item: FeeItemDto) {
    if (!window.confirm(`Delete ${item.name}? A fee item used in a fee structure or an invoice can't be deleted.`)) return;
    if ((await del.run(() => api(`/api/school/fee-items/${item.id}`, { method: "DELETE" }))).ok) reload();
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="max-w-prose text-sm text-slate-700">
          The things the school charges for, such as tuition, books or the bus. Name tuition with the word &quot;tuition&quot; so percentage discounts
          work. Optional items are added to a student&apos;s invoice only when you choose them.
        </p>
        {canEdit && (
          <span className="flex flex-wrap gap-2">
            <ImportDialog kind="fee-items" title="Import fee items" onDone={reload} />
            <Button onClick={() => setEditing({ item: null })}>Add a fee item</Button>
          </span>
        )}
      </div>
      <ErrorBox error={del.error} />
      {error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && <PageLoader />}
      {data && data.length === 0 && <EmptyState title="No fee items yet" text="Add tuition first, then anything else the school charges for." />}
      {data && data.length > 0 && (
        <RuledSheet label="Fee items" template="minmax(0,1fr) 8rem 9rem" head={[{ label: "Fee item" }, { label: "Type" }, { label: "" }]}>
          {data.map((item) => (
            <li key={item.id} className={ruledRowClass}>
              <span className="font-bold text-ink">{item.name}</span>
              <span className="col-start-1 md:col-start-auto">{item.isOptional ? <Badge tone="blazer">Optional</Badge> : <Badge>Required</Badge>}</span>
              {canEdit ? (
                <span className="col-start-2 row-span-2 row-start-1 flex justify-end gap-1 md:col-start-auto md:row-span-1 md:row-start-auto">
                  <Button variant="ghost" className="px-2.5" onClick={() => setEditing({ item })}>
                    Edit
                  </Button>
                  <Button variant="ghost" className="px-2.5 text-brick" onClick={() => remove(item)}>
                    Delete
                  </Button>
                </span>
              ) : (
                <span />
              )}
            </li>
          ))}
        </RuledSheet>
      )}
      {editing && (
        <FeeItemForm
          item={editing.item}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            reload();
          }}
        />
      )}
    </div>
  );
}

function FeeItemForm({ item, onClose, onSaved }: { item: FeeItemDto | null; onClose: () => void; onSaved: () => void }) {
  const [name, setName] = useState(item?.name ?? "");
  const [optional, setOptional] = useState(item?.isOptional ?? false);
  const { busy, error, run } = useAction();

  async function submit(e: FormEvent) {
    e.preventDefault();
    const body: SaveFeeItemRequest = { name: name.trim(), isOptional: optional };
    const res = await run(() =>
      item ? api(`/api/school/fee-items/${item.id}`, { method: "PUT", body }) : api("/api/school/fee-items", { method: "POST", body }),
    );
    if (res.ok) onSaved();
  }

  return (
    <Modal open title={item ? `Edit ${item.name}` : "Add a fee item"} onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        <Input label="Name" required maxLength={80} placeholder="e.g. Tuition" value={name} onChange={(e) => setName(e.target.value)} error={error?.field("name")} />
        <Checkbox checked={optional} onChange={setOptional} label="Optional. Add it only to the invoices of students who take it, such as the school bus." />
        <ErrorBox error={error && !error.field("name") ? error : null} />
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" loading={busy}>
            {item ? "Save fee item" : "Add fee item"}
          </Button>
        </div>
      </form>
    </Modal>
  );
}
