"use client";

import { useState, type FormEvent } from "react";
import { api, asList, toApiError, type ApiError } from "@/lib/api";
import { patchAuth, useAuth } from "@/lib/auth";
import { localPath } from "@/lib/format";
import { useCan } from "@/lib/permissions";
import { useApi } from "@/lib/use-api";
import type { AgencyKind, AgencyProfileDto, AgentProfileDto } from "@/lib/types";
import { AccountActivity } from "@/components/account-activity";
import { ChangePassword } from "@/components/change-password";
import { Avatar, Rating, VerifiedSeal } from "@/components/badges";
import { ApiErrorView, Button, Card, ErrorBox, Input, Notice, PageHeader, PageLoader, SectionTitle, Select, Textarea, linkClass } from "@/components/ui";

export default function ProfilePage() {
  const agency = useApi<AgencyProfileDto>("/api/property/profile/agency");
  const me = useApi<AgentProfileDto>("/api/property/profile/agent");
  const team = useApi<AgentProfileDto[]>("/api/property/profile/agents");
  const canEditAgency = useCan("agency.edit");
  const canEditAgent = useCan("leads.manage");

  return (
    <div className="space-y-5">
      <PageHeader title="Profile" subtitle="What seekers see on your agency and agent pages." />
      <Card>
        <SectionTitle
          action={
            agency.data?.publicUrl ? (
              <a href={localPath(agency.data.publicUrl) ?? agency.data.publicUrl} className={linkClass} target="_blank" rel="noopener noreferrer">
                View public page
              </a>
            ) : undefined
          }
        >
          Agency
        </SectionTitle>
        {agency.data ? (
          <AgencyForm key={agency.data.id} agency={agency.data} canEdit={canEditAgency} onSaved={agency.setData} />
        ) : agency.error ? (
          <ApiErrorView error={agency.error} onRetry={agency.reload} />
        ) : (
          <PageLoader />
        )}
      </Card>
      <Card>
        <SectionTitle
          action={
            me.data ? (
              <a href={`/agents/${me.data.slug}`} className={linkClass} target="_blank" rel="noopener noreferrer">
                View my page
              </a>
            ) : undefined
          }
        >
          My agent profile
        </SectionTitle>
        {me.data ? (
          <AgentForm key={me.data.id} agent={me.data} canEdit={canEditAgent} onSaved={me.setData} />
        ) : me.error ? (
          <ApiErrorView error={me.error} onRetry={me.reload} />
        ) : (
          <PageLoader />
        )}
      </Card>
      {asList(team.data).length > 1 && (
        <Card>
          <SectionTitle>Agents in this agency</SectionTitle>
          <ul className="divide-y divide-line-soft">
            {asList(team.data).map((a) => (
              <li key={a.id} className="flex items-center gap-3 py-2.5">
                <Avatar src={a.photoUrl} name={a.displayName} size={40} />
                <span className="min-w-0 flex-1">
                  <span className="flex flex-wrap items-center gap-1.5 font-medium text-asphalt">
                    {a.displayName} <VerifiedSeal verified={a.verified} />
                  </span>
                  <span className="block text-xs text-muted">{a.phone ?? "No phone"}</span>
                </span>
                <Rating average={a.ratingAverage} count={a.ratingCount} />
              </li>
            ))}
          </ul>
        </Card>
      )}
      <ChangePassword />
      <AccountActivity />
    </div>
  );
}

