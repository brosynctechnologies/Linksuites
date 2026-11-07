import type { Metadata } from "next";
import Link from "next/link";
import { AuthShell } from "@/components/auth-shell";
import { baseOpenGraph, baseTwitter } from "@/lib/config";
import { SignupForm } from "./signup-form";

const title = "Create your free WhatsApp store";
const description = "Set up a store link for your food business, boutique or shop in Nigeria. Customers order from it and the order arrives on your WhatsApp.";

export const metadata: Metadata = {
  title,
  description,
  alternates: { canonical: "/signup" },
  openGraph: { ...baseOpenGraph, url: "/signup", title, description },
  twitter: { ...baseTwitter, title, description },
};

export default function SignupPage() {
  return (
    <AuthShell
      title="Create your store"
      subtitle="Free to start, no card needed. The free plan holds up to 20 products."
      footer={
        <>
          Already have an account?{" "}
          <Link href="/login" className="font-semibold text-ink underline underline-offset-2">
            Log in
          </Link>
        </>
      }
    >
      <SignupForm />
    </AuthShell>
  );
}
