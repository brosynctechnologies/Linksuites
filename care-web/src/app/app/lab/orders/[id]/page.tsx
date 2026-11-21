"use client";

import { useParams } from "next/navigation";
import { useState, type FormEvent } from "react";
import { api, downloadFile } from "@/lib/api";
import { useCare } from "@/lib/care-me";
import { formatStamp } from "@/lib/format";
import { flagView, labStageLabel, previewFlag } from "@/lib/lab-pharmacy";
import { useAction } from "@/lib/mutate";
import { useApi } from "@/lib/use-api";
import type { LabOrderAnalyteDto, LabOrderDto, LabResultEntryDto, LabValueDto } from "@/lib/types/lab-pharmacy";
import {
  ApiErrorView,
  Badge,
  Button,
  Card,
  ErrorBox,
  Input,
  NoAccess,
  Notice,
  PageHeader,
  PageLoader,
  SectionTitle,
  Textarea,
  Wristband,
} from "@/components/ui";

/**
 * Wave 4: one lab order (`GET /api/care/lab/orders/{id}`, a logged read). Collect the sample (numbered), print its label, enter the result
 * per analyte against the patient's range (the flag shows as you type; the server sets it), or correct a result with a reason. A result
 * waits for a doctor's review before the patient's portal shows it.
 */
export default function LabOrderPage() {
  const { id } = useParams<{ id: string }>();
  const { access } = useCare();
  const order = useApi<LabOrderDto>(access.canWorkLab ? `/api/care/lab/orders/${id}` : null);
  const collect = useAction();
  const label = useAction();
  const [amending, setAmending] = useState(false);

  if (!access.canWorkLab) return <NoAccess text="Lab orders are for lab scientists, doctors and nurses." />;
  if (order.error) return <ApiErrorView error={order.error} onRetry={order.reload} />;
  if (!order.data) return <PageLoader />;
  const o = order.data;
  const current = o.values.filter((v) => !v.superseded);
  const earlier = o.values.filter((v) => v.superseded);

  return (
    <div className="space-y-5">
      <PageHeader
        back="/app/lab"
        title={o.testName ?? "Lab test"}
        subtitle={
          <span className="flex flex-wrap items-center gap-2">
            <Wristband number={o.patientNumber} />
            <span className="font-bold text-ink">{o.patientName}</span>
            <span>
              {o.sex}, {o.ageYears} years
            </span>
            <Badge tone={o.stage === "Done" ? "pass" : "scrub"}>{labStageLabel(o.stage)}</Badge>
          </span>
        }
      />

      <Card className="space-y-2 text-sm">
        <p>
          <span className="font-bold">Ordered:</span> {o.description} by {o.requestedByName}, {formatStamp(o.orderedAt)}
          {o.turnaroundHours !== null && <span className="text-muted"> (target {o.turnaroundHours} h)</span>}
        </p>
        {o.sampleType && (
          <p>
            <span className="font-bold">Sample:</span> {o.sampleType}
          </p>
        )}
        {o.sampleNumber ? (
          <div className="flex flex-wrap items-center gap-3">
            <p>
              <span className="font-bold">Sample number:</span> <span className="tnum">{o.sampleNumber}</span>, collected by {o.collectedByName} at{" "}
              {formatStamp(o.collectedAt)}
            </p>
            <Button
              variant="secondary"
              loading={label.busy}
              onClick={() => label.run(() => downloadFile(`/api/care/lab/orders/${o.id}/label.pdf`, `label-${o.sampleNumber}.pdf`))}
            >
              Print label
            </Button>
          </div>
        ) : (
          o.status === "Requested" && (
            <div className="flex flex-wrap items-center gap-3">
              <p className="text-muted">The sample hasn&apos;t been collected.</p>
              <Button
                loading={collect.busy}
                onClick={async () => {
                  const res = await collect.run(() => api(`/api/care/lab/orders/${o.id}/collect`, { method: "POST" }));
                  if (res.ok) order.reload();
                }}
              >
                Collect sample
              </Button>
            </div>
          )
        )}
        <ErrorBox error={collect.error ?? label.error} />
      </Card>

      {o.status === "Requested" && o.sampleNumber && o.labTestId && <ResultForm order={o} onSaved={order.reload} />}
      {o.status === "Requested" && !o.labTestId && (
        <Notice>This test isn&apos;t on the catalogue. Record its result as text or a file from the patient&apos;s record.</Notice>
      )}

      {o.status === "Resulted" && (
        <section className="space-y-3">
          <SectionTitle
            action={
              o.labTestId &&
              !amending && (
                <Button variant="secondary" onClick={() => setAmending(true)}>
                  Correct the result
                </Button>
              )
            }
          >
            Result
          </SectionTitle>
          {current.length > 0 ? <ValuesTable values={current} /> : <p className="text-sm whitespace-pre-line">{o.resultText}</p>}
          <p className="text-sm text-muted">
            Recorded by {o.resultedByName ?? "staff"}, {formatStamp(o.resultedAt)}.{" "}
            {o.reviewedAt ? `Reviewed ${formatStamp(o.reviewedAt)}.` : "Waiting for a doctor's review before the patient's portal shows it."}
          </p>
          {amending && (
            <ResultForm
              order={o}
              amend
              onCancel={() => setAmending(false)}
              onSaved={() => {
                setAmending(false);
                order.reload();
              }}
            />
          )}
        </section>
      )}

      {earlier.length > 0 && (
        <section className="space-y-3">
          <SectionTitle>Corrected values</SectionTitle>
          <ValuesTable values={earlier} />
        </section>
      )}
    </div>
  );
}

