import type { Viewport } from "next";
import type { ReactNode } from "react";

export const viewport: Viewport = { themeColor: "#f1f2ee" };

/**
 * The public marketing pages, in the LinkSuite frame. The suite faces are already loaded by the root layout (the app uses
 * the same two families), so this only sets the frame's ground, face and ink.
 */
export default function SiteLayout({ children }: { children: ReactNode }) {
  return <div className="ls-body flex min-h-dvh flex-col bg-ls-chalk font-ls-text text-ls-ink">{children}</div>;
}
