"use client";

import { useState, type FormEvent } from "react";
import { api } from "@/lib/api";
import { useAction } from "@/lib/mutate";
import type { ClassDto, Gender, StudentDto, StudentStatus } from "@/lib/types";
import { Button, ErrorBox, Input, Modal, Select, type BadgeTone } from "@/components/ui";

export const STATUS_TONE: Record<StudentStatus, BadgeTone> = { Active: "pass", Graduated: "blazer", Withdrawn: "neutral", Transferred: "neutral" };

const FIELDS = ["admissionNo", "firstName", "middleName", "lastName", "gender", "dateOfBirth", "classId", "admittedAt"];

/** Add a student (with their class) or edit one's details. The class of an existing student changes through a transfer. */
export function StudentForm({
  student,
  classes,
  onClose,
  onSaved,
}: {
  student?: StudentDto;
  classes: ClassDto[];
  onClose: () => void;
  onSaved: (s: StudentDto) => void;
}) {
  const [form, setForm] = useState({
    admissionNo: student?.admissionNo ?? "",
    firstName: student?.firstName ?? "",
    middleName: student?.middleName ?? "",
    lastName: student?.lastName ?? "",
    gender: (student?.gender ?? "") as Gender | "",
    dateOfBirth: student?.dateOfBirth ?? "",
    classId: student?.classId ?? classes[0]?.id ?? "",
    admittedAt: student?.admittedAt ?? "",
  });
  const { busy, error, run } = useAction();
  const set = (k: keyof typeof form) => (e: { target: { value: string } }) => setForm((f) => ({ ...f, [k]: e.target.value }));

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!form.gender) return;
    const body = {
      admissionNo: form.admissionNo.trim(),
      firstName: form.firstName.trim(),
      middleName: form.middleName.trim() || null,
      lastName: form.lastName.trim(),
      gender: form.gender,
      dateOfBirth: form.dateOfBirth || null,
      admittedAt: form.admittedAt || null,
      ...(student ? {} : { classId: form.classId }),
    };
    const res = await run(() =>
      student
        ? api<StudentDto>(`/api/school/students/${student.id}`, { method: "PUT", body })
        : api<StudentDto>("/api/school/students", { method: "POST", body }),
    );
    if (res.ok) onSaved(res.data);
  }

  const other = error && !FIELDS.some((f) => error.field(f)) ? error : null;
  return (
    <Modal open title={student ? `Edit ${student.fullName}` : "Add a student"} onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        <Input
          label="Admission number"
          required
          autoCapitalize="characters"
          spellCheck={false}
          value={form.admissionNo}
          onChange={set("admissionNo")}
          error={error?.field("admissionNo")}
        />
        <div className="grid gap-4 sm:grid-cols-2">
          <Input label="First name" required autoComplete="off" value={form.firstName} onChange={set("firstName")} error={error?.field("firstName")} />
          <Input label="Last name (surname)" required autoComplete="off" value={form.lastName} onChange={set("lastName")} error={error?.field("lastName")} />
        </div>
        <Input label="Middle name" autoComplete="off" value={form.middleName} onChange={set("middleName")} error={error?.field("middleName")} />
        <div className="grid gap-4 sm:grid-cols-2">
          <Select label="Gender" required value={form.gender} onChange={set("gender")} error={error?.field("gender")}>
            <option value="" disabled>
              Choose
            </option>
            <option value="Female">Female</option>
            <option value="Male">Male</option>
          </Select>
          <Input label="Date of birth" type="date" value={form.dateOfBirth} onChange={set("dateOfBirth")} error={error?.field("dateOfBirth")} />
        </div>
        {!student && (
          <Select label="Class" required value={form.classId} onChange={set("classId")} error={error?.field("classId")}>
            {classes.length === 0 && <option value="">Add a class first</option>}
            {classes.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </Select>
        )}
        <Input label="Admitted on" type="date" value={form.admittedAt} onChange={set("admittedAt")} hint="Leave blank for today." error={error?.field("admittedAt")} />
        <ErrorBox error={other} />
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" loading={busy} disabled={!form.gender || (!student && !form.classId)}>
            {student ? "Save details" : "Add student"}
          </Button>
        </div>
      </form>
    </Modal>
  );
}
