"use client";

import { useState, type FormEvent } from "react";
import { ChangePassword } from "@/components/change-password";
import { Button, Card, ErrorBox, Input, Notice, PageHeader, PageLoader, SectionTitle } from "@/components/ui";
import { api } from "@/lib/api";
import { getAuth, patchAuth, useAuth } from "@/lib/auth";
import { useAction } from "@/lib/mutate";
import { useApi } from "@/lib/use-api";
import { useChild } from "../_shared";

/** `GET /api/parent/profile`. `phone` is stored as +234…; null until the parent adds one. */
interface ParentProfileDto {
  fullName: string;
  email: string;
  phone: string | null;
}

export default function ParentAccountPage() {
  const { list, child } = useChild();
  const profile = useApi<ParentProfileDto>("/api/parent/profile");
  return (
    <div className="space-y-5">
      <PageHeader title="Account" />
      {profile.error && <ErrorBox error={profile.error} onRetry={profile.reload} />}
      {profile.loading && !profile.data && <PageLoader />}
      {profile.data && <Profile profile={profile.data} onSaved={profile.reload} />}
      <Card>
        <SectionTitle>Children</SectionTitle>
        {list.length === 0 ? (
          <p className="text-sm text-muted">No children are linked to this account. Ask the school office to send you an invite.</p>
        ) : (
          <ul className="space-y-2 text-sm">
            {list.map((c) => (
              <li key={c.studentId} className={c.studentId === child?.studentId ? "font-bold text-ink" : "text-ink"}>
                {c.name}
                {c.className ? ` · ${c.className}` : ""}
                <span className="block text-xs font-medium text-muted">{c.schoolName}</span>
              </li>
            ))}
          </ul>
        )}
      </Card>
      <ChangePassword />
    </div>
  );
}

function Profile({ profile, onSaved }: { profile: ParentProfileDto; onSaved: () => void }) {
  const support = !!useAuth()?.support;
  return (
    <Card>
      <SectionTitle>You</SectionTitle>
      <dl className="mb-5 grid gap-x-6 gap-y-2 text-sm sm:grid-cols-[auto_minmax(0,1fr)]">
        <dt className="text-muted">Name</dt>
        <dd className="font-bold text-ink">{profile.fullName}</dd>
        <dt className="text-muted">Email</dt>
        <dd className="break-all text-ink">{profile.email}</dd>
        <dt className="text-muted">Phone</dt>
        <dd className="text-ink">{profile.phone ?? "Not added yet"}</dd>
      </dl>
      {!support && (
        <div className="grid gap-6 md:grid-cols-2">
          <PhoneForm current={profile.phone} onSaved={onSaved} />
          <EmailForm current={profile.email} onSaved={onSaved} />
        </div>
      )}
    </Card>
  );
}

function PhoneForm({ current, onSaved }: { current: string | null; onSaved: () => void }) {
  const [phone, setPhone] = useState(current ?? "");
  const [saved, setSaved] = useState(false);
  const save = useAction();

  async function submit(e: FormEvent) {
    e.preventDefault();
    setSaved(false);
    const outcome = await save.run(() => api<ParentProfileDto>("/api/parent/profile/phone", { method: "PUT", body: { phone } }));
    if (outcome.ok) {
      setPhone(outcome.data.phone ?? "");
      setSaved(true);
      onSaved();
    }
  }

  const fieldError = save.error?.field("phone");
  return (
    <form onSubmit={submit} className="max-w-sm space-y-3">
      <h3 className="text-sm font-bold text-ink">Phone number</h3>
      <Input
        label="Phone"
        type="tel"
        inputMode="tel"
        autoComplete="tel"
        required
        hint="Your children's schools use it to reach you."
        error={fieldError}
        value={phone}
        onChange={(e) => setPhone(e.target.value)}
      />
      {save.error && !fieldError && <ErrorBox error={save.error} />}
      {saved && <Notice tone="success">Phone number saved.</Notice>}
      <Button type="submit" variant="secondary" loading={save.busy}>
        Save phone number
      </Button>
    </form>
  );
}

function EmailForm({ current, onSaved }: { current: string; onSaved: () => void }) {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [code, setCode] = useState("");
  const [sentTo, setSentTo] = useState<string | null>(null);
  const [changed, setChanged] = useState(false);
  const send = useAction();
  const confirm = useAction();

  async function sendCode(e: FormEvent) {
    e.preventDefault();
    setChanged(false);
    const outcome = await send.run(() => api("/api/parent/profile/email", { method: "POST", body: { email, currentPassword: password } }));
    if (outcome.ok) {
      setSentTo(email.trim().toLowerCase());
      setCode("");
      setPassword("");
    }
  }

  async function confirmCode(e: FormEvent) {
    e.preventDefault();
    if (!sentTo) return;
    const outcome = await confirm.run(() =>
      api<ParentProfileDto>("/api/parent/profile/email/confirm", {
        method: "POST",
        // This device stays signed in; every other session is signed out.
        body: { email: sentTo, code, refreshToken: getAuth()?.refreshToken || null },
      }),
    );
    if (outcome.ok) {
      const user = getAuth()?.user;
      if (user) patchAuth({ user: { ...user, email: outcome.data.email } });
      setSentTo(null);
      setEmail("");
      setCode("");
      setChanged(true);
      onSaved();
    }
  }

  if (sentTo) {
    const codeError = confirm.error?.field("code");
    return (
      <form onSubmit={confirmCode} className="max-w-sm space-y-3">
        <h3 className="text-sm font-bold text-ink">Email address</h3>
        <p className="text-sm text-muted">
          We sent a 6-digit code to <span className="break-all font-bold text-ink">{sentTo}</span>. Enter it to make this your email address.
          Until then, you keep using {current}.
        </p>
        <Input
          label="Code"
          inputMode="numeric"
          autoComplete="one-time-code"
          pattern="[0-9]{6}"
          maxLength={6}
          required
          error={codeError}
          value={code}
          onChange={(e) => setCode(e.target.value.replace(/\D/g, ""))}
        />
        {confirm.error && !codeError && <ErrorBox error={confirm.error} />}
        <div className="flex flex-wrap gap-2">
          <Button type="submit" loading={confirm.busy}>
            Change email
          </Button>
          <Button
            type="button"
            variant="secondary"
            onClick={() => {
              setSentTo(null);
              confirm.setError(null);
            }}
          >
            Use a different address
          </Button>
        </div>
      </form>
    );
  }

  const emailError = send.error?.field("email");
  const passwordError = send.error?.field("currentPassword");
  return (
    <form onSubmit={sendCode} className="max-w-sm space-y-3">
      <h3 className="text-sm font-bold text-ink">Email address</h3>
      <Input
        label="New email address"
        type="email"
        autoComplete="email"
        required
        hint="We'll send a code to the new address to check it's yours."
        error={emailError}
        value={email}
        onChange={(e) => setEmail(e.target.value)}
      />
      <Input
        label="Your current password"
        type="password"
        autoComplete="current-password"
        required
        hint="To make sure it's you."
        error={passwordError}
        value={password}
        onChange={(e) => setPassword(e.target.value)}
      />
      {send.error && !emailError && !passwordError && <ErrorBox error={send.error} />}
      {changed && (
        <Notice tone="success">Email address changed. Sign in with the new address from now on; your other devices have been signed out.</Notice>
      )}
      <Button type="submit" variant="secondary" loading={send.busy}>
        Send code
      </Button>
    </form>
  );
}
