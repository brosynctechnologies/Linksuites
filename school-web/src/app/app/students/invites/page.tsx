"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { STUDENT_LIST_TAKE } from "@/lib/config";
import { plural } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { useCan } from "@/lib/roles";
import { qs, useApi } from "@/lib/use-api";
import type { ClassDto, StudentDto } from "@/lib/types";
import { GuardianInvite } from "@/components/guardian-invite";
import { ApiErrorView, Badge, Button, Card, ErrorBox, ListCapNotice, NoAccess, Notice, PageLoader, RuledSheet, SectionTitle, Select, linkClass } from "@/components/ui";

interface Row {
  guardianId: string;
  fullName: string;
  phone: string | null;
  hasAccount: boolean;
  children: { id: string; name: string }[];
}

/** Each student's guardians (the list endpoint leaves them out), a few requests at a time. */
async function guardiansOf(students: StudentDto[], signal: AbortSignal): Promise<StudentDto[]> {
  const out: StudentDto[] = [];
  const queue = [...students];
  async function worker() {
    for (let s = queue.shift(); s; s = queue.shift()) {
      out.push(await api<StudentDto>(`/api/school/students/${s.id}`, { signal }));
    }
  }
  await Promise.all(Array.from({ length: Math.min(5, queue.length) }, worker));
  return out;
}

export default function InvitesPage() {
  const canManage = useCan("school.manage");
  const classes = useApi<ClassDto[]>(canManage ? "/api/school/classes" : null);
  const all = useAction();
  const [invited, setInvited] = useState<number | null>(null);

  if (!canManage) return <NoAccess />;

  return (
    <div className="space-y-5">
      <Card>
        <SectionTitle>Email every parent</SectionTitle>
        <p className="-mt-1 mb-4 max-w-prose text-sm text-slate-700">
          Sends an invitation to every parent or guardian with an email address who doesn&apos;t have a parent account or an unused invitation yet. Each link
          works once, for 14 days.
        </p>
        <ErrorBox error={all.error} />
        {invited !== null && (
          <Notice tone="success" className="mb-3">
            {invited === 0 ? "Everyone with an email address already has an account or a current invitation." : `Invited ${plural(invited, "parent")} by email.`}
          </Notice>
        )}
        <Button
          loading={all.busy}
          onClick={async () => {
            const res = await all.run(() => api<{ invited: number }>("/api/school/guardians/invite-all", { method: "POST" }));
            if (res.ok) setInvited(res.data.invited);
          }}
        >
          Invite all by email
        </Button>
      </Card>

      <section aria-labelledby="whatsapp" className="space-y-3">
        <h2 id="whatsapp" className="slab text-xl font-bold text-ink">
          Parents without email
        </h2>
        <p className="max-w-prose text-sm text-slate-700">
          Send these parents their link on WhatsApp, one class at a time. Each button opens WhatsApp with the message ready; you press send.
        </p>
        {classes.error && <ApiErrorView error={classes.error} onRetry={classes.reload} />}
        {!classes.data && !classes.error && <PageLoader />}
        {classes.data && classes.data.length === 0 && (
          <p className="text-sm text-muted">
            No classes yet.{" "}
            <Link href="/app/setup/classes" className={linkClass}>
              Add classes
            </Link>
          </p>
        )}
        {classes.data && classes.data.length > 0 && <ByClass classes={classes.data} />}
      </section>
    </div>
  );
}

function ByClass({ classes }: { classes: ClassDto[] }) {
  const [classId, setClassId] = useState(classes[0].id);
  const students = useApi<StudentDto[]>(`/api/school/students${qs({ classId, status: "Active", take: STUDENT_LIST_TAKE })}`);
  const [rows, setRows] = useState<{ key: string; rows: Row[] } | null>(null);
  const [error, setError] = useState<{ key: string; error: ApiError } | null>(null);

  useEffect(() => {
    if (!students.data) return;
    const key = classId;
    const ctrl = new AbortController();
    guardiansOf(students.data, ctrl.signal).then(
      (detailed) => {
        const byId = new Map<string, Row>();
        for (const s of detailed) {
          for (const g of s.guardians ?? []) {
            if (g.email) continue;
            const row = byId.get(g.guardianId) ?? { guardianId: g.guardianId, fullName: g.fullName, phone: g.phone, hasAccount: g.hasAccount, children: [] };
            row.children.push({ id: s.id, name: s.fullName });
            byId.set(g.guardianId, row);
          }
        }
        setRows({ key, rows: [...byId.values()].sort((a, b) => a.fullName.localeCompare(b.fullName)) });
      },
      (e) => {
        if (!ctrl.signal.aborted) setError({ key, error: toApiError(e) });
      },
    );
    return () => ctrl.abort();
  }, [students.data, classId]);

  const current = rows?.key === classId ? rows.rows : null;
  const currentError = error?.key === classId ? error.error : (students.error ?? null);
  return (
    <div className="space-y-3">
      <div className="max-w-xs">
        <Select label="Class" value={classId} onChange={(e) => setClassId(e.target.value)}>
          {classes.map((c) => (
            <option key={c.id} value={c.id}>
              {c.name}
            </option>
          ))}
        </Select>
      </div>
      {currentError && <ErrorBox error={currentError} />}
      {students.data && <ListCapNotice count={students.data.length} cap={STUDENT_LIST_TAKE} />}
      {!current && !currentError && <PageLoader label="Finding parents without email…" />}
      {current && current.length === 0 && <p className="text-sm text-muted">Every parent in this class has an email address, or there are no parents on record yet.</p>}
      {current && current.length > 0 && (
        <RuledSheet label="Parents without email">
          {current.map((g) => (
            <li key={g.guardianId} className="ruled-pad space-y-2 py-3">
              <div className="flex flex-wrap items-center gap-2">
                <span className="font-bold text-ink">{g.fullName}</span>
                <span className="tnum text-sm text-muted">{g.phone ?? "No phone"}</span>
                {g.hasAccount && <Badge tone="pass">Has a parent account</Badge>}
              </div>
              <p className="text-sm text-slate-700">
                Parent of{" "}
                {g.children.map((c, i) => (
                  <span key={c.id}>
                    {i > 0 && ", "}
                    <Link href={`/app/students/${c.id}`} className={linkClass}>
                      {c.name}
                    </Link>
                  </span>
                ))}
              </p>
              {g.phone ? (
                <GuardianInvite guardianId={g.guardianId} hasEmail={false} hasAccount={g.hasAccount} label="Get WhatsApp link" />
              ) : (
                <p className="text-xs text-muted">Add a phone number or email on the student&apos;s page to invite this parent.</p>
              )}
            </li>
          ))}
        </RuledSheet>
      )}
    </div>
  );
}
