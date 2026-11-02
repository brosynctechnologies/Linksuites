import type { Metadata } from "next";
import { ResetPasswordScreen } from "./reset-password-screen";

export const metadata: Metadata = { title: "Choose a new password", referrer: "no-referrer" };

export default function ResetPasswordPage() {
  return <ResetPasswordScreen />;
}
