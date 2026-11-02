"use client";

import { useId, useState, type FormEvent } from "react";
import { api, apiUrl, upload } from "@/lib/api";
import { formatDay, lagosToday } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { useCan } from "@/lib/roles";
import { qs, useApi } from "@/lib/use-api";
import type { ExpenseDto, SaveExpenseRequest } from "@/lib/types/fees";
import {
  ApiErrorView,
  Button,
  EmptyState,
  ErrorBox,
  Input,
  Modal,
  Notice,
  PageLoader,
  RuledSheet,
  Select,
  Textarea,
  labelClass,
  linkClass,
  ruledRowClass,
} from "@/components/ui";
import { ImportDialog } from "@/components/import-dialog";
import { TermSelect, money, nairaInput, parseNaira, useTermChoice } from "../fees-shared";

const TAKE = 1000;

/** What the school spends in a term, with private receipt photos. */
export default function ExpensesPage() {
  const canEdit = useCan("fees.manage");
  const t = useTermChoice();
  const [category, setCategory] = useState("");
  const [editing, setEditing] = useState<{ expense: ExpenseDto | null } | null>(null);
  const categories = useApi<string[]>("/api/school/expenses/categories");
  const list = useApi<ExpenseDto[]>(t.termId ? `/api/school/expenses${qs({ termId: t.termId, category, take: TAKE })}` : null);
  const del = useAction();

  async function remove(x: ExpenseDto) {
    if (!window.confirm(`Delete the ${x.category} expense of ${money(x.amountMinor)} on ${formatDay(x.date)}? Its receipt photo is deleted too.`)) return;
    if ((await del.run(() => api(`/api/school/expenses/${x.id}`, { method: "DELETE" }))).ok) {
      list.reload();
      categories.reload();
    }
  }

  if (t.error) return <ApiErrorView error={t.error} onRetry={t.reload} />;
  if (!t.loaded) return <PageLoader />;
  if (t.terms.length === 0) return <EmptyState title="No terms yet" text="Expenses are shown per term. Add the session and its terms on the setup page first." />;

  const rows = list.data ?? [];
  const total = rows.reduce((s, x) => s + x.amountMinor, 0);
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end gap-2">
        <TermSelect terms={t.terms} value={t.termId} onChange={t.setTermId} />
        <div className="w-48">
          <Select label="Category" srLabel value={category} onChange={(e) => setCategory(e.target.value)}>
            <option value="">All categories</option>
            {categories.data?.map((c) => (
              <option key={c} value={c}>
                {c}
              </option>
            ))}
          </Select>
        </div>
        <span className="flex flex-1 flex-wrap justify-end gap-2">
          {canEdit && (
            <>
              <ImportDialog kind="expenses" title="Import expenses" onDone={() => (list.reload(), categories.reload())} />
              <Button onClick={() => setEditing({ expense: null })}>Add an expense</Button>
            </>
          )}
        </span>
      </div>
      <ErrorBox error={del.error} />

      {list.error && <ApiErrorView error={list.error} onRetry={list.reload} />}
      {!list.data && !list.error && <PageLoader />}
      {list.data && rows.length === 0 && (
        <EmptyState
          title={category ? "No expenses in this category" : "No expenses this term"}
          text={category ? "Try another category." : "Record what the school spends, such as diesel, repairs or stationery, with a photo of the receipt."}
        />
      )}
      {rows.length > 0 && (
        <>
          <p className="tnum text-sm text-muted" aria-live="polite">
            {rows.length.toLocaleString("en-NG")} {rows.length === 1 ? "expense" : "expenses"}, {money(total)}
          </p>
          {rows.length >= TAKE && <Notice tone="warning">Showing the latest {TAKE.toLocaleString("en-NG")} expenses. Filter by category to see the rest.</Notice>}
          <RuledSheet
            label="Expenses"
            busy={list.loading}
            template="7rem minmax(0,1fr) 9rem 8rem 9rem"
            head={[{ label: "Date" }, { label: "Expense" }, { label: "Amount", align: "right" }, { label: "Receipt" }, { label: "" }]}
          >
            {rows.map((x) => (
              <li key={x.id} className={ruledRowClass}>
                <span className="tnum col-start-1 text-sm text-slate-700">{formatDay(x.date)}</span>
                <span className="col-start-1 min-w-0 md:col-start-auto">
                  <span className="font-bold text-ink">{x.category}</span>
                  {x.note && <span className="block text-xs text-muted">{x.note}</span>}
                </span>
                <span className="tnum col-start-2 row-start-1 text-right text-sm font-semibold md:col-start-auto md:row-start-auto">{money(x.amountMinor)}</span>
                <span className="col-start-1 text-sm md:col-start-auto">{x.hasReceipt ? <ReceiptLink expense={x} /> : <span className="text-muted">None</span>}</span>
                {canEdit ? (
                  <span className="col-start-2 row-span-3 row-start-2 flex justify-end gap-1 md:col-start-auto md:row-span-1 md:row-start-auto">
                    <Button variant="ghost" className="px-2.5" onClick={() => setEditing({ expense: x })}>
                      Edit
                    </Button>
                    <Button variant="ghost" className="px-2.5 text-brick" onClick={() => remove(x)}>
                      Delete
                    </Button>
                  </span>
                ) : (
                  <span />
                )}
              </li>
            ))}
          </RuledSheet>
        </>
      )}

      {editing && (
        <ExpenseForm
          expense={editing.expense}
          categories={categories.data ?? []}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            list.reload();
            categories.reload();
          }}
        />
      )}
    </div>
  );
}

