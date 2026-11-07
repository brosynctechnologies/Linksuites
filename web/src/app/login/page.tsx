import type { Metadata } from "next";
import Link from "next/link";
import { AuthShell } from "@/components/auth-shell";
import { LoginForm } from "./login-form";

export const metadata: Metadata = { title: "Log in to your store", robots: { index: false, follow: true }, alternates: {} };

export default function LoginPage() {
  return (
    <AuthShell
      title="Welcome back"
      subtitle="Log in to manage your store and orders."
      footer={
        <>
          New here?{" "}
          <Link href="/signup" className="font-semibold text-ink underline underline-offset-2">
            Create a free store
          </Link>
        </>
      }
    >
      <LoginForm />
    </AuthShell>
  );
}
