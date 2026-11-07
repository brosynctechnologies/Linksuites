import type { Metadata } from "next";
import { AuthShell } from "@/components/auth-shell";
import { AuthSwitchLink } from "@/components/auth-links";
import { baseOpenGraph, baseTwitter } from "@/lib/site";
import { SignupForm } from "./signup-form";

export const metadata: Metadata = {
  title: "Create your free booking page",
  description: "Start BookLink free: a booking page for your salon, barbershop or studio in Nigeria, with 50 bookings a month and no card needed.",
  alternates: { canonical: "/signup" },
  openGraph: {
    ...baseOpenGraph,
    url: "/signup",
    title: "Create your free booking page | BookLink",
    description: "A booking page for your salon, barbershop or studio, with 50 bookings a month free and no card needed.",
  },
  twitter: {
    ...baseTwitter,
    title: "Create your free booking page | BookLink",
    description: "A booking page for your salon, barbershop or studio, with 50 bookings a month free and no card needed.",
  },
};

export default function SignupPage() {
  return (
    <AuthShell
      title="Create your free account"
      subtitle="50 free bookings every month. No card needed."
      footer={
        <>
          Already have an account? <AuthSwitchLink href="/login">Log in</AuthSwitchLink>
        </>
      }
    >
      <SignupForm />
    </AuthShell>
  );
}
