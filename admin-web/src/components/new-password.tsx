"use client";

import type { ApiError } from "@/lib/api";
import { Input } from "./ui";

/** The API's rule (Identity options): at least 8 characters, nothing else required. */
export const PASSWORD_MIN = 8;

/** Local checks before sending: length and the two entries matching. */
export function newPasswordProblems(password: string, confirm: string): { newPassword?: string; confirm?: string } {
  return {
    newPassword: password.length > 0 && password.length < PASSWORD_MIN ? `Use at least ${PASSWORD_MIN} characters.` : undefined,
    confirm: confirm.length > 0 && confirm !== password ? "The passwords don't match." : undefined,
  };
}

/** New password + confirm. Shows local problems once `touched`, and the API's `newPassword` message. */
export function NewPasswordFields({
  password,
  confirm,
  onPassword,
  onConfirm,
  touched,
  error,
}: {
  password: string;
  confirm: string;
  onPassword: (v: string) => void;
  onConfirm: (v: string) => void;
  touched: boolean;
  error: ApiError | null;
}) {
  const local = touched ? newPasswordProblems(password, confirm) : {};
  return (
    <>
      <Input
        label="New password"
        type="password"
        autoComplete="new-password"
        value={password}
        onChange={(e) => onPassword(e.target.value)}
        hint={`At least ${PASSWORD_MIN} characters.`}
        error={local.newPassword ?? error?.field("newPassword")}
        required
      />
      <Input
        label="Confirm new password"
        type="password"
        autoComplete="new-password"
        value={confirm}
        onChange={(e) => onConfirm(e.target.value)}
        error={local.confirm}
        required
      />
    </>
  );
}
