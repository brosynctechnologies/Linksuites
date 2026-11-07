import { ImageResponse } from "next/og";

export const size = { width: 180, height: 180 };
export const contentType = "image/png";

/** BookLink's mark for home screens: an appointment card with a clock, on a full rose square (iOS rounds the corners). */
export default function AppleIcon() {
  return new ImageResponse(
    (
      <div style={{ width: "100%", height: "100%", display: "flex", alignItems: "center", justifyContent: "center", background: "#C2255C" }}>
        <div style={{ position: "relative", display: "flex", width: 112, height: 102, marginTop: 14, borderRadius: 16, background: "#FFFFFF" }}>
          <div style={{ position: "absolute", left: 22, top: -18, width: 14, height: 32, borderRadius: 7, background: "#FFFFFF" }} />
          <div style={{ position: "absolute", right: 22, top: -18, width: 14, height: 32, borderRadius: 7, background: "#FFFFFF" }} />
          <div style={{ position: "absolute", left: 26, top: 21, width: 60, height: 60, borderRadius: 30, border: "11px solid #C2255C" }} />
          <div style={{ position: "absolute", left: 51, top: 34, width: 10, height: 20, borderRadius: 5, background: "#C2255C" }} />
          <div style={{ position: "absolute", left: 53, top: 46, width: 18, height: 10, borderRadius: 5, background: "#C2255C" }} />
        </div>
      </div>
    ),
    { ...size },
  );
}
