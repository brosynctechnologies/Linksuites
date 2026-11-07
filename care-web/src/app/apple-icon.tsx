import { ImageResponse } from "next/og";

export const size = { width: 180, height: 180 };
export const contentType = "image/png";

/** CareLink's mark for home screens: the wristband, white on Scrub, on a full square (iOS rounds the corners). */
export default function AppleIcon() {
  return new ImageResponse(
    (
      <div style={{ width: "100%", height: "100%", display: "flex", alignItems: "center", justifyContent: "center", background: "#0f766e" }}>
        <div style={{ display: "flex", alignItems: "center", width: 136, height: 64, borderRadius: 32, background: "#ffffff", paddingLeft: 20 }}>
          <div style={{ width: 26, height: 26, borderRadius: 13, background: "#0f766e" }} />
        </div>
      </div>
    ),
    { ...size },
  );
}
