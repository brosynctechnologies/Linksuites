"use client";

import Link from "next/link";
import { useState, type FormEvent } from "react";
import { api } from "@/lib/api";
import { formatDay } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { useCan, useRole } from "@/lib/roles";
import { qs, useApi } from "@/lib/use-api";
import type { ExamPeriodDto, ExamPeriodKind, SaveExamPeriodRequest } from "@/lib/types/academic";
import { ApiErrorView, Badge, Button, EmptyState, ErrorBox, Input, PageHeader, PageLoader, RuledSheet, Select } from "@/components/ui";
import { AcademicNoAccess, TermPicker, useSessionTerms } from "../scores/academic-shared";

const KINDS: { value: ExamPeriodKind; label: string }[] = [
  { value: "Examination", label: "Examination" },
  { value: "Test", label: "Test" },
];

function kindLabel(kind: string): string {
  return KINDS.find((k) => k.value === kind)?.label ?? kind;
}

export default function ExamsPage() {
  const role = useRole();
  const canManage = useCan("school.manage");
  const terms = useSessionTerms();
  const [termPick, setTermPick] = useState("");
  const termId = termPick || terms.current?.id || "";

  if (role === "Accountant") return <AcademicNoAccess what="The exam timetable" />;

  return (
    <div className="min-w-0 space-y-4">
      <PageHeader
        title="Exams"
        subtitle={
          canManage
            ? "Create the exam period, add each sitting, then publish. Teachers see a sitting once it is published."
            : "Published sittings for subjects you teach."
        }
      />
      {terms.error && <ApiErrorView error={terms.error} onRetry={terms.reload} />}
      {!terms.loaded && !terms.error && <PageLoader />}
      {terms.loaded && terms.terms.length === 0 && <EmptyState title="No terms yet" text="Add a session and a term in Setup before setting exams." />}
      {terms.loaded && terms.terms.length > 0 && (
        <>
          <div className="w-full min-w-0 sm:w-44">
            <TermPicker terms={terms.terms} value={termId} onChange={setTermPick} />
          </div>
          {termId && <PeriodList key={termId} termId={termId} canManage={canManage} />}
        </>
      )}
    </div>
  );
}

function PeriodList({ termId, canManage }: { termId: string; canManage: boolean }) {
  const list = useApi<ExamPeriodDto[]>(`/api/school/exam-periods${qs({ termId })}`);
  const [open, setOpen] = useState(false);
  if (list.error) return <ApiErrorView error={list.error} onRetry={list.reload} />;
  if (!list.data) return <PageLoader label="Opening exam periods…" />;

  return (
    <div className="min-w-0 space-y-4">
      {list.data.length === 0 && (
        <EmptyState
          title={canManage ? "No exam periods this term" : "No published exams"}
          text={canManage ? "Name the period and the dates it runs, then add sittings." : "When the principal publishes a sitting for a subject you teach, it shows here."}
        />
      )}
      {list.data.length > 0 && (
        <RuledSheet label="Exam periods" busy={list.loading}>
          {list.data.map((period) => (
            <li key={period.id}>
              <Link href={`/app/exams/${period.id}`} className="ruled-pad flex min-w-0 flex-wrap items-center justify-between gap-2 py-3 hover:bg-slate-50">
                <span className="min-w-0">
                  <span className="block font-bold break-words text-ink">{period.name}</span>
                  <span className="tnum block text-xs text-muted">
                    {formatDay(period.start)} – {formatDay(period.end)}
                  </span>
                </span>
                <span className="flex flex-wrap gap-1.5">
                  <Badge tone="blazer">{kindLabel(period.kind)}</Badge>
                  <Badge tone={period.publishedAt ? "pass" : "neutral"}>{period.publishedAt ? "Published" : "Not published"}</Badge>
                </span>
              </Link>
            </li>
          ))}
        </RuledSheet>
      )}
      {canManage && (
        <div>
          {open ? (
            <CreatePeriod termId={termId} onClose={() => setOpen(false)} onCreated={() => list.reload()} />
          ) : (
            <Button onClick={() => setOpen(true)}>Create an exam period</Button>
          )}
        </div>
      )}
    </div>
  );
}

function CreatePeriod({ termId, onClose, onCreated }: { termId: string; onClose: () => void; onCreated: () => void }) {
  const [name, setName] = useState("");
  const [kind, setKind] = useState<ExamPeriodKind>("Examination");
  const [start, setStart] = useState("");
  const [end, setEnd] = useState("");
  const create = useAction();

  async function submit(e: FormEvent) {
    e.preventDefault();
    const body: SaveExamPeriodRequest = { termId, name: name.trim(), kind, start, end };
    if ((await create.run(() => api<ExamPeriodDto>("/api/school/exam-periods", { method: "POST", body }))).ok) {
      onCreated();
      onClose();
    }
  }

  return (
    <form className="w-full min-w-0 space-y-3 rounded-panel border border-rule bg-surface p-4" onSubmit={submit}>
      <ErrorBox error={create.error} />
      <Input label="Name" required maxLength={120} value={name} onChange={(e) => setName(e.target.value)} />
      <Select label="Kind" value={kind} onChange={(e) => setKind(e.target.value as ExamPeriodKind)}>
        {KINDS.map((k) => (
          <option key={k.value} value={k.value}>
            {k.label}
          </option>
        ))}
      </Select>
      <div className="grid w-full min-w-0 gap-3 sm:grid-cols-2">
        <Input label="Starts" type="date" required value={start} onChange={(e) => setStart(e.target.value)} />
        <Input label="Ends" type="date" required value={end} onChange={(e) => setEnd(e.target.value)} />
      </div>
      <div className="flex flex-wrap gap-2">
        <Button type="submit" loading={create.busy}>
          Create period
        </Button>
        <Button type="button" variant="secondary" onClick={onClose} disabled={create.busy}>
          Cancel
        </Button>
      </div>
    </form>
  );
}
