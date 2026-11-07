import { ogCard, ogSize } from "@/components/og-card";

export const alt = "PropertyLink for agents and landlords: let and sell homes with every cost shown. Free to start. Part of LinkSuite.";
export const size = ogSize;
export const contentType = "image/png";

export default function OpengraphImage() {
  return ogCard({
    headline: "Let and sell homes with every cost shown.",
    sub: "For agents, agencies and landlords. Free to start.",
  });
}