function FlagBadge({ flag }: { flag: LabValueDto["flag"] }) {
  const view = flagView(flag);
  return view ? <Badge tone={view.tone}>{view.label}</Badge> : null;
}

function ValuesTable({ values }: { values: LabValueDto[] }) {
  return (
    <div className="case-sheet">
      <ul className="case-rows">
        {values.map((v) => (
          <li key={v.id} className="case-pad grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-3 gap-y-1 py-3 md:grid-cols-[minmax(0,1fr)_10rem_8rem_9rem]">
            <span className="font-bold text-ink">{v.name}</span>
            <span className="tnum text-sm text-ink">
              {v.value} {v.unit}
            </span>
            <span className="tnum text-sm text-muted">{v.rangeText && `Ref ${v.rangeText}`}</span>
            <span>
              <FlagBadge flag={v.flag} />
            </span>
          </li>
        ))}
      </ul>
    </div>
  );
}

function rangeHint(a: LabOrderAnalyteDto): string | undefined {
  const parts = [a.rangeText && `Reference ${a.rangeText}${a.unit ? ` ${a.unit}` : ""}`];
  if (a.criticalLow !== null) parts.push(`critical at or below ${a.criticalLow}`);
  if (a.criticalHigh !== null) parts.push(`critical at or above ${a.criticalHigh}`);
  const text = parts.filter(Boolean).join("; ");
  return text || undefined;
}

/** Structured entry: one field per analyte, the patient's range beside it and the flag the value would get. */
function ResultForm({ order, amend, onSaved, onCancel }: { order: LabOrderDto; amend?: boolean; onSaved: () => void; onCancel?: () => void }) {
  const [values, setValues] = useState<Record<string, string>>(() =>
    Object.fromEntries(order.analytes.map((a) => [a.id, amend ? (order.values.find((v) => !v.superseded && v.analyteId === a.id)?.value ?? "") : ""])),
  );
  const [comment, setComment] = useState("");
  const [reason, setReason] = useState("");
  const { busy, error, run } = useAction();

  async function submit(e: FormEvent) {
    e.preventDefault();
    const body = {
      values: order.analytes.map((a) => ({ analyteId: a.id, value: values[a.id] ?? "" })),
      comment: comment.trim() || null,
      ...(amend ? { reason: reason.trim() } : {}),
    };
    const res = await run(() => api<LabResultEntryDto>(`/api/care/lab/orders/${order.id}/${amend ? "amend" : "results"}`, { method: "POST", body }));
    if (res.ok) onSaved();
  }

  return (
    <Card>
      <form onSubmit={submit} className="space-y-4" noValidate>
        <SectionTitle>{amend ? "Correct the result" : "Enter the result"}</SectionTitle>
        {order.analytes.map((a, i) => {
          const flag = flagView(previewFlag(values[a.id] ?? "", a));
          return (
            <div key={a.id} className="grid items-end gap-2 sm:grid-cols-[minmax(0,1fr)_9rem]">
              <Input
                label={`${a.name}${a.unit ? ` (${a.unit})` : ""}`}
                value={values[a.id] ?? ""}
                onChange={(e) => setValues((v) => ({ ...v, [a.id]: e.target.value }))}
                inputMode={a.rangeText || a.criticalLow !== null || a.criticalHigh !== null ? "decimal" : "text"}
                hint={rangeHint(a)}
                error={error?.field(`values[${i}].value`) ?? error?.field(`values[${i}].analyteId`)}
                className="tnum"
              />
              <span className="pb-2">{flag && <Badge tone={flag.tone}>{flag.label}</Badge>}</span>
            </div>
          );
        })}
        <Textarea label="Comment (optional)" value={comment} onChange={(e) => setComment(e.target.value)} error={error?.field("comment")} rows={2} />
        {amend && (
          <Input label="Why was the recorded result wrong?" required value={reason} onChange={(e) => setReason(e.target.value)} error={error?.field("reason")} />
        )}
        <ErrorBox error={error && !error.field("reason") && !error.field("comment") && !order.analytes.some((_, i) => error.field(`values[${i}].value`)) ? error : null} />
        <div className="flex justify-end gap-2">
          {onCancel && (
            <Button variant="secondary" onClick={onCancel}>
              Cancel
            </Button>
          )}
          <Button type="submit" loading={busy}>
            {amend ? "Save the correction" : "Save result"}
          </Button>
        </div>
      </form>
    </Card>
  );
}
