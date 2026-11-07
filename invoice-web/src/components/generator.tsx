"use client";

import Link from "next/link";
import { useEffect, useMemo, useState, type ReactNode } from "react";
import { apiBlob, saveBlob, toApiError, type ApiError } from "@/lib/api";
import { useHydrated } from "@/lib/auth";
import { CURRENCIES } from "@/lib/config";
import { addDays } from "@/lib/dates";
import { clearDraft, emptyDraft, loadDraft, saveDraft, toPreviewRequest, type GeneratorDraft } from "@/lib/draft";
import { computeTotals } from "@/lib/invoice-math";
import { toMinor } from "@/lib/money";
import { FREE_TEMPLATES } from "@/lib/templates";
import { DownloadIcon } from "./icons";
import { InvoicePreview, type PreviewDoc } from "./invoice-preview";
import { LineItemsEditor, mathInput, previewLines, isBlankLine } from "./line-items-editor";
import { TemplatePicker } from "./template-picker";
import { Button, Card, ErrorBox, Input, PageLoader, Segmented, Select, Textarea, buttonClass, cx } from "./ui";

/**
 * The free, no-signup invoice generator. Renders after hydration because the draft lives in localStorage.
 * `intro` is shown at the top of the form column (the landing page puts its headline there).
 */
export function Generator({ intro }: { intro?: ReactNode }) {
  const hydrated = useHydrated();
  if (!hydrated) {
    return (
      <div>
        {intro}
        <PageLoader label="Loading the invoice generator…" />
      </div>
    );
  }
  return <GeneratorForm intro={intro} />;
}

