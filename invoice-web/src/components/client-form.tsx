"use client";

import { useState, type FormEvent } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import type { ClientDto, SaveClientRequest } from "@/lib/types";
import { Button, ErrorBox, Input, Modal, Textarea } from "./ui";

type Form = Record<keyof SaveClientRequest, string>;

function toForm(c?: Partial<SaveClientRequest> | null): Form {
  return {
    name: c?.name ?? "",
    company: c?.company ?? "",
    email: c?.email ?? "",
    phone: c?.phone ?? "",
    address: c?.address ?? "",
    taxId: c?.taxId ?? "",
    notes: c?.notes ?? "",
  };
}

function toRequest(f: Form): SaveClientRequest {
  const n = (s: string) => (s.trim() ? s.trim() : null);
  return { name: f.name.trim(), company: n(f.company), email: n(f.email), phone: n(f.phone), address: n(f.address), taxId: n(f.taxId), notes: n(f.notes) };
}

/** Create or edit a client. Calls `onSaved` with the server's ClientDto. */
export function ClientForm({
  client,
  onSaved,
  onCancel,
  submitLabel,
  initialName,
}: {
  client?: ClientDto | null;
  onSaved: (c: ClientDto) => void;
  onCancel?: () => void;
  submitLabel?: string;
  initialName?: string;
}) {
  const [f, setF] = useState<Form>(() => (client ? toForm(client) : toForm({ name: initialName ?? "" })));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const set = (k: keyof Form) => (e: { target: { value: string } }) => setF((s) => ({ ...s, [k]: e.target.value }));

  async function submit(e: FormEvent) {
    e.preventDefault();
    e.stopPropagation();
    if (!f.name.trim()) {
      setError(toApiError(new Error("Client name is required.")));
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const saved = client
        ? await api<ClientDto>(`/api/clients/${client.id}`, { method: "PUT", body: toRequest(f) })
        : await api<ClientDto>("/api/clients", { method: "POST", body: toRequest(f) });
      onSaved(saved);
    } catch (err) {
      setError(toApiError(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-3" noValidate>
      <div className="grid gap-3 sm:grid-cols-2">
        <Input label="Name" required value={f.name} onChange={set("name")} error={error?.field("name")} autoFocus={!client} />
        <Input label="Company" value={f.company} onChange={set("company")} />
        <Input label="Email" type="email" value={f.email} onChange={set("email")} error={error?.field("email")} hint="Invoices are emailed here." />
        <Input label="Phone" type="tel" value={f.phone} onChange={set("phone")} />
      </div>
      <Textarea label="Address" rows={2} value={f.address} onChange={set("address")} />
      <Input label="Tax ID" value={f.taxId} onChange={set("taxId")} />
      <Textarea label="Private notes" rows={2} value={f.notes} onChange={set("notes")} hint="Only you can see these." />
      <ErrorBox error={error} />
      <div className="flex justify-end gap-2">
        {onCancel && (
          <Button variant="secondary" onClick={onCancel}>
            Cancel
          </Button>
        )}
        <Button type="submit" loading={busy}>
          {submitLabel ?? (client ? "Save client" : "Add client")}
        </Button>
      </div>
    </form>
  );
}

export function ClientModal({
  open,
  onClose,
  onSaved,
  initialName,
}: {
  open: boolean;
  onClose: () => void;
  onSaved: (c: ClientDto) => void;
  initialName?: string;
}) {
  return (
    <Modal open={open} title="Add a client" onClose={onClose} wide>
      {open && (
        <ClientForm
          onCancel={onClose}
          initialName={initialName}
          onSaved={(c) => {
            onSaved(c);
            onClose();
          }}
        />
      )}
    </Modal>
  );
}
