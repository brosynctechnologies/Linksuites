import { ImageResponse } from "next/og";

export const size = { width: 180, height: 180 };
export const contentType = "image/png";

/** OrderLink's mark for home screens: the marigold dot of the wordmark on ink, on a full square (iOS rounds the corners). */
export default function AppleIcon() {
  return new ImageResponse(
    (
      <div style={{ width: "100%", height: "100%", display: "flex", alignItems: "center", justifyContent: "center", background: "#1b1f3b" }}>
        <div style={{ width: 92, height: 92, borderRadius: 46, background: "#f2a93b" }} />
      </div>
    ),
    { ...size },
  );
}
