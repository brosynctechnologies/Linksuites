import type { Metadata } from "next";
import { ResetPasswordView } from "./reset-password-form";

export const metadata: Metadata = { title: "Choose a new password", robots: { index: false }, alternates: {} };

export default function ResetPasswordPage() {
  return <ResetPasswordView />;
}
