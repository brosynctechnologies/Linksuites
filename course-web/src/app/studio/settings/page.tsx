"use client";

import { useState, type CSSProperties, type FormEvent } from "react";
import { api, toApiError, upload, type ApiError } from "@/lib/api";
import { getAuth, patchAuth } from "@/lib/auth";
import { brandVars, normalizeHex } from "@/lib/color";
import { DEFAULT_BRAND } from "@/lib/config";
import { slugify } from "@/lib/format";
import { useCan } from "@/lib/permissions";
import { useApi } from "@/lib/use-api";
import type { AcademyDto, UpdateAcademyRequest } from "@/lib/types";
import { AccountActivity } from "@/components/account-activity";
import { ChangePassword } from "@/components/change-password";
import { CopyButton } from "@/components/copy-button";
import { ImageUpload, MarkdownField } from "@/components/editors";
import { ApiErrorView, Button, Card, ErrorBox, Input, NoAccess, Notice, PageHeader, PageLoader, SectionTitle, cx } from "@/components/ui";

/** Brand colours that read well with white text and sit comfortably beside the LearnLink palette. */
const SWATCHES = ["#0B6E6E", "#1D4E89", "#5B3A8C", "#8C2F5A", "#B3471E", "#7A5B12", "#2F6B2F", "#10202B"];

export default function AcademySettingsPage() {
  const allowed = useCan("settings.edit");
  const { data, error, reload, setData } = useApi<AcademyDto>(allowed ? "/api/academy" : null);

  if (!allowed) {
    return (
      <div>
        <PageHeader title="Academy settings" back="/studio/more" />
        <NoAccess text="Only the owner and admins change academy settings." />
        <div className="mt-6 space-y-6">
          <ChangePassword />
          <AccountActivity />
        </div>
      </div>
    );
  }
  if (!data) {
    return (
      <div>
        <PageHeader title="Academy settings" back="/studio/more" />
        {error ? <ApiErrorView error={error} onRetry={reload} /> : <PageLoader />}
        <div className="mt-6 space-y-6">
          <ChangePassword />
          <AccountActivity />
        </div>
      </div>
    );
  }
  return (
    <div className="space-y-6">
      <SettingsForm key={data.slug} academy={data} onSaved={setData} />
      <ChangePassword />
      <AccountActivity />
    </div>
  );
}

