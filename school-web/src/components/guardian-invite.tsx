"use client";

import { useState } from "react";
import { api } from "@/lib/api";
import { useAction } from "@/lib/mutate";
import type { GuardianInviteDto } from "@/lib/types";
import { CopyIcon } from "./icons";
import { Button, ErrorBox, WhatsAppLink, cx } from "./ui";

function CopyLink({ link }: { link: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <Button
      variant="secondary"
      onClick={async () => {
        try {
          await navigator.clipboard.writeText(link);
          setCopied(true);
          window.setTimeout(() => setCopied(false), 2000);
        } catch {
          window.prompt("Copy this link", link);
        }
      }}
    >
      <CopyIcon className="h-4 w-4" />
      {copied ? "Copied" : "Copy link"}
    </Button>
  );
}

/**
 * "Invite" for one guardian (`POST /api/school/guardians/{id}/invite`). The API emails guardians who have an address; the screen
 * shows the link and, when there's a phone number, a "Send on WhatsApp" button that opens the prepared message.
 */
export function GuardianInvite({
  guardianId,
  hasEmail,
  hasAccount,
  className,
  label = "Invite",
}: {
  guardianId: string;
  hasEmail: boolean;
  hasAccount: boolean;
  className?: string;
  label?: string;
}) {
  const [invite, setInvite] = useState<GuardianInviteDto | null>(null);
  const { busy, error, run } = useAction();

  async function send() {
    const res = await run(() => api<GuardianInviteDto>(`/api/school/guardians/${guardianId}/invite`, { method: "POST" }));
    if (res.ok) setInvite(res.data);
  }

  if (!invite) {
    return (
      <div className={cx("space-y-2", className)}>
        <Button variant="secondary" loading={busy} onClick={send}>
          {hasAccount ? "Send a new link" : label}
        </Button>
        <ErrorBox error={error} />
      </div>
    );
  }

  return (
    <div className={cx("space-y-2 rounded-control border border-rule bg-slate-50 p-3", className)} role="status">
      <p className="text-sm text-ink">
        {hasEmail ? "Invitation emailed. " : ""}The link works once, for 14 days.
      </p>
      <p className="text-xs break-all text-muted">{invite.link}</p>
      <div className="flex flex-wrap gap-2">
        {invite.whatsAppUrl ? <WhatsAppLink href={invite.whatsAppUrl} /> : <span className="self-center text-xs text-muted">No phone number for WhatsApp.</span>}
        <CopyLink link={invite.link} />
      </div>
    </div>
  );
}