/** Receipt links are signed for a few minutes, so fetch a fresh one when it is opened. */
function ReceiptLink({ expense }: { expense: ExpenseDto }) {
  const { busy, error, run } = useAction();
  async function open() {
    const tab = window.open("", "_blank");
    const res = await run(() => api<ExpenseDto>(`/api/school/expenses/${expense.id}`));
    const url = res.ok ? res.data.receiptUrl : null;
    if (tab && url) {
      tab.opener = null;
      tab.location.href = apiUrl(url);
    } else {
      tab?.close();
    }
  }
  return (
    <span className="inline-flex flex-col">
      <button type="button" className={linkClass} disabled={busy} onClick={open}>
        View receipt
      </button>
      {error && <span className="text-xs text-brick">{error.message}</span>}
    </span>
  );
}

function ExpenseForm({
  expense,
  categories,
  onClose,
  onSaved,
}: {
  expense: ExpenseDto | null;
  categories: string[];
  onClose: () => void;
  onSaved: () => void;
}) {
  const listId = useId();
  const fileId = useId();
  const [category, setCategory] = useState(expense?.category ?? "");
  const [amount, setAmount] = useState(expense ? nairaInput(expense.amountMinor) : "");
  const [date, setDate] = useState(expense?.date ?? lagosToday());
  const [note, setNote] = useState(expense?.note ?? "");
  const [file, setFile] = useState<File | null>(null);
  const [removeReceipt, setRemoveReceipt] = useState(false);
  const [amountError, setAmountError] = useState<string | null>(null);
  const [savedId, setSavedId] = useState<string | null>(expense?.id ?? null);
  const { busy, error, run } = useAction();

  async function submit(e: FormEvent) {
    e.preventDefault();
    const minor = parseNaira(amount);
    if (minor === null) return setAmountError("Enter the amount in naira, for example 15000.");
    setAmountError(null);
    const body: SaveExpenseRequest = { category: category.trim(), amountMinor: minor, date, note: note.trim() || null };
    const res = await run(async () => {
      // A retry after a failed photo upload reuses the expense already created.
      const id = savedId;
      const saved = id
        ? await api<ExpenseDto>(`/api/school/expenses/${id}`, { method: "PUT", body })
        : await api<ExpenseDto>("/api/school/expenses", { method: "POST", body });
      setSavedId(saved.id);
      if (file) await upload<ExpenseDto>(`/api/school/expenses/${saved.id}/receipt`, file);
      else if (removeReceipt && saved.hasReceipt) await api(`/api/school/expenses/${saved.id}/receipt`, { method: "DELETE" });
      return saved;
    });
    if (res.ok) onSaved();
  }

  const known = ["category", "amountMinor", "date", "note"];
  const other = error && !known.some((f) => error.field(f)) ? error : null;
  // An expense created before its photo failed to upload is already saved, so closing still refreshes the list.
  const close = savedId && !expense ? onSaved : onClose;
  return (
    <Modal open title={expense ? "Edit expense" : "Add an expense"} onClose={close}>
      <form onSubmit={submit} className="space-y-4">
        <Input
          label="Category"
          required
          maxLength={60}
          list={listId}
          placeholder="e.g. Diesel"
          value={category}
          onChange={(e) => setCategory(e.target.value)}
          error={error?.field("category")}
        />
        <datalist id={listId}>
          {categories.map((c) => (
            <option key={c} value={c} />
          ))}
        </datalist>
        <Input label="Amount (₦)" inputMode="decimal" required value={amount} onChange={(e) => setAmount(e.target.value)} error={amountError ?? error?.field("amountMinor")} />
        <Input label="Date spent" type="date" required max={lagosToday()} value={date} onChange={(e) => setDate(e.target.value)} error={error?.field("date")} />
        <Textarea label="Note (optional)" maxLength={300} value={note} onChange={(e) => setNote(e.target.value)} error={error?.field("note")} />
        <div className="space-y-1.5">
          <label htmlFor={fileId} className={labelClass}>
            Receipt photo (optional)
          </label>
          {expense?.hasReceipt && !file && !removeReceipt && (
            <p className="flex flex-wrap items-center gap-3 text-sm">
              <ReceiptLink expense={expense} />
              <button type="button" className="font-semibold text-brick hover:underline" onClick={() => setRemoveReceipt(true)}>
                Remove photo
              </button>
            </p>
          )}
          {removeReceipt && !file && (
            <p className="text-sm text-slate-700">
              The photo will be removed when you save.{" "}
              <button type="button" className={linkClass} onClick={() => setRemoveReceipt(false)}>
                Keep it
              </button>
            </p>
          )}
          <input
            id={fileId}
            type="file"
            accept="image/jpeg,image/png,image/webp"
            className="block w-full text-sm text-slate-700 file:mr-3 file:min-h-11 file:rounded-control file:border file:border-rule file:bg-surface file:px-4 file:text-sm file:font-semibold file:text-ink"
            onChange={(e) => setFile(e.target.files?.[0] ?? null)}
          />
          <p className="text-xs text-muted">
            A JPEG, PNG or WebP photo. It is private: only the proprietor, the principal and the bursar can open it.
            {expense?.hasReceipt ? " A new photo replaces the old one." : ""}
          </p>
          {error?.field("file") && <p className="text-xs font-medium text-brick">{error.field("file")}</p>}
        </div>
        <ErrorBox error={other && !other.field("file") ? other : null} />
        {savedId && !expense && error && <Notice tone="warning">The expense is saved. The photo didn&apos;t upload: choose it again and save, or close to add it later.</Notice>}
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={close}>
            Cancel
          </Button>
          <Button type="submit" loading={busy}>
            {expense ? "Save expense" : "Add expense"}
          </Button>
        </div>
      </form>
    </Modal>
  );
}
