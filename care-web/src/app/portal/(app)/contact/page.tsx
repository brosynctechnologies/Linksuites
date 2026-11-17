"use client";

import { useState, type FormEvent } from "react";
import { PortalError } from "@/components/portal-parts";
import { Button, Card, ErrorBox, Input, Notice, PageHeader, PageLoader, Textarea } from "@/components/ui";
import { api } from "@/lib/api";
import { useAction } from "@/lib/mutate";
import { usePatientApi, usePortal } from "@/lib/portal";
import { useApi } from "@/lib/use-api";
import type { PortalContactDto } from "@/lib/types/portal";

/**
 * The patient's phone, email and address at the clinic, read with `GET .../contact` (Wave 1: no longer the whole record, so editing them
 * isn't a record read). `PUT .../contact` replaces all three (a field left empty is cleared), so the form always sends all three.
 */
export default function PortalContactPage() {
  const { patient } = usePortal();
  const contact = useApi<PortalContactDto>(usePatientApi("/contact"));
  return (
    <div className="max-w-xl">
      <PageHeader title="Contact details" subtitle={patient ? `What ${patient.clinicName} uses to reach ${patient.relationship === "Self" ? "you" : patient.firstName}.` : undefined} />
      {contact.error && !contact.data && <PortalError error={contact.error} onRetry={contact.reload} />}
      {!contact.data && !contact.error && <PageLoader />}
      {contact.data && patient && <ContactForm key={patient.id} patientId={patient.id} initial={contact.data} />}
    </div>
  );
}

function ContactForm({ patientId, initial }: { patientId: string; initial: PortalContactDto }) {
  const [form, setForm] = useState({ phone: initial.phone ?? "", email: initial.email ?? "", address: initial.address ?? "" });
  const [saved, setSaved] = useState(false);
  const { busy, error, run } = useAction();
  const set = (k: keyof typeof form) => (e: { target: { value: string } }) => {
    setSaved(false);
    setForm((f) => ({ ...f, [k]: e.target.value }));
  };

  async function submit(e: FormEvent) {
    e.preventDefault();
    setSaved(false);
    // All three, every time: the API replaces them together.
    const body = { phone: form.phone.trim() || null, email: form.email.trim() || null, address: form.address.trim() || null };
    const res = await run(() => api<PortalContactDto>(`/api/care-portal/patients/${patientId}/contact`, { method: "PUT", body }));
    if (res.ok) {
      setForm({ phone: res.data.phone ?? "", email: res.data.email ?? "", address: res.data.address ?? "" });
      setSaved(true);
    }
  }

  const fieldError = error && (error.field("phone") || error.field("email") || error.field("address"));
  return (
    <Card>
      <form onSubmit={submit} className="space-y-4" noValidate>
        <Input label="Phone" type="tel" autoComplete="tel" value={form.phone} onChange={set("phone")} error={error?.field("phone")} />
        <Input label="Email" type="email" autoComplete="email" value={form.email} onChange={set("email")} error={error?.field("email")} />
        <Textarea label="Address" autoComplete="street-address" maxLength={300} value={form.address} onChange={set("address")} error={error?.field("address")} />
        <p className="text-xs text-muted">A field you leave empty is removed from the clinic&apos;s file. Your name and other details can only be changed at the clinic.</p>
        <ErrorBox error={error && !fieldError ? error : null} />
        {saved && <Notice tone="success">Saved. The clinic now has these details.</Notice>}
        <Button type="submit" loading={busy}>
          Save contact details
        </Button>
      </form>
    </Card>
  );
}
