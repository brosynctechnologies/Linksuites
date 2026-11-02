"use client";

import Link from "next/link";
import { useState } from "react";
import { downloadFile } from "@/lib/api";
import { useAction } from "@/lib/mutate";
import { qs, useApi } from "@/lib/use-api";
import type { ClassDto } from "@/lib/types";
import type { DebtorWhatsAppDto, DebtorsDto } from "@/lib/types/fees";
import {
  ApiErrorView,
  Badge,
  Button,
  EmptyState,
  ErrorBox,
  Modal,
  PageLoader,
  RuledSheet,
  Select,
  WhatsAppLink,
  ruledRowClass,
} from "@/components/ui";
import { TermSelect, money, useTermChoice } from "../fees-shared";

const OVERDUE = [
  { value: "0", label: "Everyone with a balance" },
  { value: "1", label: "Overdue" },
  { value: "7", label: "Overdue 7+ days" },
  { value: "14", label: "Overdue 14+ days" },
  { value: "30", label: "Overdue 30+ days" },
  { value: "60", label: "Overdue 60+ days" },
];

/** Students who still owe for the term, worst first, with the CSV export and the WhatsApp reminder list. */
export default function DebtorsPage() {
  const t = useTermChoice();
  const [classId, setClassId] = useState("");
  const [minDays, setMinDays] = useState("0");
  const [whatsApp, setWhatsApp] = useState(false);
  const classes = useApi<ClassDto[]>(t.term ? `/api/school/classes${qs({ sessionId: t.term.sessionId })}` : null);
  const filters = { termId: t.termId, classId, minDaysOverdue: minDays === "0" ? "" : minDays };
  const list = useApi<DebtorsDto>(t.termId ? `/api/school/debtors${qs(filters)}` : null);
  const csv = useAction();

  if (t.error) return <ApiErrorView error={t.error} onRetry={t.reload} />;
  if (!t.loaded) return <PageLoader />;
  if (t.terms.length === 0) return <EmptyState title="No terms yet" text="Add the session and its terms on the setup page first." />;

  const data = list.data;
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end gap-2">
        <TermSelect terms={t.terms} value={t.termId} onChange={(id) => (t.setTermId(id), setClassId(""))} />
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
        <div className="w-52">
          <Select label="How overdue" srLabel value={minDays} onChange={(e) => setMinDays(e.target.value)}>
            {OVERDUE.map((o) => (
              <option key={o.value} value={o.value}>
                {o.label}
              </option>
            ))}
          </Select>
        </div>
        <span className="flex flex-1 flex-wrap justify-end gap-2">
          <Button
            variant="secondary"
            loading={csv.busy}
            disabled={!data || data.count === 0}
            onClick={() => csv.run(() => downloadFile(`/api/school/debtors.csv${qs(filters)}`, "debtors.csv"))}
          >
            Download CSV
          </Button>
          <Button variant="secondary" disabled={!data || data.count === 0} onClick={() => setWhatsApp(true)}>
            WhatsApp list
          </Button>
        </span>
      </div>
      <ErrorBox error={csv.error} />

      {list.error && <ApiErrorView error={list.error} onRetry={list.reload} />}
      {!data && !list.error && <PageLoader />}
      {data && data.count === 0 && (
        <EmptyState
          title={minDays === "0" && !classId ? "No debtors this term" : "No debtors match"}
          text={minDays === "0" && !classId ? "Every invoice for this term is paid or cancelled, or no invoices have been generated yet." : "Try another class or a shorter overdue period."}
        />
      )}
      {data && data.count > 0 && (
        <>
          <p className="tnum text-sm text-muted" aria-live="polite">
            {data.count.toLocaleString("en-NG")} {data.count === 1 ? "student owes" : "students owe"} {money(data.totalBalance)}
          </p>
          <RuledSheet
            label="Debtors"
            busy={list.loading}
            template="minmax(0,1fr) 7rem minmax(0,12rem) 8rem 7rem"
            head={[{ label: "Student" }, { label: "Class" }, { label: "Primary guardian" }, { label: "Balance", align: "right" }, { label: "Overdue" }]}
          >
            {data.debtors.map((d) => (
              <li key={d.invoiceId} className={ruledRowClass}>
                <span className="min-w-0">
                  <Link href={`/app/fees/invoices/${d.invoiceId}`} className="font-bold text-ink hover:text-blazer hover:underline">
                    {d.studentName}
                  </Link>
                  <span className="tnum block text-xs text-muted">
                    {d.invoiceNumber} · {d.admissionNo} · paid {money(d.paid)} of {money(d.total)}
                  </span>
                </span>
                <span className="col-start-1 text-sm text-slate-700 md:col-start-auto">{d.className}</span>
                <span className="col-start-1 min-w-0 text-sm md:col-start-auto">
                  {d.primaryGuardian ?? <span className="text-muted">No guardian</span>}
                  {d.guardianPhone && (
                    <a href={`tel:${d.guardianPhone}`} className="tnum block text-xs text-blazer hover:underline">
                      {d.guardianPhone}
                    </a>
                  )}
                </span>
                <span className="tnum col-start-2 row-start-1 text-right text-sm font-semibold md:col-start-auto md:row-start-auto">{money(d.balance)}</span>
                <span className="col-start-2 row-start-2 justify-self-end md:col-start-auto md:row-start-auto md:justify-self-start">
                  {d.daysOverdue > 0 ? (
                    <Badge tone={d.daysOverdue >= 30 ? "brick" : "crest"}>{d.daysOverdue === 1 ? "1 day" : `${d.daysOverdue} days`}</Badge>
                  ) : (
                    <Badge>Not due yet</Badge>
                  )}
                </span>
              </li>
            ))}
          </RuledSheet>
        </>
      )}

      {whatsApp && <WhatsAppList termId={t.termId} minDays={filters.minDaysOverdue} allClasses={!!classId} onClose={() => setWhatsApp(false)} />}
    </div>
  );
}

