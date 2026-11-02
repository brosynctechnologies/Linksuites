"use client";

import { useState, type FormEvent } from "react";
import { api, ApiError, toApiError, upload } from "@/lib/api";
import { getAuth, patchAuth } from "@/lib/auth";
import { CURRENCIES, DEFAULT_ACCENT, SITE_URL } from "@/lib/config";
import { useApi } from "@/lib/use-api";
import type { SaveStoreRequest, StoreDto } from "@/lib/types";
import {
  ApiErrorView,
  Button,
  ErrorBox,
  FormSection,
  Input,
  Notice,
  PageHeader,
  PageLoader,
  Select,
  Spinner,
  Textarea,
  Toggle,
  cx,
} from "@/components/ui";
import { AccountActivity } from "@/components/account-activity";
import { ChangePassword } from "@/components/change-password";
import { ImageIcon } from "@/components/icons";

const MAX_BYTES = 5 * 1024 * 1024;

function ImageUpload({
  label,
  url,
  onFile,
  busy,
  wide,
}: {
  label: string;
  url: string | null;
  onFile: (f: File) => void;
  busy: boolean;
  wide?: boolean;
}) {
  return (
    <div className={wide ? "min-w-0 flex-1" : "shrink-0"}>
      <p className="mb-1.5 text-sm font-medium text-ink">{label}</p>
      <label
        className={cx(
          "relative flex cursor-pointer flex-col items-center justify-center gap-1 overflow-hidden rounded-control border-2 border-dashed border-line-strong bg-paper text-muted transition-colors hover:border-ink hover:text-ink has-[:focus-visible]:outline-3 has-[:focus-visible]:outline-offset-2 has-[:focus-visible]:outline-marigold",
          wide ? "aspect-[3/1] w-full" : "h-24 w-24",
          url && "border-solid border-line",
        )}
      >
        {url ? (
          <img src={url} alt="" className="h-full w-full object-cover" />
        ) : (
          <>
            <ImageIcon />
            <span className="text-xs font-medium">Upload</span>
          </>
        )}
        {busy && (
          <span className="absolute inset-0 flex items-center justify-center bg-surface/75" role="status">
            <Spinner />
            <span className="sr-only">Uploading…</span>
          </span>
        )}
        <span className="sr-only">Upload {label.toLowerCase()}</span>
        <input
          type="file"
          accept="image/jpeg,image/png,image/webp"
          className="sr-only"
          onChange={(e) => {
            const f = e.target.files?.[0];
            if (f) onFile(f);
            e.target.value = "";
          }}
        />
      </label>
    </div>
  );
}

