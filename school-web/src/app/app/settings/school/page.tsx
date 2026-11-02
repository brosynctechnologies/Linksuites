"use client";

import { useRef, useState, type FormEvent } from "react";
import { UploadIcon } from "@/components/icons";
import { ApiErrorView, Badge, Button, Card, ErrorBox, Input, NoAccess, Notice, PageHeader, PageLoader, SectionTitle, Textarea } from "@/components/ui";
import { api, type ApiError } from "@/lib/api";
import { useAction } from "@/lib/mutate";
import { can, useCan, useRole } from "@/lib/roles";
import { useApi } from "@/lib/use-api";
import type { SaveSchoolProfileRequest, SchoolBrandingDto } from "@/lib/types";

// The API's limits (SchoolBrandingService and ImageUploads), checked here first so a wrong or oversized file is caught before it uploads.
const MAX_NAME = 120;
const MAX_MOTTO = 200;
const MAX_ADDRESS = 300;
const MAX_IMAGE_BYTES = 5 * 1024 * 1024;
const IMAGE_TYPES = ["image/jpeg", "image/png", "image/webp"];
const PATH = "/api/school/settings/profile";

/** The school's name, motto, address, logo and the principal's signature, as printed on report cards and receipts. Owner and Admin. */
export default function SchoolSettingsPage() {
  const allowed = can(useRole(), "school.manage");
  const canEdit = useCan("school.manage"); // false in a support view, which is read only
  const profile = useApi<SchoolBrandingDto>(allowed ? PATH : null);

  if (!allowed) return <NoAccess text="Only the proprietor and the principal can change the school's details." />;
  return (
    <div className="space-y-5">
      <PageHeader title="School settings" subtitle="Your school's details, logo and the principal's signature. Report cards and receipts print them." />
      {profile.error && <ApiErrorView error={profile.error} onRetry={profile.reload} />}
      {!profile.data && !profile.error && <PageLoader />}
      {profile.data && (
        <>
          <ProfileForm profile={profile.data} canEdit={canEdit} onSaved={profile.setData} />
          <div className="grid gap-5 lg:grid-cols-2">
            <LogoCard profile={profile.data} canEdit={canEdit} onSaved={profile.setData} />
            <SignatureCard profile={profile.data} canEdit={canEdit} onSaved={profile.setData} />
          </div>
        </>
      )}
    </div>
  );
}

// ---------- Name, motto, address ----------

function ProfileForm({ profile, canEdit, onSaved }: { profile: SchoolBrandingDto; canEdit: boolean; onSaved: (p: SchoolBrandingDto) => void }) {
  const [name, setName] = useState(profile.name);
  const [motto, setMotto] = useState(profile.motto ?? "");
  const [address, setAddress] = useState(profile.address ?? "");
  const [saved, setSaved] = useState(false);
  const { busy, error, run } = useAction();
  const nameError = name.trim().length === 0 ? "Enter the school's name." : undefined;

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (nameError) return;
    // Every field is sent: an empty motto or address clears it.
    const body: SaveSchoolProfileRequest = { name: name.trim(), motto: motto.trim(), address: address.trim() };
    const res = await run(() => api<SchoolBrandingDto>(PATH, { method: "PUT", body }));
    if (res.ok) {
      onSaved(res.data);
      setName(res.data.name);
      setMotto(res.data.motto ?? "");
      setAddress(res.data.address ?? "");
      setSaved(true);
    }
  }

  const change = (set: (v: string) => void) => (v: string) => {
    set(v);
    setSaved(false);
  };

  return (
    <Card>
      <SectionTitle>School details</SectionTitle>
      <form onSubmit={submit} className="max-w-2xl space-y-4" noValidate>
        <Input
          label="School name"
          required
          maxLength={MAX_NAME}
          disabled={!canEdit}
          value={name}
          onChange={(e) => change(setName)(e.target.value)}
          error={error?.field("name") ?? nameError}
          hint={`Up to ${MAX_NAME} characters. Shown on report cards, receipts, ID cards and your sign-in page.`}
        />
        <Input
          label="Motto"
          maxLength={MAX_MOTTO}
          disabled={!canEdit}
          value={motto}
          onChange={(e) => change(setMotto)(e.target.value)}
          error={error?.field("motto")}
          hint={`Optional, up to ${MAX_MOTTO} characters. Leave it empty to remove it.`}
        />
        <Textarea
          label="Address"
          maxLength={MAX_ADDRESS}
          disabled={!canEdit}
          value={address}
          onChange={(e) => change(setAddress)(e.target.value)}
          error={error?.field("address")}
          hint={`Optional, up to ${MAX_ADDRESS} characters. Printed under the name on report cards and receipts.`}
        />
        {error && !error.field("name") && !error.field("motto") && !error.field("address") && <ErrorBox error={error} />}
        {saved && <Notice tone="success">Saved. New report cards and receipts use these details.</Notice>}
        {canEdit && (
          <Button type="submit" loading={busy} disabled={!!nameError}>
            Save details
          </Button>
        )}
      </form>
    </Card>
  );
}

// ---------- Logo and signature ----------

