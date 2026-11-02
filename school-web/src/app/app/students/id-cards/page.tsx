"use client";

import { useState } from "react";
import { downloadFile } from "@/lib/api";
import { useAction } from "@/lib/mutate";
import { STUDENT_LIST_TAKE } from "@/lib/config";
import { useCan } from "@/lib/roles";
import { qs, useApi } from "@/lib/use-api";
import type { ClassDto, StudentDto } from "@/lib/types";
import { DownloadIcon } from "@/components/icons";
import { ApiErrorView, Button, Checkbox, ErrorBox, ListCapNotice, NoAccess, PageLoader, RuledSheet, Select } from "@/components/ui";

/** ID cards as a PDF, 8 to an A4 page: a whole class, or the students ticked. */
export default function IdCardsPage() {
  const canPrint = useCan("idcards.print");
  const classes = useApi<ClassDto[]>(canPrint ? "/api/school/classes" : null);
  const [classId, setClassId] = useState("");
  const chosen = classId || classes.data?.[0]?.id || "";
  const students = useApi<StudentDto[]>(chosen ? `/api/school/students${qs({ classId: chosen, status: "Active", take: STUDENT_LIST_TAKE })}` : null);
  const [skipped, setSkipped] = useState<Set<string>>(new Set());
  const { busy, error, run } = useAction();

  if (!canPrint) return <NoAccess />;

  const list = students.data ?? [];
  const picked = list.filter((s) => !skipped.has(s.id));
  const className = classes.data?.find((c) => c.id === chosen)?.name ?? "class";

  async function download() {
    const query = picked.length === list.length ? qs({ classId: chosen }) : qs({ studentIds: picked.map((s) => s.id).join(",") });
    await run(() => downloadFile(`/api/school/students/id-cards.pdf${query}`, `id-cards-${className.replace(/\s+/g, "-").toLowerCase()}.pdf`));
  }

  return (
    <div className="space-y-4">
      <p className="max-w-prose text-sm text-slate-700">
        Each card shows the photo, name, admission number, class and session, 8 cards to an A4 page. Print on card and cut along the lines.
      </p>
      {classes.error && <ApiErrorView error={classes.error} onRetry={classes.reload} />}
      {!classes.data && !classes.error && <PageLoader />}
      {classes.data && classes.data.length === 0 && <p className="text-sm text-muted">No classes yet.</p>}
      {classes.data && classes.data.length > 0 && (
        <>
          <div className="flex flex-wrap items-end gap-2">
            <div className="w-48">
              <Select
                label="Class"
                value={chosen}
                onChange={(e) => {
                  setClassId(e.target.value);
                  setSkipped(new Set());
                }}
              >
                {classes.data.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </Select>
            </div>
            <Button onClick={download} loading={busy} disabled={picked.length === 0}>
              <DownloadIcon className="h-4 w-4" />
              Download {picked.length === list.length ? `${className} cards` : `${picked.length} cards`}
            </Button>
          </div>
          <ErrorBox error={error} />
          <ListCapNotice count={list.length} cap={STUDENT_LIST_TAKE} />
          {students.error && <ApiErrorView error={students.error} onRetry={students.reload} />}
          {!students.data && !students.error && <PageLoader />}
          {students.data && list.length === 0 && <p className="text-sm text-muted">No active students in {className}.</p>}
          {list.length > 0 && (
            <RuledSheet label={`Students in ${className}`}>
              {list.map((s) => (
                <li key={s.id} className="ruled-pad py-2">
                  <Checkbox
                    checked={!skipped.has(s.id)}
                    onChange={(on) =>
                      setSkipped((prev) => {
                        const next = new Set(prev);
                        if (on) next.delete(s.id);
                        else next.add(s.id);
                        return next;
                      })
                    }
                    label={
                      <>
                        <span className="font-bold text-ink">{s.fullName}</span> <span className="tnum text-muted">{s.admissionNo}</span>
                        {!s.photoUrl && <span className="text-muted">, no photo</span>}
                      </>
                    }
                  />
                </li>
              ))}
            </RuledSheet>
          )}
        </>
      )}
    </div>
  );
}
