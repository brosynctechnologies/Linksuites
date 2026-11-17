"use client";

// Wave 1: the clinic logo (leadership, on the settings page) and each member's own signature (on the account page). Both are private
// images: the API resizes them, keeps them out of public storage and serves them only to signed-in members, so the preview is fetched with
// the session and shown from memory.

import { useEffect, useRef, useState } from "react";
import { api, apiBlob, type ApiError } from "@/lib/api";
import { useAction } from "@/lib/mutate";
import { Badge, Button, Card, ErrorBox, SectionTitle } from "./ui";

const IMAGE_TYPES = ["image/jpeg", "image/png", "image/webp"];
const MAX_IMAGE_BYTES = 5 * 1024 * 1024;

/** Checks a chosen file against the API's rules; returns the message to show, or null when it can be sent. */
function checkImage(file: File): string | null {
  if (!IMAGE_TYPES.includes(file.type)) return "Upload a JPEG, PNG or WebP image.";
  if (file.size > MAX_IMAGE_BYTES) return "Images must be 5 MB or smaller.";
  return null;
}

/** The stored image as an object URL (null while loading or when there is none), refetched when `version` changes. */
function usePrivateImage(path: string, present: boolean, version: number) {
  const [url, setUrl] = useState<string | null>(null);
  useEffect(() => {
    if (!present) return;
    let live = true;
    let made: string | null = null;
    apiBlob(path).then(
      (blob) => {
        if (!live) return;
        made = URL.createObjectURL(blob);
        setUrl(made);
      },
      () => live && setUrl(null),
    );
    return () => {
      live = false;
      if (made) URL.revokeObjectURL(made);
    };
  }, [path, present, version]);
  return present ? url : null;
}

function ImagePicker({ label, busy, onFile }: { label: string; busy: boolean; onFile: (file: File) => void }) {
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
        {label}
      </Button>
    </>
  );
}

/** Upload (PUT, multipart `file`) and remove (DELETE) one image, with the client-side checks first. */
function useImage(path: string, onChanged: (present: boolean) => void) {
  const upload = useAction();
  const remove = useAction();
  const [local, setLocal] = useState<string | null>(null);

  async function choose(file: File) {
    const problem = checkImage(file);
    setLocal(problem);
    if (problem) return;
    const fd = new FormData();
    fd.append("file", file);
    const res = await upload.run(() => api(path, { method: "PUT", body: fd }));
    if (res.ok) onChanged(true);
  }

  async function clear(question: string) {
    if (!window.confirm(question)) return;
    setLocal(null);
    const res = await remove.run(() => api(path, { method: "DELETE" }));
    if (res.ok) onChanged(false);
  }

  const error: ApiError | string | null = local ?? upload.error ?? remove.error ?? null;
  return { choose, clear, error, uploading: upload.busy, removing: remove.busy };
}

/** The clinic logo, printed on prescriptions, discharge summaries and receipts. The medical director and admins change it. */
export function LogoCard({ hasLogo, clinicName, canEdit }: { hasLogo: boolean; clinicName: string; canEdit: boolean }) {
  const [present, setPresent] = useState(hasLogo);
  const [version, setVersion] = useState(0);
  const url = usePrivateImage("/api/care/settings/logo", present, version);
  const logo = useImage("/api/care/settings/logo", (now) => {
    setPresent(now);
    setVersion((v) => v + 1);
  });

  return (
    <Card>
      <SectionTitle>Logo</SectionTitle>
      <div className="flex flex-wrap items-start gap-4">
        <div className="flex h-24 w-24 shrink-0 items-center justify-center rounded-control border border-rule bg-slate-50 p-2">
          {url ? <img src={url} alt={`${clinicName} logo`} className="max-h-full max-w-full object-contain" /> : <span className="text-center text-xs text-muted">{present ? "Loading…" : "No logo yet"}</span>}
        </div>
        <div className="min-w-0 flex-1 space-y-3">
          <p className="max-w-prose text-sm text-slate-700">
            Printed at the top of prescriptions, discharge summaries and receipts. JPEG, PNG or WebP, up to 5 MB; it is resized to fit 600 × 600 pixels.
          </p>
          {canEdit && (
            <div className="flex flex-wrap gap-2">
              <ImagePicker label={present ? "Change logo" : "Upload a logo"} busy={logo.uploading} onFile={logo.choose} />
              {present && (
                <Button variant="ghost" loading={logo.removing} onClick={() => logo.clear("Remove the logo? Prescriptions, discharge summaries and receipts will print without it.")}>
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

/** Your own signature, printed above your name on what you sign: prescriptions and discharge summaries, and receipts for money you take. */
export function SignatureCard({ hasSignature, canEdit }: { hasSignature: boolean; canEdit: boolean }) {
  const [present, setPresent] = useState(hasSignature);
  const [version, setVersion] = useState(0);
  const url = usePrivateImage("/api/care/staff/me/signature", present, version);
  const signature = useImage("/api/care/staff/me/signature", (now) => {
    setPresent(now);
    setVersion((v) => v + 1);
  });

  return (
    <Card>
      <SectionTitle action={present ? <Badge tone="pass">On file</Badge> : <Badge>None yet</Badge>}>Your signature</SectionTitle>
      <div className="space-y-3">
        <p className="max-w-prose text-sm text-slate-700">
          Printed above your name on prescriptions and discharge summaries you sign, and on receipts for payments you take. Sign on white paper,
          photograph or scan it, and crop close to the signature. JPEG, PNG or WebP, up to 5 MB; it is resized to fit 600 × 200 pixels. Only you see it here.
        </p>
        {url && (
          <div className="inline-flex rounded-control border border-rule bg-white p-2">
            <img src={url} alt="Your signature" className="max-h-16 max-w-xs object-contain" />
          </div>
        )}
        {canEdit && (
          <div className="flex flex-wrap gap-2">
            <ImagePicker label={present ? "Replace signature" : "Upload a signature"} busy={signature.uploading} onFile={signature.choose} />
            {present && (
              <Button variant="ghost" loading={signature.removing} onClick={() => signature.clear("Remove your signature? What you sign will print without it.")}>
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