function AgencyForm({ agency, canEdit, onSaved }: { agency: AgencyProfileDto; canEdit: boolean; onSaved: (a: AgencyProfileDto) => void }) {
  const auth = useAuth();
  const [form, setForm] = useState({
    name: agency.name,
    slug: agency.slug,
    kind: agency.kind,
    phone: agency.phone ?? "",
    email: agency.email ?? "",
    address: agency.address ?? "",
    bio: agency.bio ?? "",
  });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [saved, setSaved] = useState(false);
  const set = (k: keyof typeof form) => (e: { target: { value: string } }) => {
    setSaved(false);
    setForm((f) => ({ ...f, [k]: e.target.value }));
  };

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const res = await api<AgencyProfileDto>("/api/property/profile/agency", {
        method: "PUT",
        body: {
          name: form.name.trim(),
          slug: form.slug.trim(),
          kind: form.kind,
          phone: form.phone.trim() || null,
          email: form.email.trim() || null,
          address: form.address.trim() || null,
          bio: form.bio.trim() || null,
        },
      });
      onSaved(res);
      if (auth?.tenant) patchAuth({ tenant: { ...auth.tenant, name: res.name, slug: res.slug } });
      setSaved(true);
    } catch (err) {
      setError(toApiError(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-4">
      <div className="flex items-center gap-2 text-sm text-slate-700">
        {agency.verified ? <VerifiedSeal verified /> : <span className="text-muted">Not verified yet.</span>}
      </div>
      <fieldset disabled={!canEdit} className="space-y-4">
        <div className="grid gap-4 sm:grid-cols-2">
          <Input label="Name" required value={form.name} onChange={set("name")} error={error?.field("name")} />
          <Select label="Kind" value={form.kind} onChange={(e) => setForm((f) => ({ ...f, kind: e.target.value as AgencyKind }))} error={error?.field("kind")}>
            <option value="Agency">Agency</option>
            <option value="Agent">Solo agent</option>
            <option value="Landlord">Landlord</option>
          </Select>
          <Input label="Web address" required value={form.slug} onChange={set("slug")} hint={`propertylink/agencies/${form.slug || "your-name"}`} error={error?.field("slug")} />
          <Input label="Office phone" type="tel" value={form.phone} onChange={set("phone")} error={error?.field("phone")} />
          <Input label="Email" type="email" value={form.email} onChange={set("email")} error={error?.field("email")} />
          <Input label="Office address" value={form.address} onChange={set("address")} error={error?.field("address")} />
        </div>
        <Textarea label="About the agency" rows={4} value={form.bio} onChange={set("bio")} error={error?.field("bio")} />
      </fieldset>
      {!canEdit && <p className="text-sm text-muted">Only the owner or an admin can change the agency profile.</p>}
      {error && !error.errors && <ErrorBox error={error} />}
      {saved && <Notice tone="success">Agency profile saved.</Notice>}
      {canEdit && (
        <Button type="submit" loading={busy}>
          Save agency
        </Button>
      )}
    </form>
  );
}

function AgentForm({ agent, canEdit, onSaved }: { agent: AgentProfileDto; canEdit: boolean; onSaved: (a: AgentProfileDto) => void }) {
  const [form, setForm] = useState({
    displayName: agent.displayName,
    bio: agent.bio ?? "",
    phone: agent.phone ?? "",
    whatsApp: agent.whatsApp ?? "",
    licenceNo: agent.licenceNo ?? "",
  });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [saved, setSaved] = useState(false);
  const set = (k: keyof typeof form) => (e: { target: { value: string } }) => {
    setSaved(false);
    setForm((f) => ({ ...f, [k]: e.target.value }));
  };

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      onSaved(
        await api<AgentProfileDto>("/api/property/profile/agent", {
          method: "PUT",
          body: {
            displayName: form.displayName.trim(),
            bio: form.bio.trim() || null,
            phone: form.phone.trim() || null,
            whatsApp: form.whatsApp.trim() || null,
            licenceNo: form.licenceNo.trim() || null,
          },
        }),
      );
      setSaved(true);
    } catch (err) {
      setError(toApiError(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-4">
      <div className="flex items-center gap-3">
        <Avatar src={agent.photoUrl} name={agent.displayName} size={48} />
        <div className="text-sm">
          <p className="flex items-center gap-1.5 font-semibold text-asphalt">
            {agent.displayName} <VerifiedSeal verified={agent.verified} />
          </p>
          <Rating average={agent.ratingAverage} count={agent.ratingCount} />
        </div>
      </div>
      <fieldset disabled={!canEdit} className="space-y-4">
        <div className="grid gap-4 sm:grid-cols-2">
          <Input label="Display name" required value={form.displayName} onChange={set("displayName")} error={error?.field("displayName")} />
          <Input label="Licence number" value={form.licenceNo} onChange={set("licenceNo")} error={error?.field("licenceNo")} />
          <Input label="Phone (for calls)" type="tel" value={form.phone} onChange={set("phone")} error={error?.field("phone")} hint="Shown on your listings with a Call button." />
          <Input label="WhatsApp number" type="tel" value={form.whatsApp} onChange={set("whatsApp")} error={error?.field("whatsApp")} hint="Seekers tap Chat on WhatsApp to reach this number." />
        </div>
        <Textarea label="About you" rows={3} value={form.bio} onChange={set("bio")} error={error?.field("bio")} />
      </fieldset>
      {error && !error.errors && <ErrorBox error={error} />}
      {saved && <Notice tone="success">Agent profile saved.</Notice>}
      {canEdit && (
        <Button type="submit" loading={busy}>
          Save my profile
        </Button>
      )}
    </form>
  );
}
