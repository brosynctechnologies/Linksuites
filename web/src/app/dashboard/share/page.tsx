"use client";

import { useEffect, useState } from "react";
import { storeLink } from "@/lib/config";
import { useApi } from "@/lib/use-api";
import type { ProductDto, StoreDto } from "@/lib/types";
import { ApiErrorView, PageHeader, PageLoader, Panel, Skeleton, buttonClass, listClass } from "@/components/ui";
import { CheckIcon, CopyIcon, WhatsAppIcon } from "@/components/icons";

function CopyButton({ text, label = "Copy" }: { text: string; label?: string }) {
  const [copied, setCopied] = useState(false);
  async function copy() {
    try {
      await navigator.clipboard.writeText(text);
    } catch {
      window.prompt("Copy this link:", text);
    }
    setCopied(true);
    window.setTimeout(() => setCopied(false), 1500);
  }
  return (
    <button type="button" onClick={copy} className={buttonClass("secondary", "shrink-0")} aria-label={`${label}: ${text}`}>
      {copied ? <CheckIcon width={18} height={18} /> : <CopyIcon width={18} height={18} />}
      <span aria-live="polite">{copied ? "Copied" : label}</span>
    </button>
  );
}

function QrCode({ url, fileName }: { url: string; fileName: string }) {
  const [state, setState] = useState<{ url: string; dataUrl: string } | null>(null);

  useEffect(() => {
    let cancelled = false;
    import("qrcode")
      .then((QR) => QR.toDataURL(url, { width: 512, margin: 2, errorCorrectionLevel: "M" }))
      .then((dataUrl) => {
        if (!cancelled) setState({ url, dataUrl });
      })
      .catch(() => {});
    return () => {
      cancelled = true;
    };
  }, [url]);

  const dataUrl = state?.url === url ? state.dataUrl : null;

  return (
    <div className="flex flex-col items-start gap-4 sm:flex-row sm:items-center">
      <div className="flex h-44 w-44 shrink-0 items-center justify-center rounded-control border border-line bg-surface p-2">
        {dataUrl ? <img src={dataUrl} alt={`QR code for ${url}`} className="h-full w-full" /> : <Skeleton className="h-full w-full" />}
      </div>
      <div>
        <p className="max-w-xs text-sm text-muted">Print it on flyers, packaging or your shop counter. Customers scan it to open your store.</p>
        {dataUrl && (
          <a href={dataUrl} download={fileName} className={buttonClass("secondary", "mt-3")}>
            Download QR (PNG)
          </a>
        )}
      </div>
    </div>
  );
}

export default function SharePage() {
  const { data: store, error, reload } = useApi<StoreDto>("/api/store");
  const { data: products } = useApi<ProductDto[]>("/api/products");

  if (!store) {
    return (
      <div>
        <PageHeader title="Share your store" back="/dashboard/more" />
        {error ? <ApiErrorView error={error} onRetry={reload} /> : <PageLoader rows={3} />}
      </div>
    );
  }

  const link = storeLink(store.slug);
  const waShare = `https://wa.me/?text=${encodeURIComponent(`Shop from ${store.name} here: ${link}`)}`;
  const active = (products ?? []).filter((p) => p.isActive);

  return (
    <div className="space-y-6">
      <PageHeader title="Share your store" back="/dashboard/more" subtitle="Customers order from this link." />

      <div className="grid gap-6 lg:grid-cols-2">
        <Panel>
          <h2 className="font-display text-lg font-bold text-ink">Your store link</h2>
          <div className="mt-3 flex items-center gap-2">
            <a
              href={link}
              target="_blank"
              rel="noopener noreferrer"
              className="flex min-h-11 min-w-0 flex-1 items-center truncate rounded-control bg-paper px-3 text-sm font-medium text-ink ring-1 ring-line ring-inset"
            >
              <span className="truncate">{link}</span>
            </a>
            <CopyButton text={link} />
          </div>
          <a href={waShare} target="_blank" rel="noopener noreferrer" className={buttonClass("whatsapp", "mt-3 w-full")}>
            <WhatsAppIcon /> Share on WhatsApp
          </a>
        </Panel>

        <Panel>
          <h2 className="font-display text-lg font-bold text-ink">QR code</h2>
          <div className="mt-3">
            <QrCode url={link} fileName={`${store.slug}-qr.png`} />
          </div>
        </Panel>
      </div>

      <section className="rounded-panel bg-ink-soft p-4 sm:p-5">
        <h2 className="font-display text-lg font-bold text-ink">Instagram bio tip</h2>
        <p className="mt-1 max-w-prose text-sm text-ink-2">
          Edit profile → Links → Add external link, and paste your store link. Then add a line to your bio like{" "}
          <em>“🛍️ Order here 👇 delivery in 24h”</em>. Mention the link in every post caption and story with a link sticker.
        </p>
      </section>

      <section aria-labelledby="product-links">
        <h2 id="product-links" className="mb-3 font-display text-lg font-bold text-ink">
          Product links
        </h2>
        {!products ? (
          <PageLoader rows={2} />
        ) : active.length === 0 ? (
          <p className="rounded-panel border border-line bg-surface px-4 py-4 text-sm text-muted">
            Active products appear here, each with its own link to share.
          </p>
        ) : (
          <ul className={listClass}>
            {active.map((p) => {
              const url = `${link}/p/${p.slug}`;
              return (
                <li key={p.id} className="flex items-center gap-3 px-4 py-3">
                  {p.images[0] ? (
                    <img src={p.images[0].url} alt="" className="h-11 w-11 shrink-0 rounded-control object-cover" loading="lazy" />
                  ) : (
                    <span className="h-11 w-11 shrink-0 rounded-control bg-paper" />
                  )}
                  <span className="min-w-0 flex-1">
                    <span className="block truncate text-sm font-medium text-ink">{p.name}</span>
                    <span className="block truncate text-xs text-subtle">{url}</span>
                  </span>
                  <CopyButton text={url} />
                </li>
              );
            })}
          </ul>
        )}
      </section>
    </div>
  );
}
