import type { Metadata } from "next";
import { ForgotPasswordScreen } from "./forgot-password-screen";

export const metadata: Metadata = { title: "Forgot password" };

export default function ForgotPasswordPage() {
  return <ForgotPasswordScreen />;
}
