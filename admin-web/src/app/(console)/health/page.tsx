"use client";

import { RequireArea } from "@/components/console";
import { RefreshIcon } from "@/components/icons";
import { Button, EmptyState, ErrorPanel, Loading, Mono, PageHeader, Panel, StatusBadge, TableScroll, cx } from "@/components/ui";
import { ago, num, stamp } from "@/lib/format";
import { jobHealth } from "@/lib/status";
import type { HealthDto } from "@/lib/types";
import { useApi } from "@/lib/use-api";
import { useNow } from "@/lib/use-now";

export default function HealthPage() {
  return (
    <RequireArea area="health">
      <Health />
    </RequireArea>
  );
}

function Health() {
  const { data, error, loading, reload } = useApi<HealthDto>("/api/platform/health");
  const now = useNow(30_000);

  return (
    <>
      <PageHeader
        title="Health"
        subtitle="Background jobs, the email outbox and the latest 50 failed emails. Email, photos and videos run every few minutes; the rest within 20 minutes."
        action={
          <Button variant="secondary" onClick={reload} loading={loading && !!data}>
            <RefreshIcon className="h-4 w-4" /> Refresh
          </Button>
        }
      />
      {error && <ErrorPanel error={error} onRetry={reload} className="mb-4" />}
      {!data && !error && <Loading />}
      {data && (
        <div className="space-y-4">
          <div className="grid gap-4 lg:grid-cols-[minmax(0,1.5fr)_minmax(0,1fr)]">
            <Panel title="Jobs" flush>
              {data.jobs.length === 0 ? (
                <EmptyState title="No job runs recorded yet" text="Jobs report here after their first run." />
              ) : (
                <TableScroll label="Jobs">
                  <table className="data-table min-w-[560px]">
                    <thead>
                      <tr>
                        <th>Job</th>
                        <th>State</th>
                        <th>Last success</th>
                        <th>Last started</th>
                        <th>Last error</th>
                      </tr>
                    </thead>
                    <tbody>
                      {data.jobs.map((j) => {
                        const h = jobHealth(j, now || undefined);
                        return (
                          <tr key={j.name}>
                            <td className="font-medium capitalize">{j.name}</td>
                            <td>
                              <StatusBadge tone={h.tone}>{h.label}</StatusBadge>
                            </td>
                            <td className="tnum">
                              {stamp(j.lastSucceededAt)}
                              {j.lastSucceededAt && now > 0 && <span className="block text-xs text-muted">{ago(j.lastSucceededAt, now)}</span>}
                            </td>
                            <td className="tnum">{stamp(j.lastStartedAt)}</td>
                            <td className="max-w-80">{j.lastError ? <Mono className="text-stop">{j.lastError}</Mono> : <span className="text-muted">—</span>}</td>
                          </tr>
                        );
                      })}
                    </tbody>
                  </table>
                </TableScroll>
              )}
            </Panel>
            <Panel title="Email outbox">
              <dl className="grid grid-cols-3 gap-3 text-sm">
                <OutboxFigure label="Pending" n={data.outbox.pending} tone={data.outbox.pending > 0 ? "text-wait" : undefined} />
                <OutboxFigure label="Sent" n={data.outbox.sent} />
                <OutboxFigure label="Failed" n={data.outbox.failed} tone={data.outbox.failed > 0 ? "text-stop" : undefined} />
              </dl>
            </Panel>
          </div>

          <Panel title="Failed emails" flush>
            {data.failures.length === 0 ? (
              <EmptyState title="No failed emails" text="Emails that fail after their retries are listed here." />
            ) : (
              <TableScroll label="Failed emails">
                <table className="data-table min-w-[760px]">
                  <thead>
                    <tr>
                      <th>Created</th>
                      <th>Template</th>
                      <th>Recipient</th>
                      <th className="num">Attempts</th>
                      <th>Error</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.failures.map((f) => (
                      <tr key={f.id}>
                        <td className="tnum">{stamp(f.createdAt)}</td>
                        <td>
                          <Mono>{f.templateKey}</Mono>
                        </td>
                        <td>{f.recipient}</td>
                        <td className="num">{num(f.attempts)}</td>
                        <td className="max-w-96">
                          <Mono className="text-stop">{f.error ?? "—"}</Mono>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </TableScroll>
            )}
          </Panel>
        </div>
      )}
    </>
  );
}

function OutboxFigure({ label, n, tone }: { label: string; n: number; tone?: string }) {
  return (
    <div>
      <dt className="text-muted">{label}</dt>
      <dd className={cx("tnum text-xl font-semibold", tone)}>{num(n)}</dd>
    </div>
  );
}
