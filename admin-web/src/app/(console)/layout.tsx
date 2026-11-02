import { Suspense } from "react";
import { Console } from "@/components/console";
import { Loading } from "@/components/ui";

/** Every console page is client-rendered behind the session guard (the token lives in sessionStorage). */
export default function ConsoleLayout({ children }: LayoutProps<"/">) {
  return (
    <Suspense fallback={<Loading label="Opening the console…" />}>
      <Console>{children}</Console>
    </Suspense>
  );
}