function WhatsAppList({ termId, minDays, allClasses, onClose }: { termId: string; minDays: string; allClasses: boolean; onClose: () => void }) {
  const list = useApi<DebtorWhatsAppDto[]>(`/api/school/debtors/whatsapp${qs({ termId, minDaysOverdue: minDays })}`);
  return (
    <Modal open wide title="Remind on WhatsApp" onClose={onClose}>
      <div className="space-y-4">
        <p className="text-sm text-slate-700">
          Each link opens WhatsApp with a reminder of the balance, ready to send to the primary guardian. Guardians with no phone number aren&apos;t
          listed.{allClasses ? " This list covers every class, not only the one you filtered by." : ""}
        </p>
        {list.error && <ApiErrorView error={list.error} onRetry={list.reload} />}
        {!list.data && !list.error && <PageLoader />}
        {list.data && list.data.length === 0 && <p className="text-sm text-muted">None of these debtors has a guardian with a phone number.</p>}
        {list.data && list.data.length > 0 && (
          <RuledSheet label="WhatsApp reminders" template="minmax(0,1fr) 8rem 12rem">
            {list.data.map((d, i) => (
              <li key={`${d.whatsAppUrl}-${i}`} className={ruledRowClass}>
                <span className="min-w-0">
                  <span className="font-bold text-ink">{d.studentName}</span>
                  <span className="block text-xs text-muted">To {d.guardianName}</span>
                </span>
                <span className="tnum col-start-2 row-start-1 text-right text-sm font-semibold md:col-start-auto md:row-start-auto">{money(d.balance)}</span>
                <span className="col-span-2 md:col-span-1 md:justify-self-end">
                  <WhatsAppLink href={d.whatsAppUrl} />
                </span>
              </li>
            ))}
          </RuledSheet>
        )}
        <div className="flex justify-end">
          <Button variant="secondary" onClick={onClose}>
            Done
          </Button>
        </div>
      </div>
    </Modal>
  );
}
