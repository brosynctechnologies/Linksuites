"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { STUDENT_LIST_TAKE } from "@/lib/config";
import { useCan } from "@/lib/roles";
import { qs, useApi } from "@/lib/use-api";
import { useDebounced } from "@/lib/use-debounced";
import type { ClassDto, StudentDto } from "@/lib/types";
import { ApiErrorView, Badge, Button, EmptyState, ListCapNotice, PageLoader, RuledSheet, SearchField, Select, linkClass, ruledRowClass } from "@/components/ui";
import { STATUS_TONE, StudentForm } from "./student-form";


export default function StudentsPage() {
  const canManage = useCan("school.manage");
  const router = useRouter();
  const [q, setQ] = useState("");
  const [classId, setClassId] = useState("");
  const [status, setStatus] = useState("Active");
  const [adding, setAdding] = useState(false);
  const search = useDebounced(q.trim(), 300);
  const classes = useApi<ClassDto[]>("/api/school/classes");
  const { data, error, loading, reload } = useApi<StudentDto[]>(`/api/school/students${qs({ q: search, classId, status, take: STUDENT_LIST_TAKE })}`);

  const filtered = !!(search || classId || status !== "Active");
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end gap-2">
        <SearchField id="student-search" label="Search students" placeholder="Name or admission number" value={q} onChange={setQ} className="min-w-52 flex-1" />
        <div className="w-40">
          <Select label="Class" srLabel value={classId} onChange={(e) => setClassId(e.target.value)}>
            <option value="">All classes</option>
            {classes.data?.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </Select>
        </div>
        <div className="w-36">
          <Select label="Status" srLabel value={status} onChange={(e) => setStatus(e.target.value)}>
            <option value="Active">Active</option>
            <option value="Graduated">Graduated</option>
            <option value="Withdrawn">Withdrawn</option>
            <option value="Transferred">Transferred</option>
            <option value="all">Everyone</option>
          </Select>
        </div>
        {canManage && <Button onClick={() => setAdding(true)}>Add a student</Button>}
      </div>

      {error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && <PageLoader />}
      {data && data.length === 0 && !filtered && (
        <EmptyState
          title="No students yet"
          text="Import your students from an Excel or CSV file, or add them one at a time."
          action={
            canManage ? (
              <Link href="/app/students/import" className={linkClass}>
                Import students
              </Link>
            ) : undefined
          }
        />
      )}
      {data && data.length === 0 && filtered && <p className="text-sm text-muted">No students match. Try another name, class or status.</p>}
      {data && data.length > 0 && (
        <>
          {data.length >= STUDENT_LIST_TAKE ? (
            <ListCapNotice count={data.length} cap={STUDENT_LIST_TAKE} />
          ) : (
            <p className="tnum text-sm text-muted" aria-live="polite">
              {data.length.toLocaleString("en-NG")} {data.length === 1 ? "student" : "students"}
            </p>
          )}
          <RuledSheet
            label="Students"
            busy={loading}
            template="9rem minmax(0,1fr) 7rem 7rem"
            head={[{ label: "Admission no." }, { label: "Name" }, { label: "Class" }, { label: "Status" }]}
          >
            {data.map((s) => (
              <li key={s.id}>
                <Link href={`/app/students/${s.id}`} className={`${ruledRowClass} hover:bg-slate-50`}>
                  <span className="tnum order-2 text-sm text-muted md:order-none">{s.admissionNo}</span>
                  <span className="order-1 font-bold text-ink md:order-none">{s.fullName}</span>
                  <span className="order-3 text-sm text-slate-700 md:order-none">{s.className ?? "No class"}</span>
                  <span className="order-4 md:order-none">
                    <Badge tone={STATUS_TONE[s.status]}>{s.status}</Badge>
                  </span>
                </Link>
              </li>
            ))}
          </RuledSheet>
        </>
      )}
      {adding && classes.data && (
        <StudentForm
          classes={classes.data}
          onClose={() => setAdding(false)}
          onSaved={(s) => {
            setAdding(false);
            router.push(`/app/students/${s.id}`);
          }}
        />
      )}
    </div>
  );
}
