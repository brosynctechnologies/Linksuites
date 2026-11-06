"use client";

import { useState, type FormEvent, type ReactNode } from "react";
import type { ApiError } from "@/lib/api";
import { RELATIONSHIPS, SEXES } from "@/lib/care";
import { lagosToday } from "@/lib/format";
import type { PatientDemographics, PatientSex, SavePatientRequest } from "@/lib/types";
import { ErrorBox, Input, Select } from "@/components/ui";

const FIELDS = [
  "firstName",
  "middleName",
  "lastName",
  "sex",
  "dateOfBirth",
  "phone",
  "email",
  "address",
  "nextOfKinName",
  "nextOfKinPhone",
  "nextOfKinRelationship",
] as const;

export function emptyPatient(): SavePatientRequest {
  return {
    firstName: "",
    middleName: null,
    lastName: "",
    sex: null,
    dateOfBirth: null,
    phone: null,
    email: null,
    address: null,
    nextOfKinName: null,
    nextOfKinPhone: null,
    nextOfKinRelationship: null,
  };
}

/** Demographics only: this form never carries a clinical field. */
export function fromPatient(p: PatientDemographics): SavePatientRequest {
  return {
    firstName: p.firstName,
    middleName: p.middleName,
    lastName: p.lastName,
    sex: p.sex,
    dateOfBirth: p.dateOfBirth,
    phone: p.phone,
    email: p.email,
    address: p.address,
    nextOfKinName: p.nextOfKinName,
    nextOfKinPhone: p.nextOfKinPhone,
    nextOfKinRelationship: p.nextOfKinRelationship,
  };
}

/** Trims and turns blanks into null, as the API expects. */
export function cleanPatient(v: SavePatientRequest): SavePatientRequest {
  const t = (s: string | null) => (s && s.trim() ? s.trim() : null);
  return {
    firstName: v.firstName.trim(),
    middleName: t(v.middleName),
    lastName: v.lastName.trim(),
    sex: v.sex,
    dateOfBirth: v.dateOfBirth || null,
    phone: t(v.phone),
    email: t(v.email),
    address: t(v.address),
    nextOfKinName: t(v.nextOfKinName),
    nextOfKinPhone: t(v.nextOfKinPhone),
    nextOfKinRelationship: t(v.nextOfKinRelationship),
  };
}

/** Registration and demographics form (front desk and above). Field errors show the API's own sentences. */
export function PatientForm({
  initial,
  error,
  onSubmit,
  actions,
}: {
  initial: SavePatientRequest;
  error: ApiError | null;
  onSubmit: (value: SavePatientRequest) => void;
  actions: ReactNode;
}) {
  const [v, setV] = useState<SavePatientRequest>(initial);
  const set = (k: keyof SavePatientRequest) => (e: { target: { value: string } }) => setV((f) => ({ ...f, [k]: e.target.value }));

  function submit(e: FormEvent) {
    e.preventDefault();
    onSubmit(cleanPatient(v));
  }

  return (
    <form onSubmit={submit} className="space-y-6" noValidate>
      <fieldset className="space-y-4">
        <legend className="display mb-3 text-lg font-bold text-ink">Patient</legend>
        <div className="grid gap-4 sm:grid-cols-3">
          <Input label="First name" required autoComplete="off" value={v.firstName} onChange={set("firstName")} error={error?.field("firstName")} />
          <Input label="Middle name" autoComplete="off" value={v.middleName ?? ""} onChange={set("middleName")} error={error?.field("middleName")} />
          <Input label="Last name" required autoComplete="off" value={v.lastName} onChange={set("lastName")} error={error?.field("lastName")} />
        </div>
        <div className="grid gap-4 sm:grid-cols-3">
          <Select label="Sex" required value={v.sex ?? ""} onChange={(e) => setV((f) => ({ ...f, sex: (e.target.value || null) as PatientSex | null }))} error={error?.field("sex")}>
            <option value="" disabled>
              Choose
            </option>
            {SEXES.map((s) => (
              <option key={s} value={s}>
                {s}
              </option>
            ))}
          </Select>
          <Input
            label="Date of birth"
            type="date"
            required
            max={lagosToday()}
            value={v.dateOfBirth ?? ""}
            onChange={set("dateOfBirth")}
            error={error?.field("dateOfBirth")}
            className="tnum"
          />
          <Input label="Phone" type="tel" autoComplete="off" value={v.phone ?? ""} onChange={set("phone")} error={error?.field("phone")} className="tnum" />
        </div>
        <div className="grid gap-4 sm:grid-cols-2">
          <Input label="Email" type="email" autoComplete="off" value={v.email ?? ""} onChange={set("email")} error={error?.field("email")} />
          <Input label="Address" autoComplete="off" value={v.address ?? ""} onChange={set("address")} error={error?.field("address")} />
        </div>
      </fieldset>

      <fieldset className="space-y-4">
        <legend className="display mb-3 text-lg font-bold text-ink">Next of kin</legend>
        <div className="grid gap-4 sm:grid-cols-3">
          <Input label="Name" autoComplete="off" value={v.nextOfKinName ?? ""} onChange={set("nextOfKinName")} error={error?.field("nextOfKinName")} />
          <Input
            label="Phone"
            type="tel"
            autoComplete="off"
            value={v.nextOfKinPhone ?? ""}
            onChange={set("nextOfKinPhone")}
            error={error?.field("nextOfKinPhone")}
            className="tnum"
          />
          <Input
            label="Relationship"
            list="kin-relationships"
            autoComplete="off"
            value={v.nextOfKinRelationship ?? ""}
            onChange={set("nextOfKinRelationship")}
            error={error?.field("nextOfKinRelationship")}
          />
          <datalist id="kin-relationships">
            {RELATIONSHIPS.map((r) => (
              <option key={r} value={r} />
            ))}
          </datalist>
        </div>
      </fieldset>

      <p className="text-sm text-muted">Allergies, conditions, blood group and genotype are recorded by a doctor or nurse on the patient&apos;s page.</p>
      <ErrorBox error={error && !FIELDS.some((f) => error.field(f)) ? error : null} />
      <div className="flex flex-wrap gap-2">{actions}</div>
    </form>
  );
}