function SettingsForm({ academy, onSaved }: { academy: AcademyDto; onSaved: (a: AcademyDto) => void }) {
  const [form, setForm] = useState<UpdateAcademyRequest>({
    name: academy.name,
    slug: academy.slug,
    headline: academy.headline ?? "",
    bio: academy.bio ?? "",
    brandColor: normalizeHex(academy.brandColor, DEFAULT_BRAND),
  });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [logoError, setLogoError] = useState<ApiError | null>(null);
  const [saved, setSaved] = useState(false);
  const set = <K extends keyof UpdateAcademyRequest>(k: K, v: UpdateAcademyRequest[K]) => {
    setForm((f) => ({ ...f, [k]: v }));
    setSaved(false);
  };
  const origin = typeof window !== "undefined" ? window.location.origin : "";
  const url = `${origin}/a/${form.slug || "your-academy"}`;
  const brand = normalizeHex(form.brandColor, DEFAULT_BRAND);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const res = await api<AcademyDto>("/api/academy", {
        method: "PUT",
        body: { name: form.name.trim(), slug: slugify(form.slug), headline: form.headline?.trim() || null, bio: form.bio?.trim() || null, brandColor: brand },
      });
      onSaved(res);
      const t = getAuth()?.tenant;
      if (t) patchAuth({ tenant: { ...t, name: res.name, slug: res.slug } });
      setSaved(true);
    } catch (err) {
      setError(toApiError(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-5">
      <PageHeader title="Academy settings" back="/studio/more" />

      <Card className="space-y-4">
        <SectionTitle>Profile</SectionTitle>
        <ImageUpload
          label="Logo"
          shape="round"
          src={academy.logoUrl}
          hint="Square, at least 256 by 256."
          onUpload={async (file) => {
            setLogoError(null);
            try {
              onSaved(await upload<AcademyDto>("/api/academy/logo", file));
            } catch (e) {
              setLogoError(toApiError(e));
            }
          }}
        />
        {logoError && <ErrorBox error={logoError} />}
        <Input label="Academy name" required value={form.name} onChange={(e) => set("name", e.target.value)} error={error?.field("name")} />
        <Input
          label="Headline"
          value={form.headline ?? ""}
          onChange={(e) => set("headline", e.target.value)}
          placeholder="e.g. JAMB and WAEC physics, explained the way exams ask it"
          hint="One line under your name on the academy page."
          error={error?.field("headline")}
        />
        <MarkdownField label="About you" value={form.bio ?? ""} onChange={(v) => set("bio", v)} rows={8} />
      </Card>

      <Card className="space-y-4">
        <SectionTitle>Web address</SectionTitle>
        <Input
          label="Academy link"
          value={form.slug}
          onChange={(e) => set("slug", e.target.value.toLowerCase().replace(/[^a-z0-9-]/g, "-"))}
          hint="Lowercase letters, numbers and dashes. Changing it breaks links you've already shared."
          error={error?.field("slug")}
        />
        <div className="flex flex-wrap items-center gap-2">
          <code className="min-w-0 flex-1 rounded-control bg-mist px-3 py-2 text-sm break-all text-ink">{url}</code>
          <CopyButton text={`${origin}/a/${academy.slug}`} label="Copy current link" />
        </div>
      </Card>

      <Card className="space-y-4">
        <SectionTitle>Brand colour</SectionTitle>
        <p className="text-sm text-muted">Used on your academy page and on course pages learners reach from it.</p>
        <div className="flex flex-wrap items-center gap-2" role="radiogroup" aria-label="Brand colour">
          {SWATCHES.map((c) => (
            <button
              key={c}
              type="button"
              role="radio"
              aria-checked={brand === c}
              aria-label={c}
              onClick={() => set("brandColor", c)}
              className={cx("h-9 w-9 rounded-full border-2", brand === c ? "border-ink ring-2 ring-offset-2 ring-ink" : "border-transparent")}
              style={{ background: c }}
            />
          ))}
          <label className="ml-2 flex items-center gap-2 text-sm text-slate-700">
            <input type="color" value={brand} onChange={(e) => set("brandColor", e.target.value.toUpperCase())} className="h-9 w-12 cursor-pointer rounded-control border border-line bg-surface" />
            Custom
          </label>
        </div>
        <div className="overflow-hidden rounded-panel border border-line" style={brandVars(brand) as CSSProperties}>
          <div className="px-4 py-5" style={{ background: brand, color: "var(--on-lagoon)" }}>
            <p className="font-serif text-xl font-semibold">{form.name || "Your academy"}</p>
            <p className="text-sm opacity-90">{form.headline || "Your headline"}</p>
          </div>
          <div className="flex items-center justify-between gap-3 bg-surface px-4 py-3">
            <span className="text-sm text-slate-700">Preview</span>
            <span className="inline-flex min-h-9 items-center rounded-control bg-lagoon px-4 text-sm font-semibold text-on-lagoon">Enrol for free</span>
          </div>
        </div>
      </Card>

      {error && <ApiErrorView error={error} />}
      {saved && <Notice tone="success">Settings saved.</Notice>}
      <div className="sticky bottom-[calc(4.5rem+env(safe-area-inset-bottom))] z-10 flex justify-end rounded-panel border border-line bg-surface/95 p-3 shadow-bar backdrop-blur lg:bottom-4">
        <Button type="submit" loading={busy} disabled={!form.name.trim() || !form.slug.trim()}>
          Save settings
        </Button>
      </div>
    </form>
  );
}
