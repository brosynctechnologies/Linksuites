import type { Metadata } from "next";
import { ResetPasswordView } from "./reset-password-form";

export const metadata: Metadata = { title: "Choose a new password", alternates: {}, robots: { index: false, follow: false } };

export default function ResetPasswordPage() {
  return <ResetPasswordView />;
}
