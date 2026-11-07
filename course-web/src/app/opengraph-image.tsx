import { ImageResponse } from "next/og";
import { OgCard, ogSize } from "@/components/og-card";

export const alt = "LearnLink: online courses from tutors you trust, paid in naira. Part of LinkSuite.";
export const size = ogSize;
export const contentType = "image/png";

/** The marketplace's share image, used by every page that has no image of its own. */
export default function OpengraphImage() {
  return new ImageResponse(
    <OgCard headline="Courses from tutors you trust, paid in naira." sub="WAEC and JAMB prep, professional exams and digital skills. Learn on your phone." />,
    { ...size },
  );
}