function SettingsForm({ store, onSaved }: { store: StoreDto; onSaved: (s: StoreDto) => void }) {
  const [form, setForm] = useState<SaveStoreRequest>({
    name: store.name,
    slug: store.slug,
    description: store.description ?? "",
    whatsappNumber: store.whatsappNumber,
    currency: store.currency,
    themeColor: store.themeColor || DEFAULT_ACCENT,
    isOpen: store.isOpen,
    openingHours: store.openingHours ?? "",
  });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [saved, setSaved] = useState(false);
  const [uploading, setUploading] = useState<"logo" | "cover" | null>(null);
  const [uploadError, setUploadError] = useState<ApiError | string | null>(null);
  const [images, setImages] = useState({ logoUrl: store.logoUrl, coverUrl: store.coverUrl });

  const set = <K extends keyof SaveStoreRequest>(k: K, v: SaveStoreRequest[K]) => setForm((f) => ({ ...f, [k]: v }));

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    setSaved(false);
    try {
      const updated = await api<StoreDto>("/api/store", {
        method: "PUT",
        body: {
          ...form,
          slug: form.slug.trim().toLowerCase(),
          description: form.description?.trim() || null,
          openingHours: form.openingHours?.trim() || null,
        },
      });
      onSaved(updated);
      const tenant = getAuth()?.tenant;
      if (tenant) patchAuth({ tenant: { ...tenant, name: updated.name, slug: updated.slug } });
      setSaved(true);
    } catch (err) {
      setError(toApiError(err));
    } finally {
      setBusy(false);
    }
  }

  async function uploadImage(kind: "logo" | "cover", file: File) {
    setUploadError(null);
    if (file.size > MAX_BYTES) return setUploadError("Image must be 5 MB or smaller.");
    setUploading(kind);
    try {
      const updated = await upload<StoreDto>(`/api/store/${kind}`, file);
      setImages({ logoUrl: updated.logoUrl, coverUrl: updated.coverUrl });
    } catch (err) {
      setUploadError(toApiError(err));
    } finally {
      setUploading(null);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-8">
      <FormSection title="Store status" description="Close the store when you can't take orders, for example on a day off.">
        <Toggle
          label={form.isOpen ? "Store is open" : "Store is closed"}
          description={form.isOpen ? "Customers can place orders." : "Customers can browse but not check out."}
          checked={form.isOpen}
          onChange={(v) => set("isOpen", v)}
        />
      </FormSection>

      <FormSection title="Branding" description="Your logo, cover photo and the colour used for buttons in your store.">
        <div className="flex gap-4">
          <ImageUpload label="Logo" url={images.logoUrl} busy={uploading === "logo"} onFile={(f) => uploadImage("logo", f)} />
          <ImageUpload label="Cover" url={images.coverUrl} busy={uploading === "cover"} onFile={(f) => uploadImage("cover", f)} wide />
        </div>
        {uploadError && (typeof uploadError === "string" ? <ErrorBox error={uploadError} /> : <ApiErrorView error={uploadError} />)}
        <div className="border-t border-line pt-4">
          <label htmlFor="theme-color" className="block text-sm font-medium text-ink">
            Theme colour
          </label>
          <div className="mt-1.5 flex flex-wrap items-center gap-3">
            <input
              id="theme-color"
              type="color"
              value={form.themeColor || DEFAULT_ACCENT}
              onChange={(e) => set("themeColor", e.target.value)}
              className="h-11 w-16 cursor-pointer rounded-control border border-line-strong bg-surface p-1"
            />
            <span className="text-sm font-medium text-ink-2 uppercase num">{form.themeColor}</span>
            <Button variant="ghost" onClick={() => set("themeColor", DEFAULT_ACCENT)}>
              Reset
            </Button>
          </div>
        </div>
      </FormSection>

      <FormSection title="Store details" description="Your store name, link and where orders are sent.">
        <Input label="Store name" required value={form.name} onChange={(e) => set("name", e.target.value)} />
        <Input
          label="Store link"
          required
          value={form.slug}
          onChange={(e) => set("slug", e.target.value.toLowerCase().replace(/[^a-z0-9-]/g, ""))}
          hint={
            <span className="break-all">
              {SITE_URL}/<strong className="font-semibold text-ink">{form.slug || "your-store"}</strong>
              {form.slug !== store.slug && " — changing this breaks links you've already shared."}
            </span>
          }
        />
        <Textarea label="Description" value={form.description ?? ""} onChange={(e) => set("description", e.target.value)} placeholder="What do you sell? Where do you deliver?" />
        <Input
          label="WhatsApp number"
          type="tel"
          inputMode="tel"
          required
          value={form.whatsappNumber}
          onChange={(e) => set("whatsappNumber", e.target.value)}
          hint="Orders are sent to this number."
        />
        <div className="grid gap-4 sm:grid-cols-2">
          <Select label="Currency" value={form.currency} onChange={(e) => set("currency", e.target.value)}>
            {CURRENCIES.map((c) => (
              <option key={c} value={c}>
                {c}
              </option>
            ))}
          </Select>
          <Input label="Opening hours" value={form.openingHours ?? ""} onChange={(e) => set("openingHours", e.target.value)} placeholder="Mon–Sat 9am–6pm" />
        </div>
      </FormSection>

      {error && <ApiErrorView error={error} />}
      {saved && <Notice tone="success">Settings saved.</Notice>}

      <div className="sticky bottom-[calc(4rem+1px+env(safe-area-inset-bottom))] z-20 -mx-4 border-y border-line bg-surface/95 px-4 py-3 backdrop-blur sm:-mx-6 sm:px-6 lg:bottom-4 lg:mx-0 lg:flex lg:justify-end lg:rounded-panel lg:border lg:px-4 lg:shadow-lift">
        <Button type="submit" loading={busy} className="w-full lg:w-auto">
          Save settings
        </Button>
      </div>
    </form>
  );
}

export default function SettingsPage() {
  const { data, error, reload, setData } = useApi<StoreDto>("/api/store");
  return (
    <div>
      <PageHeader title="Store settings" back="/dashboard/more" />
      {error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && <PageLoader label="Loading settings…" rows={3} />}
      {data && <SettingsForm key={data.id} store={data} onSaved={setData} />}
      <div className="mt-8 space-y-8">
        <ChangePassword />
        <AccountActivity />
      </div>
    </div>
  );
}