/** Checks a chosen file against the API's rules; returns the message to show, or null when it can be sent. */
function checkImage(file: File): string | null {
  if (!IMAGE_TYPES.includes(file.type)) return "Upload a JPEG, PNG or WebP image.";
  if (file.size > MAX_IMAGE_BYTES) return "Images must be 5 MB or smaller.";
  return null;
}

function sendImage(path: string, file: File) {
  const fd = new FormData();
  fd.append("file", file);
  return api<SchoolBrandingDto>(path, { method: "PUT", body: fd });
}

function ImagePicker({
  label,
  busy,
  onFile,
}: {
  label: string;
  busy: boolean;
  onFile: (file: File) => void;
}) {
  const input = useRef<HTMLInputElement>(null);
  return (
    <>
      <input
        ref={input}
        type="file"
        accept={IMAGE_TYPES.join(",")}
        className="sr-only"
        aria-label={label}
        onChange={(e) => {
          const file = e.target.files?.[0];
          e.target.value = "";
          if (file) onFile(file);
        }}
      />
      <Button variant="secondary" loading={busy} onClick={() => input.current?.click()}>
        <UploadIcon className="h-4 w-4" /> {label}
      </Button>
    </>
  );
}

function useImageAction(path: string, onSaved: (p: SchoolBrandingDto) => void) {
  const upload = useAction();
  const remove = useAction();
  const [local, setLocal] = useState<string | null>(null);

  async function choose(file: File) {
    const problem = checkImage(file);
    setLocal(problem);
    if (problem) return;
    const res = await upload.run(() => sendImage(path, file));
    if (res.ok) onSaved(res.data);
  }

  async function clear(profile: SchoolBrandingDto, patch: Partial<SchoolBrandingDto>, question: string) {
    if (!window.confirm(question)) return;
    setLocal(null);
    const res = await remove.run(() => api(path, { method: "DELETE" }));
    if (res.ok) onSaved({ ...profile, ...patch });
  }

  const error: ApiError | string | null = local ?? upload.error ?? remove.error ?? null;
  return { choose, clear, error, uploading: upload.busy, removing: remove.busy };
}

function LogoCard({ profile, canEdit, onSaved }: { profile: SchoolBrandingDto; canEdit: boolean; onSaved: (p: SchoolBrandingDto) => void }) {
  const logo = useImageAction("/api/school/settings/logo", onSaved);
  return (
    <Card>
      <SectionTitle>Logo</SectionTitle>
      <div className="flex flex-wrap items-start gap-4">
        <div className="flex h-28 w-28 shrink-0 items-center justify-center overflow-hidden rounded-panel border border-rule bg-white p-2">
          {profile.logoUrl ? (
            <img src={profile.logoUrl} alt={`${profile.name} logo`} className="max-h-full max-w-full object-contain" />
          ) : (
            <span className="text-center text-xs text-muted">No logo yet</span>
          )}
        </div>
        <div className="min-w-0 flex-1 space-y-2 text-sm">
          <p className="text-slate-700">
            Printed at the top of report cards and receipts, and shown on your school&apos;s sign-in page. JPEG, PNG or WebP, up to 5 MB. Large
            images are resized to fit 600 × 600 pixels.
          </p>
          {canEdit && (
            <div className="flex flex-wrap gap-2">
              <ImagePicker label={profile.logoUrl ? "Change logo" : "Upload a logo"} busy={logo.uploading} onFile={logo.choose} />
              {profile.logoUrl && (
                <Button
                  variant="ghost"
                  loading={logo.removing}
                  onClick={() => logo.clear(profile, { logoUrl: null }, "Remove the logo? Report cards and receipts will print without it.")}
                >
                  Remove
                </Button>
              )}
            </div>
          )}
          <ErrorBox error={logo.error} />
        </div>
      </div>
    </Card>
  );
}

function SignatureCard({ profile, canEdit, onSaved }: { profile: SchoolBrandingDto; canEdit: boolean; onSaved: (p: SchoolBrandingDto) => void }) {
  const signature = useImageAction("/api/school/settings/signature", onSaved);
  return (
    <Card>
      <SectionTitle>Principal&apos;s signature</SectionTitle>
      <div className="space-y-2 text-sm">
        <p className="flex flex-wrap items-center gap-2">
          {profile.hasSignature ? <Badge tone="pass">On file</Badge> : <Badge tone="neutral">None yet</Badge>}
          <span className="text-slate-700">
            {profile.hasSignature ? "Printed above the principal's remark on every report card." : "Report cards print without a signature."}
          </span>
        </p>
        <p className="text-slate-700">
          Sign on white paper, photograph or scan it, and crop close to the signature. JPEG, PNG or WebP, up to 5 MB; it is resized to fit 600 × 200
          pixels. The signature is kept private: it is only used on report cards, so it isn&apos;t shown here.
        </p>
        {canEdit && (
          <div className="flex flex-wrap gap-2">
            <ImagePicker label={profile.hasSignature ? "Replace signature" : "Upload a signature"} busy={signature.uploading} onFile={signature.choose} />
            {profile.hasSignature && (
              <Button
                variant="ghost"
                loading={signature.removing}
                onClick={() => signature.clear(profile, { hasSignature: false }, "Remove the signature? Report cards will print without it.")}
              >
                Remove
              </Button>
            )}
          </div>
        )}
        <ErrorBox error={signature.error} />
      </div>
    </Card>
  );
}
