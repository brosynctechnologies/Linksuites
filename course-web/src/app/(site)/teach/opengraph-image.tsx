import { ImageResponse } from "next/og";
import { OgCard, ogSize } from "@/components/og-card";

export const alt = "LearnLink for tutors: turn your classes into courses people pay for. Free to start. Part of LinkSuite.";
export const size = ogSize;
export const contentType = "image/png";

/** The tutors' share image, for links to /teach shared on WhatsApp and elsewhere. */
export default function OpengraphImage() {
  return new ImageResponse(
    <OgCard headline="Turn your classes into courses people pay for." sub="Your own academy, the LearnLink marketplace and payouts in naira. Free to start." />,
    { ...size },
  );
}
