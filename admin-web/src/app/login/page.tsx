import type { Metadata } from "next";
import { Suspense } from "react";
import { Loading } from "@/components/ui";
import { LoginScreen } from "./login-screen";

export const metadata: Metadata = { title: "Sign in" };

export default function LoginPage() {
  return (
    <Suspense fallback={<Loading />}>
      <LoginScreen />
    </Suspense>
  );
}
