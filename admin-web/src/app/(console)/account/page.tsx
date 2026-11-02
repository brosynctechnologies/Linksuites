"use client";

import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { RequireArea, useAdmin } from "@/components/console";
import { NewPasswordFields, newPasswordProblems } from "@/components/new-password";
import { Button, ErrorPanel, Facts, Input, Mono, PageHeader, Panel } from "@/components/ui";
import { api, toApiError, type ApiError } from "@/lib/api";
import { stamp } from "@/lib/format";
import { ROLE_LABEL } from "@/lib/roles";
import { endSession, PASSWORD_CHANGED_NOTE } from "@/lib/session";

export default function AccountPage() {
  return (
    <RequireArea area="account">
      <Account />
    </RequireArea>
  );
}

function Account() {
  const { user, expiresAt } = useAdmin();
  return (
    <>
      <PageHeader title="Your account" subtitle="Your admin sign-in. Your name and email are the ones on your customer account." />
      <div className="grid gap-4 lg:grid-cols-2">
        <Panel title="Signed in as">
          <Facts
            items={[
              { label: "Name", value: user.fullName },
              { label: "Email", value: user.email },
              { label: "Role", value: user.platformRole ? ROLE_LABEL[user.platformRole] : "—" },
              { label: "Session ends", value: stamp(expiresAt) },
              { label: "User id", value: <Mono>{user.id}</Mono> },
            ]}
          />
        </Panel>
        <ChangePassword email={user.email} />
      </div>
    </>
  );
}

/** Changing the password ends every admin-panel session for this account, this one included, so the admin signs in again. */
function ChangePassword({ email }: { email: string }) {
  const router = useRouter();
  const [current, setCurrent] = useState("");
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [touched, setTouched] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setTouched(true);
    const local = newPasswordProblems(password, confirm);
    if (!current || !password || !confirm || local.newPassword || local.confirm) return;
    setBusy(true);
    setError(null);
    try {
      await api("/api/auth/password/change", { method: "POST", body: { currentPassword: current, newPassword: password } });
      endSession(PASSWORD_CHANGED_NOTE);
      router.replace("/login");
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  }

  const otherProblem = error && !error.field("currentPassword") && !error.field("newPassword");

  return (
    <Panel title="Change password">
      <form onSubmit={submit} className="space-y-4" noValidate>
        <input type="email" autoComplete="username" value={email} readOnly hidden />
        <Input
          label="Current password"
          type="password"
          autoComplete="current-password"
          value={current}
          onChange={(e) => setCurrent(e.target.value)}
          error={error?.field("currentPassword")}
          required
        />
        <NewPasswordFields password={password} confirm={confirm} onPassword={setPassword} onConfirm={setConfirm} touched={touched} error={error} />
        {otherProblem && <ErrorPanel error={error} />}
        <p className="text-sm text-muted">This signs you out of the console everywhere, including here. Sign in again with the new password.</p>
        <div className="flex justify-end">
          <Button type="submit" loading={busy} disabled={!current || !password || !confirm}>
            Change password
          </Button>
        </div>
      </form>
    </Panel>
  );
}
