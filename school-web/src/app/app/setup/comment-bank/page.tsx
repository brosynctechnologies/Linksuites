"use client";

import { useState } from "react";
import { api } from "@/lib/api";
import { useAction } from "@/lib/mutate";
import { useCan } from "@/lib/roles";
import { useApi } from "@/lib/use-api";
import type { CommentBankDto, CommentScope } from "@/lib/types";
import { ApiErrorView, Button, ErrorBox, Input, Notice, PageLoader, Tabs, Textarea } from "@/components/ui";

const SCOPES: { value: CommentScope; label: string; match: string }[] = [
  { value: "Subject", label: "Subject", match: "Grade" },
  { value: "TeacherOverall", label: "Teacher overall", match: "Grade" },
  { value: "PrincipalOverall", label: "Principal overall", match: "Grade" },
  { value: "Developmental", label: "Developmental", match: "Scale label" },
];

interface Row {
  key: number;
  scope: CommentScope;
  matchKey: string;
  text: string;
}

let nextKey = 1;
const toRows = (data: CommentBankDto): Row[] =>
  data.entries.map((e) => ({ key: nextKey++, scope: e.scope, matchKey: e.matchKey, text: e.text }));

export default function CommentBankPage() {
  const [scope, setScope] = useState<CommentScope>("Subject");
  const [notice, setNotice] = useState<string | null>(null);
  const { data, error, reload } = useApi<CommentBankDto>("/api/school/comment-bank");
  return (
    <div className="space-y-4">
      <p className="max-w-prose text-sm text-slate-700">
        Comments offered when a result is written. Subject lines match a grade. Teacher and principal lines match the average&apos;s grade.
        Developmental lines match a scale label.
      </p>
      <Tabs label="Comment bank" items={SCOPES.map((s) => ({ value: s.value, label: s.label }))} value={scope} onChange={setScope} />
      {notice && <Notice tone="success">{notice}</Notice>}
      {error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && <PageLoader />}
      {data && (
        <Editor
          key={data.entries.map((e) => `${e.scope}:${e.order}:${e.matchKey}:${e.text}`).join("|")}
          scope={scope}
          data={data}
          onSaved={(message) => {
            setNotice(message);
            reload();
          }}
        />
      )}
    </div>
  );
}

function Editor({ scope, data, onSaved }: { scope: CommentScope; data: CommentBankDto; onSaved: (message: string) => void }) {
  const canManage = useCan("school.manage");
  const [rows, setRows] = useState<Row[]>(() => toRows(data));
  const { busy, error, run } = useAction();
  const meta = SCOPES.find((s) => s.value === scope) ?? SCOPES[0];
  const visible = rows.filter((r) => r.scope === scope);
  const initial = data.entries.map((e) => `${e.scope}\t${e.matchKey}\t${e.text}`).join("\n");
  const current = rows.map((r) => `${r.scope}\t${r.matchKey}\t${r.text}`).join("\n");
  const dirty = current !== initial;
  const ready = rows.length > 0 && rows.every((r) => r.matchKey.trim() && r.text.trim());

  async function save() {
    const order = new Map<CommentScope, number>();
    const body = {
      entries: rows.map((r) => {
        const n = order.get(r.scope) ?? 0;
        order.set(r.scope, n + 1);
        return { scope: r.scope, matchKey: r.matchKey.trim(), text: r.text.trim(), order: n };
      }),
    };
    if ((await run(() => api("/api/school/comment-bank", { method: "PUT", body }))).ok) onSaved("Comment bank saved.");
  }

  return (
    <div className="space-y-4">
      <div className="ruled-sheet">
        <ul className="ruled-rows">
          {visible.map((r) => (
            <li key={r.key} className="ruled-pad space-y-3 py-3">
              <div className="flex flex-wrap items-end gap-2">
                <div className="w-full min-w-0 sm:w-48">
                  <Input
                    label={meta.match}
                    maxLength={40}
                    value={r.matchKey}
                    disabled={!canManage}
                    onChange={(e) => setRows((rs) => rs.map((x) => (x.key === r.key ? { ...x, matchKey: e.target.value } : x)))}
                  />
                </div>
                {canManage && (
                  <Button
                    variant="ghost"
                    className="text-brick"
                    onClick={() => setRows((rs) => rs.filter((x) => x.key !== r.key))}
                    aria-label={`Remove ${r.matchKey || "comment"}`}
                  >
                    Remove
                  </Button>
                )}
              </div>
              <Textarea
                label="Comment"
                maxLength={500}
                className="w-full min-w-0"
                value={r.text}
                disabled={!canManage}
                onChange={(e) => setRows((rs) => rs.map((x) => (x.key === r.key ? { ...x, text: e.target.value } : x)))}
              />
            </li>
          ))}
        </ul>
      </div>
      {visible.length === 0 && <p className="text-sm text-muted">No comments in this section yet.</p>}
      {!ready && rows.length > 0 && <p className="text-sm text-brick">Fill in every row before saving. A blank row in another section counts too.</p>}
      {rows.length === 0 && <p className="text-sm text-brick">Add at least one comment.</p>}
      <ErrorBox error={error} />
      {canManage && (
        <div className="flex flex-wrap gap-2">
          <Button onClick={save} loading={busy} disabled={!dirty || !ready}>
            Save comment bank
          </Button>
          <Button variant="secondary" onClick={() => setRows((rs) => [...rs, { key: nextKey++, scope, matchKey: "", text: "" }])}>
            Add a comment
          </Button>
        </div>
      )}
    </div>
  );
}
