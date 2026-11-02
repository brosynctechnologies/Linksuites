import type { Metadata } from "next";
import { SupportView } from "./support-view";

export const metadata: Metadata = { title: "Support view", robots: { index: false } };

export default function SupportPage() {
  return <SupportView />;
}
