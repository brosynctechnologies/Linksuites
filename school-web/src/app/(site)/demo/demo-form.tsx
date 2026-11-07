"use client";

import { useRef, useState, type FormEvent } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { Button, ErrorBox, Input, Notice, useFocusFirstInvalid } from "@/components/ui";

const EMPTY = { schoolName: "", contactName: "", email: "", phone: "", students: "" };

/** Posts to `POST /api/school-demo-requests`, which emails the platform team. */
export function DemoForm() {
  const [form, setForm] = useState(EMPTY);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [sent, setSent] = useState(false);
  const formRef = useRef<HTMLFormElement>(null);
  useFocusFirstInvalid(formRef, error);
  const set = (k: keyof typeof form) => (e: { target: { value: string } }) => setForm((f) => ({ ...f, [k]: e.target.value }));

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api("/api/school-demo-requests", {
        method: "POST",
        auth: false,
        body: {
          schoolName: form.schoolName.trim(),
          contactName: form.contactName.trim(),
          email: form.email.trim(),
          phone: form.phone.trim(),
          students: form.students ? Number(form.students) : null,
        },
      });
      setSent(true);
    } catch (err) {
      setError(toApiError(err));
    } finally {
      setBusy(false);
    }
  }

  if (sent) {
    return (
      <div className="space-y-4">
        <Notice tone="success">Thank you. We&apos;ll call {form.contactName.trim() || "you"} within one working day to arrange the demo.</Notice>
        <Button
          variant="secondary"
          className="w-full"
          onClick={() => {
            setForm(EMPTY);
            setSent(false);
          }}
        >
          Book another demo
        </Button>
      </div>
    );
  }

  return (
    <form ref={formRef} onSubmit={submit} className="space-y-4">
      <Input label="School name" autoComplete="organization" required value={form.schoolName} onChange={set("schoolName")} error={error?.field("schoolName")} />
      <Input label="Your name" autoComplete="name" required value={form.contactName} onChange={set("contactName")} error={error?.field("contactName")} />
      <Input label="Email" type="email" autoComplete="email" required value={form.email} onChange={set("email")} error={error?.field("email")} />
      <Input
        label="Phone number"
        type="tel"
        autoComplete="tel"
        required
        placeholder="0803 123 4567"
        value={form.phone}
        onChange={set("phone")}
        error={error?.field("phone")}
      />
      <Input
        label="Number of students"
        type="number"
        inputMode="numeric"
        min={1}
        value={form.students}
        onChange={set("students")}
        error={error?.field("students")}
      />
      <ErrorBox error={error} />
      <Button type="submit" loading={busy} className="w-full">
        Book a demo
      </Button>
    </form>
  );
}
