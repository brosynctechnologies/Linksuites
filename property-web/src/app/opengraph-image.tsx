import { ogCard, ogSize } from "@/components/og-card";

export const alt = "PropertyLink: find a home in Lagos, with every cost shown. Part of LinkSuite.";
export const size = ogSize;
export const contentType = "image/png";

export default function OpengraphImage() {
  return ogCard({
    headline: "Find a home in Lagos, with every cost shown.",
    sub: "Rent, buy or book a short-let from checked agents.",
  });
}