function GeneratorForm({ intro }: { intro?: ReactNode }) {
  const [view, setView] = useState<"edit" | "preview">("edit");
  const [d, setD] = useState<GeneratorDraft>(() => loadDraft() ?? emptyDraft());
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | string | null>(null);

  useEffect(() => {
    const t = setTimeout(() => saveDraft(d), 300);
    return () => clearTimeout(t);
  }, [d]);

  const set = <K extends keyof GeneratorDraft>(k: K, v: GeneratorDraft[K]) => setD((s) => ({ ...s, [k]: v }));
  const setBiz = (k: keyof GeneratorDraft["business"]) => (e: { target: { value: string } }) =>
    setD((s) => ({ ...s, business: { ...s.business, [k]: e.target.value } }));
  const setClient = (k: keyof GeneratorDraft["client"]) => (e: { target: { value: string } }) =>
    setD((s) => ({ ...s, client: { ...s.client, [k]: e.target.value } }));

  const doc = useMemo<PreviewDoc>(() => {
    const filled = d.lines.filter((l) => !isBlankLine(l));
    const totals = computeTotals(
      filled.map((l) => mathInput(l)),
      toMinor(d.shipping) ?? 0,
    );
    return {
      kind: "invoice",
      template: d.template,
      brandColor: d.business.brandColor,
      number: d.number,
      business: d.business,
      client: d.client,
      issueDate: d.issueDate,
      secondDate: d.dueDate,
      currency: d.currency,
      lines: previewLines(d.lines),
      subtotalMinor: totals.subtotalMinor,
      discountTotalMinor: totals.discountTotalMinor,
      taxes: totals.taxes,
      shippingMinor: totals.shippingMinor,
      totalMinor: totals.totalMinor,
      notes: d.notes,
      terms: d.terms,
      showBranding: true,
    };
  }, [d]);

  async function download() {
    setError(null);
    if (!d.business.name.trim()) return setError("Add your business name first.");
    if (!d.client.name.trim()) return setError("Add your client's name first.");
    if (!d.lines.some((l) => !isBlankLine(l))) return setError("Add at least one line item.");
    setBusy(true);
    try {
      const blob = await apiBlob("/api/public/invoice-preview/pdf", { method: "POST", body: toPreviewRequest(d), auth: false, accept: "application/pdf" });
      saveBlob(blob, `${(d.number || "invoice").replace(/[^\w.-]+/g, "_")}.pdf`);
    } catch (e) {
      setError(toApiError(e));
    } finally {
      setBusy(false);
    }
  }

  function reset() {
    if (!window.confirm("Clear this invoice and start again?")) return;
    clearDraft();
    setD(emptyDraft());
  }

  const sectionTitle = "mb-4 font-semibold text-graphite";

  return (
    <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.08fr)] lg:gap-10">
      <div className="min-w-0 space-y-4">
        {intro}
        <Segmented
          className="max-w-xs lg:hidden"
          label="Generator view"
          items={[
            { value: "edit", label: "Fill in" },
            { value: "preview", label: "Preview" },
          ]}
          value={view}
          onChange={setView}
        />
        <div className={cx("space-y-4", view !== "edit" && "hidden lg:block")}>
          <Card>
            <h3 className={sectionTitle}>From (your business)</h3>
            <div className="grid gap-3 sm:grid-cols-2">
              <Input label="Business name" value={d.business.name} onChange={setBiz("name")} autoComplete="organization" />
              <Input label="Email" type="email" value={d.business.email} onChange={setBiz("email")} autoComplete="email" />
              <Input label="Phone" type="tel" value={d.business.phone} onChange={setBiz("phone")} autoComplete="tel" />
              <Input label="Tax ID (optional)" value={d.business.taxId} onChange={setBiz("taxId")} />
              <div className="sm:col-span-2">
                <Textarea label="Address" rows={2} value={d.business.address} onChange={setBiz("address")} />
              </div>
              <div className="sm:col-span-2">
                <Textarea
                  label="Bank / payment details"
                  rows={2}
                  value={d.business.bankDetails}
                  onChange={setBiz("bankDetails")}
                  placeholder={"Bank: …\nAccount name: …\nAccount number: …"}
                />
              </div>
            </div>
          </Card>

          <Card>
            <h3 className={sectionTitle}>Bill to (your client)</h3>
            <div className="grid gap-3 sm:grid-cols-2">
              <Input label="Client name" value={d.client.name} onChange={setClient("name")} />
              <Input label="Company (optional)" value={d.client.company} onChange={setClient("company")} />
              <Input label="Email" type="email" value={d.client.email} onChange={setClient("email")} />
              <Input label="Phone" type="tel" value={d.client.phone} onChange={setClient("phone")} />
              <div className="sm:col-span-2">
                <Textarea label="Address" rows={2} value={d.client.address} onChange={setClient("address")} />
              </div>
            </div>
          </Card>

          <Card>
            <h3 className={sectionTitle}>Invoice details</h3>
            <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
              <Input label="Invoice no." value={d.number} onChange={(e) => set("number", e.target.value)} />
              <Select label="Currency" value={d.currency} onChange={(e) => set("currency", e.target.value)}>
                {CURRENCIES.map((c) => (
                  <option key={c}>{c}</option>
                ))}
              </Select>
              <Input
                label="Issue date"
                type="date"
                value={d.issueDate}
                onChange={(e) => {
                  const v = e.target.value;
                  setD((s) => ({ ...s, issueDate: v, dueDate: s.dueDate < v ? addDays(v, 14) : s.dueDate }));
                }}
              />
              <Input label="Due date" type="date" value={d.dueDate} min={d.issueDate} onChange={(e) => set("dueDate", e.target.value)} />
            </div>
          </Card>

          <Card>
            <h3 className={sectionTitle}>Items</h3>
            <LineItemsEditor lines={d.lines} onChange={(lines) => set("lines", lines)} currency={d.currency} />
            <div className="mt-4 grid grid-cols-2 gap-3">
              <Input label={`Shipping (${d.currency})`} inputMode="decimal" placeholder="0" value={d.shipping} onChange={(e) => set("shipping", e.target.value)} />
            </div>
          </Card>

          <Card className="space-y-3">
            <Textarea label="Notes" rows={2} value={d.notes} onChange={(e) => set("notes", e.target.value)} placeholder="Thank you for your business!" />
            <Textarea label="Terms" rows={2} value={d.terms} onChange={(e) => set("terms", e.target.value)} placeholder="Payment due within 14 days." />
          </Card>

          <Card className="space-y-4">
            <TemplatePicker templates={FREE_TEMPLATES} value={d.template} onChange={(k) => set("template", k)} brandColor={d.business.brandColor} />
            <div className="flex items-center gap-3">
              <label htmlFor="gen-color" className="text-sm font-medium text-slate-700">
                Brand colour
              </label>
              <input
                id="gen-color"
                type="color"
                value={d.business.brandColor}
                onChange={setBiz("brandColor")}
                className="h-10 w-14 cursor-pointer rounded-control border border-field bg-paper p-1"
              />
              <span className="text-sm text-muted tabular-nums">{d.business.brandColor}</span>
            </div>
          </Card>
        </div>
      </div>

      <div className={cx("min-w-0 space-y-3 lg:sticky lg:top-6 lg:self-start", view !== "preview" && "hidden lg:block")}>
        <div className="flex flex-wrap gap-2">
          <Button onClick={download} loading={busy} className="flex-1">
            <DownloadIcon className="h-4 w-4" /> Download PDF
          </Button>
          <Link href="/signup?import=1" className={buttonClass("secondary", "flex-1")}>
            Save and send it with a free account
          </Link>
        </div>
        <ErrorBox error={error} />
        <p className="text-xs text-muted">
          Your draft is saved in this browser automatically.{" "}
          <button type="button" className="font-semibold text-slate-700 underline underline-offset-2" onClick={reset}>
            Start over
          </button>
        </p>
        <section aria-label="Invoice preview" className="pt-1 font-sans">
          <InvoicePreview doc={doc} />
        </section>
        <p className="text-center text-xs text-muted">
          Already have an account?{" "}
          <Link href="/login?import=1" className="font-semibold text-ink underline-offset-2 hover:underline">
            Log in and save this invoice
          </Link>
        </p>
      </div>
    </div>
  );
}
