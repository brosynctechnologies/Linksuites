"use client";

import Link from "next/link";
import { useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { ApiError, apiBlob, saveBlob, toApiError } from "@/lib/api";
import { useHydrated } from "@/lib/auth";
import { CURRENCIES } from "@/lib/config";
import { addDays } from "@/lib/dates";
import { clearDraft, emptyDraft, loadDraft, saveDraft, toPreviewRequest, type DraftBusiness, type GeneratorDraft } from "@/lib/draft";
import { computeTotals } from "@/lib/invoice-math";
import { DOCUMENT_TITLES, FONT_STYLE_OPTIONS, HEADER_LINE_OPTIONS, LETTERHEAD_LIMITS, letterheadErrors } from "@/lib/letterhead";
import { toMinor } from "@/lib/money";
import { FREE_TEMPLATES, normalizeColor } from "@/lib/templates";
import type { DocumentTitle, FontStyle, HeaderLine } from "@/lib/types";
import { useFocusFirstInvalid } from "./form-focus";
import { DownloadIcon, ImageIcon } from "./icons";
import { InvoicePreview, type PreviewDoc } from "./invoice-preview";
import { SecondAccentField } from "./letterhead-fields";
import { LineItemsEditor, mathInput, previewLines, isBlankLine } from "./line-items-editor";
import { TemplatePicker } from "./template-picker";
import { Button, Card, ErrorBox, Input, PageLoader, Segmented, Select, Textarea, Toggle, buttonClass, cx, labelClass, linkClass } from "./ui";

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
  const formRef = useRef<HTMLDivElement>(null);
  useFocusFirstInvalid(formRef, error);

  useEffect(() => {
    const t = setTimeout(() => saveDraft(d), 300);
    return () => clearTimeout(t);
  }, [d]);

  const set = <K extends keyof GeneratorDraft>(k: K, v: GeneratorDraft[K]) => setD((s) => ({ ...s, [k]: v }));
  const setBizValue = <K extends keyof DraftBusiness>(k: K, v: DraftBusiness[K]) => setD((s) => ({ ...s, business: { ...s.business, [k]: v } }));
  const setBiz = (k: keyof DraftBusiness) => (e: { target: { value: string } }) =>
    setD((s) => ({ ...s, business: { ...s.business, [k]: e.target.value } }));
  const fieldError = (name: keyof DraftBusiness) => (error instanceof ApiError ? error.field(`business.${name}`) : undefined);
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
    const invalid = letterheadErrors(d.business, "business.");
    if (Object.keys(invalid).length > 0) {
      setView("edit");
      return setError(new ApiError(400, { detail: "Check the highlighted fields:", errors: invalid }));
    }
    setBusy(true);
    try {
      const blob = await apiBlob("/api/public/invoice-preview/pdf", { method: "POST", body: toPreviewRequest(d), auth: false, accept: "application/pdf" });
      saveBlob(blob, `${(d.number || "invoice").replace(/[^\w.-]+/g, "_")}.pdf`);
    } catch (e) {
      const err = toApiError(e);
      if (err.errors && Object.keys(err.errors).some((k) => k.toLowerCase().startsWith("business."))) setView("edit");
      setError(err);
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
      <div ref={formRef} className="min-w-0 space-y-4">
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
            <div className="mb-4 flex items-center gap-3 rounded-control border border-dashed border-rule bg-desk px-3 py-2.5">
              <ImageIcon className="shrink-0 text-faint" width={22} height={22} aria-hidden="true" />
              <p className="text-sm text-slate-600">
                <Link href="/signup" className={linkClass}>
                  Add your logo, signature and stamp with InvoiceLink Pro
                </Link>
              </p>
            </div>
            <div className="grid gap-3 sm:grid-cols-2">
              <Input label="Business name" value={d.business.name} onChange={setBiz("name")} autoComplete="organization" />
              <Input
                label="Slogan or what you do (optional)"
                value={d.business.tagline}
                onChange={setBiz("tagline")}
                maxLength={LETTERHEAD_LIMITS.tagline}
                placeholder="Bespoke tailoring for men and women"
                error={fieldError("tagline")}
              />
              <Input
                label="Registration number (optional)"
                value={d.business.registrationNumber}
                onChange={setBiz("registrationNumber")}
                maxLength={LETTERHEAD_LIMITS.registrationNumber}
                placeholder="RC 1234567"
                error={fieldError("registrationNumber")}
              />
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
            <h3 className="font-semibold text-graphite">Letterhead and design</h3>
            <TemplatePicker
              label="Design"
              templates={FREE_TEMPLATES}
              value={d.template}
              onChange={(k) => set("template", k)}
              brandColor={d.business.brandColor}
              secondaryColor={d.business.secondaryColor}
            />
            <div className="grid gap-3 sm:grid-cols-2">
              <div className="space-y-1.5">
                <label htmlFor="gen-color" className={labelClass}>
                  Brand colour
                </label>
                <div className="flex items-center gap-3">
                  <input
                    id="gen-color"
                    type="color"
                    value={normalizeColor(d.business.brandColor)}
                    onChange={setBiz("brandColor")}
                    className="h-10 w-14 cursor-pointer rounded-control border border-field bg-paper p-1"
                  />
                  <span className="text-sm text-muted tabular-nums">{d.business.brandColor}</span>
                </div>
              </div>
              <SecondAccentField
                id="gen-second"
                brandColor={d.business.brandColor}
                value={d.business.secondaryColor}
                onChange={(v) => setBizValue("secondaryColor", v)}
                error={fieldError("secondaryColor")}
              />
              <Select
                label="Line under your name"
                value={d.business.headerLine}
                onChange={(e) => setBizValue("headerLine", e.target.value as HeaderLine)}
              >
                {HEADER_LINE_OPTIONS.map((o) => (
                  <option key={o.value} value={o.value}>
                    {o.label}
                  </option>
                ))}
              </Select>
              <Select label="Font style" value={d.business.fontStyle} onChange={(e) => setBizValue("fontStyle", e.target.value as FontStyle)}>
                {FONT_STYLE_OPTIONS.map((o) => (
                  <option key={o.value} value={o.value}>
                    {o.label}
                  </option>
                ))}
              </Select>
              <Select
                label="Document title"
                value={d.business.documentTitle}
                onChange={(e) => setBizValue("documentTitle", e.target.value as DocumentTitle)}
              >
                {DOCUMENT_TITLES.map((t) => (
                  <option key={t}>{t}</option>
                ))}
              </Select>
            </div>
            <Toggle
              checked={d.business.showSignature}
              onChange={(v) => setBizValue("showSignature", v)}
              label="Signature line"
              description="An “Authorised signature” line to sign by hand after printing."
            />
            {d.business.showSignature && (
              <div className="grid gap-3 sm:grid-cols-2">
                <Input
                  label="Name (optional)"
                  value={d.business.signatureName}
                  onChange={setBiz("signatureName")}
                  maxLength={LETTERHEAD_LIMITS.signatureName}
                  placeholder="Ada Okafor"
                  error={fieldError("signatureName")}
                />
                <Input
                  label="Role (optional)"
                  value={d.business.signatureRole}
                  onChange={setBiz("signatureRole")}
                  maxLength={LETTERHEAD_LIMITS.signatureRole}
                  placeholder="Director"
                  error={fieldError("signatureRole")}
                />
              </div>
            )}
            <Input
              label="Footer line (optional)"
              value={d.business.footerText}
              onChange={setBiz("footerText")}
              maxLength={LETTERHEAD_LIMITS.footerText}
              placeholder="Thank you for your business."
              hint="Printed at the bottom of every page."
              error={fieldError("footerText")}
            />
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
