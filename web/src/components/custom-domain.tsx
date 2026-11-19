"use client";

import Link from "next/link";
import { useState, type FormEvent } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useApi } from "@/lib/use-api";
import type { CustomDomainDto, CustomDomainStatus } from "@/lib/types";
import { ApiErrorView, Button, FormSection, Input, Notice, Skeleton, buttonClass, formatDate } from "./ui";

const STATUS: Record<CustomDomainStatus, { label: string; tone: string }> = {
  PendingDns: { label: "Waiting for DNS", tone: "bg-marigold-soft text-marigold-deep" },
  Verified: { label: "DNS found, setting up", tone: "bg-ink-soft text-ink" },
  Live: { label: "Live", tone: "bg-ink-soft text-ink" },
  Failed: { label: "Needs attention", tone: "bg-hibiscus-soft text-hibiscus-deep" },
};

/** Store settings: the owner's own domain, its DNS record and progress (Wave 3, plans that include custom domains). */
export function CustomDomainSection() {
  const auth = useAuth();
  const isOwner = auth?.tenant.role === "Owner" && !auth.support;
  const { data, error, reload, setData } = useApi<CustomDomainDto>("/api/store/domain");
  const [domain, setDomain] = useState("");
  const [busy, setBusy] = useState<"save" | "check" | "remove" | null>(null);
  const [actionError, setActionError] = useState<ApiError | null>(null);

  async function run(kind: "save" | "check" | "remove", call: () => Promise<CustomDomainDto | null>) {
    setBusy(kind);
    setActionError(null);
    try {
      const next = await call();
      if (next) setData(next);
      else reload();
      if (kind !== "check") setDomain("");
    } catch (e) {
      setActionError(toApiError(e));
    } finally {
      setBusy(null);
    }
  }

  function save(e: FormEvent) {
    e.preventDefault();
    run("save", () => api<CustomDomainDto>("/api/store/domain", { method: "PUT", body: { domain: domain.trim() } }));
  }

  const description = "Customers can reach your store on your own domain, such as shop.yourbusiness.com.";
  if (error) return (
    <FormSection title="Custom domain" description={description}>
      <ApiErrorView error={error} onRetry={reload} />
    </FormSection>
  );
  if (!data) return (
    <FormSection title="Custom domain" description={description}>
      <Skeleton className="h-16 w-full" />
    </FormSection>
  );

  const status = data.status ? STATUS[data.status] : null;
  return (
    <FormSection title="Custom domain" description={description}>
      {!data.included && (
        <div className="space-y-3 text-sm">
          <p className="text-ink-2">
            {data.domain
              ? `${data.domain} is paused: your plan doesn't include custom domains, so your store is back on its LinkSuite link.`
              : "Custom domains come with the Pro plan."}
          </p>
          <Link href="/dashboard/billing" className={buttonClass("secondary")}>
            See plans
          </Link>
        </div>
      )}

      {data.domain && (
        <div className="space-y-3">
          <div className="flex flex-wrap items-center gap-2">
            <p className="font-display text-lg font-bold break-all text-ink">{data.domain}</p>
            {status && data.included && <span className={`rounded-full px-2.5 py-0.5 text-xs font-semibold ${status.tone}`}>{status.label}</span>}
          </div>
          {data.served && (
            <Notice tone="success">
              Your store is live at{" "}
              <a className="font-semibold underline" href={`https://${data.domain}`} target="_blank" rel="noopener noreferrer">
                https://{data.domain}
              </a>
              .
            </Notice>
          )}
          {data.included && data.status === "Verified" && (
            <Notice>Your DNS is set. We&apos;re getting a security certificate; this usually takes under 10 minutes.</Notice>
          )}
          {data.included && data.error && <Notice tone="warning">{data.error}</Notice>}
          {data.included && data.dns && data.status !== "Live" && (
            <div className="rounded-control border border-line p-3 text-sm">
              <p className="font-medium text-ink">Add these two records where you manage your domain&apos;s DNS:</p>
              {[data.dns, data.verification].map((record, i) =>
                record ? (
                  <dl key={i} className="mt-2 grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-1 border-t border-line pt-2 first-of-type:border-t-0 first-of-type:pt-0">
                    <dt className="text-muted">Type</dt>
                    <dd className="font-mono text-ink">{record.type}</dd>
                    <dt className="text-muted">Name / host</dt>
                    <dd className="font-mono break-all text-ink">{record.name}</dd>
                    <dt className="text-muted">{i === 0 ? "Value / points to" : "Value"}</dt>
                    <dd className="font-mono break-all text-ink">{record.value}</dd>
                  </dl>
                ) : null,
              )}
              <p className="mt-2 text-xs text-subtle">
                The TXT record shows the domain is yours; keep it in place after your store goes live. Remove any other A, AAAA or CNAME
                record for this name. DNS changes can take a few hours; we check every few minutes.
              </p>
              {data.releasesAt && (
                <p className="mt-1 text-xs text-subtle">
                  If we can&apos;t verify the domain by {formatDate(data.releasesAt)}, we&apos;ll remove it so its owner can use it.
                </p>
              )}
            </div>
          )}
          {data.checkedAt && <p className="text-xs text-subtle">Last checked {formatDate(data.checkedAt)}</p>}
          {isOwner && (
            <div className="flex flex-wrap gap-2">
              {data.included && data.status !== "Live" && (
                <Button variant="secondary" loading={busy === "check"} disabled={busy !== null}
                  onClick={() => run("check", () => api<CustomDomainDto>("/api/store/domain/check", { method: "POST" }))}>
                  Check again
                </Button>
              )}
              <Button variant="ghost" loading={busy === "remove"} disabled={busy !== null}
                onClick={() => {
                  if (window.confirm(`Stop using ${data.domain}? Your store stays on its LinkSuite link.`))
                    run("remove", async () => {
                      await api("/api/store/domain", { method: "DELETE" });
                      return null;
                    });
                }}>
                Remove domain
              </Button>
            </div>
          )}
        </div>
      )}

      {isOwner && data.included && (
        <form onSubmit={save} className="space-y-3 border-t border-line pt-4 first:border-t-0 first:pt-0">
          <Input
            label={data.domain ? "Use a different domain" : "Your domain"}
            value={domain}
            onChange={(e) => setDomain(e.target.value)}
            placeholder="shop.yourbusiness.com"
            autoCapitalize="none"
            autoCorrect="off"
            spellCheck={false}
            hint="A subdomain (shop.yourbusiness.com) is easiest. A whole domain (yourbusiness.com) needs an A record."
          />
          <Button type="submit" loading={busy === "save"} disabled={busy !== null || !domain.trim()}>
            {data.domain ? "Change domain" : "Add domain"}
          </Button>
        </form>
      )}
      {!isOwner && data.included && !data.domain && <p className="text-sm text-muted">Only the store owner can add a domain.</p>}
      {actionError && <ApiErrorView error={actionError} />}
    </FormSection>
  );
}
