import { ImageResponse } from "next/og";

export const size = { width: 180, height: 180 };
export const contentType = "image/png";

/** LearnLink's mark for home screens: the learning path, two lessons done and the next one ringed, on Lagoon. */
export default function AppleIcon() {
  const node = { position: "absolute" as const, left: 72, width: 36, height: 36, borderRadius: 18 };
  return new ImageResponse(
    (
      <div style={{ width: "100%", height: "100%", display: "flex", position: "relative", background: "#0b6e6e" }}>
        <div style={{ position: "absolute", left: 86, top: 42, width: 8, height: 96, background: "#ffffff" }} />
        <div style={{ ...node, top: 24, background: "#ffffff" }} />
        <div style={{ ...node, top: 72, background: "#ffffff" }} />
        <div style={{ ...node, top: 120, background: "#ffffff", display: "flex", alignItems: "center", justifyContent: "center" }}>
          <div style={{ width: 20, height: 20, borderRadius: 10, background: "#0b6e6e" }} />
        </div>
      </div>
    ),
    { ...size },
  );
}
