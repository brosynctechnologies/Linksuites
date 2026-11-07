import type { Metadata } from "next";
import { SchoolMessage } from "./school-sign-in";

export const metadata: Metadata = { title: "School not found", robots: { index: false, follow: false }, alternates: {} };

/** An address that isn't a SchoolLink school (`notFound()` in this segment). */
export default function SchoolNotFound() {
  return (
    <SchoolMessage title="We can't find this school">
      Check the address the school sent you, or ask the school office for the right link.
    </SchoolMessage>
  );
}
